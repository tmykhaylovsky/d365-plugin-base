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
