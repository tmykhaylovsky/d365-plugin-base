# Ops.Plugins

Deployable Dataverse plugin assembly project.

## What belongs here

| Item | Purpose |
|------|---------|
| `Ops.Plugins.csproj` | SDK-style `net462` class library that emits `Ops.Plugins.dll`. |
| `OpportunityWonPlugin.cs` | Starter plugin example and template for new plugin classes. |
| `PluginKey.snk` | Passwordless strong-name key used to sign the plugin assembly. This starter repo allows this specific key to be committed so every machine builds the same assembly identity. |
| `New-PluginSigningKey.ps1` | Build helper that creates `PluginKey.snk` with Windows SDK `sn.exe` when the key is missing and ensures the project references it. |

## Build

From the repo root:

```powershell
dotnet build Ops.Plugins/Ops.Plugins.csproj -c Release
```

If `PluginKey.snk` is missing, the build creates it automatically on Windows as a passwordless `.snk` strong-name key pair.

Committing this `.snk` is convenient for a starter template or shared dev/test signing identity. It does not let someone alter an existing DLL, but it does let them build another DLL with the same strong-name identity. Use an organization-controlled private key instead if that distinction matters for your production process.

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
