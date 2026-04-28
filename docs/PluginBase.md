# OPS Crm Shared — Architecture Decision Log

**Last updated:** 2026-04-27
**Purpose:** Dataverse plugin base library for .NET Framework 4.6.2 classic plugin assemblies.
**Audience:** Developer returning to this codebase — explains WHY decisions were made, not HOW to use it (see README.md for that).

> .NET 4.8 support confirmed by Microsoft for Q4 2026. Structure is designed for a `TargetFramework` bump with no rework.

---

## Library Evaluation Summary

No single community library covered all requirements. Evaluated 8+ repos across 3 research passes.

| Library | Stars | Decision | Reason |
|---------|-------|----------|--------|
| [delegateas/XrmPluginCore](https://github.com/delegateas/XrmPluginCore) | 1 (active Apr 2026) | Pattern reference | Best modern pattern: net462+net8, typed images, custom API, MIT |
| [daryllabar/DLaB.Xrm](https://github.com/daryllabar/DLaB.Xrm) | 150+ | Replaced | Source NuGet — decided to internalize rather than depend |
| [DynamicsValue/fake-xrm-easy](https://github.com/DynamicsValue/fake-xrm-easy) | 900+ | Testing only | Commercial license required; V2 not suitable for enterprise without purchase |
| [jordimontana82/fake-xrm-easy](https://github.com/jordimontana82/fake-xrm-easy) | 266 | **Used (MIT)** | V1 archived June 2024, MIT license, free commercial use, FakeXrmEasy.9 NuGet |
| [rappen/JonasPluginBase](https://github.com/rappen/JonasPluginBase) | 33 | Pattern reference | Plugin bag / context wrapper pattern informed LocalPluginContext |
| [kip-dk/dynamics-plugin](https://github.com/kip-dk/dynamics-plugin) | 11 | Skip | SOLID but too opinionated (reflection-based DI); studied image injection pattern |
| [emerbrito/XrmUtils-PluginExtensions](https://github.com/emerbrito/XrmUtils-PluginExtensions) | 9 | Skip | Light maintenance, no test setup |

**Result:** All five source files are written from scratch, synthesizing patterns from XrmPluginCore, JonasPluginBase, FakeXrmEasy, and Microsoft samples. Zero runtime dependencies beyond `Microsoft.CrmSdk.CoreAssemblies`.

---

## File Inventory and Design Rationale

### PluginBase.cs

Abstract base implementing `IPlugin`. Houses `LocalPluginContext` as an inner class.

**Key decisions:**
- Both constructors (parameterless + unsecure/secure config) are required. Dataverse invokes the parameterless one when no config is registered on the step; the two-parameter one when config strings are present.
- `LocalPluginContext` is the canonical abstraction from FakeXrmEasy / Microsoft samples — all service resolution in one place, plugin subclasses never call `serviceProvider.GetService()` directly.
- `HasChangedAttribute` compares Target vs PreImage. Returns `true` when no pre-image exists (first-time set).
- `GetSharedVariable<T>` checks both `ExecutionContext.SharedVariables` and `ParentContext?.SharedVariables` — required because PreValidation-set variables live on `ParentContext` for Pre/PostOperation stages.
- `ParentContext` is exposed but null for plugins not triggered by a parent pipeline operation.
- Depth is logged (Verbose) but never gated on — Microsoft explicitly warns against using Depth for business logic.
- `PluginStage` enum values match Dataverse SDK constants (PreValidation=10, PreOperation=20, PostOperation=40).

### PluginLogger.cs

Dual-write logger: `ITracingService` always; `Microsoft.Xrm.Sdk.PluginTelemetry.ILogger` as graceful no-op until AppInsights is connected.

**Key decisions:**
- `using Microsoft.Xrm.Sdk.PluginTelemetry` — NOT `Microsoft.Extensions.Logging`. The wrong namespace compiles cleanly but `ILogger` resolves to null at runtime with no error.
- `GlobalLevel` static property defaults to `TraceLevel.Verbose` (log everything). Control via static initializer in the consuming plugin project or a future config mechanism. Not exposed via PRT unsecure config — that was intentionally removed to reduce PRT field management.
- `Func<string>` lazy overload prevents string allocation and interpolation on suppressed trace calls — preferred for any message involving entity attribute access.
- Stack trace extraction filters out `Microsoft.Crm.`, `Microsoft.Xrm.`, `System.Runtime.`, `System.Threading.`, `System.Reflection.`, `System.AppDomain.`, `System.Web.`. Keeps up to 5 application frames. Falls back to raw top frames when exception originates in platform code. Chains through `InnerException` up to 3 levels.
- `PluginConfig` (`public static`) parses `Key=Value;Key2=Value2` unsecure config strings. Available for plugin-specific config keys (e.g. timeout, feature flags) from consuming plugin constructors. TraceLevel is no longer one of them. Made `public` so consumer projects can call `PluginConfig.GetInt(unsecureConfig, "TimeoutSeconds", 30)` directly without wrapping or re-implementing the parser.
- `ITracingService` has a 10KB limit per plugin execution — older lines are removed first. The Func overload and level gating help stay within budget.

### PluginExtensions.cs

Self-contained. No DLaB.Xrm dependency.

**Key decisions:**
- `GetAttributeValue<T>(logicalName, defaultValue)` overload — the SDK built-in returns `default(T)` and cannot distinguish "absent" from "zero/false" for value types. This overload returns `defaultValue` when the attribute is absent or null.
- `GetFirst<T>` sets `TopCount = 1` automatically and throws `InvalidPluginExecutionException` if none found.
- `GetAll<T>` uses paged retrieval with a `maxRecords` guard that throws rather than silently over-fetching. Default 5000.
- `GetRecordOrDefault<T>` catches by error message `0x80040217` (Entity Does Not Exist). Fragile but acceptable — `FaultException<OrganizationServiceFault>` catching requires `System.ServiceModel` import, not worth the dependency for this pattern.
- `AssociateRecord` renamed (not `Associate`) to avoid compiler ambiguity with `IOrganizationService.Associate`.
- `ExecuteMultipleRequest` was considered and removed — Microsoft explicitly prohibits it inside plugins.
- `ToFetchXml` involves a service round-trip — Verbose trace use only during development.
- `QueryExpression` fluent extensions (`WithTopCount`, `WithNoLock`, `WithOrdering`, `WithCondition`) return `QueryExpression` for chaining.
- `NoLock = true` on read-only queries reduces blocking on high-volume environments.

### CrmFormat.cs

Two-tier formatting:

**Static `CrmFormat`** — zero cost, no service. `Of(OptionSetValue)` → `"3"`.

**Instance `CrmFormatter`** — with `IOrganizationService`, lazy `RetrieveAttributeRequest` per `entityName.attributeName`, cached for plugin execution lifetime. `Of(OptionSetValue, entityName, attributeName)` → `"3 (Won)"`.

**Key decisions:**
- Display labels are not stored on `OptionSetValue` — they live in metadata only. The static class cannot provide them without a service call.
- One `RetrieveAttributeRequest` per distinct `entityName.attributeName` pair, then cached. Subsequent calls in the same execution are dictionary lookups.
- `CrmFormatter` constructor takes the system-level `OrganizationService` (not initiating user) — metadata is not security-trimmed per user.
- `Of(Entity)` and `Of(ParameterCollection)` accept `params string[] include` — pass nothing for full dump, pass attribute logical names for targeted traces.
- `OfObject` catch-all dispatches typed formatting for `bool`, `DateTime`, `byte[]`, `EntityCollection`, `OptionSetValueCollection`.
- `EntityReference.Name` is included in `Of(EntityReference)` when populated — useful when the lookup field includes the name.
- `Of(DateTime value)` and `Of(DateTime? value)` are explicit typed overloads (not just `OfObject` dispatch). Required because `CrmFormat.Of(entity.GetAttributeValue<DateTime>("field"))` resolves to `DateTime` not `object` — the `OfObject` overload was not reachable via typed call sites. `Of(DateTime?)` covers the `preImage?.GetAttributeValue<DateTime>()` nullable pattern.

### PluginTestBase.cs

**Key decisions:**
- FakeXrmEasy.9 (`jordimontana82`, V1, MIT) — free commercial use, archived June 2024. Stable API, no license friction for enterprise engagements.
- FakeXrmEasy V2+ (DynamicsValue) requires commercial license for enterprise — excluded.
- `PluginTestBase` is abstract — test classes inherit and get `Context` + `Service` pre-wired.
- `BuildEntity` uses C# 7.0 value tuples `(string key, object value)[]` for concise test setup.
- `Seed(params Entity[])` wraps `Context.Initialize()` — call before executing the plugin.
- `BuildCustomApiContext` sets empty `InputParameters` dict — caller populates before execution.
- `PluginStage` enum is re-declared locally in the Testing namespace to avoid a project reference to the Shared assembly's enum from the test class.

---

## Deployment Architecture

```
Source change
  → dotnet build -c Release
  → pac plugin push --pluginId <guid> --pluginFile <path>.dll --type Assembly
  → PRT (step registration changes only — not touched by pac plugin push)
```

**Verified command** (tested against qlaprod.crm.dynamics.com, 2026-04-27):
```bash
pac plugin push \
  --pluginId c8e7e93a-b442-f111-88b5-7c1e52577e1e \
  --pluginFile examples/bin/Release/net462/Ops.Crm.Plugins.Examples.dll \
  --type Assembly
# Output: Plug-in assembly was updated successfully
```

**`pac plugin push` boundaries:**
- Updates the assembly binary in Dataverse. Nothing else.
- `--pluginId` is required — this is the GUID of the existing `pluginassembly` record. There is no create path via pac; first registration is always PRT.
- `--type Assembly` is required for `.dll` files. Default is `Nuget`, which fails on a DLL.
- Step registrations (message, entity, stage, filtering attributes, images, unsecure config) are untouched.

**macOS arm64:** pac requires x64 .NET 10 and runs under Rosetta. See README.md "macOS pac CLI Setup" for the one-time install steps.

**Dependent assembly path** (Shared as separate DLL):
- `pac plugin init --skip-signing` scaffolds a NuGet package project
- First registration: PRT → Register New Package → select `.nupkg`
- Updates: `pac plugin push --type Nuget --pluginFile <path>.nupkg --pluginId <guid>`

---

## Explicitly Rejected Patterns

| Pattern | Reason |
|---------|--------|
| `if (context.ExecutionContext.Depth > 1) return;` | Microsoft docs: do not gate business logic on Depth. Breaks legitimate multi-plugin pipelines silently. |
| `ExecuteMultipleRequest` in plugins | Microsoft best practice violation: prohibited inside plugin/workflow context. |
| `setNotification` on web resource controls | Silently fails — relevant for HC web resources, not plugins. Kept in d365-ux-form-patterns.md. |
| `PluginStepAttribute` (spkl-style) | Community convention only, not PAC CLI native. Worth adding when CI/CD pipeline is established via solution-based deployment. |
| TraceLevel via unsecure config | Removed — PRT field overhead for marginal benefit. Use `PluginLogger.GlobalLevel` static property instead. |
| DLaB.Xrm.Source NuGet | Initially planned; removed in favor of self-contained source to eliminate any runtime dependency. |
| FakeXrmEasy V2 (DynamicsValue) | Commercial license required for enterprise use. V1 (MIT) chosen. |
