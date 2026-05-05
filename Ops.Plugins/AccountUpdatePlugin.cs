using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Ops.Plugins.Model;
using Ops.Plugins.Shared;
using AccountFields = Ops.Plugins.Model.Account.Fields;

// Starter account update plugin. RegisteredEvent metadata is the source of
// truth for message, stage, filtering attributes, images, and run context.

namespace Ops.Plugins
{
    public sealed class AccountUpdatePlugin : PluginBase
    {
        public AccountUpdatePlugin() { }

        public AccountUpdatePlugin(string unsecureConfig, string secureConfig)
            : base(unsecureConfig, secureConfig) { }

        protected override IEnumerable<RegisteredEvent> GetRegisteredEvents()
        {
            var identityColumns = new[] { AccountFields.AccountNumber };

            var profileColumns = new[] { AccountFields.Name, AccountFields.Telephone1 };

            return new[]
            {
                new RegisteredEvent(
                    PluginStage.PreOperation,
                    SdkMessageProcessingStepMode.Synchronous,
                    Messages.Update,
                    Account.EntityLogicalName,
                    AccountPreOpUpdateSync,
                    requiredPreImageName: PluginImageNames.PreImage,
                    filteringColumns: identityColumns,
                    preImageColumns: identityColumns,
                    runInUserContext: RunInUserContext.CallingUser,
                    stepDescription: "Protects assigned account numbers before update."),

                new RegisteredEvent(
                    PluginStage.PostOperation,
                    SdkMessageProcessingStepMode.Synchronous,
                    Messages.Update,
                    Account.EntityLogicalName,
                    AccountPostOpUpdateSync,
                    requiredPostImageName: PluginImageNames.PostImage,
                    filteringColumns: profileColumns,
                    postImageColumns: profileColumns,
                    runInUserContext: RunInUserContext.CallingUser,
                    stepDescription: "Summarizes committed account profile updates.")
            };
        }

        private void AccountPreOpUpdateSync(LocalPluginContext context)
        {
            var target = context.GetTarget()?.ToEntity<Account>();
            var preImage = context.GetPreImage<Account>();

            if (target == null || !target.Contains(AccountFields.AccountNumber)) return;

            var previousAccountNumber = preImage?.AccountNumber;
            if (string.IsNullOrWhiteSpace(previousAccountNumber)) return;
            if (!target.HasChangedFrom(preImage, AccountFields.AccountNumber)) return;

            throw new InvalidPluginExecutionException(
                "Account number is already assigned and cannot be changed by a standard account update.");
        }

        private void AccountPostOpUpdateSync(LocalPluginContext context)
        {
            var target = context.GetTarget()?.ToEntity<Account>();
            var postImage = context.GetPostImage<Account>();

            if (target == null) return;

            // Target contains only columns submitted in this Update request.
            // The trace shows the sparse payload that triggered the plugin.
            context.TraceTarget();

            context.Trace(
                $"Account profile committed | Name: {postImage?.Name ?? target.Name ?? "null"} | Phone: {postImage?.Telephone1 ?? target.Telephone1 ?? "null"}",
                TraceLevel.Info);

            // PostImage is the committed row state after Dataverse applies Target.
            // Use it for downstream notifications, auditing, or integration payloads
            // that need the final values without issuing a retrieve.
            context.Trace(
                () => $"PostImage committed profile: {CrmFormat.Of(postImage, AccountFields.Name, AccountFields.Telephone1)}",
                TraceLevel.Verbose);
        }
    }
}
