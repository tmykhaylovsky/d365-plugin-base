# Ops.Plugins Starter Solution

Root-level Visual Studio starter solution for signed Dataverse plugin DLL development.

## Layout

```text
Ops.Plugins.slnx
Ops.Plugins/             signed plugin assembly project
Ops.Plugins.Shared/      shared plugin base source imported by the plugin assembly
Ops.Plugins.Model/       early-bound model shared project imported by the plugin assembly
Ops.Plugins.Testing/     xUnit/FakeXrmEasy test project
```

## Open, Build, And Test

Open `Ops.Plugins.slnx` from the repository root.

```powershell
dotnet restore Ops.Plugins.slnx
dotnet build Ops.Plugins.slnx -c Debug
dotnet test Ops.Plugins.slnx
```

## Power Platform CLI Prerequisites

See [`PAC_CLI.md`](PAC_CLI.md) for the concise command reference covering `pac auth`, `pac plugin push`, and `pac modelbuilder build`.

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
dotnet build Ops.Plugins/Ops.Plugins.csproj -c Release
```

The signed assembly is emitted at:

```text
Ops.Plugins/bin/Release/net462/Ops.Plugins.dll
```

## Projects

| Project | Purpose |
|---------|---------|
| `Ops.Plugins` | Deployable `net462` Dataverse plugin class library. Imports shared base and model source, references `Microsoft.CrmSdk.CoreAssemblies`, and signs with `PluginKey.snk`. |
| `Ops.Plugins.Shared` | Visual Studio shared project for `PluginBase`, logging, formatting, extensions, and FetchXML builders. It does not produce a DLL. |
| `Ops.Plugins.Model` | Visual Studio shared project for early-bound Dataverse model code. It does not produce a DLL. |
| `Ops.Plugins.Testing` | xUnit test project using FakeXrmEasy. It references only `Ops.Plugins.csproj` among local projects. |

## Best Practices

See [`BEST_PRACTICES.md`](BEST_PRACTICES.md) for plugin authoring, registration, testing, shared project, model regeneration, logging, and deployment conventions.

## Deploying The Plugin DLL

Initial registration still happens in the Plugin Registration Tool. Register `Ops.Plugins.dll`, then create plugin steps for concrete plugin classes such as `Ops.Plugins.OpportunityWonPlugin`. After that, use `pac plugin push` to update the existing assembly binary. See [`PAC_CLI.md`](PAC_CLI.md) for the exact command.

## Automating Step Registration

Yes, step registration can be automated for assemblies that are already in Dataverse. The safe pattern is:

1. Build and push the signed assembly with `pac plugin push`.
2. Query Dataverse for the target `pluginassembly` and its `plugintype` rows.
3. For each plugin class, compare the code-declared `RegisteredEvent` metadata with existing `sdkmessageprocessingstep` rows.
4. Create only missing steps and images; avoid silently changing existing managed or manually tuned steps unless the script has an explicit update mode.

`RegisteredEvent` includes deployment metadata for this: message, entity, stage, mode, filtering attributes, required image names, and image attributes. The starter plugin uses that metadata for `OpportunityWonPlugin`.

## Included Starter Plugin

`OpportunityWonPlugin` is a small example plugin registered on `Update` of `opportunity` at synchronous `PostOperation`, filtered on `statuscode`, with a `PreImage` containing `statuscode` and `actualclosedate`.

When the opportunity moves to Won, it stamps `actualclosedate` if that field was not already set.

## Early-Bound Model Regeneration

Generation settings live in `Ops.Plugins.Model/builderSettings.json`. See [`PAC_CLI.md`](PAC_CLI.md) for the `pac modelbuilder build` command. After regeneration, update `Ops.Plugins.Model/Ops.Plugins.Model.projitems` if new entity, option set, or message files are added.

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
```

`Rename-SolutionPrefix.ps1` only targets `Ops.`-style prefixes by default, so standalone strings like `Ops` are left alone. Use `-ReplaceStandalonePrefix` only when you really want every standalone `Ops` identifier changed too.
