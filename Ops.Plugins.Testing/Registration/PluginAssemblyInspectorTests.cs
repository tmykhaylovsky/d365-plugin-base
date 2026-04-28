extern alias PluginAssembly;

using System.Linq;
using Ops.Plugins.Registration;
using Ops.Plugins.Shared;
using PluginAssembly::Ops.Plugins;
using PluginAssembly::Ops.Plugins.Model;
using Xunit;

namespace Ops.Plugins.Testing.Registration
{
    public class PluginAssemblyInspectorTests
    {
        [Fact]
        public void Inspect_ReadsRegisteredEventsFromBuiltPluginAssembly()
        {
            var assemblyPath = typeof(OpportunityWonPlugin).Assembly.Location;

            var registration = new PluginAssemblyInspector().Inspect(assemblyPath);
            var step = registration.PluginTypes.Single(t => t.TypeName == typeof(OpportunityWonPlugin).FullName).Steps.Single();
            var preImage = step.Images.Single(i => i.Alias == PluginImageNames.PreImage);
            var postImage = step.Images.Single(i => i.Alias == PluginImageNames.PostImage);

            Assert.Equal(typeof(OpportunityWonPlugin).Assembly.GetName().Name, registration.AssemblyName);
            Assert.Equal(Messages.Update, step.MessageName);
            Assert.Equal(Opportunity.EntityLogicalName, step.EntityLogicalName);
            Assert.Equal((int)PluginBase.PluginStage.PostOperation, step.Stage);
            Assert.Equal((int)SdkMessageProcessingStepMode.Synchronous, step.Mode);
            Assert.Equal(1, step.Rank);
            Assert.Equal(RegisteredEvent.CallingUser, step.RunInUserContext);
            Assert.Equal("Stamps actual close date when an opportunity is won.", step.Description);
            Assert.Equal(ExpectedFilteringAttributes(), step.FilteringAttributes.ToString());
            Assert.Equal((int)sdkmessageprocessingstepimage_imagetype.PreImage, preImage.ImageType);
            Assert.Equal(ExpectedPreImageAttributes(), preImage.Attributes.ToString());
            Assert.Equal((int)sdkmessageprocessingstepimage_imagetype.PostImage, postImage.ImageType);
            Assert.Equal(ExpectedPostImageAttributes(), postImage.Attributes.ToString());
        }

        private static string ExpectedFilteringAttributes()
        {
            return AttributeList.From(new[] { Opportunity.Fields.StatusCode, Opportunity.Fields.ActualCloseDate, Opportunity.Fields.StateCode }).ToString();
        }

        private static string ExpectedPreImageAttributes()
        {
            return AttributeList.From(new[] { Opportunity.Fields.StatusCode, Opportunity.Fields.ActualCloseDate, Opportunity.Fields.ActualValue }).ToString();
        }

        private static string ExpectedPostImageAttributes()
        {
            return AttributeList.From(new[] { Opportunity.Fields.StatusCode, Opportunity.Fields.ActualCloseDate, Opportunity.Fields.BudgetAmount }).ToString();
        }
    }
}
