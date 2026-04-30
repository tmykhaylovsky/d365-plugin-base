# Ops.Plugins Starter Solution

Root-level Visual Studio starter solution for signed Dataverse plugin DLL development.

## Layout

```text
Ops.Plugins.slnx
Ops.Plugins/             signed plugin assembly project
Ops.Plugins.Registration/ console registration sync tool
Ops.Plugins.Shared/      shared plugin base source imported by the plugin assembly
Ops.Plugins.Model/       early-bound model shared project imported by the plugin assembly
Ops.Plugins.Testing/     xUnit/FakeXrmEasy test project
Ops.Plugins.Tools/       optional Windows launcher for script-backed workflows
Scripts/                 setup, model, deployment, registration, and strip helpers
```

## Open, Build, And Test

Open `Ops.Plugins.slnx` from the repository root.

```powershell
dotnet restore Ops.Plugins.slnx
dotnet build Ops.Plugins.slnx -c Debug
dotnet test Ops.Plugins.slnx
```

## Power Platform CLI Prerequisites

See [`PAC_CLI.md`](PAC_CLI.md) for the concise command reference covering `pac auth`, `pac plugin push`, and early-bound model generation.

## Windows Command Notes

These notes reflect the local Windows validation path for this repo:

| Situation | Recommendation |
|-----------|----------------|
| NuGet restore fails with socket or permission errors | Re-run `dotnet restore Ops.Plugins.slnx` from a normal, network-enabled shell. |
| Solution build fails with zero MSBuild errors while projects build individually | Avoid parallel solution-entry duplication; this `.slnx` builds the test project and pulls in `Ops.Plugins` through `ProjectReference`. |
| A build and test run happen at the same time | Run them sequentially to avoid Windows file locks in `obj/Debug/net462`. |
| Git reports dubious ownership | Use a local command flag such as `git -c safe.directory=<repo-path> status` instead of changing global config unless you want the trust setting permanently. |

Build the deployable signed DLL with:

```powershell
dotnet build Ops.Plugins/Ops.Plugins.csproj -c Debug
```

The signed assembly is emitted at:

```text
Ops.Plugins/bin/Debug/net462/Ops.Plugins.dll
```

## Projects

| Project | Purpose |
|---------|---------|
| `Ops.Plugins` | Deployable `net462` Dataverse plugin class library. Imports shared base and model source, references `Microsoft.CrmSdk.CoreAssemblies`, and signs with `PluginKey.snk`. |
| `Ops.Plugins.Registration` | Console tool that syncs Dataverse step and image registration from built `RegisteredEvent` metadata after `pac plugin push`; see [`Ops.Plugins.Registration/README.md`](Ops.Plugins.Registration/README.md). |
| `Ops.Plugins.Shared` | Visual Studio shared project for `PluginBase`, logging, formatting, extensions, and FetchXML builders. It does not produce a DLL. |
| `Ops.Plugins.Model` | Visual Studio shared project for early-bound Dataverse model code. It does not produce a DLL. |
| `Ops.Plugins.Testing` | xUnit test project using FakeXrmEasy. It references only `Ops.Plugins.csproj` among local projects. |
| `Ops.Plugins.Tools` | Optional `net8.0-windows` WPF launcher that reads `Scripts/script-catalog.json`, guides PAC setup, previews script commands, and hides actions whose scripts are absent. |

## Best Practices

See [`BEST_PRACTICES.md`](BEST_PRACTICES.md) for plugin authoring, registration, testing, shared project, model regeneration, logging, and deployment conventions.

## GUI Launcher And Script Fallback

On Windows, use `Ops.Plugins.Tools` for the guided workflow when it is present. The launcher reads `Scripts/script-catalog.json`, stores only non-secret environment names/URLs under `.local`, and runs the same PowerShell scripts documented in [`Scripts/README.md`](Scripts/README.md).

The command-line scripts remain the source of truth for automation, stripped starters, and non-Windows workflows. Use `Scripts/README.md` for fallback commands covering setup, PAC authentication, model generation, deployment, and optional registration sync.

## Deploying The Plugin DLL

Initial assembly registration still happens in the Plugin Registration Tool. Register `Ops.Plugins.dll` once so Dataverse has the plugin assembly row. After that, `Scripts\Sync-PluginRegistration.ps1 -Apply` uploads the rebuilt assembly binary before it compares and applies step/image registration from code metadata. See [`PAC_CLI.md`](PAC_CLI.md) for the exact commands.

