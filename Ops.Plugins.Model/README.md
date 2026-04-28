# Ops.Plugins.Model — Early-Bound Classes

This is a [Shared Project](https://learn.microsoft.com/en-us/xamarin/cross-platform/app-fundamentals/shared-projects) (`Ops.Plugins.Model.shproj`). It contains no assembly of its own — consuming projects import `Ops.Plugins.Model.projitems` and the source files are compiled directly into them.

## Prerequisites

Install the Power Platform CLI as a .NET global tool if you haven't already:

```bash
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
```

Authenticate against your target environment:

```bash
pac auth create --url https://<your-org>.crm.dynamics.com
pac auth select --index <n>   # use `pac auth list` to find the right index
```

## Regenerating classes

All generation settings are captured in [`builderSettings.json`](builderSettings.json). Run from the repo root:

```bash
~/.dotnet/tools/pac modelbuilder build \
  --settingsTemplateFile Ops.Plugins.Model/builderSettings.json \
  --outdirectory Ops.Plugins.Model
```

The tool overwrites all files it manages (`Entities/`, `OptionSets/`, `CrmServiceContext.cs`, `EntityOptionSetEnum.cs`). After running, sync `Ops.Plugins.Model.projitems` with any files that were added or removed (see below).

## Adding a new table

1. Add the logical name to `entityNamesFilter` in `builderSettings.json`:

```json
"entityNamesFilter": [
  "opportunity",
  "account"
]
```

2. Re-run the generation command above.
3. Add the new `Entities\<tablename>.cs` to `Ops.Plugins.Model.projitems`:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Entities\account.cs" />
```

4. Add any new option set files that appeared in `OptionSets\` to the same `projitems` `<ItemGroup>`.

## Adding columns to an existing table

No change to `builderSettings.json` is needed — the tool always reads all attributes for every entity in the filter. Re-run generation; the entity file is overwritten with the updated columns.

## Adding global option sets

By default only option sets referenced by a filtered entity are emitted. To emit all global option sets (e.g. for shared picklists used across many tables), set in `builderSettings.json`:

```json
"generateGlobalOptionSets": true
```

Then add all new files that appear in `OptionSets\` to `Ops.Plugins.Model.projitems`.

To add a specific global option set without enabling all of them, the simplest approach is to add a lightweight entity that references it to `entityNamesFilter` — the option set will then be emitted automatically.

## Adding SDK messages / actions

Set in `builderSettings.json`:

```json
"generateSdkMessages": true,
"messageNamesFilter": [
  "WinOpportunity",
  "LoseOpportunity"
]
```

Use a trailing wildcard to match by prefix (e.g. `"ops_*"`). Message files land in `Messages/`. After generation, add them to `Ops.Plugins.Model.projitems`:

```xml
<Compile Include="$(MSBuildThisFileDirectory)Messages\WinOpportunity.cs" />
```

## Excluding custom columns from public repos

The generator emits every attribute it finds. Before committing, remove any properties and `Fields` constants whose logical names carry internal prefixes (e.g. `ops_`, `qla_`). This repo intentionally excludes those — any prefixed columns that appear in a future regeneration should be deleted before committing.

## What is compiled into consuming projects

| File | Purpose |
|------|---------|
| `Entities/opportunity.cs` | `Opportunity` early-bound class + entity-local enums + `Fields` constants |
| `OptionSets/*.cs` | Global option set enums referenced by filtered entities |
| `CrmServiceContext.cs` | `OrganizationServiceContext` subclass for LINQ queries |
| `EntityOptionSetEnum.cs` | Internal helper used by generated property getters |
