# d365-plugin-base

A production-ready, self-contained base library for Microsoft Dynamics 365 / Dataverse classic plugin assemblies targeting **.NET Framework 4.6.2**.

Built from synthesized patterns across the D365 community (XrmPluginCore, JonasPluginBase, FakeXrmEasy, DLaB.Xrm). No opinionated framework lock-in — copy the source, rename the namespace, own the code.

---

## Repo Layout

```
d365-plugin-base/
├── README.md
├── .gitignore
├── d365-plugin-base.sln
├── src/                          ← copy these four files into your shared library project
│   ├── PluginBase.cs
│   ├── PluginLogger.cs
│   ├── PluginExtensions.cs
│   ├── CrmFormat.cs
│   └── Ops.Crm.Shared.csproj
├── testing/                      ← copy PluginTestBase.cs into your test project
│   ├── PluginTestBase.cs
│   └── Ops.Crm.Shared.Testing.csproj
├── examples/                     ← two complete plugin + test pairs
│   ├── OpportunityWonPlugin.cs
│   ├── OpportunityWonPluginTests.cs
│   ├── GetOpportunitySummaryApi.cs
│   ├── GetOpportunitySummaryApiTests.cs
│   └── Ops.Crm.Plugins.Examples.csproj
└── docs/
    └── PluginBase.md             ← architecture decision log
```

## What Is In This Repo

| File | Purpose |
|------|---------|
| `src/PluginBase.cs` | Abstract base class implementing `IPlugin`. Provides `LocalPluginContext` with typed service resolution, image helpers, shared variable access, custom API support, and stage guards. |
| `src/PluginLogger.cs` | Dual-write logger: `ITracingService` (always) + `Microsoft.Xrm.Sdk.PluginTelemetry.ILogger` (AppInsights, graceful no-op when not configured). Includes `TraceLevel` enum, lazy `Func<string>` overload, and intelligent 5-frame stack trace extraction. |
| `src/PluginExtensions.cs` | Extension methods for `Entity`, `EntityReference`, `IOrganizationService`, `QueryExpression`, and `LocalPluginContext`. Self-contained — no DLaB.Xrm dependency. |
| `src/CrmFormat.cs` | Readable trace formatting for SDK types. Static `CrmFormat.Of(...)` for no-cost formatting. Instance `CrmFormatter` for OptionSet display labels via lazy metadata cache. |
| `testing/PluginTestBase.cs` | xUnit-compatible base class using FakeXrmEasy.9 (MIT). Builders for Create/Update/Delete/Custom API contexts with pre/post image support. |

---

## Prerequisites

