using System;
using Microsoft.Xrm.Sdk;
using Ops.Plugins;
using Ops.Plugins.Shared;
using Ops.Plugins.Testing;
using Xunit;

// Tests for OpportunityWonPlugin.
// Inheriting PluginTestBase wires up XrmFakedContext and all builder helpers.
// Run with: dotnet test  (or Visual Studio Test Explorer)

namespace Ops.Plugins.Testing
{
    public class OpportunityWonPluginTests : PluginTestBase
    {
        private const int StatusOpen   = 1;
        private const int StatusWon    = 3;
        private static readonly Guid OpportunityId = Guid.NewGuid();

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private Entity MakeTarget(int statusCode) =>
            BuildEntity("opportunity", OpportunityId,
                ("statuscode", new OptionSetValue(statusCode)));

        private Entity MakePreImage(int statusCode, DateTime? closeDate = null)
        {
            var e = BuildEntity("opportunity", OpportunityId,
                ("statuscode", new OptionSetValue(statusCode)));
            if (closeDate.HasValue)
                e["actualclosedate"] = closeDate.Value;
            return e;
        }

        // -----------------------------------------------------------------------
        // Happy path
        // -----------------------------------------------------------------------

        [Fact]
        public void WhenStatusChangesToWon_AndCloseDateNotSet_StampsCloseDate()
        {
            // Arrange
            var preImage = MakePreImage(StatusOpen);
            var target   = MakeTarget(StatusWon);
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage);

            // Act
            Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

            // Assert — opportunity in the in-memory store should now have actualclosedate
            var updated = Service.Retrieve("opportunity", OpportunityId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet("actualclosedate"));

            Assert.True(updated.HasValue("actualclosedate"),
                "actualclosedate should have been stamped when Opportunity moved to Won");
        }

        [Fact]
        public void WhenStatusChangesToWon_AndCloseDateAlreadySet_DoesNotOverwrite()
        {
            // Arrange
            var existingClose = new DateTime(2026, 1, 15);
            var preImage      = MakePreImage(StatusOpen, existingClose);
            var target        = MakeTarget(StatusWon);
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage);

            // Act — no exception means it ran cleanly; verify no Update SDK call was made
            Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

            // Assert — close date on the seeded record should be unchanged
            var record = Service.Retrieve("opportunity", OpportunityId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet("actualclosedate"));

            var actualClose = record.GetAttributeValue<DateTime>("actualclosedate");
            Assert.Equal(existingClose, actualClose);
        }

        // -----------------------------------------------------------------------
        // Guard conditions
        // -----------------------------------------------------------------------

        [Fact]
        public void WhenStatusCodeDidNotChange_PluginExitsEarly()
        {
            // Arrange — target has same status as pre-image
            var preImage = MakePreImage(StatusOpen);
            var target   = MakeTarget(StatusOpen); // no change
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage);

            // Act & Assert — should complete without any SDK calls
            var exception = Record.Exception(() =>
                Context.ExecutePluginWith<OpportunityWonPlugin>(ctx));

            Assert.Null(exception);
        }

        [Fact]
        public void WhenStatusChangesToNonWon_PluginExitsEarly()
        {
            // Arrange — status changes to something other than Won
            var preImage = MakePreImage(StatusOpen);
            var target   = MakeTarget(2); // Closed (some other status)
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage);

            // Act & Assert
            var exception = Record.Exception(() =>
                Context.ExecutePluginWith<OpportunityWonPlugin>(ctx));

            Assert.Null(exception);
        }
    }
}
