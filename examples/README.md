# Examples

Two complete examples showing the full plugin base library surface area.
Each example includes a plugin class and a paired test class.

---

## Example 1 — Standard CRUD Plugin: `OpportunityWonPlugin`

**Files:** `OpportunityWonPlugin.cs` + `OpportunityWonPluginTests.cs`

**What it shows:**
- `PluginBase` inheritance with both required constructors
- `HasChangedAttribute` early-exit guard
- `GetTarget()` and `GetPreImage<Entity>()` with named image
- `CrmFormatter` (instance) for OptionSet label resolution: `"3 (Won)"`
- `CrmFormat.Of()` (static) for Money and DateTime formatting
- `HasValue()` entity extension for null-safe attribute presence check
- `TraceLevel.Info` vs `TraceLevel.Verbose` annotations
- Lazy `Func<string>` trace overload for zero-cost suppressed messages
- `OrganizationService.Update()` for a minimal update entity

**PRT registration (required before testing against a live org):**

| Field | Value |
|-------|-------|
| Message | Update |
| Primary Entity | opportunity |
| Stage | PostOperation |
| Execution Mode | Synchronous |
| Filtering Attributes | `statuscode` |
| Pre-Image Name | `PreImage` |
| Pre-Image Attributes | `statuscode, actualclosedate` |

---

## Example 2 — Custom API Plugin: `GetOpportunitySummaryApi`

**Files:** `GetOpportunitySummaryApi.cs` + `GetOpportunitySummaryApiTests.cs`

**What it shows:**
- `IsCustomApi` guard
- `GetInputParameter<T>()` for typed input reading
- `SetOutputParameter()` for writing API response values
- `GetRecordOrDefault<T>()` for null-safe record retrieval by Id
- `CrmFormatter` + `CrmFormat` for composite summary string
- `GetAttributeValue<T>(logicalName, defaultValue)` overload for strings with fallback
- Graceful not-found path returning typed output parameters

**Custom API definition (create in your solution before registering the plugin):**

| Field | Value |
|-------|-------|
| Unique Name | `ops_GetOpportunitySummary` |
| Binding Type | Global |
| Input: `opportunityid` | Type: EntityReference, Required: true |
| Output: `found` | Type: Boolean |
| Output: `summary` | Type: String |

**PRT registration:**

| Field | Value |
|-------|-------|
| Message | `ops_GetOpportunitySummary` |
| Stage | PostOperation |
| Execution Mode | Synchronous |

---

## Running the Tests

From the solution root:

```powershell
dotnet test
```

Or open **Test Explorer** in Visual Studio and run all.

Tests use **FakeXrmEasy.9** (V1, MIT, free commercial use). No live Dataverse connection needed.

---

## Deploying Updates via PAC CLI

After first-time registration in PRT, push code changes with:

```bash
pac plugin push \
  --pluginId <your-assembly-guid-from-prt> \
  --pluginFile examples/bin/Release/net462/Ops.Crm.Plugins.Examples.dll \
  --type Assembly
```

`--type Assembly` is required — the default is `Nuget` and will fail on a `.dll`.

The assembly GUID is shown in PRT when you select the registered assembly in the tree (properties panel on the right).

---

## Adapting for Your Project

1. Copy the plugin file into `YourClient.Crm.Plugins/`
2. Copy the test file into `YourClient.Crm.Plugins.Tests/`
3. Find and replace `Ops.Crm.Plugins.Examples` → `YourClient.Crm.Plugins`
4. Rename the class and adjust business logic
5. Register the step in PRT as described above
6. Deploy updates via `pac plugin push` (see root README.md for full command and macOS setup)

---

## What These Examples Do Not Show

| Pattern | Where to find it |
|---------|-----------------|
| Shared variables between plugins | `PluginBase.cs` — `GetSharedVariable` / `SetSharedVariable` |
| PreValidation step (throw to block) | Same base — register on Stage 10, throw `InvalidPluginExecutionException` |
| Custom unsecure config keys | `PluginLogger.cs` — `PluginConfig.Get()` / `GetInt()` / `GetBool()` |
| AppInsights level control | `PluginLogger.GlobalLevel = TraceLevel.Info` in static initializer |
| QueryExpression fluent builder | `PluginExtensions.cs` — `WithNoLock().WithTopCount().WithCondition()` |
| Multi-select OptionSet formatting | `CrmFormat.cs` — `CrmFormat.Of(OptionSetValueCollection)` |
