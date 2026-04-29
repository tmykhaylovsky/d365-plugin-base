using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

// Self-contained — no DLaB.Xrm dependency required.
// Covers: typed entity attribute access, service query helpers, image/context patterns,
// QueryExpression builder extensions, and debug utilities.

namespace Ops.Plugins.Shared
{
    // -------------------------------------------------------------------------
    // Entity extensions
    // -------------------------------------------------------------------------
    public static class EntityExtensions
    {
        // Overload with explicit default — returns defaultValue when the attribute is absent or null.
        // The SDK's built-in GetAttributeValue<T>(string) returns default(T) and cannot distinguish
        // "absent" from "zero" for value types. Use this overload when that distinction matters.
        public static T GetAttributeValue<T>(this Entity entity, string logicalName, T defaultValue)
        {
            if (entity == null || !entity.Contains(logicalName) || entity[logicalName] == null)
                return defaultValue;
            return entity.GetAttributeValue<T>(logicalName);
        }

        // True if the attribute is present and non-null
        public static bool HasValue(this Entity entity, string logicalName) =>
            entity != null && entity.Contains(logicalName) && entity[logicalName] != null;

        // Returns the integer value of an OptionSet attribute; null if absent or null
        public static int? GetOptionSetValue(this Entity entity, string logicalName) =>
            entity?.GetAttributeValue<OptionSetValue>(logicalName)?.Value;

        // Returns the decimal value of a Money attribute; null if absent or null
        public static decimal? GetMoneyValue(this Entity entity, string logicalName) =>
            entity?.GetAttributeValue<Money>(logicalName)?.Value;

        // True if the OptionSet attribute equals the given integer value
        public static bool OptionSetEquals(this Entity entity, string logicalName, int value) =>
            entity.GetOptionSetValue(logicalName) == value;

        // Copies attributes from source that are absent in this entity.
        // Use to fill gaps from a pre-image without overwriting target changes.
        public static void MergeAttributesFrom(this Entity entity, Entity source)
        {
            if (source == null) return;
            foreach (var attr in source.Attributes)
                if (!entity.Contains(attr.Key))
                    entity[attr.Key] = attr.Value;
        }

        // Returns true if the attribute value in this entity (target) differs from preImage.
        // Returns true when preImage is null or the attribute is absent in preImage (first-time set).
        public static bool HasChangedFrom(this Entity target, Entity preImage, string logicalName)
        {
            if (target == null || !target.Contains(logicalName)) return false;
            if (preImage == null || !preImage.Contains(logicalName)) return true;
            return !Equals(target[logicalName], preImage[logicalName]);
        }

        // Returns an EntityReference pointing to this record
        public static EntityReference ToEntityReference(this Entity entity) =>
            new EntityReference(entity.LogicalName, entity.Id);

        // Human-readable attribute dump — for Verbose tracing only, never in production output
        public static string ToTraceString(this Entity entity)
        {
            if (entity == null) return "[null entity]";
            var attrs = string.Join(", ", entity.Attributes.Select(a => $"{a.Key}={FormatAttributeValue(a.Value)}"));
            return $"{entity.LogicalName}({entity.Id:D}) [{attrs}]";
        }

        // Human-readable single attribute value. Use when tracing image-to-image comparisons.
        public static string FormatAttribute(this Entity entity, string logicalName)
        {
            return entity != null && entity.Attributes.TryGetValue(logicalName, out var value)
                ? CrmFormat.OfObject(value)
                : "null";
        }

        private static string FormatAttributeValue(object value)
        {
            if (value == null)                    return "null";
            if (value is OptionSetValue osv)       return osv.Value.ToString();
            if (value is EntityReference er)       return $"{er.LogicalName}:{er.Id:D}";
            if (value is Money m)                  return m.Value.ToString("F2");
            if (value is EntityCollection ec)      return $"[{ec.Entities.Count} entities]";
            if (value is OptionSetValueCollection mc) return $"[{string.Join(",", mc.Select(o => o.Value))}]";
            return value.ToString();
        }
    }

