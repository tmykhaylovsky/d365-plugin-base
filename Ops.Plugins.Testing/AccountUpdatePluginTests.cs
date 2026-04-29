extern alias PluginAssembly;

using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Ops.Plugins.Shared;
using PluginAssembly::Ops.Plugins;
using PluginAssembly::Ops.Plugins.Model;
using AccountFields = PluginAssembly::Ops.Plugins.Model.Account.Fields;
using Xunit;

namespace Ops.Plugins.Testing
{
    public class AccountUpdatePluginTests : PluginTestBase
    {
        private static readonly Guid AccountId = Guid.NewGuid();
        private const string NonAccountEntityLogicalName = "contact";

        [Fact]
        public void WhenProfileFieldChanges_PostOperationRunsWithoutUpdatingRecord()
        {
            var preImage = BuildAccount("Before", "A-100", "555-0100");
            var target = new Account { Id = AccountId, Name = "After" };
            var postImage = BuildAccount("After", "A-100", "555-0100");
            Seed(preImage);

            var ctx = BuildUpdateContext(target, preImage: preImage, postImage: postImage);

            Context.ExecutePluginWith<AccountUpdatePlugin>(ctx);

            var stored = RetrieveAccount();
            Assert.Equal("Before", stored.Name);
            Assert.Equal("A-100", stored.AccountNumber);
            Assert.Equal("555-0100", stored.Telephone1);
        }

        [Fact]
        public void WhenAssignedAccountNumberChanges_PreOperationThrows()
        {
            var preImage = BuildAccount("Before", "A-100", "555-0100");
            var target = new Account { Id = AccountId, AccountNumber = "A-200" };
            Seed(preImage);

            var ctx = BuildContext(
                Messages.Update,
                Account.EntityLogicalName,
                target,
                PluginStage.PreOperation,
                preImage: preImage);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                Context.ExecutePluginWith<AccountUpdatePlugin>(ctx));

            Assert.Contains("Account number is already assigned", ex.Message);
            Assert.Equal("A-100", RetrieveAccount().AccountNumber);
        }

        [Fact]
        public void WhenAccountNumberIsAssignedForFirstTime_PreOperationAllowsUpdate()
        {
            var preImage = BuildAccount("Before", null, "555-0100");
            var target = new Account { Id = AccountId, AccountNumber = "A-100" };
            Seed(preImage);

            var ctx = BuildContext(
                Messages.Update,
                Account.EntityLogicalName,
                target,
                PluginStage.PreOperation,
                preImage: preImage);

            Context.ExecutePluginWith<AccountUpdatePlugin>(ctx);

            Assert.Null(RetrieveAccount().AccountNumber);
        }

        [Fact]
        public void WhenPrimaryEntityDoesNotMatch_RegisteredEventsExitEarly()
        {
            var existing = BuildAccount("Before", "A-100", "555-0100");
            var target = BuildEntity(
                NonAccountEntityLogicalName,
                AccountId,
                (AccountFields.Name, "After"));
            Seed(existing);

            var ctx = BuildContext(
                Messages.Update,
                target.LogicalName,
                target,
                PluginStage.PostOperation,
                preImage: existing,
                postImage: target);

            Context.ExecutePluginWith<AccountUpdatePlugin>(ctx);

            Assert.Equal("Before", RetrieveAccount().Name);
        }

        [Fact]
        public void WhenStageDoesNotMatch_RegisteredEventsExitEarly()
        {
            var existing = BuildAccount("Before", "A-100", "555-0100");
            var target = new Account { Id = AccountId, Name = "After" };
            Seed(existing);

            var ctx = BuildContext(
                Messages.Update,
                Account.EntityLogicalName,
                target,
                PluginStage.PreValidation,
                preImage: existing,
                postImage: target);

            Context.ExecutePluginWith<AccountUpdatePlugin>(ctx);

            Assert.Equal("Before", RetrieveAccount().Name);
        }

        [Fact]
        public void WhenExecutionModeDoesNotMatch_RegisteredEventsExitEarly()
        {
            var existing = BuildAccount("Before", "A-100", "555-0100");
            var target = new Account { Id = AccountId, Name = "After" };
            Seed(existing);

            var ctx = BuildUpdateContext(target, preImage: existing, postImage: target);
            ctx.Mode = (int)SdkMessageProcessingStepMode.Asynchronous;

            Context.ExecutePluginWith<AccountUpdatePlugin>(ctx);

            Assert.Equal("Before", RetrieveAccount().Name);
        }

        [Fact]
        public void WhenPreImageIsMissing_PreOperationEventExitsEarly()
        {
            var existing = BuildAccount("Before", "A-100", "555-0100");
            var target = new Account { Id = AccountId, AccountNumber = "A-200" };
            Seed(existing);

            var ctx = BuildContext(
                Messages.Update,
                Account.EntityLogicalName,
                target,
                PluginStage.PreOperation);

            Context.ExecutePluginWith<AccountUpdatePlugin>(ctx);

            Assert.Equal("A-100", RetrieveAccount().AccountNumber);
        }

        [Fact]
        public void WhenPostImageIsMissing_PostOperationEventExitsEarly()
        {
            var existing = BuildAccount("Before", "A-100", "555-0100");
            var target = new Account { Id = AccountId, Name = "After" };
            Seed(existing);

            var ctx = BuildUpdateContext(target, preImage: existing);

            Context.ExecutePluginWith<AccountUpdatePlugin>(ctx);

            Assert.Equal("Before", RetrieveAccount().Name);
        }

        private static Account BuildAccount(string name, string accountNumber, string telephone1)
        {
            return new Account
            {
                Id = AccountId,
                Name = name,
                AccountNumber = accountNumber,
                Telephone1 = telephone1
            };
        }

        private Account RetrieveAccount() =>
            Service.Retrieve(Account.EntityLogicalName, AccountId,
                new ColumnSet(AccountFields.Name, AccountFields.AccountNumber, AccountFields.Telephone1)).ToEntity<Account>();
    }
}
