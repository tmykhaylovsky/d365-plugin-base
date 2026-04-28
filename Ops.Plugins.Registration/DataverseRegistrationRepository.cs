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
using Ops.Plugins.Model;

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
                throw new InvalidOperationException("Dataverse connection failed: " + client.LastError);

            return new DataverseRegistrationRepository(client);
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
                if (change.Target == RegistrationTargetKind.Step)
                    ApplyStep(change);
                else if (change.Target == RegistrationTargetKind.Image)
                    ApplyImage(change);

                change.Applied = true;
            }
        }

        public virtual void PushAssembly(Guid pluginAssemblyId, string assemblyPath)
        {
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            var update = new PluginAssembly
            {
                Id = pluginAssemblyId,
                Content = Convert.ToBase64String(File.ReadAllBytes(assemblyPath)),
                Version = assemblyName.Version?.ToString(),
                PublicKeyToken = PublicKeyTokenToString(assemblyName.GetPublicKeyToken()),
                Culture = string.IsNullOrWhiteSpace(assemblyName.CultureName) ? "neutral" : assemblyName.CultureName
            };
            _service.Update(update);
        }

        private ActualPluginAssembly FindAssembly(DesiredRegistration desired, RegistrationOptions options)
        {
            var query = new QueryExpression(PluginAssembly.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(PluginAssembly.Fields.PluginAssemblyId, PluginAssembly.Fields.Name)
            };

            if (options.PluginAssemblyId.HasValue)
                query.Criteria.AddCondition(PluginAssembly.Fields.PluginAssemblyId, ConditionOperator.Equal, options.PluginAssemblyId.Value);
            else
                query.Criteria.AddCondition(PluginAssembly.Fields.Name, ConditionOperator.Equal, string.IsNullOrWhiteSpace(options.AssemblyName) ? desired.AssemblyName : options.AssemblyName);

            var entity = _service.RetrieveMultiple(query).Entities.SingleOrDefault()?.ToEntity<PluginAssembly>();
            if (entity == null) throw new InvalidOperationException("Dataverse pluginassembly was not found. Run pac plugin push first or pass --pluginAssemblyId.");

            return new ActualPluginAssembly
            {
                Id = entity.Id,
                Name = entity.Name
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
            var query = new QueryExpression(PluginType.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(PluginType.Fields.PluginTypeId, PluginType.Fields.TypeName)
            };
            query.Criteria.AddCondition(PluginType.Fields.PluginAssemblyId, ConditionOperator.Equal, assemblyId);

            return _service.RetrieveMultiple(query).Entities
                .Select(e =>
                {
                    var pluginType = e.ToEntity<PluginType>();
                    return new ActualPluginType { Id = pluginType.Id, TypeName = pluginType.TypeName };
                })
                .GroupBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        private IReadOnlyCollection<ActualStep> LoadSteps(Guid[] pluginTypeIds, IReadOnlyDictionary<string, ActualPluginType> pluginTypes)
        {
            if (pluginTypeIds.Length == 0) return Array.Empty<ActualStep>();

            var query = new QueryExpression(SdkMessageProcessingStep.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(
                    SdkMessageProcessingStep.Fields.SdkMessageProcessingStepId,
                    SdkMessageProcessingStep.Fields.EventHandler,
                    SdkMessageProcessingStep.Fields.SdkMessageId,
                    SdkMessageProcessingStep.Fields.SdkMessageFilterId,
                    SdkMessageProcessingStep.Fields.Stage,
                    SdkMessageProcessingStep.Fields.Mode,
                    SdkMessageProcessingStep.Fields.Rank,
                    SdkMessageProcessingStep.Fields.FilteringAttributes,
                    SdkMessageProcessingStep.Fields.StateCode,
                    SdkMessageProcessingStep.Fields.IsManaged,
                    SdkMessageProcessingStep.Fields.Description,
                    SdkMessageProcessingStep.Fields.Configuration,
                    SdkMessageProcessingStep.Fields.ImpersonatingUserId)
            };
            query.Criteria.AddCondition(SdkMessageProcessingStep.Fields.EventHandler, ConditionOperator.In, pluginTypeIds.Cast<object>().ToArray());

            var typeById = pluginTypes.Values.ToDictionary(t => t.Id, t => t);
            var steps = new List<ActualStep>();

            foreach (var row in _service.RetrieveMultiple(query).Entities)
            {
                var entity = row.ToEntity<SdkMessageProcessingStep>();
                var pluginTypeRef = entity.EventHandler;
                if (pluginTypeRef == null || !typeById.ContainsKey(pluginTypeRef.Id)) continue;

                var messageRef = entity.SdkMessageId;
                var filterRef = entity.SdkMessageFilterId;
                var filter = filterRef == null ? null : RetrieveFilter(filterRef.Id)?.ToEntity<SdkMessageFilter>();

                steps.Add(new ActualStep
                {
                    Id = entity.Id,
                    PluginTypeId = pluginTypeRef.Id,
                    PluginTypeName = typeById[pluginTypeRef.Id].TypeName,
                    MessageId = messageRef == null ? Guid.Empty : messageRef.Id,
                    MessageName = messageRef == null ? null : RetrieveMessage(messageRef.Id).ToEntity<SdkMessage>().Name,
                    MessageFilterId = filterRef == null ? (Guid?)null : filterRef.Id,
                    EntityLogicalName = filter?.PrimaryObjectTypeCode,
                    Stage = (int?)entity.Stage ?? 0,
                    Mode = (int?)entity.Mode ?? 0,
                    Rank = entity.Rank ?? 1,
                    FilteringAttributes = AttributeList.Parse(entity.FilteringAttributes),
                    StateCode = (int?)entity.StateCode ?? 0,
                    IsManaged = entity.IsManaged ?? false,
                    Description = entity.Description,
                    UnsecureConfiguration = entity.Configuration,
                    ImpersonatingUserId = entity.ImpersonatingUserId
                });
            }

            return steps;
        }

        private IReadOnlyCollection<ActualImage> LoadImages(IReadOnlyCollection<ActualStep> steps)
        {
            if (!steps.Any()) return Array.Empty<ActualImage>();

            var query = new QueryExpression(SdkMessageProcessingStepImage.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(
                    SdkMessageProcessingStepImage.Fields.SdkMessageProcessingStepImageId,
                    SdkMessageProcessingStepImage.Fields.SdkMessageProcessingStepId,
                    SdkMessageProcessingStepImage.Fields.EntityAlias,
                    SdkMessageProcessingStepImage.Fields.ImageType,
                    SdkMessageProcessingStepImage.Fields.MessagePropertyName,
                    SdkMessageProcessingStepImage.Fields.Attributes1)
            };
            query.Criteria.AddCondition(SdkMessageProcessingStepImage.Fields.SdkMessageProcessingStepId, ConditionOperator.In, steps.Select(s => (object)s.Id).ToArray());

            var stepById = steps.ToDictionary(s => s.Id, s => s);
            var images = new List<ActualImage>();
            foreach (var row in _service.RetrieveMultiple(query).Entities)
            {
                var entity = row.ToEntity<SdkMessageProcessingStepImage>();
                var stepRef = entity.SdkMessageProcessingStepId;
                if (stepRef == null || !stepById.ContainsKey(stepRef.Id)) continue;

                images.Add(new ActualImage
                {
                    Id = entity.Id,
                    StepId = stepRef.Id,
                    StepKey = stepById[stepRef.Id].Key,
                    Alias = entity.EntityAlias,
                    ImageType = (int?)entity.ImageType ?? 0,
                    MessagePropertyName = entity.MessagePropertyName,
                    Attributes = AttributeList.Parse(entity.Attributes1)
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
                var entity = new SdkMessageProcessingStep
                {
                    Name = $"{desired.PluginTypeName}: {desired.MessageName} of {desired.EntityLogicalName}",
                    EventHandler = new EntityReference(PluginType.EntityLogicalName, pluginType.Id),
                    SdkMessageId = new EntityReference(SdkMessage.EntityLogicalName, message.Id),
                    SdkMessageFilterId = filter == null ? null : new EntityReference(SdkMessageFilter.EntityLogicalName, filter.Id)
                };
                SetStepFields(entity, desired, runAsUserId);
                var id = _service.Create(entity);
                _createdStepIds[desired.Key] = id;
                return;
            }

            var update = new SdkMessageProcessingStep { Id = change.ActualStep.Id };
            SetStepFields(update, desired, ResolveRunInUserContext(desired.RunInUserContext));
            _service.Update(update);
        }

        private void ApplyImage(RegistrationChange change)
        {
            var desired = change.DesiredImage;
            if (change.Action == RegistrationActionKind.Create)
            {
                var stepId = change.ActualStep?.Id ?? FindStepId(desired.StepKey);
                var entity = new SdkMessageProcessingStepImage
                {
                    SdkMessageProcessingStepId = new EntityReference(SdkMessageProcessingStep.EntityLogicalName, stepId)
                };
                SetImageFields(entity, desired);
                _service.Create(entity);
                return;
            }

            var update = new SdkMessageProcessingStepImage { Id = change.ActualImage.Id };
            SetImageFields(update, desired);
            _service.Update(update);
        }

        private void SetStepFields(SdkMessageProcessingStep entity, DesiredStep desired, Guid? runAsUserId)
        {
            entity.Stage = (sdkmessageprocessingstep_stage)desired.Stage;
            entity.Mode = (sdkmessageprocessingstep_mode)desired.Mode;
            entity.Rank = desired.Rank;
            entity.FilteringAttributes = desired.FilteringAttributes.ToString();
            entity.SupportedDeployment = sdkmessageprocessingstep_supporteddeployment.ServerOnly;
            if (!string.IsNullOrWhiteSpace(desired.Description)) entity.Description = desired.Description;
            entity.ImpersonatingUserId = runAsUserId.HasValue ? new EntityReference("systemuser", runAsUserId.Value) : null;
        }

        private void SetImageFields(SdkMessageProcessingStepImage entity, DesiredImage desired)
        {
            entity.EntityAlias = desired.Alias;
            entity.Name = desired.Alias;
            entity.ImageType = (sdkmessageprocessingstepimage_imagetype)desired.ImageType;
            entity.MessagePropertyName = desired.MessagePropertyName;
            entity.Attributes1 = desired.Attributes.ToString();
        }

        private ActualPluginType LoadPluginTypeByName(string typeName)
        {
            var query = new QueryExpression(PluginType.EntityLogicalName) { ColumnSet = new ColumnSet(PluginType.Fields.PluginTypeId, PluginType.Fields.TypeName) };
            query.Criteria.AddCondition(PluginType.Fields.TypeName, ConditionOperator.Equal, typeName);
            if (_loadedAssemblyId.HasValue)
                query.Criteria.AddCondition(PluginType.Fields.PluginAssemblyId, ConditionOperator.Equal, _loadedAssemblyId.Value);

            var entities = _service.RetrieveMultiple(query).Entities;
            if (entities.Count == 0) throw new InvalidOperationException("plugintype not found for target assembly: " + typeName);
            if (entities.Count > 1) throw new InvalidOperationException("Multiple plugintype rows found for target assembly: " + typeName);

            var entity = entities.Single().ToEntity<PluginType>();
            return new ActualPluginType { Id = entity.Id, TypeName = entity.TypeName };
        }

        private Guid? ResolveRunInUserContext(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Calling User", StringComparison.OrdinalIgnoreCase))
                return null;

            Guid id;
            if (Guid.TryParse(value, out id)) return id;
            if (_optionsUserAliases != null && _optionsUserAliases.TryGetValue(value, out id)) return id;

            throw new InvalidOperationException($"Run in User's Context '{value}' was not found. Use 'Calling User', a systemuserid GUID, or an alias in --userMap.");
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
            cached = _service.Retrieve(SdkMessage.EntityLogicalName, id, new ColumnSet(SdkMessage.Fields.Name));
            _messageCache[key] = cached;
            return cached;
        }

        private Entity RetrieveFilter(Guid id)
        {
            var key = id.ToString("N");
            Entity cached;
            if (_filterCache.TryGetValue(key, out cached)) return cached;
            cached = _service.Retrieve(SdkMessageFilter.EntityLogicalName, id, new ColumnSet(SdkMessageFilter.Fields.PrimaryObjectTypeCode));
            _filterCache[key] = cached;
            return cached;
        }

        private Entity GetMessage(string messageName)
        {
            var key = "name:" + messageName;
            Entity cached;
            if (_messageCache.TryGetValue(key, out cached)) return cached;

            var query = new QueryExpression(SdkMessage.EntityLogicalName) { ColumnSet = new ColumnSet(SdkMessage.Fields.SdkMessageId, SdkMessage.Fields.Name) };
            query.Criteria.AddCondition(SdkMessage.Fields.Name, ConditionOperator.Equal, messageName);
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

            var query = new QueryExpression(SdkMessageFilter.EntityLogicalName) { ColumnSet = new ColumnSet(SdkMessageFilter.Fields.SdkMessageFilterId, SdkMessageFilter.Fields.PrimaryObjectTypeCode) };
            query.Criteria.AddCondition(SdkMessageFilter.Fields.SdkMessageId, ConditionOperator.Equal, messageId);
            query.Criteria.AddCondition(SdkMessageFilter.Fields.PrimaryObjectTypeCode, ConditionOperator.Equal, entityLogicalName);
            cached = _service.RetrieveMultiple(query).Entities.SingleOrDefault();
            if (cached == null) throw new InvalidOperationException($"sdkmessagefilter not found for {entityLogicalName}.");
            _filterCache[key] = cached;
            return cached;
        }
    }
}
