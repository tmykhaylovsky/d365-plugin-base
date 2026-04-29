extern alias PluginAssembly;

using System;
using System.Collections.Generic;
using System.Linq;
using Ops.Plugins.Registration;
using Ops.Plugins.Shared;
using PluginAssembly::Ops.Plugins;
using PluginAssembly::Ops.Plugins.Model;
using AccountFields = PluginAssembly::Ops.Plugins.Model.Account.Fields;
using Xunit;

namespace Ops.Plugins.Testing.Registration
{
    public class RegistrationComparerTests
    {
        private const int PreImageType = 0;
        private const int PostOperationStage = (int)PluginBase.PluginStage.PostOperation;
        private const int PreOperationStage = (int)PluginBase.PluginStage.PreOperation;
        private const int EnabledState = 0;
        private const int DisabledState = 1;

        private static readonly string PluginTypeName = typeof(AccountUpdatePlugin).FullName;
        private static readonly string PluginAssemblyName = typeof(AccountUpdatePlugin).Assembly.GetName().Name;

        [Fact]
        public void Compare_CreatesMissingStepAndImage()
        {
            var desired = Desired();
            var actual = Actual(Array.Empty<ActualStep>(), Array.Empty<ActualImage>());

            var plan = Compare(desired, actual);

            Assert.Equal(2, plan.Creates);
            Assert.Contains(plan.Changes, c => c.Action == RegistrationActionKind.Create && c.Target == RegistrationTargetKind.Step);
            Assert.Contains(plan.Changes, c => c.Action == RegistrationActionKind.Create && c.Target == RegistrationTargetKind.Image);
        }

        [Fact]
        public void Compare_UpdatesFilteringAttributesAndImageAttributes()
        {
            var desired = Desired();
            var step = MatchingStep(filteringAttributes: "");
            var image = MatchingImage(step, attributes: AccountFields.Name);

            var plan = Compare(desired, Actual(new[] { step }, new[] { image }));

            Assert.Equal(2, plan.Updates);
            Assert.Contains(plan.Changes, c => c.Detail.Contains("filteringattributes"));
            Assert.Contains(plan.Changes, c => c.Detail.Contains("PreImage attributes"));
        }

        [Fact]
        public void Compare_ReportsExtrasWithoutDeleting()
        {
            var desired = Desired();
            var extra = MatchingStep(message: "Create");

            var plan = Compare(desired, Actual(new[] { extra }, Array.Empty<ActualImage>()));

            Assert.Equal(1, plan.Extras);
            Assert.DoesNotContain(plan.Changes, c => c.Action == RegistrationActionKind.Update);
        }

        [Fact]
        public void Compare_UpdatesStageModeDriftForSamePluginMessageAndEntity()
        {
            var desired = Desired();
            var step = MatchingStep();
            step.Stage = PreOperationStage;
            var image = MatchingImage(step, attributes: ExpectedImageAttributes());

            var plan = Compare(desired, Actual(new[] { step }, new[] { image }));

            Assert.Contains(plan.Changes, c => c.Action == RegistrationActionKind.Update && c.Target == RegistrationTargetKind.Step && c.Detail.Contains("stage/mode"));
            Assert.DoesNotContain(plan.Changes, c => c.Action == RegistrationActionKind.Create);
        }

        [Fact]
        public void Compare_MatchesDisabledStepInsteadOfCreatingDuplicate()
        {
            var desired = Desired();
            var step = MatchingStep();
            step.StateCode = DisabledState;
            var image = MatchingImage(step, attributes: ExpectedImageAttributes());

            var plan = Compare(desired, Actual(new[] { step }, new[] { image }));

            Assert.Equal(0, plan.Creates);
            Assert.Contains(plan.Changes, c => c.Action == RegistrationActionKind.Warning && c.Detail.Contains("disabled"));
        }

        [Fact]
        public void Compare_ExplainsStalePluginTypeWhenAssemblyHasOlderType()
        {
            var desired = Desired();
            var actual = Actual(
                Array.Empty<ActualStep>(),
                Array.Empty<ActualImage>(),
                new Dictionary<string, ActualPluginType>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Ops.Plugins.OpportunityWonPlugin"] = new ActualPluginType { Id = Guid.NewGuid(), TypeName = "Ops.Plugins.OpportunityWonPlugin" }
                });

