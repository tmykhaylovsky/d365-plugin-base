# Ops.Plugins.Registration

Console tool for syncing Dataverse plugin step and image registration from the built plugin assembly.

The tool reads `RegisteredEvent` metadata from `Ops.Plugins.dll`, compares it with `pluginassembly`, `plugintype`, `sdkmessageprocessingstep`, and `sdkmessageprocessingstepimage` rows in Dataverse, then prints a dry-run plan. It only writes when `--apply` is passed.

## Typical Flow

```powershell
dotnet build Ops.Plugins/Ops.Plugins.csproj -c Release

dotnet run --project Ops.Plugins.Registration/Ops.Plugins.Registration.csproj -- `
  --assembly Ops.Plugins/bin/Release/net462/Ops.Plugins.dll `
  --pluginAssemblyId <pluginassembly-guid> `
  --connectionString DATAVERSE_CONNECTION
```

Add `--apply` after reviewing the dry-run. Add `--pushAssembly` when you also want to update the matched `pluginassembly` binary from the DLL before comparing step metadata.

## Authentication

Prefer a user environment variable containing a Dataverse connection string for repeatable runs:

```powershell
setx DATAVERSE_CONNECTION_QLAPROD "AuthType=OAuth;Url=https://<org>.crm.dynamics.com;LoginPrompt=Auto"
```

PAC auth profiles can expire or become unreadable. If `pac modelbuilder` or `pac plugin push` fails with token-cache or refresh-token errors, create a fresh device-code profile:

```powershell
pac auth create --url https://<org>.crm.dynamics.com --deviceCode --name <short-name>
pac auth list
pac auth select --index <n>
```

Device-code auth avoids the embedded-browser path and is usually the least fussy repair.

## Run In User's Context

Plugin code should keep `RegisteredEvent.CallingUser` unless the step must run as a fixed Dataverse user. For a fixed user, put a stable alias in `runInUserContext`:

```csharp
runInUserContext: "Plugin Service User"
```

Then map that alias to a per-environment `systemuserid` in the default local file:

```text
%APPDATA%\Ops.Plugins\dataverse-registration-users.json
```

Example:

```json
{
  "Plugin Service User": "00000000-0000-0000-0000-000000000000"
}
```

You can also pass an explicit map with `--userMap <path>`. Keep this file out of source control; `systemuserid` values are environment-specific.

## Model Pattern

`Ops.Plugins.Registration` imports `Ops.Plugins.Model` and should use generated early-bound classes for registration tables:

- Use generated entity classes for create/update rows, such as `PluginAssembly`, `SdkMessageProcessingStep`, and `SdkMessageProcessingStepImage`.
- Use generated option-set enums for stage, mode, state, supported deployment, and image type.
- Use generated `Fields.*` constants in `QueryExpression`, `ColumnSet`, and criteria.
- Keep raw logical strings only where the model does not include the entity, such as the built-in `systemuser` reference used for impersonation.

## Safety

The sync is intentionally conservative. It creates missing steps/images and updates safe drift fields such as rank, filtering attributes, description, image message property, and image attributes. It reports extras, disabled steps, managed rows, unsecure configuration, and impersonation rather than silently changing or deleting them.
