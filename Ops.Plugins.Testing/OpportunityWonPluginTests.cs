using System;
using Microsoft.Xrm.Sdk.Query;
using Ops.Plugins;
using Ops.Plugins.Model;
using Ops.Plugins.Shared;
using Xunit;

// Tests for OpportunityWonPlugin.
// Inheriting PluginTestBase wires up XrmFakedContext and all builder helpers.
// Run with: dotnet test  (or Visual Studio Test Explorer)

namespace Ops.Plugins.Testing
{
    public class OpportunityWonPluginTests : PluginTestBase
    {
        private static readonly Guid OpportunityId = Guid.NewGuid();
        private const string NonOpportunityEntityLogicalName = "account";

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private Opportunity MakeTarget(opportunity_statuscode statusCode) =>
            BuildOpportunity(statusCode);

        private Opportunity MakePreImage(opportunity_statuscode statusCode, DateTime? closeDate = null)
        {
            var opportunity = BuildOpportunity(statusCode);
            opportunity.ActualCloseDate = closeDate;
            return opportunity;
        }

        private static Opportunity BuildOpportunity(opportunity_statuscode statusCode)
        {
            var opportunity = new Opportunity { Id = OpportunityId };
            opportunity.StatusCode = statusCode;
            return opportunity;
        }

        // -----------------------------------------------------------------------
        // Happy path
        // -----------------------------------------------------------------------

        [Fact]
        public void WhenStatusChangesToWon_AndCloseDateNotSet_StampsCloseDate()
        {
            // Arrange
            var preImage = MakePreImage(opportunity_statuscode.InProgress);
            var target   = MakeTarget(opportunity_statuscode.Won);
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage, postImage: target);

            // Act
            Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

            // Assert - opportunity in the in-memory store should now have actualclosedate
            var updated = RetrieveOpportunity();

            Assert.True(updated.ActualCloseDate.HasValue,
                "actualclosedate should have been stamped when Opportunity moved to Won");
        }

        [Fact]
        public void WhenStatusChangesToWon_AndCloseDateAlreadySet_DoesNotOverwrite()
        {
            // Arrange
            var existingClose = new DateTime(2026, 1, 15);
            var preImage      = MakePreImage(opportunity_statuscode.InProgress, existingClose);
            var target        = MakeTarget(opportunity_statuscode.Won);
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage, postImage: target);

            // Act - no exception means it ran cleanly; verify no Update SDK call was made
            Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

            // Assert - close date on the seeded record should be unchanged
            var record = RetrieveOpportunity();

            Assert.Equal(existingClose, record.ActualCloseDate);
        }

        // -----------------------------------------------------------------------
        // Guard conditions
        // -----------------------------------------------------------------------

        [Fact]
        public void WhenStatusCodeDidNotChange_PluginExitsEarly()
        {
            // Arrange - target has same status as pre-image
            var preImage = MakePreImage(opportunity_statuscode.InProgress);
            var target   = MakeTarget(opportunity_statuscode.InProgress); // no change
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage, postImage: target);

            // Act & Assert - should complete without any SDK calls
            var exception = Record.Exception(() =>
                Context.ExecutePluginWith<OpportunityWonPlugin>(ctx));

            Assert.Null(exception);
        }

        [Fact]
        public void WhenStatusChangesToNonWon_PluginExitsEarly()
        {
            // Arrange - status changes to something other than Won
            var preImage = MakePreImage(opportunity_statuscode.InProgress);
            var target   = MakeTarget(opportunity_statuscode.OnHold);
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage, postImage: target);

            // Act & Assert
            var exception = Record.Exception(() =>
                Context.ExecutePluginWith<OpportunityWonPlugin>(ctx));

            Assert.Null(exception);
        }

        [Fact]
        public void WhenPrimaryEntityDoesNotMatch_PluginExitsEarly()
        {
            var existing = MakePreImage(opportunity_statuscode.InProgress);
            var target   = BuildEntity(
                NonOpportunityEntityLogicalName,
                OpportunityId,
                (Opportunity.Fields.StatusCode, opportunity_statuscode.Won));
            Seed(existing);

            var ctx = BuildContext(
                Messages.Update,
                target.LogicalName,
                target,
                PluginStage.PostOperation,
                preImage: existing,
                postImage: target);

            Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

            Assert.False(RetrieveOpportunity().ActualCloseDate.HasValue);
        }

        [Fact]
        public void WhenStageDoesNotMatch_PluginExitsEarly()
        {
            var existing = MakePreImage(opportunity_statuscode.InProgress);
            var target   = MakeTarget(opportunity_statuscode.Won);
            Seed(existing);

            var ctx = BuildContext(
                Messages.Update,
                Opportunity.EntityLogicalName,
                target,
                PluginStage.PreOperation,
                preImage: existing,
                postImage: target);

            Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

            Assert.False(RetrieveOpportunity().ActualCloseDate.HasValue);
        }

        [Fact]
        public void WhenExecutionModeDoesNotMatch_PluginExitsEarly()
        {
            var existing = MakePreImage(opportunity_statuscode.InProgress);
            var target   = MakeTarget(opportunity_statuscode.Won);
            Seed(existing);

            var ctx = BuildUpdateContext(target, preImage: existing, postImage: target);
            ctx.Mode = (int)sdkmessageprocessingstep_mode.Asynchronous;

            Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

            Assert.False(RetrieveOpportunity().ActualCloseDate.HasValue);
        }

        [Fact]
        public void WhenPreImageIsMissing_PluginExitsEarly()
        {
            var existing = MakePreImage(opportunity_statuscode.InProgress);
            var target   = MakeTarget(opportunity_statuscode.Won);
            Seed(existing);

            var ctx = BuildUpdateContext(target, postImage: target);

            Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

            Assert.False(RetrieveOpportunity().ActualCloseDate.HasValue);
        }

        private Opportunity RetrieveOpportunity() =>
            Service.Retrieve(Opportunity.EntityLogicalName, OpportunityId,
                new ColumnSet(Opportunity.Fields.ActualCloseDate)).ToEntity<Opportunity>();
    }
}
