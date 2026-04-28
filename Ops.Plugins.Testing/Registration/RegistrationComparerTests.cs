using System;
using System.Collections.Generic;
using System.Linq;
using Ops.Plugins;
using Ops.Plugins.Model;
using Ops.Plugins.Registration;
using Ops.Plugins.Shared;
using Xunit;

namespace Ops.Plugins.Testing.Registration
{
    public class RegistrationComparerTests
    {
        private static readonly string PluginTypeName = typeof(OpportunityWonPlugin).FullName;
        private static readonly string PluginAssemblyName = typeof(OpportunityWonPlugin).Assembly.GetName().Name;

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
            var image = MatchingImage(step, attributes: Opportunity.Fields.StatusCode);

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
            step.Stage = 20;
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
            step.StateCode = 1;
            var image = MatchingImage(step, attributes: ExpectedImageAttributes());

            var plan = Compare(desired, Actual(new[] { step }, new[] { image }));

            Assert.Equal(0, plan.Creates);
            Assert.Contains(plan.Changes, c => c.Action == RegistrationActionKind.Warning && c.Detail.Contains("disabled"));
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
                EntityLogicalName = Opportunity.EntityLogicalName,
                StepStage = 40,
                StepMode = 0,
                Alias = PluginImageNames.PreImage,
                ImageType = 0,
                MessagePropertyName = "Target",
                Attributes = AttributeList.From(new[] { Opportunity.Fields.StatusCode, Opportunity.Fields.ActualCloseDate })
            };

            var step = new DesiredStep
            {
                PluginTypeName = PluginTypeName,
                MessageName = Messages.Update,
                EntityLogicalName = Opportunity.EntityLogicalName,
                Stage = 40,
                Mode = 0,
                Rank = 1,
                FilteringAttributes = AttributeList.From(new[] { Opportunity.Fields.StatusCode }),
                Images = new[] { image }
            };

            image.PluginTypeName = step.PluginTypeName;
            image.MessageName = step.MessageName;
            image.EntityLogicalName = step.EntityLogicalName;

            return new DesiredRegistration(PluginAssemblyName, new[] { new DesiredPluginType(step.PluginTypeName, new[] { step }) });
        }

        private static ActualRegistration Actual(IReadOnlyCollection<ActualStep> steps, IReadOnlyCollection<ActualImage> images)
        {
            return new ActualRegistration(
                new ActualPluginAssembly { Id = Guid.NewGuid(), Name = PluginAssemblyName },
                new Dictionary<string, ActualPluginType>(StringComparer.OrdinalIgnoreCase)
                {
                    [PluginTypeName] = new ActualPluginType { Id = Guid.NewGuid(), TypeName = PluginTypeName }
                },
                steps,
                images);
        }

        private static ActualStep MatchingStep(string message = null, string filteringAttributes = null)
        {
            return new ActualStep
            {
                Id = Guid.NewGuid(),
                PluginTypeName = PluginTypeName,
                PluginTypeId = Guid.NewGuid(),
                MessageName = message ?? Messages.Update,
                EntityLogicalName = Opportunity.EntityLogicalName,
                Stage = 40,
                Mode = 0,
                Rank = 1,
                FilteringAttributes = AttributeList.Parse(filteringAttributes ?? Opportunity.Fields.StatusCode),
                StateCode = 0
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
                ImageType = 0,
                MessagePropertyName = "Target",
                Attributes = AttributeList.Parse(attributes)
            };
        }

        private static string ExpectedImageAttributes()
        {
            return AttributeList.From(new[] { Opportunity.Fields.StatusCode, Opportunity.Fields.ActualCloseDate }).ToString();
        }
    }
}
