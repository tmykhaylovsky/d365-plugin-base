[CmdletBinding()]
param(
    [string] $Environment,

    [Guid] $PluginAssemblyId = [Guid]::Empty,

    [string] $PluginProject = "Ops.Plugins/Ops.Plugins.csproj",

    [string] $PluginFile = "Ops.Plugins/bin/Debug/net462/Ops.Plugins.dll",

    [string] $AssemblyName,

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [switch] $NoBuild,

    [switch] $PreviewTarget,

    [switch] $Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Format-PowerShellArgument {
    param([string] $Value)

    if ($null -eq $Value) { return "''" }
    if ($Value -match '^[A-Za-z0-9_./:\\-]+$') { return $Value }
    return "'" + $Value.Replace("'", "''") + "'"
}

function Write-CommandPreview {
    param([string[]] $Command)

    Write-Host "Running:"
    Write-Host ("  " + (($Command | ForEach-Object { Format-PowerShellArgument $_ }) -join " "))
    Write-Host ""
}

function Test-IsPlaceholderEnvironment {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return $Value -match "<|>" -or $Value -match "https://(your-)?org\.crm\.dynamics\.com/?$"
}

function Invoke-ExternalCommand {
    param([string[]] $Command)

    Write-CommandPreview -Command $Command

    $output = @(& $Command[0] @($Command | Select-Object -Skip 1) 2>&1)
    $exitCode = $LASTEXITCODE

    foreach ($line in $output) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        exit $exitCode
    }

    if ($output | Where-Object { $_.ToString() -match '^\s*Error:' -or $_.ToString() -match '^\s*\[ERROR\]' }) {
        throw "Command failed: $($Command -join ' ')"
    }
}

function Invoke-CapturedExternalCommand {
    param([string[]] $Command)

    Write-CommandPreview -Command $Command
    $output = @(& $Command[0] @($Command | Select-Object -Skip 1) 2>&1)
    $exitCode = $LASTEXITCODE

    foreach ($line in $output) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        exit $exitCode
    }

    if ($output | Where-Object { $_.ToString() -match '^\s*Error:' -or $_.ToString() -match '^\s*\[ERROR\]' }) {
        throw "Command failed: $($Command -join ' ')"
    }

    return $output
}

function ConvertTo-FetchXmlValue {
    param([string] $Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Resolve-PluginAssemblyId {
    param(
        [Parameter(Mandatory = $true)][string] $TargetAssemblyName
    )

    $fetchXml = @"
<fetch>
  <entity name='pluginassembly'>
    <attribute name='pluginassemblyid' />
    <attribute name='name' />
    <filter>
      <condition attribute='name' operator='eq' value='$(ConvertTo-FetchXmlValue $TargetAssemblyName)' />
    </filter>
  </entity>
</fetch>
"@

    $fetchXmlFile = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), ".xml")

    try {
        Set-Content -LiteralPath $fetchXmlFile -Value $fetchXml -Encoding UTF8
        $output = @(Invoke-CapturedExternalCommand -Command @("pac", "org", "fetch", "--xmlFile", $fetchXmlFile))
    }
    finally {
        if (Test-Path -LiteralPath $fetchXmlFile) {
            Remove-Item -LiteralPath $fetchXmlFile -Force
        }
    }

    $tableStarted = $false
    $ids = New-Object System.Collections.Generic.List[Guid]

    foreach ($line in $output) {
        if ($line -match '\bpluginassemblyid\b') {
            $tableStarted = $true
            continue
        }

        if (-not $tableStarted) {
            continue
        }

        foreach ($match in [regex]::Matches($line.ToString(), '\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b')) {
            $id = [Guid]::Parse($match.Value)
            if (-not $ids.Contains($id)) {
                $ids.Add($id) | Out-Null
            }
        }
    }

    if ($ids.Count -eq 0) {
        throw "Could not find a Dataverse pluginassembly named '$TargetAssemblyName'. Register the assembly once in Plugin Registration Tool, or pass -PluginAssemblyId."
    }

    if ($ids.Count -gt 1) {
        throw "Found multiple Dataverse pluginassembly rows named '$TargetAssemblyName'. Pass -PluginAssemblyId to choose the target."
    }

    return $ids[0]
}

