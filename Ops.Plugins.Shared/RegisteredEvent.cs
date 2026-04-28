using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace Ops.Plugins.Shared
{
    public sealed class RegisteredEvent
    {
        public const string CallingUser = "Calling User";

        public RegisteredEvent(
            PluginBase.PluginStage stage,
            SdkMessageProcessingStepMode mode,
            string messageName,
            string entityLogicalName,
            Action<PluginBase.LocalPluginContext> execute = null,
            string requiredPreImageName = null,
            string requiredPostImageName = null,
            IEnumerable<string> filteringAttributes = null,
            IEnumerable<string> preImageAttributes = null,
            IEnumerable<string> postImageAttributes = null,
            int rank = 1,
            string runInUserContext = CallingUser,
            string stepDescription = null)
        {
            Stage = stage;
            Mode = mode;
            MessageName = messageName ?? throw new ArgumentNullException(nameof(messageName));
            EntityLogicalName = entityLogicalName;
            Execute = execute;
            RequiredPreImageName = requiredPreImageName;
            RequiredPostImageName = requiredPostImageName;
            FilteringAttributes = NormalizeAttributes(filteringAttributes);
            PreImageAttributes = NormalizeAttributes(preImageAttributes);
            PostImageAttributes = NormalizeAttributes(postImageAttributes);
            Rank = rank;
            RunInUserContext = string.IsNullOrWhiteSpace(runInUserContext) ? CallingUser : runInUserContext.Trim();
            StepDescription = string.IsNullOrWhiteSpace(stepDescription) ? null : stepDescription.Trim();
        }

        public PluginBase.PluginStage Stage { get; }
        public SdkMessageProcessingStepMode Mode { get; }
        public string MessageName { get; }
        public string EntityLogicalName { get; }
        public Action<PluginBase.LocalPluginContext> Execute { get; }
        public string RequiredPreImageName { get; }
        public string RequiredPostImageName { get; }
        public IReadOnlyCollection<string> FilteringAttributes { get; }
        public IReadOnlyCollection<string> PreImageAttributes { get; }
        public IReadOnlyCollection<string> PostImageAttributes { get; }
        public int Rank { get; }
        public string RunInUserContext { get; }
        public string StepDescription { get; }

        public bool Matches(IPluginExecutionContext context)
        {
            return context != null
                && (int)Stage == context.Stage
                && ((int)Mode == context.Mode || Mode == SdkMessageProcessingStepMode.CustomApi)
                && string.Equals(MessageName, context.MessageName, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(EntityLogicalName)
                    || string.Equals(EntityLogicalName, context.PrimaryEntityName, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasRequiredImages(IPluginExecutionContext context)
        {
            return string.IsNullOrWhiteSpace(RequiredPreImageName)
                || context?.PreEntityImages?.ContainsKey(RequiredPreImageName) == true;
        }

        public bool HasRequiredPostImage(IPluginExecutionContext context)
        {
            return string.IsNullOrWhiteSpace(RequiredPostImageName)
                || context?.PostEntityImages?.ContainsKey(RequiredPostImageName) == true;
        }

        private static IReadOnlyCollection<string> NormalizeAttributes(IEnumerable<string> attributes)
        {
            if (attributes == null) return Array.Empty<string>();

            return attributes
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
