# Dataverse Plugin Best Practices

This solution is a starter for signed Dataverse plugin assemblies. These practices apply across `Ops.Plugins`, `Ops.Plugins.Shared`, `Ops.Plugins.Model`, and `Ops.Plugins.Testing`.

## Authoring plugins

- Keep plugin classes stateless. Dataverse can cache and reuse `IPlugin` instances, so do not store services, execution context, target records, or other invocation data in instance fields.
- Declare the expected runtime and deployment shape in `GetRegisteredEvents()`: message, primary entity, pipeline stage, execution mode, handler, filtering attributes, required image aliases, and image attributes.
- Point each registered event at a concise step-oriented handler name, such as `OppPostOpUpdateSync`.
- Use `Messages.*`, `SdkMessageProcessingStepMode`, and `PluginImageNames.*` from `Ops.Plugins.Shared` instead of raw strings for standard Dataverse values.
- Use generated early-bound model types from `Ops.Plugins.Model` whenever available. Prefer `Opportunity.EntityLogicalName`, `Opportunity.Fields.*`, and generated option-set enums over raw logical names or integer option values.
- Convert Dataverse `Target` and images to early-bound types with `ToEntity<T>()`; do not replace input parameters with early-bound entity instances.
- Throw `InvalidPluginExecutionException` for expected business failures that should be shown as plugin errors.

## Registration shape

- Register `Update` steps with filtering attributes whenever possible. Do not include the primary key as a filtering attribute.
- Register only the image aliases and image columns the handler actually needs. Prefer images over retrieves when comparing before and after values.
- Keep `RegisteredEvent` filtering and image attribute metadata aligned with the real step registration so tests and deployment automation have one source of intent.
- Treat missing filtering attributes and missing image columns as deployment-time validation concerns. Runtime plugin code can trace likely registration issues, but it cannot directly read the Dataverse step registration.
- Use runtime registration diagnostics as hints: a fired `Update` Target with none of the expected filtering attributes suggests an over-broad step, while an image missing an expected attribute may mean either image misconfiguration or a selected column whose value is null.
- Choose stage intentionally:
  - Use `PreValidation` for checks that can cancel before the main transaction when security and transaction semantics fit.
  - Use `PreOperation` to modify fields on the target entity before Dataverse writes it.
  - Use `PostOperation` for logic that depends on post-operation values; synchronous post-operation steps still run within the transaction. Avoid updating the same row here unless you intentionally want another pipeline event.
- Set execution order intentionally when multiple steps share the same entity, message, and stage.
- Avoid duplicate step registrations. The same class registered twice for the same event will run twice.
- Use asynchronous post-operation steps for non-blocking work that does not need to affect the caller's transaction.

## Data access and performance

- Retrieve only the columns needed. Avoid `ColumnSet(true)` in production plugin paths.
- Keep synchronous plugins fast. The user operation waits for all synchronous steps in the pipeline.
- Avoid broad `Retrieve` and `RetrieveMultiple` plugins; they affect every load path that touches those messages.
- Do not use `ExecuteMultipleRequest` or `ExecuteTransactionRequest` inside plugins.
- Do not use parallel or multi-threaded execution inside plugins.
- Set explicit timeouts for external calls. Treat outbound calls as exceptional in synchronous plugins.
- Keep FetchXML and QueryExpression helpers on explicit logical names from early-bound `Fields.*` constants. Do not feed untrusted values into XML-building helpers.

## Context, recursion, and shared variables

- Do not depend on `Depth` for business logic. It exists for platform loop prevention, and configuration changes can alter call depth.
- Use targeted recursion guards or shared variables when a plugin can trigger the same pipeline again.
- Shared variable values must be serializable. For later stages reading values set in `PreValidation`, check `ParentContext.SharedVariables` as well.
- Keep `UnsecureConfig` for non-sensitive behavior flags. Keep secrets out of plugin step configuration unless the platform secure config storage is explicitly appropriate.

## Logging and diagnostics

- Use `context.Trace(...)` for execution diagnostics and include identifiers that help correlate a run.
- Keep verbose entity dumps limited to development or troubleshooting paths. They can expose data and consume storage.
- Trace logging consumes Dataverse organization storage; enable broad trace logging for investigations and turn it down afterward.
- Use `Microsoft.Xrm.Sdk.PluginTelemetry.ILogger` when available. Optional services must be resolved defensively because local test tools may not provide them.

## Early-bound model maintenance

- Regenerate model code from `Ops.Plugins.Model/builderSettings.json`; do not hand-edit generated entity or option-set files except for intentional cleanup before committing.
- After regeneration, sync `Ops.Plugins.Model.projitems` with added or removed generated files.
- Keep generated public-repo models minimal. Remove accidental internal/custom-prefixed columns before committing when this repository should not expose them.
- Add new entities to `entityNamesFilter` only when the plugin or tests need them.

## Shared project boundaries

- `Ops.Plugins.Shared` and `Ops.Plugins.Model` are shared projects, not deployable DLLs. Their `.projitems` files control what compiles into `Ops.Plugins.dll`.
- Add shared infrastructure only when it is broadly useful across plugins. Keep client-specific business logic in `Ops.Plugins`.
- Keep helper APIs conservative: no hidden all-column retrieves, no hidden bulk reads without limits, and no surprise writes.
- Keep C# plugin helper calls compact when readable. Short method calls and LINQ chains can stay on one line; wrap only when the line becomes hard to scan.

## Testing

- Tests should exercise business behavior and registration guards: message, primary entity, stage, mode, required images, filtering behavior, and missing-image exits.
- Build records with early-bound model classes when available.
- Use `Messages.*`, `PluginImageNames.*`, `Opportunity.EntityLogicalName`, and `Opportunity.Fields.*` in tests.
- Raw logical-name strings are acceptable only for generic infrastructure, explicit negative-path tests, or entities not generated into `Ops.Plugins.Model`.
- Keep test project references narrow. `Ops.Plugins.Testing` should reference `Ops.Plugins.csproj`; shared and model code are tested through the compiled plugin assembly.

## Build, signing, and deployment

- Build and test sequentially on Windows to avoid file locks in `obj` and `bin`.
- Keep the signing-key decision explicit. The committed `.snk` is convenient for starter/dev/test assembly identity, but production environments may require an organization-controlled private key.
- `pac plugin push` updates the assembly binary. Step registration details such as stage, mode, filtering attributes, and images remain managed separately unless you automate that process.
- Step registration automation should be additive by default: create missing `sdkmessageprocessingstep` and image rows for the assembly being deployed, but do not rewrite existing steps unless an explicit update mode is requested.
- A lightweight registration sync tool should run in dry-run mode by default, show creates/updates/deletes before applying, and support explicit `-Apply` behavior for correcting filtering attributes and image definitions after `pac plugin push`.
- Rename namespaces only after the starter builds and tests cleanly.

## Microsoft references

- [Write a plug-in](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/write-plug-in)
- [Register a plug-in](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in)
- [Dataverse plugin and workflow best practices](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/best-practices/business-logic/)
- [Include filtering attributes with plug-in registration](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/best-practices/business-logic/include-filtering-attributes-plugin-registration)
- [Understand the execution context](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/understand-the-data-context)
- [IExecutionContext.Depth](https://learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.iexecutioncontext.depth)
- [Tracing and logging](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/logging-tracing)
