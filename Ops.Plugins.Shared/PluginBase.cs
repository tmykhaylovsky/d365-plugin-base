using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace Ops.Plugins.Shared
{
    // Target: .NET Framework 4.6.2 | Microsoft.CrmSdk.CoreAssemblies 9.0.2.59
    // Synthesized from: delegateas/XrmPluginCore, rappen/JonasPluginBase, DynamicsValue/fake-xrm-easy
    public abstract class PluginBase : IPlugin
    {
        private readonly string _unsecureConfig;
        private readonly string _secureConfig;

        // Parameterless constructor required by Dataverse when no config is registered on the step
        protected PluginBase() { }

        // Constructor invoked by Dataverse when unsecure/secure config is set on the step registration
        protected PluginBase(string unsecureConfig, string secureConfig)
        {
            _unsecureConfig = unsecureConfig;
            _secureConfig = secureConfig;
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            var context = new LocalPluginContext(serviceProvider, GetType().Name, _unsecureConfig, _secureConfig);

            context.Logger.Trace(TraceLevel.Verbose, () =>
                $"Enter | Correlation: {context.ExecutionContext.CorrelationId:N} | Message: {context.ExecutionContext.MessageName} | Entity: {context.ExecutionContext.PrimaryEntityName} | Stage: {context.ExecutionContext.Stage} | Depth: {context.ExecutionContext.Depth}");

            try
            {
                var registeredEvents = GetRegisteredEvents().ToList();
                var registeredEvent = registeredEvents.FirstOrDefault(e => e.Matches(context.ExecutionContext));
                if (registeredEvent == null && registeredEvents.Count > 0)
                {
                    context.Trace(
                        $"No registered event found | Message: {context.ExecutionContext.MessageName} | Entity: {context.ExecutionContext.PrimaryEntityName} | Stage: {context.ExecutionContext.Stage} | Mode: {context.ExecutionContext.Mode}",
                        TraceLevel.Info);
                    return;
                }

                if (registeredEvent != null && !registeredEvent.HasRequiredImages(context.ExecutionContext))
                {
                    context.Trace(
                        $"Registered event missing required pre-image '{registeredEvent.RequiredPreImageName}' | Message: {context.ExecutionContext.MessageName} | Entity: {context.ExecutionContext.PrimaryEntityName} | Stage: {context.ExecutionContext.Stage} | Mode: {context.ExecutionContext.Mode}",
                        TraceLevel.Info);
                    return;
                }

                if (registeredEvent != null && !registeredEvent.HasRequiredPostImage(context.ExecutionContext))
                {
                    context.Trace(
                        $"Registered event missing required post-image '{registeredEvent.RequiredPostImageName}' | Message: {context.ExecutionContext.MessageName} | Entity: {context.ExecutionContext.PrimaryEntityName} | Stage: {context.ExecutionContext.Stage} | Mode: {context.ExecutionContext.Mode}",
                        TraceLevel.Info);
                    return;
                }

                TraceRegistrationDiagnostics(context, registeredEvent);

                if (registeredEvent?.Execute != null)
                    registeredEvent.Execute(context);
                else
                    ExecutePlugin(context);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, $"Unhandled exception in {GetType().Name}");
                throw new InvalidPluginExecutionException($"An error occurred in {GetType().Name}: {ex.Message}", ex);
            }
            finally
            {
                context.Logger.Trace(TraceLevel.Verbose, "Exit");
            }
        }

        protected virtual void ExecutePlugin(LocalPluginContext context)
        {
            throw new InvalidPluginExecutionException(
                $"{GetType().Name} did not match a registered event and does not override ExecutePlugin.");
        }

        protected virtual IEnumerable<RegisteredEvent> GetRegisteredEvents()
        {
            return Enumerable.Empty<RegisteredEvent>();
        }

        private static void TraceRegistrationDiagnostics(LocalPluginContext context, RegisteredEvent registeredEvent)
        {
            if (context == null || registeredEvent == null) return;

            TraceFilteringAttributeDiagnostics(context, registeredEvent);
            TraceImageAttributeDiagnostics(context, PluginImageNames.PreImage, registeredEvent.RequiredPreImageName, registeredEvent.PreImageAttributes, context.ExecutionContext.PreEntityImages);
            TraceImageAttributeDiagnostics(context, PluginImageNames.PostImage, registeredEvent.RequiredPostImageName, registeredEvent.PostImageAttributes, context.ExecutionContext.PostEntityImages);
        }

        private static void TraceFilteringAttributeDiagnostics(LocalPluginContext context, RegisteredEvent registeredEvent)
        {
            if (!context.IsMessage(Messages.Update) || !registeredEvent.FilteringAttributes.Any()) return;

            var target = context.GetTarget();
            if (target == null) return;

            var missingAttributes = GetMissingAttributes(registeredEvent.FilteringAttributes, target.Attributes, requireAllExpectedAttributes: false);
            if (!missingAttributes.Any()) return;

            context.Trace(
                $"Registered event expected Update filtering attributes [{FormatList(missingAttributes)}], but Target did not contain any of them. Confirm the step filtering attributes include these logical names if this event should only run for targeted changes.",
                TraceLevel.Info);
        }

        private static void TraceImageAttributeDiagnostics(LocalPluginContext context, string imageLabel, string imageName, IReadOnlyCollection<string> expectedAttributes, EntityImageCollection images)
        {
            if (string.IsNullOrWhiteSpace(imageName) || expectedAttributes == null || !expectedAttributes.Any()) return;
            if (images == null || !images.TryGetValue(imageName, out var image) || image == null) return;

            var missingAttributes = GetMissingAttributes(expectedAttributes, image.Attributes, requireAllExpectedAttributes: true);
            if (!missingAttributes.Any()) return;

            context.Trace(
                $"Registered event {imageLabel} '{imageName}' did not contain expected attributes [{FormatList(missingAttributes)}]. They may be missing from the step image configuration, or selected but absent because the Dataverse value is null.",
                TraceLevel.Verbose);
        }

        private static IReadOnlyCollection<string> GetMissingAttributes(IReadOnlyCollection<string> expectedAttributes, AttributeCollection actualAttributes, bool requireAllExpectedAttributes)
        {
            if (expectedAttributes == null || !expectedAttributes.Any()) return Array.Empty<string>();
            if (actualAttributes == null) return expectedAttributes.ToArray();

            var missingAttributes = expectedAttributes.Where(a => !actualAttributes.ContainsKey(a)).ToArray();

            if (requireAllExpectedAttributes) return missingAttributes;
            return missingAttributes.Length == expectedAttributes.Count ? expectedAttributes.ToArray() : Array.Empty<string>();
        }

        private static string FormatList(IEnumerable<string> values) =>
            string.Join(", ", values ?? Enumerable.Empty<string>());

        public sealed class LocalPluginContext
        {
            public IPluginExecutionContext ExecutionContext { get; }

            // System-level service — runs as the plugin-registered user
            public IOrganizationService OrganizationService { get; }

            // User-level service — runs as the initiating user; CLS and security rules apply
            public IOrganizationService InitiatingUserService { get; }

            public PluginLogger Logger { get; }
            public string UnsecureConfig { get; }
            public string SecureConfig { get; }

            private readonly string _pluginName;

            public LocalPluginContext(IServiceProvider serviceProvider, string pluginName, string unsecureConfig, string secureConfig)
            {
                _pluginName = pluginName;
                ExecutionContext = serviceProvider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext
                    ?? throw new InvalidPluginExecutionException("IPluginExecutionContext not available.");

                var factory = serviceProvider.GetService(typeof(IOrganizationServiceFactory)) as IOrganizationServiceFactory
                    ?? throw new InvalidPluginExecutionException("IOrganizationServiceFactory not available.");

                OrganizationService        = factory.CreateOrganizationService(ExecutionContext.UserId);
                InitiatingUserService      = factory.CreateOrganizationService(ExecutionContext.InitiatingUserId);
                UnsecureConfig             = unsecureConfig;
                SecureConfig               = secureConfig;
                Logger                     = new PluginLogger(serviceProvider, pluginName, ExecutionContext.CorrelationId);
            }

            // --- Target ---

            public Entity GetTarget() =>
                ExecutionContext.InputParameters.TryGetValue(ParameterNames.Target, out var raw) && raw is Entity e ? e : null;

            public EntityReference GetTargetReference() =>
                ExecutionContext.InputParameters.TryGetValue(ParameterNames.Target, out var raw) && raw is EntityReference r ? r : null;

            // --- Images ---

            public T GetPreImage<T>(string imageName = PluginImageNames.PreImage) where T : Entity =>
                ExecutionContext.PreEntityImages.TryGetValue(imageName, out var img) ? img.ToEntity<T>() : null;

            public T GetPostImage<T>(string imageName = PluginImageNames.PostImage) where T : Entity =>
                ExecutionContext.PostEntityImages.TryGetValue(imageName, out var img) ? img.ToEntity<T>() : null;

            // Returns true if the attribute is present in the target AND its value differs from the pre-image.
            // Returns true when there is no pre-image (first-time set).
            public bool HasChangedAttribute(string logicalName, string preImageName = PluginImageNames.PreImage)
            {
                var target = GetTarget();
                if (target == null || !target.Contains(logicalName)) return false;
                if (!ExecutionContext.PreEntityImages.TryGetValue(preImageName, out var pre)) return true;
                return !Equals(pre.Contains(logicalName) ? pre[logicalName] : null, target[logicalName]);
            }

            // --- Shared variables ---
            // Serializable types only. PreValidation vars are on ParentContext for Pre/PostOperation stages.

            public T GetSharedVariable<T>(string key)
            {
                if (ExecutionContext.SharedVariables.TryGetValue(key, out var v) && v is T t) return t;
                if (ExecutionContext.ParentContext?.SharedVariables.TryGetValue(key, out var pv) == true && pv is T pt) return pt;
                return default;
            }

            public void SetSharedVariable<T>(string key, T value) =>
                ExecutionContext.SharedVariables[key] = value;

            // Null for plugins not triggered by a parent pipeline operation
            public IPluginExecutionContext ParentContext => ExecutionContext.ParentContext;

            // --- Custom API ---

            public bool IsCustomApi => !ExecutionContext.InputParameters.Contains(ParameterNames.Target);

            public T GetInputParameter<T>(string name) =>
                ExecutionContext.InputParameters.TryGetValue(name, out var v) && v is T t ? t : default;

            public bool TryGetInputParameter<T>(string name, out T value)
            {
                if (ExecutionContext.InputParameters.TryGetValue(name, out var v) && v is T t)
                {
                    value = t;
                    return true;
                }
                value = default;
                return false;
            }

            public void SetOutputParameter(string name, object value) =>
                ExecutionContext.OutputParameters[name] = value;

            // --- Recursive execution guard ---
            // Returns true on the second (or later) call for the same plugin+message+stage+entity+mode
            // within the same pipeline. Call at the top of ExecutePlugin and return immediately if true.
            public bool IsRecursiveExecution()
            {
                var key = $"{_pluginName}|{ExecutionContext.MessageName}|{ExecutionContext.Stage}|{ExecutionContext.PrimaryEntityId}|{ExecutionContext.Mode}";
                if (ExecutionContext.SharedVariables.TryGetValue(key, out var v) && v is bool b && b) return true;
                ExecutionContext.SharedVariables[key] = true;
                return false;
            }

            // --- IOrganizationService Shortcuts (Defaults to OrganizationService) ---

            public Guid Create(Entity entity) => OrganizationService.Create(entity);
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => OrganizationService.Retrieve(entityName, id, columnSet);
            public T Retrieve<T>(string entityName, Guid id, ColumnSet columnSet) where T : Entity => OrganizationService.Retrieve(entityName, id, columnSet).ToEntity<T>();
            public void Update(Entity entity) => OrganizationService.Update(entity);
            public void Delete(string entityName, Guid id) => OrganizationService.Delete(entityName, id);
            public OrganizationResponse Execute(OrganizationRequest request) => OrganizationService.Execute(request);
            public EntityCollection RetrieveMultiple(QueryBase query) => OrganizationService.RetrieveMultiple(query);
            public IEnumerable<T> RetrieveMultiple<T>(QueryBase query) where T : Entity => OrganizationService.RetrieveMultiple(query).Entities.Select(e => e.ToEntity<T>());
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => OrganizationService.Associate(entityName, entityId, relationship, relatedEntities);
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => OrganizationService.Disassociate(entityName, entityId, relationship, relatedEntities);


            // --- Stage / message guards ---

            public bool IsMessage(string messageName) =>
                string.Equals(ExecutionContext.MessageName, messageName, StringComparison.OrdinalIgnoreCase);

            public bool IsStage(PluginStage stage) => ExecutionContext.Stage == (int)stage;

            public bool IsPreValidation => ExecutionContext.Stage == (int)PluginStage.PreValidation;
            public bool IsPreOperation  => ExecutionContext.Stage == (int)PluginStage.PreOperation;
            public bool IsPostOperation => ExecutionContext.Stage == (int)PluginStage.PostOperation;
        }

        public enum PluginStage
        {
            PreValidation = 10,
            PreOperation  = 20,
            PostOperation = 40
        }
    }
}
