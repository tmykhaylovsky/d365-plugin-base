extern alias PluginAssembly;

using System.Linq;
using Ops.Plugins.Registration;
using Ops.Plugins.Shared;
using PluginAssembly::Ops.Plugins;
using PluginAssembly::Ops.Plugins.Model;
using AccountFields = PluginAssembly::Ops.Plugins.Model.Account.Fields;
using Xunit;

namespace Ops.Plugins.Testing.Registration
{
    public class PluginAssemblyInspectorTests
    {
        [Fact]
        public void Inspect_ReadsRegisteredEventsFromBuiltPluginAssembly()
        {
            var assemblyPath = typeof(AccountUpdatePlugin).Assembly.Location;

            var registration = new PluginAssemblyInspector().Inspect(assemblyPath);
            var pluginType = registration.PluginTypes.Single(t => t.TypeName == typeof(AccountUpdatePlugin).FullName);
            var preOperationStep = pluginType.Steps.Single(s => s.Stage == (int)PluginBase.PluginStage.PreOperation);
            var postOperationStep = pluginType.Steps.Single(s => s.Stage == (int)PluginBase.PluginStage.PostOperation);
            var preImage = preOperationStep.Images.Single(i => i.Alias == PluginImageNames.PreImage);
            var postImage = postOperationStep.Images.Single(i => i.Alias == PluginImageNames.PostImage);

            Assert.Equal(typeof(AccountUpdatePlugin).Assembly.GetName().Name, registration.AssemblyName);
            Assert.Equal(2, pluginType.Steps.Count);

            Assert.Equal(Messages.Update, preOperationStep.MessageName);
            Assert.Equal(Account.EntityLogicalName, preOperationStep.EntityLogicalName);
            Assert.Equal((int)SdkMessageProcessingStepMode.Synchronous, preOperationStep.Mode);
            Assert.Equal(1, preOperationStep.Rank);
            Assert.Equal(RegisteredEvent.CallingUser, preOperationStep.RunInUserContext);
            Assert.Equal("Protects assigned account numbers before update.", preOperationStep.Description);
            Assert.Equal(ExpectedIdentityAttributes(), preOperationStep.FilteringAttributes.ToString());
            Assert.Equal(0, preImage.ImageType);
            Assert.Equal(ExpectedIdentityAttributes(), preImage.Attributes.ToString());

            Assert.Equal(Messages.Update, postOperationStep.MessageName);
            Assert.Equal(Account.EntityLogicalName, postOperationStep.EntityLogicalName);
            Assert.Equal((int)SdkMessageProcessingStepMode.Synchronous, postOperationStep.Mode);
            Assert.Equal(1, postOperationStep.Rank);
            Assert.Equal(RegisteredEvent.CallingUser, postOperationStep.RunInUserContext);
            Assert.Equal("Summarizes committed account profile updates.", postOperationStep.Description);
            Assert.Equal(ExpectedProfileAttributes(), postOperationStep.FilteringAttributes.ToString());
            Assert.Equal(1, postImage.ImageType);
            Assert.Equal(ExpectedProfileAttributes(), postImage.Attributes.ToString());
        }

        private static string ExpectedIdentityAttributes()
        {
            return AttributeList.From(new[] { AccountFields.AccountNumber }).ToString();
        }

        private static string ExpectedProfileAttributes()
        {
            return AttributeList.From(new[] { AccountFields.Name, AccountFields.Telephone1 }).ToString();
        }
    }
}
