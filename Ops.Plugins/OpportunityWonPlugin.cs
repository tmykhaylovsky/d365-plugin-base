using System;
using Ops.Plugins.Model;
using Ops.Plugins.Shared;

// Example 1: Standard CRUD Plugin
// -----------------------------------------------------------------------
// Scenario: When an Opportunity's Status Reason changes to Won,
//           stamp the actual close date if it was not already set.
//
// PRT Step Registration:
//   Message:              Update
//   Primary Entity:       opportunity
//   Stage:                PostOperation (40) - react after the platform commits
//   Execution Mode:       Synchronous
//   Filtering Attributes: statuscode - critical for performance; omitting this fires on every update
//   Pre-Image:            Name = "PreImage", Attributes = statuscode, actualclosedate
//   Unsecure Config:      (none required for this plugin)
//
// Namespace: replace Ops.Plugins with YourClient.Crm.Plugins after cloning
// -----------------------------------------------------------------------

namespace Ops.Plugins
{
    public sealed class OpportunityWonPlugin : PluginBase
    {
        // Required: Dataverse calls this when no config strings are set on the step
        public OpportunityWonPlugin() { }

        // Required: Dataverse calls this when unsecure/secure config is set on the step
        public OpportunityWonPlugin(string unsecureConfig, string secureConfig)
            : base(unsecureConfig, secureConfig) { }

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            // Guard 1: only act on statuscode changes (belt + suspenders; PRT filtering handles this too)
            if (!context.HasChangedAttribute(Opportunity.Fields.StatusCode)) return;

            var target   = context.GetTarget()?.ToEntity<Opportunity>();
            var preImage = context.GetPreImage<Opportunity>(); // "PreImage" registered in PRT

            if (target?.StatusCode != opportunity_statuscode.Won) return;

            context.Trace($"Opportunity Won | {preImage?.StatusCode?.ToString() ?? "null"} -> {target.StatusCode}", TraceLevel.Info);

            // Stamp actual close date only when it was not already set on either target or pre-image
            var alreadySet = target.ActualCloseDate.HasValue || preImage?.ActualCloseDate.HasValue == true;
            if (!alreadySet)
            {
                var stamp = new Opportunity
                {
                    Id = target.Id,
                    ActualCloseDate = DateTime.UtcNow.Date
                };
                context.OrganizationService.Update(stamp);

                context.Trace($"Stamped actualclosedate = {DateTime.UtcNow:yyyy-MM-dd}", TraceLevel.Verbose);
            }
            else
            {
                context.Trace($"actualclosedate already set - skipping stamp | " +
                    $"target={CrmFormat.Of(target.ActualCloseDate)} " +
                    $"preImage={CrmFormat.Of(preImage?.ActualCloseDate)}",
                    TraceLevel.Verbose);
            }
        }
    }
}