    // -------------------------------------------------------------------------
    // EntityReference extensions
    // -------------------------------------------------------------------------
    public static class EntityReferenceExtensions
    {
        // True if both references point to the same logical name and Id
        public static bool Matches(this EntityReference reference, EntityReference other) =>
            reference != null && other != null &&
            reference.Id == other.Id &&
            string.Equals(reference.LogicalName, other.LogicalName, StringComparison.OrdinalIgnoreCase);

        // True if the reference Id matches the given Guid (no entity name check)
        public static bool MatchesId(this EntityReference reference, Guid id) =>
            reference != null && reference.Id == id;
    }

    // -------------------------------------------------------------------------
    // IOrganizationService extensions
    // -------------------------------------------------------------------------
    public static class OrganizationServiceExtensions
    {
        // Returns the first record matching the query; throws if none found.
        // Sets TopCount = 1 automatically.
        public static T GetFirst<T>(this IOrganizationService service, QueryExpression query) where T : Entity
        {
            var result = service.GetFirstOrDefault<T>(query);
            if (result == null)
                throw new InvalidPluginExecutionException(
                    $"Expected at least one {query.EntityName} record but found none.");
            return result;
        }

        // Returns the first record matching the query; null if none found.
        // Sets TopCount = 1 automatically.
        public static T GetFirstOrDefault<T>(this IOrganizationService service, QueryExpression query) where T : Entity
        {
            query.TopCount = 1;
            return service.RetrieveMultiple(query).Entities.FirstOrDefault()?.ToEntity<T>();
        }

