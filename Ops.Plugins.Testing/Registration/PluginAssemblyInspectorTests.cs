using System.Linq;
using Ops.Plugins;
using Ops.Plugins.Model;
using Ops.Plugins.Registration;
using Ops.Plugins.Shared;
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

            Assert.Equal("Ops.Plugins", registration.AssemblyName);
            Assert.Equal(Messages.Update, step.MessageName);
            Assert.Equal(Opportunity.EntityLogicalName, step.EntityLogicalName);
            Assert.Equal((int)PluginBase.PluginStage.PostOperation, step.Stage);
            Assert.Equal((int)SdkMessageProcessingStepMode.Synchronous, step.Mode);
            Assert.Equal(1, step.Rank);
            Assert.Equal(RegisteredEvent.CallingUser, step.RunInUserContext);
            Assert.Equal("Stamps actual close date when an opportunity is won.", step.Description);
            Assert.Equal(Opportunity.Fields.StatusCode, step.FilteringAttributes.ToString());
            Assert.Equal(0, preImage.ImageType);
            Assert.Equal("actualclosedate,statuscode", preImage.Attributes.ToString());
            Assert.Equal(1, postImage.ImageType);
            Assert.Equal("actualclosedate,statuscode", postImage.Attributes.ToString());
        }
    }
}