- Visual Studio 2019 or later (Windows) or Rider / VS Code (cross-platform)
- .NET Framework 4.6.2 SDK
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction)
  - **Windows:** `winget install Microsoft.PowerPlatformCLI`
  - **macOS:** see [macOS pac CLI setup](#macos-pac-cli-setup) below
- NuGet access to nuget.org

---

## Setting Up for a New Project

### 1. Create the Visual Studio solution

```
YourClient.sln
├── YourClient.Crm.Shared/        ← .NET 4.6.2 Class Library (this repo's files)
├── YourClient.Crm.Plugins/       ← .NET 4.6.2 Class Library (your plugin classes)
└── YourClient.Crm.Plugins.Tests/ ← .NET 4.6.2 test project
```

Create each as a **Class Library (.NET Framework)** targeting **.NET Framework 4.6.2** in Visual Studio.

### 2. Copy source files

Copy these files into `YourClient.Crm.Shared/`:
```
PluginBase.cs
PluginLogger.cs
PluginExtensions.cs
CrmFormat.cs
```

Copy this file into `YourClient.Crm.Plugins.Tests/`:
```
PluginTestBase.cs
```

### 3. Rename the namespace

Run this from the `YourClient.Crm.Shared` project folder (PowerShell):

```powershell
Get-ChildItem -Filter *.cs | ForEach-Object {
    (Get-Content $_.FullName) -replace 'Ops\.Crm\.Shared', 'YourClient.Crm.Shared' |
    Set-Content $_.FullName
}
```

Or use Visual Studio's **Edit → Find and Replace → Replace in Files** (`Ctrl+Shift+H`):
- Find: `Ops.Crm.Shared`
- Replace: `YourClient.Crm.Shared`
- Scope: Entire Solution

Repeat for `Ops.Crm.Shared.Testing` → `YourClient.Crm.Shared.Testing` in `PluginTestBase.cs`.

### 4. Add NuGet references

**YourClient.Crm.Shared** — edit `.csproj` or use NuGet Package Manager:
```xml
<PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.59" />
```

**YourClient.Crm.Plugins** — add both:
```xml
<PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.59" />
<ProjectReference Include="..\YourClient.Crm.Shared\YourClient.Crm.Shared.csproj" />
```

**YourClient.Crm.Plugins.Tests** — add all:
```xml
<PackageReference Include="FakeXrmEasy.9" Version="1.58.1" />
<PackageReference Include="xunit" Version="2.6.6" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
<ProjectReference Include="..\YourClient.Crm.Shared\YourClient.Crm.Shared.csproj" />
```

### 5. Write your first plugin

```csharp
using YourClient.Crm.Shared;

namespace YourClient.Crm.Plugins
{
    public class OpportunityWonPlugin : PluginBase
    {
        // Required: parameterless constructor for Dataverse when no config is on the step
        public OpportunityWonPlugin() { }

        // Required: receives unsecure/secure config strings set on the step registration
        public OpportunityWonPlugin(string unsecureConfig, string secureConfig)
            : base(unsecureConfig, secureConfig) { }

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            // Guard: only run on Opportunity Update, PostOperation
            if (!context.IsMessage("Update") || !context.IsPostOperation) return;

            var target   = context.GetTarget();
            var preImage = context.GetPreImage<Entity>("PreImage");

            // Only act when statuscode changed
            if (!context.HasChangedAttribute("statuscode")) return;

            context.Logger.Trace(TraceLevel.Verbose, () =>
                $"Status changed | {CrmFormat.Of(preImage?.GetAttributeValue<OptionSetValue>("statuscode"))} → {CrmFormat.Of(target.GetAttributeValue<OptionSetValue>("statuscode"))}");

            // Your business logic here
        }
    }
}
```

---

## Deployment Guide — PAC CLI + PRT

### Authenticate once per environment

```powershell
pac auth create --url https://yourorg.crm.dynamics.com
```

Verify the connection:
```powershell
pac org who
```

### First-time assembly registration (PRT)

The first registration must go through PRT — PAC CLI does not create step registrations.

```powershell
pac tool prt
```

In PRT:
1. Connect to your environment
2. **Register → New Assembly**
3. Browse to `YourClient.Crm.Plugins.dll` (in `bin\Debug\` or `bin\Release\`)
4. Isolation mode: **Sandbox** | Location: **Database**
5. Click **Register Selected Plugins**
6. Right-click your plugin class → **Register New Step**
7. Set: Message, Primary Entity, Stage (Pre/PostOperation), Execution Mode
8. Set **Filtering Attributes** for Update steps — this is a Microsoft best practice for performance
9. Set Unsecure Configuration if needed (e.g. future config keys)

> **Note on Shared as dependent assembly:** If `YourClient.Crm.Shared.dll` is a separate project (not merged into Plugins), you need the NuGet package approach. Run `pac plugin init --skip-signing` in your Plugins project folder. This scaffolds a NuGet package that bundles both DLLs. Register the `.nupkg` via PRT → **Register → New Package** instead of Register → New Assembly.

### Redeploying code updates (PAC CLI — no PRT needed)

After the initial registration, code-only updates go through PAC CLI:

```powershell
# Assembly (most common — single DLL, no dependent assemblies)
pac plugin push --pluginId <assembly-guid-from-prt> --pluginFile "bin\Release\YourClient.Crm.Plugins.dll" --type Assembly

# NuGet package (bundled multi-DLL approach)
pac plugin push --pluginId <package-guid-from-prt> --pluginFile "bin\Release\YourClient.Crm.Plugins.nupkg" --type Nuget
```

`--type Assembly` is required when the file extension is `.dll`. Without it the command defaults to `Nuget` and fails.

Find the assembly GUID in PRT: select your assembly in the tree → copy the **Id** shown in the properties panel on the right. It is a standard GUID (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`).

> **Step registrations are NOT touched by `pac plugin push`.** Adding or changing steps (message, entity, stage, filtering attributes, images, unsecure config) still requires PRT.

> **`pac plugin push` is update-only.** It requires `--pluginId`, which means the assembly must already be registered in PRT before you can use this command. There is no `pac plugin create` equivalent — first registration is always PRT.

### Controlling trace verbosity without redeployment

Set a log level in PRT on a specific step's **Unsecure Configuration** field and read it in your plugin's constructor using `PluginConfig`:

```csharp
public OpportunityWonPlugin(string unsecureConfig, string secureConfig)
    : base(unsecureConfig, secureConfig)
{
    // Override global level for this specific step if config is set
    var stepLevel = PluginConfig.GetEnum(unsecureConfig, "TraceLevel", PluginLogger.GlobalLevel);
    PluginLogger.GlobalLevel = stepLevel;
}
```

Or simply set it globally in a static initializer when you want a project-wide default other than Verbose:

```csharp
static OpportunityWonPlugin()
{
    PluginLogger.GlobalLevel = TraceLevel.Info; // reduce noise in production
}
```

---

## macOS pac CLI Setup

The standard `winget` and `dotnet tool install` pac paths do not work on macOS arm64 (Apple Silicon). Use this one-time setup:

```bash
# 1. Download the macOS x64 pac binary from NuGet
mkdir -p ~/tools/pac
curl -L "https://www.nuget.org/api/v2/package/Microsoft.PowerApps.CLI.Core.osx-x64/2.6.4" \
  -o /tmp/pac-osx.nupkg
cd ~/tools/pac && unzip -q /tmp/pac-osx.nupkg tools/pac -d .
chmod +x tools/pac

# 2. Download .NET 10 ASP.NET Core runtime for osx-x64 (pac requires x64 .NET 10)
mkdir -p ~/tools/dotnet-x64
curl -L "https://dotnetcli.azureedge.net/dotnet/Runtime/10.0.5/dotnet-runtime-10.0.5-osx-x64.tar.gz" \
  | tar -xz -C ~/tools/dotnet-x64
curl -L "https://dotnetcli.azureedge.net/dotnet/aspnetcore/Runtime/10.0.5/aspnetcore-runtime-10.0.5-osx-x64.tar.gz" \
  | tar -xz -C ~/tools/dotnet-x64

# 3. Create a shell alias (add to ~/.zshrc)
alias pac='DOTNET_ROOT_X64=~/tools/dotnet-x64 arch -x86_64 ~/tools/pac/tools/pac'
```

After setup, all `pac` commands work normally:

```bash
pac auth create --url https://yourorg.crm.dynamics.com   # browser login opens automatically
pac org who
pac plugin push --pluginId <guid> --pluginFile bin/Release/YourClient.Crm.Plugins.dll --type Assembly
```

> Rosetta 2 must be installed (it is by default on all Apple Silicon Macs running macOS Ventura+).

---

## Testing Quick Start

```csharp
using YourClient.Crm.Shared;
using YourClient.Crm.Shared.Testing;
using Xunit;

public class OpportunityWonPluginTests : PluginTestBase
{
    [Fact]
    public void GivenStatusChangedToWon_ExecutesSuccessfully()
    {
        // Arrange
        var opportunityId = Guid.NewGuid();

        var preImage = BuildEntity("opportunity", opportunityId,
            ("statuscode", new OptionSetValue(1))); // Open

        var target = BuildEntity("opportunity", opportunityId,
            ("statuscode", new OptionSetValue(3))); // Won

        Seed(preImage);
        var ctx = BuildUpdateContext(target, preImage: preImage);

        // Act
        Context.ExecutePluginWith<OpportunityWonPlugin>(ctx);

        // Assert — inspect Context.Data or output parameters
    }
}
```

Run tests from Visual Studio Test Explorer or:
```powershell
dotnet test
```

---

## Adjusting Trace Verbosity in AppInsights

When your org connects Application Insights, `PluginLogger` automatically writes to both `ITracingService` and `Microsoft.Xrm.Sdk.PluginTelemetry.ILogger` with no code changes required.

To enable: Power Platform Admin Center → your environment → **Settings → Application Insights**.

> **Namespace warning:** The AppInsights logger uses `using Microsoft.Xrm.Sdk.PluginTelemetry` — NOT `Microsoft.Extensions.Logging`. The wrong namespace compiles cleanly but resolves to null at runtime with no error.

---

## Upcoming: .NET 4.8 Support

Microsoft has confirmed Dataverse plugin support for **.NET Framework 4.8 in Q4 2026**. This repo is structured to survive a `TargetFramework` bump — change `net462` to `net48` in the `.csproj` files when Microsoft announces general availability.

---

## Architecture Reference

See `docs/PluginBase.md` for the full decision log: library evaluation, PAC CLI vs PRT boundaries, AppInsights dual-write design, StackTrace extraction rationale, and OptionSet label resolution tradeoffs.

---

## License

MIT — copy, rename, adapt freely for any client engagement.
