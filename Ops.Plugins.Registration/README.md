# Ops.Plugins.Registration

Console tool for syncing Dataverse plugin step and image registration from the built plugin assembly.

The tool reads `RegisteredEvent` metadata from `Ops.Plugins.dll`, compares it with `pluginassembly`, `plugintype`, `sdkmessageprocessingstep`, and `sdkmessageprocessingstepimage` rows in Dataverse, then prints a dry-run plan. It only writes when `--apply` is passed.

## Typical Flow

From the repository root, use the wrapper for the most direct path:

```powershell
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com -Apply
```

Add `-PushAssembly` when you also want to update the matched `pluginassembly`
binary from the DLL before comparing step metadata:

```powershell
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com -PushAssembly -Apply
```

The lower-level console can still be run directly:

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

`Ops.Plugins.Registration` intentionally does not import `Ops.Plugins.Model`.
The registration console reflects over the deployable plugin DLL, and that DLL
already contains the generated early-bound model types. Importing the same model
into the console creates duplicate Dataverse proxy types in the same process.

Use late-bound SDK entities for registration rows:

- Keep registration table logical names and field names in `RegistrationEntityNames`.
- Use `Entity`, `EntityReference`, `OptionSetValue`, `QueryExpression`, and `ColumnSet`.
- Do not compile business early-bound entities such as `Opportunity` into this console.

## Safety

The sync is intentionally conservative. It creates missing steps/images and updates safe drift fields such as rank, filtering attributes, description, image message property, and image attributes. It reports extras, disabled steps, managed rows, unsecure configuration, and impersonation rather than silently changing or deleting them.

## Image Update Findings

`sdkmessageprocessingstepimage` uses the standard Dataverse `Update` message; there is no separate public save/update image request. The image table and field logical names are:

- Table: `sdkmessageprocessingstepimage`
- Parent step lookup: `sdkmessageprocessingstepid`
- Attribute list: `attributes`
- Alias: `entityalias`
- Image type: `imagetype`
- Message property: `messagepropertyname`

When updating an existing image, include the existing parent
`sdkmessageprocessingstepid` lookup in the update payload along with changed fields
such as `attributes`. Updating only `attributes` can trigger a generic Dataverse
500 from `Microsoft.Crm.ObjectModel.SdkMessageProcessingStepImageServiceInternal`
with a `NullReferenceException`, even though `attributes` is writable. Including
the parent step lookup preserves the existing `sdkmessageprocessingstepimageid`;
the tool does not delete/recreate images for normal drift correction.

The root wrapper builds `Ops.Plugins` and `Ops.Plugins.Registration` in `Release`
by default before syncing. Use `-NoBuild` only when intentionally applying from an
already-built DLL.
