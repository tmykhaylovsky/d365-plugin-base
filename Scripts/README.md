# Scripts

Use the Windows launcher first when it is available. It reads `Scripts/script-catalog.json`, shows only scripts that still exist in the current checkout, guides PAC authentication, and previews commands before it runs them.

Command-line usage remains fully supported for stripped starters, non-Windows machines, and automation.

## GUI-First Workflow

1. Open `Ops.Plugins.Tools` from the solution or run the launcher from the repository root.
2. Add a friendly environment name and Dataverse URL in the setup screen. The launcher stores only non-secret name/URL cache data under `.local`.
3. Use the PAC setup actions to create or select an auth profile. Credentials, tokens, passwords, client secrets, and connection strings stay outside this repo.
4. Choose an action from Setup, Model, Deployment, Registration, or Maintenance.
5. Review the command preview and any write/destructive confirmation before running.

The launcher is a convenience wrapper around these scripts. If a script was removed by `Strip-StarterContent.ps1`, the launcher hides that action and the command-line examples below still describe the remaining fallback flow.

## Script Catalog

`Scripts/script-catalog.json` describes launcher actions, categories, safety levels, and parameter metadata. Keep it next to the scripts so stripped copies can still discover available actions.

Safety levels used by the launcher:

| Level | Meaning |
|-------|---------|
| `read` | No intended file or Dataverse writes. |
| `localWrites` | Writes local repository files only. |
| `writes` | Writes to Dataverse or deploys an assembly. |
| `destructive` | Removes files/folders or performs irreversible cleanup. |

## Command Fallback

Run commands from the repository root in PowerShell.

### Setup

```powershell
# Preview all namespace-style Ops. renames without changing files.
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso

# Apply the previewed content, file, and folder renames.
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso -Apply

# Explicitly create or verify the plugin signing key.
.\Scripts\New-PluginSigningKey.ps1 -ProjectPath .\Ops.Plugins\Ops.Plugins.csproj

# Preview the default starter strip.
.\Scripts\Strip-StarterContent.ps1

# Apply the default strip after copying and renaming the starter.
.\Scripts\Strip-StarterContent.ps1 -Apply
```

`Rename-SolutionPrefix.ps1` only targets `Ops.`-style prefixes by default. Add `-ReplaceStandalonePrefix` only when standalone `Ops` identifiers should also be changed.

The default strip keeps `Scripts/README.md`, `Scripts/script-catalog.json`, the launcher project when present, signing support, and manual deployment support. It removes the full registration sync layer unless you pass the keep switches shown by `.\Scripts\Strip-StarterContent.ps1 -Help`.

### PAC Authentication

Install Power Platform CLI, then authenticate outside repo files:

```powershell
pac auth create --url https://<org>.crm.dynamics.com --deviceCode --name <friendly-name>
pac auth list
pac auth select --index <n>
pac org who
```

Use environment URLs such as `https://<org>.crm.dynamics.com`. Do not store credentials, tokens, passwords, client secrets, or connection strings in repo files.

### Model

```powershell
.\Scripts\Update-EarlyBoundModel.ps1 -Environment https://<org>.crm.dynamics.com
.\Scripts\Update-EarlyBoundModel.ps1 -Environment https://<org>.crm.dynamics.com -LogLevel Information
```

### Deployment

Register the assembly once with Plugin Registration Tool so Dataverse has an existing `pluginassembly` row. After that, deploy the rebuilt DLL with PAC:

```powershell
# Preview DLL path, assembly name, and pluginassembly target without build/upload.
.\Scripts\Deploy-PluginAssembly.ps1 -Environment https://<org>.crm.dynamics.com -PreviewTarget

# Resolve the existing pluginassembly by assembly name and upload the DLL.
.\Scripts\Deploy-PluginAssembly.ps1 -Environment https://<org>.crm.dynamics.com

# Or target a known pluginassembly row explicitly.
.\Scripts\Deploy-PluginAssembly.ps1 -Environment https://<org>.crm.dynamics.com -PluginAssemblyId 00000000-0000-0000-0000-000000000000
```

`Deploy-PluginAssembly.ps1` uploads the assembly binary only. It does not create or update plugin steps, images, filtering attributes, or run-as settings.

### Registration

These commands are available only when the registration sync scripts/projects were kept in the checkout.

```powershell
# Create or update local run-in-user aliases used by registration sync.
.\Scripts\Set-RunInUserContext.ps1 -SystemAdminId 00000000-0000-0000-0000-000000000001 -SystemAdminFullName "System Admin"

# Dry-run registration changes.
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com

# Apply registration changes and upload the built assembly.
.\Scripts\Sync-PluginRegistration.ps1 -Environment https://<org>.crm.dynamics.com -Apply
```

`Set-RunInUserContext.ps1` writes `.local/run-in-user-context.json`, which stays local to your machine. `Sync-PluginRegistration.ps1` dry-runs by default; use `-Apply` only after reviewing the plan.
