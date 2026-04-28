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

PAC stores auth profiles under the current Windows user profile. That is the safest
way to cache interactive environment access locally; do not copy PAC auth profile
files into the repo.

For the registration sync tool, prefer either interactive `--environment <url>` or
a user environment variable containing a connection string:

```powershell
setx DATAVERSE_CONNECTION_QLAPROD "AuthType=OAuth;Url=https://qlaprod.crm.dynamics.com;LoginPrompt=Auto"
```

Ignored repo-local notes can live under `.claude/`, but keep only URLs, command
templates, and environment variable names there. Do not store passwords, client
secrets, tokens, or literal connection strings in markdown.

For fixed step impersonation, prefer a local alias-to-`systemuserid` map:

```json
{
  "Ops Plugin Service": "00000000-0000-0000-0000-000000000000"
}
```

Pass it with `--userMap <path>`, or let the tool use the default ignored path
`.claude/dataverse-registration-users.local.json`. Plugin code should use
`RegisteredEvent.CallingUser` for the default run-as behavior, or a stable alias
that resolves to a per-environment `systemuserid`.

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

Run the registration sync tool after `pac plugin push`. Dry-run is the default and performs no writes:

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
  --apply
```

The tool creates missing steps/images and corrects mismatched rank, filtering attributes, image message property, and image attributes. Existing-step matching uses plug-in type, message, primary entity, stage, and mode. Extra steps/images are reported only; the tool does not delete, disable, enable, change impersonation, or change secure/unsecure configuration.

The tool validates declared entity logical names before apply, creates steps before
images, and scopes new step creation to the matched plugin assembly. If a matching
step is disabled, it is treated as an existing step and reported as a warning rather
than duplicated.

Step metadata can also include an optional description and Run in User's Context.
Calling User is represented by a null `impersonatinguserid`; fixed users should be
resolved from `systemuserid` GUIDs rather than display names.

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
- `Ops.Plugins.Registration` reads the same metadata from the built DLL and can create or update Dataverse registration rows after the assembly is pushed.
- Runtime code still cannot directly read the Dataverse step registration, so deployment automation remains the authoritative validation point.
