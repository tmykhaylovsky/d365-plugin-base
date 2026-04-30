# Scripts

Repository setup helpers for cloning this starter into a client-specific plugin solution.

| Script | Purpose |
|--------|---------|
| `Deploy-PluginAssembly.ps1` | Builds the plugin assembly and uploads it to an existing Dataverse `pluginassembly` row with `pac plugin push`. It does not sync steps/images. |
| `Rename-SolutionPrefix.ps1` | Previews or applies `Ops.` namespace, file, and folder prefix renames. Preview is the default; add `-Apply` to write changes. |
| `New-PluginSigningKey.ps1` | Creates or verifies the plugin strong-name key and ensures the plugin project has signing properties. `Ops.Plugins.csproj` also calls this script automatically when `PluginKey.snk` is missing. |
| `Set-RunInUserContext.ps1` | Creates or updates the ignored repo-local run-as user config used by plugin registration sync. |
| `Strip-StarterContent.ps1` | Previews or applies removal of advanced starter content after copying and renaming a project. |
| `Update-EarlyBoundModel.ps1` | Runs `pac modelbuilder build` from `Ops.Plugins.Model/builderSettings.json` and syncs `Ops.Plugins.Model.projitems`. |
| `Sync-PluginRegistration.ps1` | Builds the plugin and syncs Dataverse plugin steps/images from code metadata. Dry-run is the default; add `-Apply` to upload the assembly and write step/image changes. |

Power Platform CLI commands for authentication and deployment are in [`../PAC_CLI.md`](../PAC_CLI.md).
Run-as user setup is documented in [`../Ops.Plugins.Registration/README.md`](../Ops.Plugins.Registration/README.md#run-in-users-context).

Typical usage from the repository root:

```powershell
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso -Apply

.\Scripts\New-PluginSigningKey.ps1 -ProjectPath .\Ops.Plugins\Ops.Plugins.csproj

.\Scripts\Strip-StarterContent.ps1
.\Scripts\Strip-StarterContent.ps1 -Apply

.\Scripts\Set-RunInUserContext.ps1 -SystemAdminId 00000000-0000-0000-0000-000000000001 -SystemAdminFullName "System Admin" -SystemAdmin2Id 00000000-0000-0000-0000-000000000002 -SystemAdmin2FullName "System Admin 2"

.\Scripts\Update-EarlyBoundModel.ps1
.\Scripts\Update-EarlyBoundModel.ps1 -Environment https://<org>.crm.dynamics.com

.\Scripts\Deploy-PluginAssembly.ps1 -Environment https://<org>.crm.dynamics.com -PluginAssemblyId 00000000-0000-0000-0000-000000000000

.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com -Apply
```

`Rename-SolutionPrefix.ps1` only targets `Ops.`-style prefixes by default. Use `-ReplaceStandalonePrefix` only when standalone `Ops` identifiers should also be changed.

`Set-RunInUserContext.ps1` is commonly run after cloning or when changing test users. It writes `.local/run-in-user-context.json`, which stays local to your machine.

`Strip-StarterContent.ps1` is intended for copied project folders after `Rename-SolutionPrefix.ps1` has already been applied. The default strip keeps manual assembly deployment available through `Deploy-PluginAssembly.ps1` and removes the full registration sync layer.
