# Scripts

Repository setup helpers for cloning this starter into a client-specific plugin solution.

| Script | Purpose |
|--------|---------|
| `Rename-SolutionPrefix.ps1` | Previews or applies `Ops.` namespace, file, and folder prefix renames. Preview is the default; add `-Apply` to write changes. |
| `New-PluginSigningKey.ps1` | Creates or verifies the plugin strong-name key and ensures the plugin project has signing properties. `Ops.Plugins.csproj` also calls this script automatically when `PluginKey.snk` is missing. |

Power Platform CLI commands for authentication, deployment, and model generation are in [`../PAC_CLI.md`](../PAC_CLI.md).

Typical usage from the repository root:

```powershell
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso -Apply
.\Scripts\New-PluginSigningKey.ps1 -ProjectPath .\Ops.Plugins\Ops.Plugins.csproj
```

`Rename-SolutionPrefix.ps1` only targets `Ops.`-style prefixes by default. Use `-ReplaceStandalonePrefix` only when standalone `Ops` identifiers should also be changed.
