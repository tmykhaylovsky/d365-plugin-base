# Ops.Plugins

Deployable Dataverse plugin assembly project.

## What belongs here

| Item | Purpose |
|------|---------|
| `Ops.Plugins.csproj` | SDK-style `net462` class library that emits `Ops.Plugins.dll`. |
| `OpportunityWonPlugin.cs` | Starter plugin example and template for new plugin classes. |
| `PluginKey.snk` | Passwordless strong-name key used to sign the plugin assembly. This starter repo allows this specific key to be committed so every machine builds the same assembly identity. |

## Build

From the repo root:

```powershell
dotnet build Ops.Plugins/Ops.Plugins.csproj -c Release
```

If `PluginKey.snk` is missing, the build creates it automatically on Windows as a passwordless `.snk` strong-name key pair.

Committing this `.snk` is convenient for a starter template or shared dev/test signing identity. It does not let someone alter an existing DLL, but it does let them build another DLL with the same strong-name identity. Use an organization-controlled private key instead if that distinction matters for your production process.

To create it explicitly from the repository root:

```powershell
.\Scripts\New-PluginSigningKey.ps1 -ProjectPath .\Ops.Plugins\Ops.Plugins.csproj
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

## Plugin authoring conventions

The canonical guidance lives in [`../BEST_PRACTICES.md`](../BEST_PRACTICES.md). Keep this checklist aligned with that file.

- Declare expected runtime shape in `GetRegisteredEvents()` for each plugin: message, primary entity, stage, execution mode, and any required image name.
- Include deployment metadata in each `RegisteredEvent`: filtering attributes, pre-image and post-image attribute lists, optional description, and Run in User's Context when the step needs them.
- Point each registered event at a meaningfully named handler such as `OppPostOpUpdateSync`; avoid using generic handler names for business logic.
- Use `Messages.*`, `SdkMessageProcessingStepMode`, and `PluginImageNames.*` from `Ops.Plugins.Shared` for standard Dataverse message names, execution modes, and image aliases.
- Use generated early-bound model constants such as `Opportunity.EntityLogicalName` and `Opportunity.Fields.*` instead of raw logical-name strings whenever the entity is in `Ops.Plugins.Model`.
- Keep raw logical-name strings only for generic infrastructure, explicit negative-path tests, or entities not yet generated into the model.
