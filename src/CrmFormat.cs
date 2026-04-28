using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

// CrmFormat   — static helpers for formatting SDK types into readable trace strings.
//               No service required. OptionSetValue shows integer only.
//
// CrmFormatter — instance class with IOrganizationService + lazy metadata cache.
//               Resolves OptionSet display labels via a single SDK call per attribute,
//               cached for the lifetime of the instance (one plugin execution).
//               Pass to trace calls when label context matters.
//
// Usage (static):
//   context.Logger.Trace(TraceLevel.Verbose, () =>
//       $"Owner: {CrmFormat.Of(owner)} | Revenue: {CrmFormat.Of(revenue)}");
//
// Usage (instance with labels):
//   var fmt = new CrmFormatter(context.OrganizationService);
//   context.Logger.Trace(TraceLevel.Verbose, () =>
//       $"Status: {fmt.Of(opportunity.StatusCode, "opportunity", "statuscode")}");
//   // Output: "Status: 3 (Won)"

namespace Ops.Crm.Shared
{
    public static class CrmFormat
    {
        // OptionSetValue — integer only (no metadata call)
        public static string Of(OptionSetValue value) =>
            value == null ? "null" : value.Value.ToString();

        // Multi-select OptionSet — comma-separated integers
        public static string Of(OptionSetValueCollection values)
        {
            if (values == null) return "null";
            if (values.Count == 0) return "[]";
            return $"[{string.Join(", ", values.Select(v => v.Value))}]";
        }

        // EntityReference — "entityname:guid (Name)" when Name is populated
        public static string Of(EntityReference reference)
        {
            if (reference == null) return "null";
            var name = string.IsNullOrEmpty(reference.Name) ? string.Empty : $" ({reference.Name})";
            return $"{reference.LogicalName}:{reference.Id:D}{name}";
        }

        // Money — two decimal places
        public static string Of(Money money) =>
            money == null ? "null" : money.Value.ToString("F2");

        // DateTime — "yyyy-MM-dd HH:mm:ss"; returns "null" for default(DateTime) (field absent in SDK)
        public static string Of(DateTime value) =>
            value == default ? "null" : value.ToString("yyyy-MM-dd HH:mm:ss");

        // Nullable DateTime — null-safe wrapper for preImage?.GetAttributeValue<DateTime>() call sites
        public static string Of(DateTime? value) =>
            value == null ? "null" : Of(value.Value);

        // Entity — all attributes, or a filtered subset when include is specified
        public static string Of(Entity entity, params string[] include)
        {
            if (entity == null) return "null";
            var attributes = include.Length > 0
                ? entity.Attributes.Where(a => include.Contains(a.Key, StringComparer.OrdinalIgnoreCase))
                : entity.Attributes.AsEnumerable();
            var body = string.Join(", ", attributes.Select(a => $"{a.Key}={OfObject(a.Value)}"));
            return $"{entity.LogicalName}({entity.Id:D}) [{body}]";
        }

        // ParameterCollection — key=value pairs, or filtered subset
        public static string Of(ParameterCollection parameters, params string[] include)
        {
            if (parameters == null) return "null";
            if (parameters.Count == 0) return "{}";
            var pairs = include.Length > 0
                ? parameters.Where(p => include.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                : parameters.AsEnumerable();
            return "{" + string.Join(", ", pairs.Select(p => $"{p.Key}: {OfObject(p.Value)}")) + "}";
        }

        // Catch-all dispatch — handles any SDK value type with typed formatting
        public static string OfObject(object value)
        {
            if (value == null)                          return "null";
            if (value is OptionSetValue osv)            return Of(osv);
            if (value is OptionSetValueCollection osvc) return Of(osvc);
            if (value is EntityReference er)            return Of(er);
            if (value is Money m)                       return Of(m);
            if (value is Entity e)                      return Of(e);
            if (value is EntityCollection ec)           return $"[{ec.Entities.Count} records]";
            if (value is bool b)                        return b ? "true" : "false";
            if (value is DateTime dt)                   return dt.ToString("yyyy-MM-dd HH:mm:ss");
            if (value is byte[])                        return "[binary]";
            return value.ToString();
        }
    }