            var plan = Compare(desired, actual);

            var error = Assert.Single(plan.Changes.Where(c => c.Action == RegistrationActionKind.Error && c.Target == RegistrationTargetKind.PluginType));
            Assert.Contains("OpportunityWonPlugin", error.Detail);
            Assert.Contains("older DLL", error.Detail);
            Assert.Contains("-PushAssembly", error.Detail);
            Assert.True(new RegistrationComparer().CanResolveByPushingAssembly(plan));
        }

        [Fact]
        public void Compare_WhenPushAssemblyWasRequestedExplainsMissingTypeAfterPush()
        {
            var desired = Desired();
            var actual = Actual(
                Array.Empty<ActualStep>(),
                Array.Empty<ActualImage>(),
                new Dictionary<string, ActualPluginType>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Ops.Plugins.OpportunityWonPlugin"] = new ActualPluginType { Id = Guid.NewGuid(), TypeName = "Ops.Plugins.OpportunityWonPlugin" }
                });

            var plan = new RegistrationComparer().Compare(desired, actual, new RegistrationOptions { PushAssembly = true });

            var error = Assert.Single(plan.Changes.Where(c => c.Action == RegistrationActionKind.Error && c.Target == RegistrationTargetKind.PluginType));
            Assert.Contains("already pushed", error.Detail);
            Assert.Contains("implements IPlugin", error.Detail);
        }

        [Fact]
        public void Compare_WhenAssemblyHasNoPluginTypesRecommendsPacPluginPush()
        {
            var desired = Desired();
            var actual = Actual(
                Array.Empty<ActualStep>(),
                Array.Empty<ActualImage>(),
                new Dictionary<string, ActualPluginType>(StringComparer.OrdinalIgnoreCase));

            var plan = new RegistrationComparer().Compare(desired, actual, new RegistrationOptions { AssemblyPath = "Ops.Plugins/bin/Release/net462/Ops.Plugins.dll" });

            var error = Assert.Single(plan.Changes.Where(c => c.Action == RegistrationActionKind.Error && c.Target == RegistrationTargetKind.PluginType));
            Assert.Contains("No plug-in types are currently registered", error.Detail);
            Assert.Contains("pac plugin push", error.Detail);
            Assert.Contains(actual.Assembly.Id.ToString("D"), error.Detail);
            Assert.False(new RegistrationComparer().CanResolveByPushingAssembly(plan));
        }

        [Fact]
        public void ComparePushAssemblyReadiness_BlocksStalePluginTypeWithDependentStepAndImages()
        {
            var desired = Desired();
            var staleTypeName = "Ops.Plugins.OpportunityWonPlugin";
            var staleStep = MatchingStep(pluginTypeName: staleTypeName);
            var images = new[]
            {
                MatchingImage(staleStep, attributes: ExpectedImageAttributes()),
                new ActualImage
                {
                    Id = Guid.NewGuid(),
                    StepId = staleStep.Id,
                    StepKey = staleStep.Key,
                    Alias = PluginImageNames.PostImage,
                    ImageType = 1,
                    MessagePropertyName = SdkMessagePropertyNames.Target,
                    Attributes = AttributeList.Parse(AccountFields.Name)
                }
            };
            var actual = Actual(
                new[] { staleStep },
                images,
                new Dictionary<string, ActualPluginType>(StringComparer.OrdinalIgnoreCase)
                {
                    [PluginTypeName] = new ActualPluginType { Id = Guid.NewGuid(), TypeName = PluginTypeName },
                    [staleTypeName] = new ActualPluginType { Id = Guid.NewGuid(), TypeName = staleTypeName }
                });

            var plan = new RegistrationComparer().ComparePushAssemblyReadiness(desired, actual);

            var error = Assert.Single(plan.Changes);
            Assert.Equal(RegistrationActionKind.Error, error.Action);
            Assert.Equal(RegistrationTargetKind.PluginType, error.Target);
            Assert.Equal(staleTypeName, error.PluginTypeName);
            Assert.Contains("missing from the current DLL", error.Detail);
            Assert.Contains("1 step(s), 2 image(s)", error.Detail);
            Assert.Contains("Enabled step(s): 1", error.Detail);
            Assert.Contains("PostOperation synchronous enabled", error.Detail);
            Assert.DoesNotContain("stage 40 mode 0", error.Detail);
            Assert.Contains("Manual review required", error.Detail);
            Assert.False(new RegistrationComparer().CanResolveByPushingAssembly(plan));
        }

        [Fact]
        public void ComparePushAssemblyReadiness_AllowsRegisteredTypesStillPresentInDll()
        {
            var desired = Desired();
            var actual = Actual(Array.Empty<ActualStep>(), Array.Empty<ActualImage>());

            var plan = new RegistrationComparer().ComparePushAssemblyReadiness(desired, actual);

            Assert.Equal(0, plan.Errors);
        }

        private static RegistrationPlan Compare(DesiredRegistration desired, ActualRegistration actual)
        {
            return new RegistrationComparer().Compare(desired, actual, new RegistrationOptions());
        }

        private static DesiredRegistration Desired()
        {
            var image = new DesiredImage
            {
                PluginTypeName = PluginTypeName,
                MessageName = Messages.Update,
                EntityLogicalName = Account.EntityLogicalName,
                StepStage = PostOperationStage,
                StepMode = (int)SdkMessageProcessingStepMode.Synchronous,
                Alias = PluginImageNames.PreImage,
                ImageType = PreImageType,
                MessagePropertyName = SdkMessagePropertyNames.Target,
                Attributes = AttributeList.From(MonitoredAccountAttributes())
            };

            var step = new DesiredStep
            {
                PluginTypeName = PluginTypeName,
                MessageName = Messages.Update,
                EntityLogicalName = Account.EntityLogicalName,
                Stage = PostOperationStage,
                Mode = (int)SdkMessageProcessingStepMode.Synchronous,
                Rank = 1,
                FilteringAttributes = AttributeList.From(MonitoredAccountAttributes()),
                Images = new[] { image }
            };

            image.PluginTypeName = step.PluginTypeName;
            image.MessageName = step.MessageName;
            image.EntityLogicalName = step.EntityLogicalName;

            return new DesiredRegistration(PluginAssemblyName, new[] { new DesiredPluginType(step.PluginTypeName, new[] { step }) });
        }

        private static ActualRegistration Actual(IReadOnlyCollection<ActualStep> steps, IReadOnlyCollection<ActualImage> images)
        {
            return Actual(
                steps,
                images,
                new Dictionary<string, ActualPluginType>(StringComparer.OrdinalIgnoreCase)
                {
                    [PluginTypeName] = new ActualPluginType { Id = Guid.NewGuid(), TypeName = PluginTypeName }
                });
        }

        private static ActualRegistration Actual(
            IReadOnlyCollection<ActualStep> steps,
            IReadOnlyCollection<ActualImage> images,
            IReadOnlyDictionary<string, ActualPluginType> pluginTypes)
        {
            return new ActualRegistration(
                new ActualPluginAssembly { Id = Guid.NewGuid(), Name = PluginAssemblyName },
                pluginTypes,
                steps,
                images);
        }

        private static ActualStep MatchingStep(string message = null, string filteringAttributes = null, string pluginTypeName = null)
        {
            return new ActualStep
            {
                Id = Guid.NewGuid(),
                PluginTypeName = pluginTypeName ?? PluginTypeName,
                PluginTypeId = Guid.NewGuid(),
                MessageName = message ?? Messages.Update,
                EntityLogicalName = Account.EntityLogicalName,
                Stage = PostOperationStage,
                Mode = (int)SdkMessageProcessingStepMode.Synchronous,
                Rank = 1,
                FilteringAttributes = AttributeList.Parse(filteringAttributes ?? AccountFields.Name),
                StateCode = EnabledState
            };
        }

        private static ActualImage MatchingImage(ActualStep step, string attributes)
        {
            return new ActualImage
            {
                Id = Guid.NewGuid(),
                StepId = step.Id,
                StepKey = step.Key,
                Alias = PluginImageNames.PreImage,
                ImageType = PreImageType,
                MessagePropertyName = SdkMessagePropertyNames.Target,
                Attributes = AttributeList.Parse(attributes)
            };
        }

        private static string ExpectedImageAttributes()
        {
            return AttributeList.From(MonitoredAccountAttributes()).ToString();
        }

        private static string[] MonitoredAccountAttributes()
        {
            return new[] { AccountFields.Name, AccountFields.AccountNumber, AccountFields.Telephone1 };
        }
    }
}
