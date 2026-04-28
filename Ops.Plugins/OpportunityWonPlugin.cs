using System;
using Microsoft.Xrm.Sdk;
using Ops.Plugins.Shared;

// Example 1: Standard CRUD Plugin
// -----------------------------------------------------------------------
// Scenario: When an Opportunity's Status Reason changes to Won (3),
//           stamp the actual close date if it was not already set.
//
// PRT Step Registration:
//   Message:              Update
//   Primary Entity:       opportunity
//   Stage:                PostOperation (40) — react after the platform commits
//   Execution Mode:       Synchronous
//   Filtering Attributes: statuscode   ← critical for performance; omitting this fires on every update
//   Pre-Image:            Name = "PreImage", Attributes = statuscode, actualclosedate
//   Unsecure Config:      (none required for this plugin)
//
// Namespace: replace Ops.Plugins → YourClient.Crm.Plugins after cloning
// -----------------------------------------------------------------------

namespace Ops.Plugins
{
    public sealed class OpportunityWonPlugin : PluginBase
    {
        // Opportunity statuscode value for "Won" — verify in your org's OptionSet metadata
        private const int StatusWon = 3;

        // Required: Dataverse calls this when no config strings are set on the step
        public OpportunityWonPlugin() { }

        // Required: Dataverse calls this when unsecure/secure config is set on the step
        public OpportunityWonPlugin(string unsecureConfig, string secureConfig)
            : base(unsecureConfig, secureConfig) { }

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            // Guard 1: only act on statuscode changes (belt + suspenders; PRT filtering handles this too)
            if (!context.HasChangedAttribute("statuscode")) return;

            var target   = context.GetTarget();
            var preImage = context.GetPreImage<Entity>(); // "PreImage" registered in PRT

            var newStatus = target.GetOptionSetValue("statuscode");
            if (newStatus != StatusWon) return;

            // Log the status transition with display labels (one metadata call per attribute, cached)
            var fmt = new CrmFormatter(context.OrganizationService);
            context.Logger.Trace(TraceLevel.Info, () =>
                $"Opportunity Won | " +
                $"{fmt.Of(preImage?.GetAttributeValue<OptionSetValue>("statuscode"), "opportunity", "statuscode")} → " +
                $"{fmt.Of(target.GetAttributeValue<OptionSetValue>("statuscode"), "opportunity", "statuscode")}");

            // Stamp actual close date only when it was not already set on either target or pre-image
            var alreadySet = target.HasValue("actualclosedate") || preImage.HasValue("actualclosedate");
            if (!alreadySet)
            {
                var stamp = new Entity("opportunity", target.Id)
                {
                    ["actualclosedate"] = DateTime.UtcNow.Date
                };
                context.OrganizationService.Update(stamp);

                context.Logger.Trace(TraceLevel.Verbose, () =>
                    $"Stamped actualclosedate = {DateTime.UtcNow:yyyy-MM-dd}");
            }
            else
            {
                context.Logger.Trace(TraceLevel.Verbose, () =>
                    $"actualclosedate already set — skipping stamp | " +
                    $"target={CrmFormat.Of(target.GetAttributeValue<DateTime>("actualclosedate"))} " +
                    $"preImage={CrmFormat.Of(preImage?.GetAttributeValue<DateTime>("actualclosedate"))}");
            }
        }
    }
}
