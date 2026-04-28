# Ops.Plugins

Deployable Dataverse plugin assembly project.

## What belongs here

| Item | Purpose |
|------|---------|
| `Ops.Plugins.csproj` | SDK-style `net462` class library that emits `Ops.Plugins.dll`. |
| `OpportunityWonPlugin.cs` | Starter plugin example and template for new plugin classes. |
| `PluginKey.snk` | Strong-name key used to sign the plugin assembly. Replace with your org key if needed. |

## Build

From the repo root:

```powershell
dotnet build Ops.Plugins/Ops.Plugins.csproj -c Release
```

Output:

```text
Ops.Plugins/bin/Release/net462/Ops.Plugins.dll
```

## Notes

This project imports source from `Ops.Plugins.Shared` and `Ops.Plugins.Model` through `.projitems` files. Those shared projects do not produce separate DLLs; the deployable output stays one signed assembly.
