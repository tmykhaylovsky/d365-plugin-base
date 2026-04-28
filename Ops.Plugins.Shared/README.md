# Ops.Plugins.Shared

Shared source for Dataverse plugin infrastructure.

## What belongs here

| Item | Purpose |
|------|---------|
| `PluginBase.cs` | Base `IPlugin` implementation and `LocalPluginContext`. |
| `PluginLogger.cs` | Tracing and optional Application Insights logger wrapper. |
| `PluginExtensions.cs` | Dataverse SDK helper extensions. |
| `CrmFormat.cs` | Readable formatting for trace output. |
| `FetchXml/` | Lightweight FetchXML builder helpers. |

## How it is used

`Ops.Plugins/Ops.Plugins.csproj` imports `Ops.Plugins.Shared.projitems`, so these files compile into `Ops.Plugins.dll`.

## Testing note

FakeXrmEasy v1 does not support every service type that Dataverse exposes. Optional services, especially `Microsoft.Xrm.Sdk.PluginTelemetry.ILogger`, should be resolved defensively and treated as absent when unsupported.