function Resolve-PluginFilePath {
    param([string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Write-DeploymentTargetPreview {
    param(
        [string] $PluginFilePath,
        [string] $TargetAssemblyName
    )

    Write-Host "Deployment target preview"
    Write-Host "  Plugin project: $PluginProject"
    Write-Host "  Configuration: $Configuration"
    Write-Host "  Plugin file: $PluginFilePath"

    if (Test-Path -LiteralPath $PluginFilePath) {
        Write-Host "  Plugin file status: found"
    } else {
        Write-Host "  Plugin file status: missing; run without -PreviewTarget to build before deploy."
    }

    if ([string]::IsNullOrWhiteSpace($TargetAssemblyName)) {
        Write-Host "  Assembly name: unknown until the plugin DLL exists or -AssemblyName is passed."
        Write-Host "  Pluginassembly target: unresolved"
        return
    }

    Write-Host "  Assembly name: $TargetAssemblyName"

    if ($PluginAssemblyId -ne [Guid]::Empty) {
        Write-Host "  Pluginassembly target: $($PluginAssemblyId.ToString("D"))"
        return
    }

    Write-Host "  Pluginassembly target: resolving by assembly name"
    if ($null -eq (Get-Command pac -ErrorAction SilentlyContinue)) {
        throw "Power Platform CLI 'pac' was not found. Install it and authenticate with pac auth create before resolving the deployment target."
    }

    if (-not [string]::IsNullOrWhiteSpace($Environment)) {
        Invoke-ExternalCommand -Command @("pac", "org", "select", "--environment", $Environment)
    }

    $resolvedId = Resolve-PluginAssemblyId -TargetAssemblyName $TargetAssemblyName
    Write-Host "  Pluginassembly id: $($resolvedId.ToString("D"))"
}

if ($Help) {
    Write-Host "Build and upload an existing Dataverse plugin assembly row with PAC CLI."
    Write-Host ""
    Write-Host "Run from the repository root:"
    Write-Host "  .\Scripts\Deploy-PluginAssembly.ps1 -Environment https://org.crm.dynamics.com -PluginAssemblyId <pluginassembly-guid>"
    Write-Host "  .\Scripts\Deploy-PluginAssembly.ps1 -Environment https://org.crm.dynamics.com"
    Write-Host "  .\Scripts\Deploy-PluginAssembly.ps1 -Environment https://org.crm.dynamics.com -PreviewTarget"
    Write-Host ""
    Write-Host "Defaults:"
    Write-Host "  - Builds Ops.Plugins in Debug unless -NoBuild is passed."
    Write-Host "  - Selects the PAC auth environment when -Environment is passed."
    Write-Host "  - If -PluginAssemblyId is omitted, resolves the target by built DLL assembly name."
    Write-Host "  - -PreviewTarget reports the DLL path, assembly name, and resolved/entered target without building or uploading."
    Write-Host "  - Uses pac plugin push to upload the built DLL only."
    Write-Host "  - Does not create or update plugin steps, images, filtering attributes, or run-as settings."
    exit 0
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
Push-Location $repoRoot

try {
    if (Test-IsPlaceholderEnvironment $Environment) {
        throw "Replace the placeholder -Environment value '$Environment' with a real Dataverse URL."
    }

    if (-not $PreviewTarget -and $null -eq (Get-Command pac -ErrorAction SilentlyContinue)) {
        throw "Power Platform CLI 'pac' was not found. Install it and authenticate with pac auth create before deploying."
    }

    $pluginFilePath = Resolve-PluginFilePath -Path $PluginFile

    if ($PreviewTarget) {
        $targetAssemblyName = if (-not [string]::IsNullOrWhiteSpace($AssemblyName)) {
            $AssemblyName.Trim()
        } elseif (Test-Path -LiteralPath $pluginFilePath) {
            [System.Reflection.AssemblyName]::GetAssemblyName($pluginFilePath).Name
        } else {
            $null
        }

        Write-DeploymentTargetPreview -PluginFilePath $pluginFilePath -TargetAssemblyName $targetAssemblyName
        exit 0
    }

    if (-not $NoBuild) {
        Invoke-ExternalCommand -Command @("dotnet", "build", $PluginProject, "-c", $Configuration)
    }

    if (-not (Test-Path -LiteralPath $pluginFilePath)) {
        throw "Built plugin DLL was not found: $pluginFilePath"
    }

    $targetAssemblyName = if ([string]::IsNullOrWhiteSpace($AssemblyName)) {
        [System.Reflection.AssemblyName]::GetAssemblyName($pluginFilePath).Name
    } else {
        $AssemblyName.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($Environment)) {
        Invoke-ExternalCommand -Command @("pac", "org", "select", "--environment", $Environment)
    }

    if ($PluginAssemblyId -eq [Guid]::Empty) {
        Write-Host "Resolving Dataverse pluginassembly by name: $targetAssemblyName"
        $PluginAssemblyId = Resolve-PluginAssemblyId -TargetAssemblyName $targetAssemblyName
        Write-Host "Resolved pluginassembly id: $($PluginAssemblyId.ToString("D"))"
        Write-Host ""
    }

    Invoke-ExternalCommand -Command @(
        "pac",
        "plugin",
        "push",
        "--pluginId", $PluginAssemblyId.ToString("D"),
        "--pluginFile", $pluginFilePath,
        "--type", "Assembly"
    )

    Write-Host "Plugin assembly upload complete."
}
finally {
    Pop-Location
}
