# Scripts

Repository setup helpers for cloning this starter into a client-specific plugin solution.

| Script | Purpose |
|--------|---------|
| `Rename-SolutionPrefix.ps1` | Previews or applies `Ops.` namespace, file, and folder prefix renames. Preview is the default; add `-Apply` to write changes. |
| `New-PluginSigningKey.ps1` | Creates or verifies the plugin strong-name key and ensures the plugin project has signing properties. `Ops.Plugins.csproj` also calls this script automatically when `PluginKey.snk` is missing. |
| `Set-RunInUserContext.ps1` | Creates or updates the ignored repo-local run-as user config used by plugin registration sync. |
| `Update-EarlyBoundModel.ps1` | Runs `pac modelbuilder build` from `Ops.Plugins.Model/builderSettings.json` and syncs `Ops.Plugins.Model.projitems`. |
| `Sync-PluginRegistration.ps1` | Builds the plugin and syncs Dataverse plugin steps/images from code metadata. Dry-run is the default; add `-Apply` to write changes. |

Power Platform CLI commands for authentication and deployment are in [`../PAC_CLI.md`](../PAC_CLI.md).
Run-as user setup is documented in [`../Ops.Plugins.Registration/README.md`](../Ops.Plugins.Registration/README.md#run-in-users-context).

Typical usage from the repository root:

```powershell
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso -Apply
.\Scripts\New-PluginSigningKey.ps1 -ProjectPath .\Ops.Plugins\Ops.Plugins.csproj
.\Scripts\Update-EarlyBoundModel.ps1
.\Scripts\Update-EarlyBoundModel.ps1 -Environment https://<org>.crm.dynamics.com
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com -Apply
```

`Rename-SolutionPrefix.ps1` only targets `Ops.`-style prefixes by default. Use `-ReplaceStandalonePrefix` only when standalone `Ops` identifiers should also be changed.
