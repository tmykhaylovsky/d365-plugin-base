using System;
using Microsoft.Xrm.Sdk;
using Ops.Crm.Shared;

// Example 2: Custom API Plugin
// -----------------------------------------------------------------------
// Scenario: Custom API "ops_GetOpportunitySummary"
//           Input:  opportunityid  (EntityReference)
//           Output: summary        (string)
//                   found          (bool)
//
// PRT Step Registration:
//   Message:              ops_GetOpportunitySummary
//   Primary Entity:       (none)
//   Stage:                PostOperation (40)
//   Execution Mode:       Synchronous
//   Pre/Post Images:      (none)
//
// Custom API must be created in the solution BEFORE registering this plugin step.
// Create via: Power Apps maker portal → Custom APIs → New, or via solution XML.
//
// Namespace: replace Ops.Crm.Plugins → YourClient.Crm.Plugins after cloning
// -----------------------------------------------------------------------

namespace Ops.Crm.Plugins.Examples
{
    public sealed class GetOpportunitySummaryApi : PluginBase
    {
        public GetOpportunitySummaryApi() { }

        public GetOpportunitySummaryApi(string unsecureConfig, string secureConfig)
            : base(unsecureConfig, secureConfig) { }

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            // Guard: verify this is a Custom API invocation
            if (!context.IsCustomApi)
                throw new InvalidPluginExecutionException("This plugin is registered as a Custom API handler only.");

            // Read input parameter
            var opportunityRef = context.GetInputParameter<EntityReference>("opportunityid");
            if (opportunityRef == null)
                throw new InvalidPluginExecutionException("Required input parameter 'opportunityid' was not provided.");

            context.Logger.Trace(TraceLevel.Verbose, () =>
                $"GetOpportunitySummary | Input: {CrmFormat.Of(opportunityRef)}");

            // Retrieve the opportunity
            var opportunity = context.OrganizationService.GetRecordOrDefault<Entity>(
                "opportunity",
                opportunityRef.Id,
                new Microsoft.Xrm.Sdk.Query.ColumnSet("name", "estimatedvalue", "statuscode", "actualclosedate"));

            if (opportunity == null)
            {
                context.SetOutputParameter("found",   false);
                context.SetOutputParameter("summary", $"Opportunity {opportunityRef.Id:D} not found.");
                return;
            }

            // Build summary using CrmFormat helpers
            var fmt     = new CrmFormatter(context.OrganizationService);
            var name    = opportunity.GetAttributeValue<string>("name", "(no name)");
            var value   = CrmFormat.Of(opportunity.GetAttributeValue<Money>("estimatedvalue"));
            var status  = fmt.Of(opportunity.GetAttributeValue<OptionSetValue>("statuscode"), "opportunity", "statuscode");
            var closed  = CrmFormat.Of(opportunity.GetAttributeValue<DateTime>("actualclosedate"));

            var summary = $"{name} | Value: {value} | Status: {status} | Close: {closed}";

            context.Logger.Trace(TraceLevel.Verbose, () => $"Summary: {summary}");

            context.SetOutputParameter("found",   true);
            context.SetOutputParameter("summary", summary);
        }
    }
}