    // Instance formatter with lazy OptionSet label resolution.
    // Construct once per plugin execution and reuse — metadata is cached per attribute.
    public sealed class CrmFormatter
    {
        private readonly IOrganizationService _service;
        private readonly Dictionary<string, Dictionary<int, string>> _cache;

        public CrmFormatter(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _cache   = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        }

        // OptionSetValue with label: "3 (Won)" — falls back to "3" if metadata unavailable
        public string Of(OptionSetValue value, string entityName, string attributeName)
        {
            if (value == null) return "null";
            var label = GetLabel(entityName, attributeName, value.Value);
            return label != null ? $"{value.Value} ({label})" : value.Value.ToString();
        }

        // Multi-select with labels: "[3 (Won), 5 (Active)]"
        public string Of(OptionSetValueCollection values, string entityName, string attributeName)
        {
            if (values == null) return "null";
            if (values.Count == 0) return "[]";
            var parts = values.Select(v =>
            {
                var label = GetLabel(entityName, attributeName, v.Value);
                return label != null ? $"{v.Value} ({label})" : v.Value.ToString();
            });
            return $"[{string.Join(", ", parts)}]";
        }

        // Entity with OptionSet labels resolved — all attributes or filtered subset.
        // Label resolution requires entityName for metadata lookup; pass the entity's logical name.
        public string Of(Entity entity, string entityName, params string[] include)
        {
            if (entity == null) return "null";
            var attributes = include.Length > 0
                ? entity.Attributes.Where(a => include.Contains(a.Key, StringComparer.OrdinalIgnoreCase))
                : entity.Attributes.AsEnumerable();

            var body = string.Join(", ", attributes.Select(a => $"{a.Key}={FormatWithLabels(a.Value, entityName, a.Key)}"));
            return $"{entity.LogicalName}({entity.Id:D}) [{body}]";
        }

        // ParameterCollection with OptionSet labels — pass the entity name for OptionSet resolution
        public string Of(ParameterCollection parameters, string entityName, params string[] include)
        {
            if (parameters == null) return "null";
            if (parameters.Count == 0) return "{}";
            var pairs = include.Length > 0
                ? parameters.Where(p => include.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                : parameters.AsEnumerable();
            return "{" + string.Join(", ", pairs.Select(p =>
                $"{p.Key}: {FormatWithLabels(p.Value, entityName, p.Key)}")) + "}";
        }

        // Delegates to instance overloads for SDK types, falls back to static CrmFormat.OfObject
        private string FormatWithLabels(object value, string entityName, string attributeName)
        {
            if (value is OptionSetValue osv)            return Of(osv, entityName, attributeName);
            if (value is OptionSetValueCollection osvc) return Of(osvc, entityName, attributeName);
            return CrmFormat.OfObject(value);
        }

        private string GetLabel(string entityName, string attributeName, int value)
        {
            var cacheKey = $"{entityName}.{attributeName}";
            if (!_cache.TryGetValue(cacheKey, out var options))
            {
                options = LoadLabels(entityName, attributeName);
                _cache[cacheKey] = options; // null when metadata unavailable — prevents repeated failed calls
            }
            return options != null && options.TryGetValue(value, out var label) ? label : null;
        }

        // Single SDK call per attribute — result is cached for the plugin execution lifetime
        private Dictionary<int, string> LoadLabels(string entityName, string attributeName)
        {
            try
            {
                var response = (RetrieveAttributeResponse)_service.Execute(new RetrieveAttributeRequest
                {
                    EntityLogicalName    = entityName,
                    LogicalName          = attributeName,
                    RetrieveAsIfPublished = true
                });

                if (response.AttributeMetadata is EnumAttributeMetadata enumMeta)
                {
                    return enumMeta.OptionSet.Options
                        .Where(o => o.Value.HasValue)
                        .ToDictionary(
                            o => o.Value.Value,
                            o => o.Label?.UserLocalizedLabel?.Label ?? o.Value.Value.ToString());
                }
            }
            catch { /* metadata unavailable — fall back to integer-only */ }
            return null;
        }
    }
}
