using System;
using System.Collections.Generic;
using FakeXrmEasy;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Ops.Plugins.Shared;

// NuGet: FakeXrmEasy.9 (jordimontana82, MIT license — free for commercial use)
// Target: .NET Framework 4.6.2
//
// Inherit from this class in every plugin test class.
// It wires up the in-memory Dataverse context and provides
// builder methods for constructing plugin execution contexts.
//
// Usage:
//   public class OpportunityWonPluginTests : PluginTestBase
//   {
//       [Fact]
//       public void GivenOpportunityWon_SetsCloseDate()
//       {
//           var opportunity = new Opportunity { Id = Guid.NewGuid() };
//           opportunity.StatusCode = opportunity_statuscode.Won;
//           var ctx = BuildUpdateContext(opportunity);
//           Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);
//           // assert on Context.Data or service calls
//       }
//   }

namespace Ops.Plugins.Testing
{
    public abstract class PluginTestBase
    {
        // In-memory Dataverse context — pre-seed with Context.Initialize(entities)
        protected XrmFakedContext Context { get; }

        // Pre-wired organization service backed by the in-memory context
        protected IOrganizationService Service { get; }

        protected PluginTestBase()
        {
            Context = new XrmFakedContext();
            Service = Context.GetOrganizationService();
        }

        // -------------------------------------------------------------------------
        // Context builder — configure the plugin execution context for a test
        // -------------------------------------------------------------------------

        protected XrmFakedPluginExecutionContext BuildContext(
            string          messageName,
            string          primaryEntityName,
            Entity          target            = null,
            PluginStage     stage             = PluginStage.PostOperation,
            Entity          preImage          = null,
            Entity          postImage         = null,
            Guid?           userId            = null,
            Guid?           initiatingUserId  = null)
        {
            var ctx = Context.GetDefaultPluginContext();
            ctx.MessageName         = messageName;
            ctx.PrimaryEntityName   = primaryEntityName;
            ctx.Stage               = (int)stage;
            ctx.UserId              = userId ?? Guid.NewGuid();
            ctx.InitiatingUserId    = initiatingUserId ?? ctx.UserId;
            ctx.CorrelationId       = Guid.NewGuid();
            ctx.Depth               = 1;

            ctx.InputParameters     = new ParameterCollection();
            ctx.OutputParameters    = new ParameterCollection();
            ctx.SharedVariables     = new ParameterCollection();
            ctx.PreEntityImages     = new EntityImageCollection();
            ctx.PostEntityImages    = new EntityImageCollection();

            if (target != null)
                ctx.InputParameters["Target"] = target;

            if (preImage != null)
                ctx.PreEntityImages[PluginImageNames.PreImage] = preImage;

            if (postImage != null)
                ctx.PostEntityImages[PluginImageNames.PostImage] = postImage;

            return ctx;
        }

        // Shorthand for Create context
        protected XrmFakedPluginExecutionContext BuildCreateContext(
            Entity  target,
            Entity  postImage        = null,
            Guid?   userId           = null) =>
            BuildContext(Messages.Create, target.LogicalName, target,
                PluginStage.PostOperation, postImage: postImage, userId: userId);

        // Shorthand for Update context
        protected XrmFakedPluginExecutionContext BuildUpdateContext(
            Entity  target,
            Entity  preImage         = null,
            Entity  postImage        = null,
            Guid?   userId           = null) =>
            BuildContext(Messages.Update, target.LogicalName, target,
                PluginStage.PostOperation, preImage, postImage, userId);

        // Shorthand for Delete context (Target is an EntityReference)
        protected XrmFakedPluginExecutionContext BuildDeleteContext(
            string  entityName,
            Guid    recordId,
            Entity  preImage         = null,
            Guid?   userId           = null)
        {
            var ctx = BuildContext(Messages.Delete, entityName,
                stage: PluginStage.PreOperation, preImage: preImage, userId: userId);
            ctx.InputParameters["Target"] = new EntityReference(entityName, recordId);
            return ctx;
        }

        // -------------------------------------------------------------------------
        // Entity builder — fluent helper for constructing test entities
        // -------------------------------------------------------------------------

        protected static Entity BuildEntity(string logicalName, Guid id, params (string key, object value)[] attributes)
        {
            var entity = new Entity(logicalName, id);
            foreach (var (key, value) in attributes)
                entity[key] = value;
            return entity;
        }

        // Pre-seeds the in-memory context with existing records before executing a plugin
        protected void Seed(params Entity[] records) =>
            Context.Initialize(records);

        // -------------------------------------------------------------------------
        // Custom API context builder
        // -------------------------------------------------------------------------

        protected XrmFakedPluginExecutionContext BuildCustomApiContext(
            string messageName,
            Dictionary<string, object> inputParameters = null,
            Guid? userId = null)
        {
            var ctx = Context.GetDefaultPluginContext();
            ctx.MessageName       = messageName;
            ctx.Stage             = (int)PluginStage.PostOperation;
            ctx.UserId            = userId ?? Guid.NewGuid();
            ctx.InitiatingUserId  = ctx.UserId;
            ctx.CorrelationId     = Guid.NewGuid();
            ctx.Depth             = 1;

            ctx.InputParameters   = new ParameterCollection();
            ctx.OutputParameters  = new ParameterCollection();
            ctx.SharedVariables   = new ParameterCollection();
            ctx.PreEntityImages   = new EntityImageCollection();
            ctx.PostEntityImages  = new EntityImageCollection();

            if (inputParameters != null)
                foreach (var kvp in inputParameters)
                    ctx.InputParameters[kvp.Key] = kvp.Value;

            return ctx;
        }

        // -------------------------------------------------------------------------
        // Pipeline stage enum matching PluginBase.PluginStage
        // -------------------------------------------------------------------------
        protected enum PluginStage
        {
            PreValidation = 10,
            PreOperation  = 20,
            PostOperation = 40
        }
    }
}
