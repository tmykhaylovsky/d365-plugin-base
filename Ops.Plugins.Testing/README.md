# Ops.Plugins.Testing

xUnit test project for plugin behavior.

## What belongs here

| Item | Purpose |
|------|---------|
| `Ops.Plugins.Testing.csproj` | Test project targeting `net462`. |
| `PluginTestBase.cs` | FakeXrmEasy setup and plugin execution helpers. |
| `OpportunityWonPluginTests.cs` | Starter tests for the included plugin. |

## Run tests

From the repo root:

```powershell
dotnet test Ops.Plugins.slnx --no-restore
```

Use `dotnet restore Ops.Plugins.slnx` first after package changes.

## Project reference rule

This project should reference only `../Ops.Plugins/Ops.Plugins.csproj` among local projects. Shared and model code are tested through the compiled plugin assembly.

## Test data conventions

The canonical testing guidance lives in [`../BEST_PRACTICES.md`](../BEST_PRACTICES.md). In short:

Prefer the generated early-bound models from `Ops.Plugins.Model` when building Dataverse records in tests:

- Create records with generated entity types such as `Opportunity` so logical names come from the model.
- Use `Opportunity.EntityLogicalName` for static entity logical-name references, or `target.LogicalName` when the test already has an early-bound instance.
- Use generated option-set enums such as `opportunity_statuscode.Won` instead of integer status values.
- Use `Opportunity.Fields.*` constants for column sets and attribute assertions.
- Use `Messages.*` and `PluginImageNames.*` constants from `Ops.Plugins.Shared` for standard Dataverse message names and image aliases.
- Read assembly and plugin type names from `typeof(SomePlugin).Assembly.GetName().Name` and `typeof(SomePlugin).FullName` instead of duplicating project metadata.
- Keep raw logical-name strings only in generic test infrastructure, negative-path tests for entities not present in the model, or when no early-bound model exists yet.
