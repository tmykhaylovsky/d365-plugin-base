using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Ops.Crm.Plugins.Examples;
using Ops.Crm.Shared.Testing;
using Xunit;

// Tests for GetOpportunitySummaryApi (Custom API plugin).

namespace Ops.Crm.Plugins.Examples.Tests
{
    public class GetOpportunitySummaryApiTests : PluginTestBase
    {
        private static readonly Guid OpportunityId = Guid.NewGuid();

        // -----------------------------------------------------------------------
        // Happy path
        // -----------------------------------------------------------------------

        [Fact]
        public void GivenExistingOpportunity_ReturnsSummaryAndFoundTrue()
        {
            // Arrange — seed an opportunity in the in-memory context
            var opportunity = BuildEntity("opportunity", OpportunityId,
                ("name",           "Acme Corp Deal"),
                ("estimatedvalue", new Money(150_000m)),
                ("statuscode",     new OptionSetValue(3))); // Won

            Seed(opportunity);

            var ctx = BuildCustomApiContext(
                messageName: "ops_GetOpportunitySummary",
                inputParameters: new Dictionary<string, object>
                {
                    ["opportunityid"] = new EntityReference("opportunity", OpportunityId)
                });

            // Act
            Context.ExecutePluginWith<GetOpportunitySummaryApi>(ctx);

            // Assert — read output parameters from the execution context
            var found   = (bool)ctx.OutputParameters["found"];
            var summary = (string)ctx.OutputParameters["summary"];

            Assert.True(found);
            Assert.Contains("Acme Corp Deal", summary);
            Assert.Contains("150000.00", summary);
        }

        [Fact]
        public void GivenNonExistentOpportunity_ReturnsFalseAndNotFoundMessage()
        {
            // Arrange — do NOT seed; record does not exist
            var missingId = Guid.NewGuid();
            var ctx = BuildCustomApiContext(
                messageName: "ops_GetOpportunitySummary",
                inputParameters: new Dictionary<string, object>
                {
                    ["opportunityid"] = new EntityReference("opportunity", missingId)
                });

            // Act
            Context.ExecutePluginWith<GetOpportunitySummaryApi>(ctx);

            // Assert
            var found   = (bool)ctx.OutputParameters["found"];
            var summary = (string)ctx.OutputParameters["summary"];

            Assert.False(found);
            Assert.Contains("not found", summary, StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------------
        // Guard conditions
        // -----------------------------------------------------------------------

        [Fact]
        public void WhenInputParameterMissing_ThrowsInvalidPluginExecutionException()
        {
            // Arrange — no input parameters
            var ctx = BuildCustomApiContext(
                messageName: "ops_GetOpportunitySummary");

            // Act & Assert
            Assert.Throws<InvalidPluginExecutionException>(() =>
                Context.ExecutePluginWith<GetOpportunitySummaryApi>(ctx));
        }
    }
}
