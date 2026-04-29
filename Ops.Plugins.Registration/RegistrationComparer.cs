using System;
using System.Collections.Generic;
using System.Linq;

namespace Ops.Plugins.Registration
{
    public sealed class RegistrationComparer
    {
        private const string PushAssemblyRecommendation = "Re-run with -PushAssembly to update the assembly binary, then review/apply the step changes.";

        public RegistrationPlan ComparePushAssemblyReadiness(DesiredRegistration desired, ActualRegistration actual)
        {
            var desiredPluginTypeNames = new HashSet<string>(
                desired.PluginTypeNamesInAssembly ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var changes = actual.PluginTypes.Values
                .Where(t => !desiredPluginTypeNames.Contains(t.TypeName))
                .OrderBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase)
                .Select(t => BuildStalePluginTypeChange(t, actual))
                .ToArray();

            return new RegistrationPlan(changes);
        }

        public bool CanResolveByPushingAssembly(RegistrationPlan plan)
        {
            if (plan == null || plan.Errors == 0) return false;

            var errors = plan.Changes
                .Where(c => c.Action == RegistrationActionKind.Error)
                .ToArray();

            return errors.Length > 0
                && errors.All(c => c.Target == RegistrationTargetKind.PluginType
                    && c.Detail != null
                    && c.Detail.IndexOf(PushAssemblyRecommendation, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public RegistrationPlan Compare(DesiredRegistration desired, ActualRegistration actual, RegistrationOptions options)
        {
            var changes = new List<RegistrationChange>();
            var actualSteps = actual.Steps
                .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
            var desiredSteps = desired.PluginTypes.SelectMany(t => t.Steps).OrderBy(s => s.PluginTypeName).ThenBy(s => s.MessageName).ThenBy(s => s.EntityLogicalName).ToArray();
            var desiredStepKeys = new HashSet<string>(desiredSteps.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);

            foreach (var desiredStep in desiredSteps)
            {
                ActualPluginType pluginType;
                if (!actual.PluginTypes.TryGetValue(desiredStep.PluginTypeName, out pluginType))
                {
                    changes.Add(Change(RegistrationActionKind.Error, RegistrationTargetKind.PluginType, desiredStep, null, BuildMissingPluginTypeMessage(desiredStep.PluginTypeName, actual, options)));
                    continue;
                }

                ActualStep[] matches;
                actualSteps.TryGetValue(desiredStep.Key, out matches);
                matches = matches ?? Array.Empty<ActualStep>();

                if (matches.Length > 1)
                {
                    changes.Add(Change(RegistrationActionKind.Error, RegistrationTargetKind.Step, desiredStep, matches[0], "Duplicate matching steps found; manual cleanup required before apply."));
                    continue;
                }

                if (matches.Length == 0)
                {
                    matches = FindStageOrModeDrift(actual.Steps, desiredStep, desiredStepKeys).ToArray();
                    if (matches.Length > 1)
                    {
                        changes.Add(Change(RegistrationActionKind.Error, RegistrationTargetKind.Step, desiredStep, matches[0], "Multiple same plug-in/message/entity steps found with different stage or mode; manual cleanup required before apply."));
                        continue;
                    }

                    if (matches.Length == 1)
                    {
                        var driftedStep = matches[0];
                        var drift = $"stage/mode: {FormatStageAndMode(driftedStep.Stage, driftedStep.Mode)} -> {FormatStageAndMode(desiredStep.Stage, desiredStep.Mode)}";
                        changes.Add(Change(RegistrationActionKind.Update, RegistrationTargetKind.Step, desiredStep, driftedStep, drift));
                        AddStepWarnings(changes, desiredStep, driftedStep);
                        AddImageComparison(changes, desiredStep, driftedStep, actual.Images.Where(i => i.StepId == driftedStep.Id).ToArray());
                        continue;
                    }

                    changes.Add(Change(RegistrationActionKind.Create, RegistrationTargetKind.Step, desiredStep, null, "Missing step"));
                    foreach (var image in desiredStep.Images)
                        changes.Add(Change(RegistrationActionKind.Create, RegistrationTargetKind.Image, desiredStep, null, "Missing image " + image.Alias, image, null));
                    continue;
                }

                var actualStep = matches[0];
                AddStepComparison(changes, desiredStep, actualStep, options);
                AddStepWarnings(changes, desiredStep, actualStep);
                AddImageComparison(changes, desiredStep, actualStep, actual.Images.Where(i => i.StepId == actualStep.Id).ToArray());
            }

            foreach (var extraStep in actual.Steps.Where(a => !desiredSteps.Any(d => string.Equals(d.Key, a.Key, StringComparison.OrdinalIgnoreCase))).OrderBy(s => s.PluginTypeName).ThenBy(s => s.MessageName))
                changes.Add(Change(RegistrationActionKind.Extra, RegistrationTargetKind.Step, null, extraStep, "No RegisteredEvent metadata matched this existing step."));

            return new RegistrationPlan(changes);
        }

        private static RegistrationChange BuildStalePluginTypeChange(ActualPluginType pluginType, ActualRegistration actual)
        {
            var steps = actual.Steps
                .Where(s => string.Equals(s.PluginTypeName, pluginType.TypeName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.MessageName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.EntityLogicalName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var stepIds = new HashSet<Guid>(steps.Select(s => s.Id));
            var imageCount = actual.Images.Count(i => stepIds.Contains(i.StepId));
            var enabledCount = steps.Count(s => s.StateCode == 0);
            var managedCount = steps.Count(s => s.IsManaged);
            var stepSummary = string.Join("; ", steps.Select(s =>
                $"{s.MessageName} {s.EntityLogicalName ?? "(none)"} {FormatStageAndMode(s.Stage, s.Mode)}" + (s.StateCode == 0 ? " enabled" : " disabled")));

            var detail = "Plug-in type is registered in Dataverse but is missing from the current DLL. "
                + $"Manual review required before pushing assembly content. Dependent registrations: {steps.Length} step(s), {imageCount} image(s).";

            if (enabledCount > 0)
                detail += $" Enabled step(s): {enabledCount}.";
            if (managedCount > 0)
                detail += $" Managed step(s): {managedCount}.";
            if (!string.IsNullOrWhiteSpace(stepSummary))
                detail += " Steps: " + stepSummary + ".";
            detail += " Remove or retire this stale plug-in type and its dependent registrations in Dataverse, then rerun -PushAssembly.";

            return new RegistrationChange
            {
                Action = RegistrationActionKind.Error,
                Target = RegistrationTargetKind.PluginType,
                PluginTypeName = pluginType.TypeName,
                Detail = detail
            };
        }

        private static string FormatStageAndMode(int stage, int mode)
        {
            return FormatStage(stage) + " " + FormatMode(mode);
        }

        private static string FormatStage(int stage)
        {
            switch (stage)
            {
                case 10:
                    return "PreValidation";
                case 20:
                    return "PreOperation";
                case 40:
                    return "PostOperation";
                default:
                    return "stage " + stage;
            }
        }

        private static string FormatMode(int mode)
        {
            switch (mode)
            {
                case 0:
                    return "synchronous";
                case 1:
                    return "asynchronous";
                default:
                    return "mode " + mode;
            }
        }

        private static string BuildMissingPluginTypeMessage(string desiredPluginTypeName, ActualRegistration actual, RegistrationOptions options)
        {
            var existingTypeNames = actual.PluginTypes.Keys
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var detail = "Plug-in type not found under existing pluginassembly '" + actual.Assembly.Name + "'.";
            if (existingTypeNames.Length > 0)
            {
                detail += " Existing type(s): " + string.Join(", ", existingTypeNames) + ".";
                detail += " This usually means Dataverse has an older DLL for this assembly row.";
            }
            else
            {
                detail += " No plug-in types are currently registered for this assembly row.";
            }

            if (existingTypeNames.Length == 0)
            {
                var assemblyPath = string.IsNullOrWhiteSpace(options.AssemblyPath)
                    ? "Ops.Plugins/bin/Release/net462/Ops.Plugins.dll"
                    : options.AssemblyPath;

                return detail + " Register the plug-in type first with:"
                    + $" pac plugin push --pluginId {actual.Assembly.Id:D} --pluginFile {assemblyPath} --type Assembly"
                    + ". Then rerun the sync.";
            }

            if (options.PushAssembly)
                return detail + " The DLL was already pushed in this run; confirm the type is public, implements IPlugin, and the pushed DLL is the expected build.";

            return detail + " " + PushAssemblyRecommendation;
        }

        private static IEnumerable<ActualStep> FindStageOrModeDrift(IEnumerable<ActualStep> actualSteps, DesiredStep desiredStep, ISet<string> desiredStepKeys)
        {
            return actualSteps.Where(s =>
                string.Equals(s.PluginTypeName, desiredStep.PluginTypeName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.MessageName, desiredStep.MessageName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.EntityLogicalName ?? string.Empty, desiredStep.EntityLogicalName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && (s.Stage != desiredStep.Stage || s.Mode != desiredStep.Mode)
                && !desiredStepKeys.Contains(s.Key));
        }

        private static void AddStepComparison(List<RegistrationChange> changes, DesiredStep desired, ActualStep actual, RegistrationOptions options)
        {
            var diffs = new List<string>();
            var desiredName = RegistrationStepNames.For(desired);
            if (!string.Equals(desiredName, actual.Name, StringComparison.Ordinal))
                diffs.Add($"name: \"{actual.Name}\" -> \"{desiredName}\"");
            if (desired.Rank != actual.Rank) diffs.Add($"rank: {actual.Rank} -> {desired.Rank}");
            if (!desired.FilteringAttributes.SetEquals(actual.FilteringAttributes))
                diffs.Add($"filteringattributes: \"{actual.FilteringAttributes}\" -> \"{desired.FilteringAttributes}\"");
            if (!SameRunInUserContext(desired.RunInUserContext, actual.ImpersonatingUserId, options))
                diffs.Add($"run in user's context: \"{RunInUserLabel(actual.ImpersonatingUserId)}\" -> \"{RunInUserLabel(desired.RunInUserContext, options)}\"");
            if (!string.IsNullOrWhiteSpace(desired.Description) && !string.Equals(desired.Description, actual.Description, StringComparison.Ordinal))
                diffs.Add($"description: \"{actual.Description}\" -> \"{desired.Description}\"");

            changes.Add(Change(
                diffs.Any() ? RegistrationActionKind.Update : RegistrationActionKind.Ok,
                RegistrationTargetKind.Step,
                desired,
                actual,
                diffs.Any() ? string.Join("; ", diffs) : "Step matches"));
        }

        private static void AddStepWarnings(List<RegistrationChange> changes, DesiredStep desired, ActualStep actual)
        {
            if (actual.StateCode != 0)
                changes.Add(Change(RegistrationActionKind.Warning, RegistrationTargetKind.Step, desired, actual, "Existing matching step is disabled; --apply will not enable it."));
            if (actual.IsManaged)
                changes.Add(Change(RegistrationActionKind.Warning, RegistrationTargetKind.Step, desired, actual, "Existing matching step is managed; Dataverse may reject updates."));
            if (!string.IsNullOrWhiteSpace(actual.UnsecureConfiguration))
                changes.Add(Change(RegistrationActionKind.Warning, RegistrationTargetKind.Step, desired, actual, "Existing step has unsecure configuration; sync will preserve it."));
            if (actual.ImpersonatingUserId != null)
                changes.Add(Change(RegistrationActionKind.Warning, RegistrationTargetKind.Step, desired, actual, "Existing step has impersonation configured."));
        }

        private static bool SameRunInUserContext(string desired, Microsoft.Xrm.Sdk.EntityReference actual, RegistrationOptions options)
        {
            if (string.IsNullOrWhiteSpace(desired) || string.Equals(desired, "Calling User", StringComparison.OrdinalIgnoreCase))
                return actual == null;

            Guid desiredId;
            if (options.UserAliases != null && options.UserAliases.TryGetValue(desired, out desiredId))
                return actual != null && actual.Id == desiredId;

            return Guid.TryParse(desired, out desiredId) && actual != null && actual.Id == desiredId;
        }

        private static string RunInUserLabel(Microsoft.Xrm.Sdk.EntityReference actual)
        {
            return actual == null ? "Calling User" : actual.Id.ToString("D");
        }

        private static string RunInUserLabel(string desired, RegistrationOptions options)
        {
            RunInUserContextReference reference;
            if (!string.IsNullOrWhiteSpace(desired)
                && options.UserReferences != null
                && options.UserReferences.TryGetValue(desired, out reference))
                return reference.DisplayName;

            return string.IsNullOrWhiteSpace(desired) ? "Calling User" : desired;
        }

        private static void AddImageComparison(List<RegistrationChange> changes, DesiredStep desiredStep, ActualStep actualStep, IReadOnlyCollection<ActualImage> actualImages)
        {
            var actualByKey = actualImages.GroupBy(i => ImageKey(i.Alias, i.ImageType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

            foreach (var desiredImage in desiredStep.Images.OrderBy(i => i.ImageType).ThenBy(i => i.Alias))
            {
                ActualImage[] matches;
                actualByKey.TryGetValue(ImageKey(desiredImage.Alias, desiredImage.ImageType), out matches);
                matches = matches ?? Array.Empty<ActualImage>();

                if (matches.Length == 0)
                {
                    changes.Add(Change(RegistrationActionKind.Create, RegistrationTargetKind.Image, desiredStep, actualStep, "Missing image " + desiredImage.Alias, desiredImage, null));
                    continue;
                }

                if (matches.Length > 1)
                {
                    changes.Add(Change(RegistrationActionKind.Error, RegistrationTargetKind.Image, desiredStep, actualStep, "Duplicate matching images found; manual cleanup required before apply.", desiredImage, matches[0]));
                    continue;
                }

                var actualImage = matches[0];
                var diffs = new List<string>();
                if (!string.Equals(desiredImage.MessagePropertyName, actualImage.MessagePropertyName, StringComparison.OrdinalIgnoreCase))
                    diffs.Add($"messagepropertyname: \"{actualImage.MessagePropertyName}\" -> \"{desiredImage.MessagePropertyName}\"");
                if (!desiredImage.Attributes.SetEquals(actualImage.Attributes))
                    diffs.Add($"{desiredImage.Alias} attributes: \"{actualImage.Attributes}\" -> \"{desiredImage.Attributes}\"");

                changes.Add(Change(
                    diffs.Any() ? RegistrationActionKind.Update : RegistrationActionKind.Ok,
                    RegistrationTargetKind.Image,
                    desiredStep,
                    actualStep,
                    diffs.Any() ? string.Join("; ", diffs) : desiredImage.Alias + " attributes match",
                    desiredImage,
                    actualImage));
            }

            var desiredImageKeys = desiredStep.Images.Select(i => ImageKey(i.Alias, i.ImageType)).ToArray();
            foreach (var extra in actualImages.Where(i => !desiredImageKeys.Any(k => string.Equals(k, ImageKey(i.Alias, i.ImageType), StringComparison.OrdinalIgnoreCase))))
                changes.Add(Change(RegistrationActionKind.Extra, RegistrationTargetKind.Image, desiredStep, actualStep, "Extra image " + extra.Alias, null, extra));
        }

        private static string ImageKey(string alias, int imageType)
        {
            return (alias ?? string.Empty).Trim().ToLowerInvariant() + "|" + imageType;
        }

        private static RegistrationChange Change(RegistrationActionKind action, RegistrationTargetKind target, DesiredStep desired, ActualStep actual, string detail, DesiredImage desiredImage = null, ActualImage actualImage = null)
        {
            return new RegistrationChange
            {
                Action = action,
                Target = target,
                PluginTypeName = desired?.PluginTypeName ?? actual?.PluginTypeName ?? desiredImage?.PluginTypeName,
                MessageName = desired?.MessageName ?? actual?.MessageName ?? desiredImage?.MessageName,
                EntityLogicalName = desired?.EntityLogicalName ?? actual?.EntityLogicalName ?? desiredImage?.EntityLogicalName,
                Detail = detail,
                DesiredStep = desired,
                ActualStep = actual,
                DesiredImage = desiredImage,
                ActualImage = actualImage
            };
        }
    }
}