Local environment access should be cached through PAC auth profiles or user
environment variables. `.claude/` is ignored and is fine for local URLs and command
templates, but keep secrets and literal connection strings out of repo files.
For fixed Run in User's Context setup, see
[`Ops.Plugins.Registration/README.md`](Ops.Plugins.Registration/README.md#run-in-users-context).

## Automating Step Registration

Yes, step registration can be automated for assemblies that are already in Dataverse. The included safe pattern is:

1. Build and push the signed assembly with `pac plugin push`.
2. Query Dataverse for the target `pluginassembly` and its `plugintype` rows.
3. For each plugin class, compare the code-declared `RegisteredEvent` metadata with existing `sdkmessageprocessingstep` rows.
4. Dry-run by default, then use `--apply` to create missing steps/images and update safe drift fields.

`RegisteredEvent` includes deployment metadata for this: message, entity, stage, mode, rank, filtering attributes, required image names, and image attributes. The starter plugin uses that metadata for `AccountUpdatePlugin`.

Before applying, the sync tool validates declared entity logical names against the
target environment, creates steps before images, and treats disabled matching steps
as existing rows so it does not create duplicates.
It can also sync optional step description and Run in User's Context metadata.

## Included Starter Plugin

`AccountUpdatePlugin` is a small example plugin with two synchronous `Update` steps on `account`:

- `PreOperation`, filtered on `accountnumber`, requires `PreImage` and blocks changes after an account number is assigned.
- `PostOperation`, filtered on `name` and `telephone1`, requires `PostImage` and traces the committed account profile without issuing a retrieve.

Together they show how one plugin class can expose multiple `RegisteredEvent` entries while keeping stage-specific handlers separate.

## Early-Bound Model Regeneration

Generation settings live in `Ops.Plugins.Model/builderSettings.json`. After editing them, run:

```powershell
.\Scripts\Update-EarlyBoundModel.ps1
```

The wrapper runs `pac modelbuilder build` and updates `Ops.Plugins.Model/Ops.Plugins.Model.projitems` when generated entity, option set, or message files change.

## Signing

`Ops.Plugins/PluginKey.snk` is used by `Ops.Plugins.csproj` via `SignAssembly` and `AssemblyOriginatorKeyFile`. This starter repo allows that specific key file to be committed so all machines can build assemblies with the same strong-name identity.

On Windows, the first Visual Studio or MSBuild build creates a local `PluginKey.snk` automatically by running `Scripts/New-PluginSigningKey.ps1`, which uses the Windows SDK `sn.exe` tool. The generated `.snk` is a passwordless strong-name key pair. The helper also ensures `Ops.Plugins.csproj` contains `SignAssembly` and `AssemblyOriginatorKeyFile`.

A public `.snk` is acceptable for a shared starter template or dev/test assembly identity, but it is not a security boundary. Anyone with the private `.snk` can build a different DLL with the same strong name. They still need Dataverse deployment permissions to upload that DLL, but for production environments you may prefer an organization-controlled private key.

You can also create the key explicitly from the repository root:

```powershell
.\Scripts\New-PluginSigningKey.ps1 -ProjectPath .\Ops.Plugins\Ops.Plugins.csproj
```

If key creation fails, install the .NET Framework SDK component for Visual Studio, or run this from a Visual Studio Developer PowerShell:

```powershell
cd Ops.Plugins
sn -k PluginKey.snk
```

## Notes For Customization

Rename namespaces from `Ops.Plugins` to your client or product namespace only after the starter solution builds and tests cleanly. Keep the plugin base and model folders imported through shared project `.projitems` so the deployable assembly stays a single signed DLL.

Use the root scripts for repeatable setup tasks:

```powershell
# Preview all namespace-style Ops. renames without changing files.
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso

# Apply the previewed content, file, and folder renames.
.\Scripts\Rename-SolutionPrefix.ps1 -NewPrefix Contoso -Apply

# Explicitly create or verify the plugin signing key.
.\Scripts\New-PluginSigningKey.ps1 -ProjectPath .\Ops.Plugins\Ops.Plugins.csproj

# Regenerate early-bound model code from builderSettings.json.
.\Scripts\Update-EarlyBoundModel.ps1
```

`Rename-SolutionPrefix.ps1` only targets `Ops.`-style prefixes by default, so standalone strings like `Ops` are left alone. Use `-ReplaceStandalonePrefix` only when you really want every standalone `Ops` identifier changed too.
