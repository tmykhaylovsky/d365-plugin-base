# PAC CLI Quick Reference

Quick commands for this starter solution. Run these from the repository root unless noted.

## Install And Select Environment

```powershell
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
pac auth create --url https://<your-org>.crm.dynamics.com
pac auth list
pac auth select --index <n>
```

Use `pac auth list` before deploys or model regeneration when you work across multiple environments.

## Build Deployable Assembly

```powershell
dotnet build Ops.Plugins/Ops.Plugins.csproj -c Release
```

Output:

```text
Ops.Plugins/bin/Release/net462/Ops.Plugins.dll
```

## Push Existing Plugin Assembly

```powershell
pac plugin push `
  --pluginId <pluginassembly-guid> `
  --pluginFile Ops.Plugins/bin/Release/net462/Ops.Plugins.dll `
  --type Assembly
```

`pac plugin push` updates the assembly binary. It does not create or update step details such as message, entity, stage, mode, filtering attributes, image aliases, image attributes, or config strings.

## Sync Plugin Step Registration

Planned lightweight tooling should run after `pac plugin push`:

```powershell
dotnet run --project Ops.Plugins.Registration/Ops.Plugins.Registration.csproj -- `
  --assembly Ops.Plugins/bin/Release/net462/Ops.Plugins.dll `
  --pluginAssemblyId <pluginassembly-guid>
```

Default behavior should be dry-run: inspect `RegisteredEvent` metadata, compare it to Dataverse `sdkmessageprocessingstep` and `sdkmessageprocessingstepimage` rows, then print a concise summary of creates and updates.

Use an explicit apply flag for changes:

```powershell
dotnet run --project Ops.Plugins.Registration/Ops.Plugins.Registration.csproj -- `
  --assembly Ops.Plugins/bin/Release/net462/Ops.Plugins.dll `
  --pluginAssemblyId <pluginassembly-guid> `
  --apply
```

The tool should create missing steps/images and correct mismatched stage, mode, rank, filtering attributes, image aliases, and image attributes. It should never delete or disable existing steps without a separate explicit flag.

## Regenerate Early-Bound Model

```powershell
pac modelbuilder build `
  --settingsTemplateFile Ops.Plugins.Model/builderSettings.json `
  --outdirectory Ops.Plugins.Model
```

After regeneration, update `Ops.Plugins.Model/Ops.Plugins.Model.projitems` when entity, option set, or message files are added or removed.

## Step Registration Automation Status

The plugin code declares expected step shape through `RegisteredEvent`, including message, entity, stage, mode, filtering attributes, image aliases, and image attributes.

Current behavior:

- Runtime dispatch validates message, entity, stage, and mode.
- Runtime tracing reports when a required pre-image or post-image alias is missing.
- Runtime tracing reports when an `Update` step declares filtering attributes but the fired Target contains none of them. This suggests the step may be registered too broadly.
- Runtime tracing reports when an existing image does not contain expected image attributes. This is diagnostic only, because absent image attributes and null Dataverse values can look the same to plugin code.
- Runtime code cannot directly read the Dataverse step registration, so deployment automation is still the right place for authoritative validation.

Future automation can read `RegisteredEvent` metadata and create missing `sdkmessageprocessingstep` and `sdkmessageprocessingstepimage` rows after the assembly is pushed.
