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
                ExecutionContext.InputParameters.TryGetValue("Target", out var raw) && raw is Entity e ? e : null;

            public EntityReference GetTargetReference() =>
                ExecutionContext.InputParameters.TryGetValue("Target", out var raw) && raw is EntityReference r ? r : null;

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

            public bool IsCustomApi => !ExecutionContext.InputParameters.Contains("Target");

            public T GetInputParameter<T>(string name) =>
                ExecutionContext.InputParameters.TryGetValue(name, out var v) && v is T t ? t : default;

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
