# Ops.Plugins.Registration

Console tool for syncing Dataverse plugin step and image registration from the built plugin assembly.

The tool reads `RegisteredEvent` metadata from `Ops.Plugins.dll`, compares it with `pluginassembly`, `plugintype`, `sdkmessageprocessingstep`, and `sdkmessageprocessingstepimage` rows in Dataverse, then prints a dry-run plan. It only writes when `--apply` is passed.

One plugin class may declare multiple `RegisteredEvent` entries. The starter `AccountUpdatePlugin` does this for a pre-operation account-number guard with `PreImage` and a post-operation account-profile trace with `PostImage`.

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

In `-Apply` mode, the tool automatically pushes the assembly when the only
blocking issue is that Dataverse has not registered a plug-in type that exists in
the current DLL. The push still performs the stale-type preflight described below.

Use `-PushAssembly` when the `pluginassembly` row already exists but Dataverse
still shows older plug-in types under it. For example, if the assembly is
`Ops.Plugins` but the only registered type is `Ops.Plugins.OpportunityWonPlugin`
and the current DLL now contains a different plug-in class, the assembly binary
must be pushed before the sync can create/update steps for the new type. After
the push, old steps/images are reported as extras; the tool does not delete them.

If Dataverse has registered plug-in types that are missing from the current DLL,
the tool stops before pushing assembly content and lists the stale type plus its
dependent steps/images. Review and retire those registrations manually in
Dataverse, then rerun the sync. This avoids silently deleting production behavior
or leaving Dataverse with steps that point to a class no longer present in the
assembly.

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
setx DATAVERSE_CONNECTION_CONTOSO "AuthType=OAuth;Url=https://contoso.crm.dynamics.com;ClientId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=app://58145B91-0C36-4500-8554-080854F2AC97;LoginPrompt=Auto"
```

PAC auth profiles can expire or become unreadable. If `pac modelbuilder` or `pac plugin push` fails with token-cache or refresh-token errors, create a fresh device-code profile:

```powershell
pac auth create --url https://<org>.crm.dynamics.com --deviceCode --name <short-name>
pac auth list
pac auth select --index <n>
```

Device-code auth avoids the embedded-browser path and is usually the least fussy repair.

## Run In User's Context

Plugin code should keep `RunInUserContext.CallingUser` unless the step must run
as a fixed Dataverse user. For a fixed user, use one of the shared labels:

```csharp
runInUserContext: RunInUserContext.SystemAdmin
```

Then map that label to a per-environment `systemuserid` in the default ignored
repo-local file:

```text
.local\run-in-user-context.json
```

The committed template lives at:

```text
Ops.Plugins.Registration\run-in-user-context.template.json
```

Example local file:

```json
[
  {
    "label": "Calling User",
    "systemuserid": null,
    "fullname": "Calling User"
  },
  {
    "label": "System Admin",
    "systemuserid": "656dc3f6-7b48-ee11-be6d-000d3a1f08bb",
    "fullname": "# crm-prod-dataenrichment"
  }
]
```

You can create or update the local file without opening it directly:

```powershell
.\Scripts\Set-RunInUserContext.ps1 `
  -Label "System Admin" `
  -SystemUserId 656dc3f6-7b48-ee11-be6d-000d3a1f08bb `
  -FullName "# crm-prod-dataenrichment"
```

Or set the predefined labels in one command:

```powershell
.\Scripts\Set-RunInUserContext.ps1 `
  -SystemAdminId 656dc3f6-7b48-ee11-be6d-000d3a1f08bb `
  -SystemAdminFullName "# crm-prod-dataenrichment"
```

You can also pass an explicit map with `--userMap <path>`. Keep the local file
out of source control; `systemuserid` values are environment-specific. The
committed template should stay in source control. The sync tool rejects fixed
labels from plugin code when they are missing from the local run-as user config
or do not have a real `systemuserid`.

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

The sync is intentionally conservative. It creates missing steps/images and
updates safe drift fields such as rank, filtering attributes, description, Run in
User's Context, image message property, and image attributes. It reports extras,
disabled steps, managed rows, and unsecure configuration rather than silently
changing or deleting them.

`-PushAssembly` also performs a preflight check for stale registered plug-in
types. If an existing type is no longer present in the DLL, the push is blocked
and the dependent step/image inventory is printed for manual review.

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
