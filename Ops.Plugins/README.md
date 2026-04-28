# Ops.Plugins

Deployable Dataverse plugin assembly project.

## What belongs here

| Item | Purpose |
|------|---------|
| `Ops.Plugins.csproj` | SDK-style `net462` class library that emits `Ops.Plugins.dll`. |
| `OpportunityWonPlugin.cs` | Starter plugin example and template for new plugin classes. |
| `PluginKey.snk` | Local passwordless strong-name key used to sign the plugin assembly. It is generated on first build and ignored by git. Replace with your org key if needed. |
| `New-PluginSigningKey.ps1` | Build helper that creates `PluginKey.snk` with Windows SDK `sn.exe` when the key is missing. |

## Build

From the repo root:

```powershell
dotnet build Ops.Plugins/Ops.Plugins.csproj -c Release
```

If `PluginKey.snk` is missing, the build creates it automatically on Windows as a passwordless `.snk` strong-name key pair.

To create it explicitly after changing into this folder:

```powershell
.\New-PluginSigningKey.ps1 -Path .\PluginKey.snk
```

If your machine does not have `sn.exe`, install the .NET Framework SDK component for Visual Studio or run this from a Visual Studio Developer PowerShell while in this folder:

```powershell
sn -k PluginKey.snk
```

Output:

```text
Ops.Plugins/bin/Release/net462/Ops.Plugins.dll
```

## Notes

This project imports source from `Ops.Plugins.Shared` and `Ops.Plugins.Model` through `.projitems` files. Those shared projects do not produce separate DLLs; the deployable output stays one signed assembly.
