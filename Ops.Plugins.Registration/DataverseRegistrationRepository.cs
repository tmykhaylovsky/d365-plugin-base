using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Ops.Plugins.Registration
{
    public class DataverseRegistrationRepository
    {
        private readonly IOrganizationService _service;
        private readonly Dictionary<string, Entity> _messageCache = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Entity> _filterCache = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _createdStepIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyDictionary<string, Guid> _optionsUserAliases = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        private Guid? _loadedAssemblyId;

        public DataverseRegistrationRepository(IOrganizationService service)
        {
            _service = service;
        }

        public static DataverseRegistrationRepository Create(RegistrationOptions options)
        {
            ServiceClient client;
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                client = new ServiceClient(options.ConnectionString);
            }
            else
            {
                client = new ServiceClient($"AuthType=OAuth;Url={options.EnvironmentUrl};LoginPrompt=Auto");
            }

            if (!client.IsReady)
                throw new InvalidOperationException(BuildConnectionFailureMessage(client, options), client.LastException);

            return new DataverseRegistrationRepository(client);
        }

        private static string BuildConnectionFailureMessage(ServiceClient client, RegistrationOptions options)
        {
            var lines = new List<string>
            {
                "Dataverse connection failed."
            };

            if (!string.IsNullOrWhiteSpace(options.EnvironmentUrl))
                lines.Add("Environment: " + options.EnvironmentUrl);
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
                lines.Add("Connection: " + DescribeConnectionString(options.ConnectionString));
            if (!string.IsNullOrWhiteSpace(client.LastError))
                lines.Add("SDK error: " + client.LastError);

            var exception = client.LastException;
            var depth = 0;
            while (exception != null && depth < 6)
            {
                if (!string.IsNullOrWhiteSpace(exception.Message))
                    lines.Add((depth == 0 ? "Exception: " : "Inner exception: ") + exception.GetType().Name + ": " + exception.Message);

                exception = exception.InnerException;
                depth++;
            }

            lines.Add("Auth tips: if browser/device auth failed, retry once. If it still fails, create/use a connection string env var, for example:");
            lines.Add("  setx DATAVERSE_CONNECTION \"AuthType=OAuth;Url=https://<org>.crm.dynamics.com;LoginPrompt=Auto\"");
            lines.Add("  .\\Scripts\\Sync-PluginRegistration.ps1 -ConnectionString DATAVERSE_CONNECTION");

            return string.Join(Environment.NewLine, lines);
        }

        private static string DescribeConnectionString(string connectionString)
        {
            var parts = connectionString
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !part.StartsWith("Password=", StringComparison.OrdinalIgnoreCase)
                    && !part.StartsWith("ClientSecret=", StringComparison.OrdinalIgnoreCase)
                    && !part.StartsWith("Secret=", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return string.Join(";", parts);
        }

        public virtual ActualRegistration Load(DesiredRegistration desired, RegistrationOptions options)
        {
            var assembly = FindAssembly(desired, options);
            _loadedAssemblyId = assembly.Id;
            _optionsUserAliases = options.UserAliases;
            ValidateDesiredEntities(desired);
            var pluginTypes = LoadPluginTypes(assembly.Id);
            var targetTypeIds = pluginTypes.Values.Select(t => t.Id).ToArray();
            var steps = LoadSteps(targetTypeIds, pluginTypes);
            var images = LoadImages(steps);
            return new ActualRegistration(assembly, pluginTypes, steps, images);
        }

        public virtual void Apply(RegistrationPlan plan)
        {
            foreach (var change in plan.Changes
                .Where(c => c.Action == RegistrationActionKind.Create || c.Action == RegistrationActionKind.Update)
                .OrderBy(c => c.Target == RegistrationTargetKind.Step ? 0 : 1))
            {
                try
                {
                    if (change.Target == RegistrationTargetKind.Step)
                        ApplyStep(change);
                    else if (change.Target == RegistrationTargetKind.Image)
                        ApplyImage(change);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(BuildApplyFailureMessage(change), ex);
                }

                change.Applied = true;
            }
        }

        private static string BuildApplyFailureMessage(RegistrationChange change)
        {
            var lines = new List<string>
            {
                $"Failed to {change.Action.ToString().ToLowerInvariant()} {change.Target.ToString().ToLowerInvariant()}.",
                "Plugin: " + (change.PluginTypeName ?? "(unknown)"),
                "Message: " + (change.MessageName ?? "(unknown)"),
                "Entity: " + (change.EntityLogicalName ?? "(none)"),
                "Detail: " + (change.Detail ?? "(none)")
            };

            if (change.ActualStep != null)
                lines.Add("Step id: " + change.ActualStep.Id);
            if (change.ActualImage != null)
            {
                lines.Add("Image id: " + change.ActualImage.Id);
                lines.Add("Image alias: " + change.ActualImage.Alias);
                lines.Add("Image type: " + change.ActualImage.ImageType);
                lines.Add("Message property name: " + change.ActualImage.MessagePropertyName);
                lines.Add("Current image attributes: " + change.ActualImage.Attributes);
            }
            if (change.DesiredImage != null)
            {
                lines.Add("Desired message property name: " + change.DesiredImage.MessagePropertyName);
                lines.Add("Desired image attributes: " + change.DesiredImage.Attributes);
                lines.Add("SDK update payload: " + DescribeImageUpdatePayload(change.DesiredImage, change.ActualImage, change.ActualStep));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string DescribeImageUpdatePayload(DesiredImage desired, ActualImage actual, ActualStep actualStep)
        {
            var fields = new List<string>();
            if (actualStep != null)
                fields.Add(RegistrationEntityNames.ImageFields.SdkMessageProcessingStepId + "=" + actualStep.Id);
            if (actual == null || !string.Equals(desired.MessagePropertyName, actual.MessagePropertyName, StringComparison.OrdinalIgnoreCase))
                fields.Add(RegistrationEntityNames.ImageFields.MessagePropertyName + "=" + desired.MessagePropertyName);
            if (actual == null || !desired.Attributes.SetEquals(actual.Attributes))
                fields.Add(RegistrationEntityNames.ImageFields.Attributes + "=" + desired.Attributes);

            return fields.Count == 0 ? "(none)" : string.Join("; ", fields);
        }

        public virtual void PushAssembly(Guid pluginAssemblyId, string assemblyPath)
        {
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            var update = new Entity(RegistrationEntityNames.PluginAssembly, pluginAssemblyId);
            update[RegistrationEntityNames.PluginAssemblyFields.Content] = Convert.ToBase64String(File.ReadAllBytes(assemblyPath));
            update[RegistrationEntityNames.PluginAssemblyFields.Version] = assemblyName.Version?.ToString();
            update[RegistrationEntityNames.PluginAssemblyFields.PublicKeyToken] = PublicKeyTokenToString(assemblyName.GetPublicKeyToken());
            update[RegistrationEntityNames.PluginAssemblyFields.Culture] = string.IsNullOrWhiteSpace(assemblyName.CultureName) ? "neutral" : assemblyName.CultureName;
            _service.Update(update);
        }

        private ActualPluginAssembly FindAssembly(DesiredRegistration desired, RegistrationOptions options)
        {
            var query = new QueryExpression(RegistrationEntityNames.PluginAssembly)
            {
                ColumnSet = new ColumnSet(RegistrationEntityNames.PluginAssemblyFields.PluginAssemblyId, RegistrationEntityNames.PluginAssemblyFields.Name)
            };

            if (options.PluginAssemblyId.HasValue)
                query.Criteria.AddCondition(RegistrationEntityNames.PluginAssemblyFields.PluginAssemblyId, ConditionOperator.Equal, options.PluginAssemblyId.Value);
            else
                query.Criteria.AddCondition(RegistrationEntityNames.PluginAssemblyFields.Name, ConditionOperator.Equal, string.IsNullOrWhiteSpace(options.AssemblyName) ? desired.AssemblyName : options.AssemblyName);

            var entity = _service.RetrieveMultiple(query).Entities.SingleOrDefault();
            if (entity == null) throw new InvalidOperationException("Dataverse pluginassembly was not found. Run pac plugin push first or pass --pluginAssemblyId.");

            return new ActualPluginAssembly
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(RegistrationEntityNames.PluginAssemblyFields.Name)
            };
        }

        private void ValidateDesiredEntities(DesiredRegistration desired)
        {
            var entityNames = desired.PluginTypes
                .SelectMany(t => t.Steps)
                .Select(s => s.EntityLogicalName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

            foreach (var entityName in entityNames)
            {
                try
                {
                    _service.Execute(new RetrieveEntityRequest
                    {
                        LogicalName = entityName,
                        EntityFilters = EntityFilters.Entity
                    });
                }
                catch (FaultException<OrganizationServiceFault> ex)
                {
                    throw new InvalidOperationException($"Entity logical name '{entityName}' was not found in this Dataverse environment. Confirm the table exists before applying registration.", ex);
                }
            }
        }

        private IReadOnlyDictionary<string, ActualPluginType> LoadPluginTypes(Guid assemblyId)
        {
            var query = new QueryExpression(RegistrationEntityNames.PluginType)
            {
                ColumnSet = new ColumnSet(RegistrationEntityNames.PluginTypeFields.PluginTypeId, RegistrationEntityNames.PluginTypeFields.TypeName)
            };
            query.Criteria.AddCondition(RegistrationEntityNames.PluginTypeFields.PluginAssemblyId, ConditionOperator.Equal, assemblyId);

            return _service.RetrieveMultiple(query).Entities
                .Select(e =>
                {
                    return new ActualPluginType { Id = e.Id, TypeName = e.GetAttributeValue<string>(RegistrationEntityNames.PluginTypeFields.TypeName) };
                })
                .GroupBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        private IReadOnlyCollection<ActualStep> LoadSteps(Guid[] pluginTypeIds, IReadOnlyDictionary<string, ActualPluginType> pluginTypes)
        {
            if (pluginTypeIds.Length == 0) return Array.Empty<ActualStep>();

            var query = new QueryExpression(RegistrationEntityNames.SdkMessageProcessingStep)
            {
                ColumnSet = new ColumnSet(
                    RegistrationEntityNames.StepFields.SdkMessageProcessingStepId,
                    RegistrationEntityNames.StepFields.EventHandler,
                    RegistrationEntityNames.StepFields.SdkMessageId,
                    RegistrationEntityNames.StepFields.SdkMessageFilterId,
                    RegistrationEntityNames.StepFields.Stage,
                    RegistrationEntityNames.StepFields.Mode,
                    RegistrationEntityNames.StepFields.Rank,
                    RegistrationEntityNames.StepFields.FilteringAttributes,
                    RegistrationEntityNames.StepFields.StateCode,
                    RegistrationEntityNames.StepFields.IsManaged,
                    RegistrationEntityNames.StepFields.Description,
                    RegistrationEntityNames.StepFields.Configuration,
                    RegistrationEntityNames.StepFields.ImpersonatingUserId)
            };
            query.Criteria.AddCondition(RegistrationEntityNames.StepFields.EventHandler, ConditionOperator.In, pluginTypeIds.Cast<object>().ToArray());

            var typeById = pluginTypes.Values.ToDictionary(t => t.Id, t => t);
            var steps = new List<ActualStep>();

            foreach (var row in _service.RetrieveMultiple(query).Entities)
            {
                var pluginTypeRef = row.GetAttributeValue<EntityReference>(RegistrationEntityNames.StepFields.EventHandler);
                if (pluginTypeRef == null || !typeById.ContainsKey(pluginTypeRef.Id)) continue;

                var messageRef = row.GetAttributeValue<EntityReference>(RegistrationEntityNames.StepFields.SdkMessageId);
                var filterRef = row.GetAttributeValue<EntityReference>(RegistrationEntityNames.StepFields.SdkMessageFilterId);
                var filter = filterRef == null ? null : RetrieveFilter(filterRef.Id);

                steps.Add(new ActualStep
                {
                    Id = row.Id,
                    PluginTypeId = pluginTypeRef.Id,
                    PluginTypeName = typeById[pluginTypeRef.Id].TypeName,
                    MessageId = messageRef == null ? Guid.Empty : messageRef.Id,
                    MessageName = messageRef == null ? null : RetrieveMessage(messageRef.Id).GetAttributeValue<string>(RegistrationEntityNames.SdkMessageFields.Name),
                    MessageFilterId = filterRef == null ? (Guid?)null : filterRef.Id,
                    EntityLogicalName = filter?.GetAttributeValue<string>(RegistrationEntityNames.SdkMessageFilterFields.PrimaryObjectTypeCode),
                    Stage = row.GetAttributeValue<OptionSetValue>(RegistrationEntityNames.StepFields.Stage)?.Value ?? 0,
                    Mode = row.GetAttributeValue<OptionSetValue>(RegistrationEntityNames.StepFields.Mode)?.Value ?? 0,
                    Rank = row.GetAttributeValue<int?>(RegistrationEntityNames.StepFields.Rank) ?? 1,
                    FilteringAttributes = AttributeList.Parse(row.GetAttributeValue<string>(RegistrationEntityNames.StepFields.FilteringAttributes)),
                    StateCode = row.GetAttributeValue<OptionSetValue>(RegistrationEntityNames.StepFields.StateCode)?.Value ?? 0,
                    IsManaged = row.GetAttributeValue<bool?>(RegistrationEntityNames.StepFields.IsManaged) ?? false,
                    Description = row.GetAttributeValue<string>(RegistrationEntityNames.StepFields.Description),
                    UnsecureConfiguration = row.GetAttributeValue<string>(RegistrationEntityNames.StepFields.Configuration),
                    ImpersonatingUserId = row.GetAttributeValue<EntityReference>(RegistrationEntityNames.StepFields.ImpersonatingUserId)
                });
            }

            return steps;
        }

        private IReadOnlyCollection<ActualImage> LoadImages(IReadOnlyCollection<ActualStep> steps)
        {
            if (!steps.Any()) return Array.Empty<ActualImage>();

            var query = new QueryExpression(RegistrationEntityNames.SdkMessageProcessingStepImage)
            {
                ColumnSet = new ColumnSet(
                    RegistrationEntityNames.ImageFields.SdkMessageProcessingStepImageId,
                    RegistrationEntityNames.ImageFields.SdkMessageProcessingStepId,
                    RegistrationEntityNames.ImageFields.EntityAlias,
                    RegistrationEntityNames.ImageFields.ImageType,
                    RegistrationEntityNames.ImageFields.MessagePropertyName,
                    RegistrationEntityNames.ImageFields.Attributes)
            };
            query.Criteria.AddCondition(RegistrationEntityNames.ImageFields.SdkMessageProcessingStepId, ConditionOperator.In, steps.Select(s => (object)s.Id).ToArray());

            var stepById = steps.ToDictionary(s => s.Id, s => s);
            var images = new List<ActualImage>();
            foreach (var row in _service.RetrieveMultiple(query).Entities)
            {
                var stepRef = row.GetAttributeValue<EntityReference>(RegistrationEntityNames.ImageFields.SdkMessageProcessingStepId);
                if (stepRef == null || !stepById.ContainsKey(stepRef.Id)) continue;

                images.Add(new ActualImage
                {
                    Id = row.Id,
                    StepId = stepRef.Id,
                    StepKey = stepById[stepRef.Id].Key,
                    Alias = row.GetAttributeValue<string>(RegistrationEntityNames.ImageFields.EntityAlias),
                    ImageType = row.GetAttributeValue<OptionSetValue>(RegistrationEntityNames.ImageFields.ImageType)?.Value ?? 0,
                    MessagePropertyName = row.GetAttributeValue<string>(RegistrationEntityNames.ImageFields.MessagePropertyName),
                    Attributes = AttributeList.Parse(row.GetAttributeValue<string>(RegistrationEntityNames.ImageFields.Attributes))
                });
            }

            return images;
        }

        private void ApplyStep(RegistrationChange change)
        {
            var desired = change.DesiredStep;
            if (change.Action == RegistrationActionKind.Create)
            {
                var pluginType = LoadPluginTypeByName(desired.PluginTypeName);
                var message = GetMessage(desired.MessageName);
                var filter = GetFilter(message.Id, desired.EntityLogicalName);
                var runAsUserId = ResolveRunInUserContext(desired.RunInUserContext);
                var entity = new Entity(RegistrationEntityNames.SdkMessageProcessingStep);
                entity[RegistrationEntityNames.StepFields.Name] = $"{desired.PluginTypeName}: {desired.MessageName} of {desired.EntityLogicalName}";
                entity[RegistrationEntityNames.StepFields.EventHandler] = new EntityReference(RegistrationEntityNames.PluginType, pluginType.Id);
                entity[RegistrationEntityNames.StepFields.SdkMessageId] = new EntityReference(RegistrationEntityNames.SdkMessage, message.Id);
                if (filter != null) entity[RegistrationEntityNames.StepFields.SdkMessageFilterId] = new EntityReference(RegistrationEntityNames.SdkMessageFilter, filter.Id);
                SetStepFields(entity, desired, runAsUserId);
                var id = _service.Create(entity);
                _createdStepIds[desired.Key] = id;
                return;
            }

            var update = new Entity(RegistrationEntityNames.SdkMessageProcessingStep, change.ActualStep.Id);
            SetStepFields(update, desired, ResolveRunInUserContext(desired.RunInUserContext));
            _service.Update(update);
        }

        private void ApplyImage(RegistrationChange change)
        {
            var desired = change.DesiredImage;
            if (change.Action == RegistrationActionKind.Create)
            {
                var stepId = change.ActualStep?.Id ?? FindStepId(desired.StepKey);
                var entity = new Entity(RegistrationEntityNames.SdkMessageProcessingStepImage);
                entity[RegistrationEntityNames.ImageFields.SdkMessageProcessingStepId] = new EntityReference(RegistrationEntityNames.SdkMessageProcessingStep, stepId);
                SetImageCreateFields(entity, desired);
                _service.Create(entity);
                return;
            }

            var update = new Entity(RegistrationEntityNames.SdkMessageProcessingStepImage, change.ActualImage.Id);
            SetImageUpdateFields(update, desired, change.ActualImage, change.ActualStep);
            _service.Update(update);
        }

        private void SetStepFields(Entity entity, DesiredStep desired, Guid? runAsUserId)
        {
            entity[RegistrationEntityNames.StepFields.Stage] = new OptionSetValue(desired.Stage);
            entity[RegistrationEntityNames.StepFields.Mode] = new OptionSetValue(desired.Mode);
            entity[RegistrationEntityNames.StepFields.Rank] = desired.Rank;
            entity[RegistrationEntityNames.StepFields.FilteringAttributes] = desired.FilteringAttributes.ToString();
            entity[RegistrationEntityNames.StepFields.SupportedDeployment] = new OptionSetValue(0);
            if (!string.IsNullOrWhiteSpace(desired.Description)) entity[RegistrationEntityNames.StepFields.Description] = desired.Description;
            entity[RegistrationEntityNames.StepFields.ImpersonatingUserId] = runAsUserId.HasValue ? new EntityReference(RegistrationEntityNames.SystemUser, runAsUserId.Value) : null;
        }

        private void SetImageCreateFields(Entity entity, DesiredImage desired)
        {
            entity[RegistrationEntityNames.ImageFields.EntityAlias] = desired.Alias;
            entity[RegistrationEntityNames.ImageFields.Name] = desired.Alias;
            entity[RegistrationEntityNames.ImageFields.ImageType] = new OptionSetValue(desired.ImageType);
            entity[RegistrationEntityNames.ImageFields.MessagePropertyName] = desired.MessagePropertyName;
            entity[RegistrationEntityNames.ImageFields.Attributes] = desired.Attributes.ToString();
        }

        private void SetImageUpdateFields(Entity entity, DesiredImage desired, ActualImage actual, ActualStep actualStep)
        {
            if (actualStep != null)
                entity[RegistrationEntityNames.ImageFields.SdkMessageProcessingStepId] = new EntityReference(RegistrationEntityNames.SdkMessageProcessingStep, actualStep.Id);
            if (actual == null || !string.Equals(desired.MessagePropertyName, actual.MessagePropertyName, StringComparison.OrdinalIgnoreCase))
                entity[RegistrationEntityNames.ImageFields.MessagePropertyName] = desired.MessagePropertyName;
            if (actual == null || !desired.Attributes.SetEquals(actual.Attributes))
                entity[RegistrationEntityNames.ImageFields.Attributes] = desired.Attributes.ToString();
        }

        private ActualPluginType LoadPluginTypeByName(string typeName)
        {
            var query = new QueryExpression(RegistrationEntityNames.PluginType) { ColumnSet = new ColumnSet(RegistrationEntityNames.PluginTypeFields.PluginTypeId, RegistrationEntityNames.PluginTypeFields.TypeName) };
            query.Criteria.AddCondition(RegistrationEntityNames.PluginTypeFields.TypeName, ConditionOperator.Equal, typeName);
            if (_loadedAssemblyId.HasValue)
                query.Criteria.AddCondition(RegistrationEntityNames.PluginTypeFields.PluginAssemblyId, ConditionOperator.Equal, _loadedAssemblyId.Value);

            var entities = _service.RetrieveMultiple(query).Entities;
            if (entities.Count == 0) throw new InvalidOperationException("plugintype not found for target assembly: " + typeName);
            if (entities.Count > 1) throw new InvalidOperationException("Multiple plugintype rows found for target assembly: " + typeName);

            var entity = entities.Single();
            return new ActualPluginType { Id = entity.Id, TypeName = entity.GetAttributeValue<string>(RegistrationEntityNames.PluginTypeFields.TypeName) };
        }

        private Guid? ResolveRunInUserContext(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Calling User", StringComparison.OrdinalIgnoreCase))
                return null;

            Guid id;
            if (Guid.TryParse(value, out id)) return id;
            if (_optionsUserAliases != null && _optionsUserAliases.TryGetValue(value, out id)) return id;

            throw new InvalidOperationException($"Run in User's Context '{value}' was not found. Use 'Calling User', a systemuserid GUID, or a label in the run-as user config.");
        }

        private static string PublicKeyTokenToString(byte[] token)
        {
            if (token == null || token.Length == 0) return null;
            return string.Concat(token.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private Guid FindStepId(string key)
        {
            Guid id;
            if (_createdStepIds.TryGetValue(key, out id)) return id;
            throw new InvalidOperationException("Image create could not find its step. Re-run after the step is created.");
        }

        private Entity RetrieveMessage(Guid id)
        {
            var key = id.ToString("N");
            Entity cached;
            if (_messageCache.TryGetValue(key, out cached)) return cached;
            cached = _service.Retrieve(RegistrationEntityNames.SdkMessage, id, new ColumnSet(RegistrationEntityNames.SdkMessageFields.Name));
            _messageCache[key] = cached;
            return cached;
        }

        private Entity RetrieveFilter(Guid id)
        {
            var key = id.ToString("N");
            Entity cached;
            if (_filterCache.TryGetValue(key, out cached)) return cached;
            cached = _service.Retrieve(RegistrationEntityNames.SdkMessageFilter, id, new ColumnSet(RegistrationEntityNames.SdkMessageFilterFields.PrimaryObjectTypeCode));
            _filterCache[key] = cached;
            return cached;
        }

        private Entity GetMessage(string messageName)
        {
            var key = "name:" + messageName;
            Entity cached;
            if (_messageCache.TryGetValue(key, out cached)) return cached;

            var query = new QueryExpression(RegistrationEntityNames.SdkMessage) { ColumnSet = new ColumnSet(RegistrationEntityNames.SdkMessageFields.SdkMessageId, RegistrationEntityNames.SdkMessageFields.Name) };
            query.Criteria.AddCondition(RegistrationEntityNames.SdkMessageFields.Name, ConditionOperator.Equal, messageName);
            cached = _service.RetrieveMultiple(query).Entities.SingleOrDefault();
            if (cached == null) throw new InvalidOperationException("sdkmessage not found: " + messageName);
            _messageCache[key] = cached;
            return cached;
        }

        private Entity GetFilter(Guid messageId, string entityLogicalName)
        {
            if (string.IsNullOrWhiteSpace(entityLogicalName)) return null;
            var key = messageId.ToString("N") + ":" + entityLogicalName;
            Entity cached;
            if (_filterCache.TryGetValue(key, out cached)) return cached;

            var query = new QueryExpression(RegistrationEntityNames.SdkMessageFilter) { ColumnSet = new ColumnSet(RegistrationEntityNames.SdkMessageFilterFields.SdkMessageFilterId, RegistrationEntityNames.SdkMessageFilterFields.PrimaryObjectTypeCode) };
            query.Criteria.AddCondition(RegistrationEntityNames.SdkMessageFilterFields.SdkMessageId, ConditionOperator.Equal, messageId);
            query.Criteria.AddCondition(RegistrationEntityNames.SdkMessageFilterFields.PrimaryObjectTypeCode, ConditionOperator.Equal, entityLogicalName);
            cached = _service.RetrieveMultiple(query).Entities.SingleOrDefault();
            if (cached == null) throw new InvalidOperationException($"sdkmessagefilter not found for {entityLogicalName}.");
            _filterCache[key] = cached;
            return cached;
        }
    }
}