        // Convenience overload: match a single attribute value without building a QueryExpression.
        // Passing null for columns selects all columns — prefer an explicit ColumnSet in production.
        public static T GetFirstOrDefault<T>(
            this IOrganizationService service,
            string entityName,
            string attributeName,
            object value,
            ColumnSet columns = null) where T : Entity
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = columns ?? new ColumnSet(true),
                TopCount  = 1,
                NoLock    = true
            };
            query.Criteria.AddCondition(attributeName, ConditionOperator.Equal, value);
            return service.RetrieveMultiple(query).Entities.FirstOrDefault()?.ToEntity<T>();
        }

        // Returns all records matching a QueryExpression using paged retrieval.
        // Throws if the total result count exceeds maxRecords to prevent unintended bulk reads.
        public static List<T> GetAll<T>(
            this IOrganizationService service,
            QueryExpression query,
            int maxRecords = 5000) where T : Entity
        {
            var results      = new List<T>();
            int page         = 1;
            string cookie    = null;

            while (true)
            {
                query.PageInfo = new PagingInfo
                {
                    PageNumber   = page,
                    PagingCookie = cookie,
                    Count        = Math.Min(maxRecords - results.Count, 5000)
                };

                var response = service.RetrieveMultiple(query);
                results.AddRange(response.Entities.Select(e => e.ToEntity<T>()));

                if (results.Count >= maxRecords && response.MoreRecords)
                    throw new InvalidPluginExecutionException(
                        $"GetAll<{typeof(T).Name}> exceeded maxRecords limit ({maxRecords}). Refine the query or increase the limit.");

                if (!response.MoreRecords) break;
                cookie = response.PagingCookie;
                page++;
            }

            return results;
        }

        // Retrieve a single record by Id; returns null if the record does not exist.
        // Catches error 0x80040217 (Entity Does Not Exist) — all other exceptions propagate.
        public static T GetRecordOrDefault<T>(
            this IOrganizationService service,
            string entityName,
            Guid id,
            ColumnSet columns) where T : Entity
        {
            try
            {
                return service.Retrieve(entityName, id, columns).ToEntity<T>();
            }
            catch (Exception ex) when (
                ex.Message.Contains("0x80040217") ||
                ex.Message.Contains("Does Not Exist"))
            {
                return null;
            }
        }

        // Returns true if a record with the given Id exists — uses no-lock, selects no columns
        public static bool RecordExists(this IOrganizationService service, string entityName, Guid id)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount  = 1,
                NoLock    = true
            };
            query.Criteria.AddCondition(entityName + "id", ConditionOperator.Equal, id);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        // Creates the record if Id is empty; updates if Id is set. Returns the record Id.
        public static Guid CreateOrUpdate(this IOrganizationService service, Entity entity)
        {
            if (entity.Id == Guid.Empty)
                return service.Create(entity);
            service.Update(entity);
            return entity.Id;
        }

        // Associates two records via a named relationship.
        // Named AssociateRecord to avoid ambiguity with IOrganizationService.Associate.
        public static void AssociateRecord(
            this IOrganizationService service,
            string entityName,    Guid entityId,
            string relationshipName,
            string relatedEntityName, Guid relatedEntityId)
        {
            service.Associate(
                entityName, entityId,
                new Relationship(relationshipName),
                new EntityReferenceCollection
                {
                    new EntityReference(relatedEntityName, relatedEntityId)
                });
        }

        // Converts a QueryExpression to its FetchXml equivalent.
        // Use for Verbose trace logging during development — involves a service round-trip.
        public static string ToFetchXml(this QueryExpression query, IOrganizationService service)
        {
            var request  = new QueryExpressionToFetchXmlRequest { Query = query };
            var response = (QueryExpressionToFetchXmlResponse)service.Execute(request);
            return response.FetchXml;
        }
    }

    // -------------------------------------------------------------------------
    // LocalPluginContext convenience extensions
    // -------------------------------------------------------------------------
    public static class LocalPluginContextExtensions
    {
        public static void Trace(
            this PluginBase.LocalPluginContext context,
            string message,
            TraceLevel? level = null)
        {
            var effectiveLevel = level ?? PluginLogger.GlobalLevel;
            if (effectiveLevel == TraceLevel.Off) return;
            context.Logger.Trace(effectiveLevel, message);
        }

        public static void Trace(
            this PluginBase.LocalPluginContext context,
            Func<string> messageFactory,
            TraceLevel? level = null)
        {
            var effectiveLevel = level ?? PluginLogger.GlobalLevel;
            if (effectiveLevel == TraceLevel.Off) return;
            context.Logger.Trace(effectiveLevel, messageFactory);
        }
        // Logs the full target entity at Verbose level — call at the top of ExecutePlugin
        public static void TraceTarget(this PluginBase.LocalPluginContext context) =>
            context.Trace(
                () => context.GetTarget()?.ToTraceString() ?? "[no target entity]",
                TraceLevel.Verbose);

        // Logs all registered pre-images at Verbose level
        public static void TracePreImages(this PluginBase.LocalPluginContext context) =>
            context.Trace(() =>
            {
                var images = context.ExecutionContext.PreEntityImages;
                return images.Count == 0
                    ? "[no pre-images]"
                    : string.Join(" | ", images.Select(kvp => $"PreImage[{kvp.Key}]: {kvp.Value.ToTraceString()}"));
            }, TraceLevel.Verbose);

        // Logs the before/after value for an attribute using pre/post images.
        public static void TraceAttributeChange(
            this PluginBase.LocalPluginContext context,
            Entity preImage,
            Entity postImage,
            string logicalName,
            bool changed,
            TraceLevel level = TraceLevel.Info) =>
            context.Trace(
                () => $"{logicalName} changed: {changed} | {preImage.FormatAttribute(logicalName)} -> {postImage.FormatAttribute(logicalName)}",
                level);
    }

    // -------------------------------------------------------------------------
    // QueryExpression builder extensions — fluent style
    // -------------------------------------------------------------------------
    public static class QueryExpressionExtensions
    {
        public static QueryExpression WithTopCount(this QueryExpression query, int topCount)
        {
            query.TopCount = topCount;
            return query;
        }

        // Reduces blocking on high-volume reads — use only for read-only queries
        public static QueryExpression WithNoLock(this QueryExpression query)
        {
            query.NoLock = true;
            return query;
        }

        public static QueryExpression WithOrdering(
            this QueryExpression query,
            string attributeName,
            OrderType orderType = OrderType.Ascending)
        {
            query.AddOrder(attributeName, orderType);
            return query;
        }

        public static QueryExpression WithCondition(
            this QueryExpression query,
            string attributeName,
            ConditionOperator op,
            object value)
        {
            query.Criteria.AddCondition(attributeName, op, value);
            return query;
        }
    }
}
