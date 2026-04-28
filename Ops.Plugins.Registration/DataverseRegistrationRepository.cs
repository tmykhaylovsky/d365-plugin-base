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
            var update = new Entity("pluginassembly", pluginAssemblyId);
            update["content"] = Convert.ToBase64String(File.ReadAllBytes(assemblyPath));
            update["version"] = assemblyName.Version?.ToString();
            update["publickeytoken"] = PublicKeyTokenToString(assemblyName.GetPublicKeyToken());
            update["culture"] = string.IsNullOrWhiteSpace(assemblyName.CultureName) ? "neutral" : assemblyName.CultureName;
            _service.Update(update);
        }

        private ActualPluginAssembly FindAssembly(DesiredRegistration desired, RegistrationOptions options)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid", "name")
            };

            if (options.PluginAssemblyId.HasValue)
                query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, options.PluginAssemblyId.Value);
            else
                query.Criteria.AddCondition("name", ConditionOperator.Equal, string.IsNullOrWhiteSpace(options.AssemblyName) ? desired.AssemblyName : options.AssemblyName);

            var entity = _service.RetrieveMultiple(query).Entities.SingleOrDefault();
            if (entity == null) throw new InvalidOperationException("Dataverse pluginassembly was not found. Run pac plugin push first or pass --pluginAssemblyId.");

            return new ActualPluginAssembly
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("name")
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
            var query = new QueryExpression("plugintype")
            {
                ColumnSet = new ColumnSet("plugintypeid", "typename")
            };
            query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);

            return _service.RetrieveMultiple(query).Entities
                .Select(e => new ActualPluginType { Id = e.Id, TypeName = e.GetAttributeValue<string>("typename") })
                .GroupBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        private IReadOnlyCollection<ActualStep> LoadSteps(Guid[] pluginTypeIds, IReadOnlyDictionary<string, ActualPluginType> pluginTypes)
        {
            if (pluginTypeIds.Length == 0) return Array.Empty<ActualStep>();

            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet(
                    "sdkmessageprocessingstepid",
                    "eventhandler",
                    "sdkmessageid",
                    "sdkmessagefilterid",
                    "stage",
                    "mode",
                    "rank",
                    "filteringattributes",
                    "statecode",
                    "ismanaged",
                    "description",
                    "configuration",
                    "impersonatinguserid")
            };
            query.Criteria.AddCondition("eventhandler", ConditionOperator.In, pluginTypeIds.Cast<object>().ToArray());

            var typeById = pluginTypes.Values.ToDictionary(t => t.Id, t => t);
            var steps = new List<ActualStep>();

            foreach (var entity in _service.RetrieveMultiple(query).Entities)
            {
                var pluginTypeRef = entity.GetAttributeValue<EntityReference>("eventhandler");
                if (pluginTypeRef == null || !typeById.ContainsKey(pluginTypeRef.Id)) continue;

                var messageRef = entity.GetAttributeValue<EntityReference>("sdkmessageid");
                var filterRef = entity.GetAttributeValue<EntityReference>("sdkmessagefilterid");
                var filter = filterRef == null ? null : RetrieveFilter(filterRef.Id);

                steps.Add(new ActualStep
                {
                    Id = entity.Id,
                    PluginTypeId = pluginTypeRef.Id,
                    PluginTypeName = typeById[pluginTypeRef.Id].TypeName,
                    MessageId = messageRef == null ? Guid.Empty : messageRef.Id,
                    MessageName = messageRef == null ? null : RetrieveMessage(messageRef.Id).GetAttributeValue<string>("name"),
                    MessageFilterId = filterRef == null ? (Guid?)null : filterRef.Id,
                    EntityLogicalName = filter?.GetAttributeValue<string>("primaryobjecttypecode"),
                    Stage = entity.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0,
                    Mode = entity.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0,
                    Rank = entity.GetAttributeValue<int?>("rank") ?? 1,
                    FilteringAttributes = AttributeList.Parse(entity.GetAttributeValue<string>("filteringattributes")),
                    StateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0,
                    IsManaged = entity.GetAttributeValue<bool?>("ismanaged") ?? false,
                    Description = entity.GetAttributeValue<string>("description"),
                    UnsecureConfiguration = entity.GetAttributeValue<string>("configuration"),
                    ImpersonatingUserId = entity.GetAttributeValue<EntityReference>("impersonatinguserid")
                });
            }

            return steps;
        }

        private IReadOnlyCollection<ActualImage> LoadImages(IReadOnlyCollection<ActualStep> steps)
        {
            if (!steps.Any()) return Array.Empty<ActualImage>();

            var query = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet("sdkmessageprocessingstepimageid", "sdkmessageprocessingstepid", "entityalias", "imagetype", "messagepropertyname", "attributes")
            };
            query.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.In, steps.Select(s => (object)s.Id).ToArray());

            var stepById = steps.ToDictionary(s => s.Id, s => s);
            var images = new List<ActualImage>();
            foreach (var entity in _service.RetrieveMultiple(query).Entities)
            {
                var stepRef = entity.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid");
                if (stepRef == null || !stepById.ContainsKey(stepRef.Id)) continue;

                images.Add(new ActualImage
                {
                    Id = entity.Id,
                    StepId = stepRef.Id,
                    StepKey = stepById[stepRef.Id].Key,
                    Alias = entity.GetAttributeValue<string>("entityalias"),
                    ImageType = entity.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0,
                    MessagePropertyName = entity.GetAttributeValue<string>("messagepropertyname"),
                    Attributes = AttributeList.Parse(entity.GetAttributeValue<string>("attributes"))
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
                var entity = new Entity("sdkmessageprocessingstep");
                entity["name"] = $"{desired.PluginTypeName}: {desired.MessageName} of {desired.EntityLogicalName}";
                entity["eventhandler"] = new EntityReference("plugintype", pluginType.Id);
                entity["sdkmessageid"] = new EntityReference("sdkmessage", message.Id);
                if (filter != null) entity["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filter.Id);
                SetStepFields(entity, desired, runAsUserId);
                var id = _service.Create(entity);
                _createdStepIds[desired.Key] = id;
                return;
            }

            var update = new Entity("sdkmessageprocessingstep", change.ActualStep.Id);
            SetStepFields(update, desired, ResolveRunInUserContext(desired.RunInUserContext));
            _service.Update(update);
        }

        private void ApplyImage(RegistrationChange change)
        {
            var desired = change.DesiredImage;
            if (change.Action == RegistrationActionKind.Create)
            {
                var stepId = change.ActualStep?.Id ?? FindStepId(desired.StepKey);
                var entity = new Entity("sdkmessageprocessingstepimage");
                entity["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId);
                SetImageFields(entity, desired);
                _service.Create(entity);
                return;
            }

            var update = new Entity("sdkmessageprocessingstepimage", change.ActualImage.Id);
            SetImageFields(update, desired);
            _service.Update(update);
        }

        private void SetStepFields(Entity entity, DesiredStep desired, Guid? runAsUserId)
        {
            entity["stage"] = new OptionSetValue(desired.Stage);
            entity["mode"] = new OptionSetValue(desired.Mode);
            entity["rank"] = desired.Rank;
            entity["filteringattributes"] = desired.FilteringAttributes.ToString();
            entity["supporteddeployment"] = new OptionSetValue(0);
            if (!string.IsNullOrWhiteSpace(desired.Description)) entity["description"] = desired.Description;
            entity["impersonatinguserid"] = runAsUserId.HasValue ? new EntityReference("systemuser", runAsUserId.Value) : null;
        }

        private void SetImageFields(Entity entity, DesiredImage desired)
        {
            entity["entityalias"] = desired.Alias;
            entity["name"] = desired.Alias;
            entity["imagetype"] = new OptionSetValue(desired.ImageType);
            entity["messagepropertyname"] = desired.MessagePropertyName;
            entity["attributes"] = desired.Attributes.ToString();
        }

        private ActualPluginType LoadPluginTypeByName(string typeName)
        {
            var query = new QueryExpression("plugintype") { ColumnSet = new ColumnSet("plugintypeid", "typename") };
            query.Criteria.AddCondition("typename", ConditionOperator.Equal, typeName);
            if (_loadedAssemblyId.HasValue)
                query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, _loadedAssemblyId.Value);

            var entities = _service.RetrieveMultiple(query).Entities;
            if (entities.Count == 0) throw new InvalidOperationException("plugintype not found for target assembly: " + typeName);
            if (entities.Count > 1) throw new InvalidOperationException("Multiple plugintype rows found for target assembly: " + typeName);

            var entity = entities.Single();
            return new ActualPluginType { Id = entity.Id, TypeName = entity.GetAttributeValue<string>("typename") };
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
            cached = _service.Retrieve("sdkmessage", id, new ColumnSet("name"));
            _messageCache[key] = cached;
            return cached;
        }

        private Entity RetrieveFilter(Guid id)
        {
            var key = id.ToString("N");
            Entity cached;
            if (_filterCache.TryGetValue(key, out cached)) return cached;
            cached = _service.Retrieve("sdkmessagefilter", id, new ColumnSet("primaryobjecttypecode"));
            _filterCache[key] = cached;
            return cached;
        }

        private Entity GetMessage(string messageName)
        {
            var key = "name:" + messageName;
            Entity cached;
            if (_messageCache.TryGetValue(key, out cached)) return cached;

            var query = new QueryExpression("sdkmessage") { ColumnSet = new ColumnSet("sdkmessageid", "name") };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, messageName);
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

            var query = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet("sdkmessagefilterid", "primaryobjecttypecode") };
            query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
            query.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, entityLogicalName);
            cached = _service.RetrieveMultiple(query).Entities.SingleOrDefault();
            if (cached == null) throw new InvalidOperationException($"sdkmessagefilter not found for {entityLogicalName}.");
            _filterCache[key] = cached;
            return cached;
        }
    }
}
