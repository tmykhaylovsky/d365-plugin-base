# PAC CLI Quick Reference

Quick commands for this starter solution. Run these from the repository root unless noted.

## Install And Select Environment

```powershell
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
pac auth create --url https://<your-org>.crm.dynamics.com
pac auth create --url https://<your-org>.crm.dynamics.com --deviceCode --name <short-name>
pac auth list
pac auth select --index <n>
```

Use `pac auth list` before deploys or model regeneration when you work across multiple environments.
If PAC reports token-cache, DPAPI, or expired refresh-token errors, create a fresh
device-code profile and select it. Device-code auth avoids the embedded browser
and is the most reliable quick repair for stale local PAC profiles.

PAC stores auth profiles under the current Windows user profile. That is the safest
way to cache interactive environment access locally; do not copy PAC auth profile
files into the repo.

For the registration sync tool, prefer either interactive `--environment <url>` or
a user environment variable containing a connection string:

```powershell
setx DATAVERSE_CONNECTION_CONTOSO "AuthType=OAuth;Url=https://contoso.crm.dynamics.com;ClientId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=app://58145B91-0C36-4500-8554-080854F2AC97;LoginPrompt=Auto"
```

Ignored repo-local notes can live under `.claude/`, but keep only URLs, command
templates, and environment variable names there. Do not store passwords, client
secrets, tokens, or literal connection strings in markdown.

For fixed step impersonation, configure the repo-local run-as user map described
in [`Ops.Plugins.Registration/README.md`](Ops.Plugins.Registration/README.md#run-in-users-context).

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

Run the wrapper from the repository root. Dry-run is the default and performs no writes:

```powershell
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<your-org>.crm.dynamics.com
```

Apply after reviewing the dry-run:

```powershell
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<your-org>.crm.dynamics.com -Apply
```

Apply and update the matched plugin assembly binary in one command:

```powershell
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<your-org>.crm.dynamics.com -PushAssembly -Apply
```

In `-Apply` mode, the sync can automatically push the assembly when the only
blocking issue is a plug-in type that exists in the current DLL but is not yet
registered in Dataverse.

Use `-PushAssembly` when Dataverse already has the `pluginassembly` row but its
registered plug-in types are from an older DLL. The sync will push the current
DLL first, then compare step/image metadata. Old steps/images are reported as
extras for manual cleanup; they are not deleted automatically.

If an existing registered plug-in type is missing from the current DLL, the sync
stops before uploading the assembly and prints the stale type plus dependent
step/image counts. Review and retire those Dataverse registrations manually, then
rerun the command.

The lower-level console command is also available:

```powershell
dotnet run --project Ops.Plugins.Registration/Ops.Plugins.Registration.csproj -- `
  --assembly Ops.Plugins/bin/Release/net462/Ops.Plugins.dll `
  --pluginAssemblyId <pluginassembly-guid> `
  --connectionString DATAVERSE_CONNECTION
```

`DATAVERSE_CONNECTION` can be either the literal Dataverse connection string or the name of an environment variable that contains it.

For interactive OAuth, pass the environment URL:

```powershell
dotnet run --project Ops.Plugins.Registration/Ops.Plugins.Registration.csproj -- `
  --assembly Ops.Plugins/bin/Release/net462/Ops.Plugins.dll `
  --environment https://<your-org>.crm.dynamics.com
```

The tool inspects `RegisteredEvent` metadata, compares it to Dataverse `sdkmessageprocessingstep` and `sdkmessageprocessingstepimage` rows, then prints a concise summary of creates, updates, extras, warnings, and errors.

Use an explicit apply flag for changes:

```powershell
dotnet run --project Ops.Plugins.Registration/Ops.Plugins.Registration.csproj -- `
  --assembly Ops.Plugins/bin/Release/net462/Ops.Plugins.dll `
  --pluginAssemblyId <pluginassembly-guid> `
  --connectionString DATAVERSE_CONNECTION `
  --pushAssembly `
  --apply
```

The tool creates missing steps/images and corrects mismatched rank, filtering
attributes, step description, Run in User's Context, image message property, and
image attributes. Existing-step matching uses plug-in type, message, primary
entity, stage, and mode. Extra steps/images are reported only; the tool does not
delete, disable, enable, or change secure/unsecure configuration.

Use `--pushAssembly` when PAC auth is unavailable or you want one command to update
the matched `pluginassembly` binary before comparing step metadata. It updates the
assembly content, version, public key token, and culture on the existing assembly
row; initial assembly registration still belongs in PRT or an explicit deployment
process.

The tool validates declared entity logical names before apply, creates steps before
images, and scopes new step creation to the matched plugin assembly. If a matching
step is disabled, it is treated as an existing step and reported as a warning rather
than duplicated.

Step metadata can also include an optional description and Run in User's Context;
see [`Ops.Plugins.Registration/README.md`](Ops.Plugins.Registration/README.md#run-in-users-context).

## Regenerate Early-Bound Model

Use the wrapper script after editing `Ops.Plugins.Model/builderSettings.json`:

```powershell
.\Scripts\Update-EarlyBoundModel.ps1
```

Pass `-Environment` to target a specific Dataverse environment instead of the
currently selected PAC auth profile:

```powershell
.\Scripts\Update-EarlyBoundModel.ps1 -Environment https://<your-org>.crm.dynamics.com
```

The script runs `pac modelbuilder build` and then updates
`Ops.Plugins.Model/Ops.Plugins.Model.projitems` with generated `.cs` files.
Use `-NoProjItemsUpdate` if you want to inspect raw PAC output first.

The underlying PAC command is:

```powershell
pac modelbuilder build `
  --settingsTemplateFile Ops.Plugins.Model/builderSettings.json `
  --outdirectory Ops.Plugins.Model
```

Before regeneration, ensure PAC is authenticated with `pac auth create` and the
intended profile is selected with `pac auth select`, or pass `-Environment`.

## Step Registration Automation Status

The plugin code declares expected step shape through `RegisteredEvent`, including message, entity, stage, mode, filtering attributes, image aliases, and image attributes.

Current behavior:

- Runtime dispatch validates message, entity, stage, and mode.
- Runtime tracing reports when a required pre-image or post-image alias is missing.
- Runtime tracing reports when an `Update` step declares filtering attributes but the fired Target contains none of them. This suggests the step may be registered too broadly.
- Runtime tracing reports when an existing image does not contain expected image attributes. This is diagnostic only, because absent image attributes and null Dataverse values can look the same to plugin code.
- `Ops.Plugins.Registration` reads the same metadata from the built DLL and can create or update Dataverse registration rows after the assembly is pushed.
- Runtime code still cannot directly read the Dataverse step registration, so deployment automation remains the authoritative validation point.
