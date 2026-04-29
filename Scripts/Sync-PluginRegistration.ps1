param(
    [string] $Environment,
    [string] $ConnectionString,
    [switch] $Apply,
    [switch] $PushAssembly,
    [string] $AssemblyName,
    [Guid] $PluginAssemblyId,
    [string] $Assembly = "Ops.Plugins/bin/Release/net462/Ops.Plugins.dll",
    [string] $UserMap,
    [switch] $NoBuild,
    [switch] $VerboseOutput,
    [switch] $Help
)

function Format-PowerShellArgument {
    param([string] $Value)

    if ($null -eq $Value) { return "''" }
    if ($Value -match '^[A-Za-z0-9_./:\-]+$') { return $Value }
    return "'" + $Value.Replace("'", "''") + "'"
}

function Write-ApplyCommand {
    $command = @(".\Scripts\Sync-PluginRegistration.ps1")

    if (-not [string]::IsNullOrWhiteSpace($Environment)) {
        $command += "-Environment"
        $command += (Format-PowerShellArgument $Environment)
    }

    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        $command += "-ConnectionString"
        $command += (Format-PowerShellArgument $ConnectionString)
    }

    if ($PSBoundParameters.ContainsKey("PluginAssemblyId") -and $PluginAssemblyId -ne [Guid]::Empty) {
        $command += "-PluginAssemblyId"
        $command += $PluginAssemblyId.ToString()
    }

    if (-not [string]::IsNullOrWhiteSpace($AssemblyName)) {
        $command += "-AssemblyName"
        $command += (Format-PowerShellArgument $AssemblyName)
    }

    if ($Assembly -ne "Ops.Plugins/bin/Release/net462/Ops.Plugins.dll") {
        $command += "-Assembly"
        $command += (Format-PowerShellArgument $Assembly)
    }

    if (-not [string]::IsNullOrWhiteSpace($UserMap)) {
        $command += "-UserMap"
        $command += (Format-PowerShellArgument $UserMap)
    }

    if ($PushAssembly) {
        $command += "-PushAssembly"
    }

    if ($NoBuild) {
        $command += "-NoBuild"
    }

    $command += "-Apply"

    Write-Host ""
    Write-Host "To apply this dry-run, run:"
    Write-Host ("  " + ($command -join " "))
}

function Get-CachedEnvironmentUrls {
    $paths = @()
    foreach ($relativePath in @(".claude", ".codex", ".local")) {
        $fullPath = Join-Path $repoRoot $relativePath
        if (Test-Path $fullPath) {
            $paths += Get-ChildItem -Path $fullPath -Recurse -File -Include *.md,*.json,*.txt -ErrorAction SilentlyContinue
        }
    }

    foreach ($relativePath in @("PAC_CLI.md", "Ops.Plugins.Registration\README.md", "Scripts\README.md")) {
        $fullPath = Join-Path $repoRoot $relativePath
        if (Test-Path $fullPath) {
            $paths += Get-Item $fullPath
        }
    }

    $urls = [System.Collections.Generic.List[string]]::new()
    foreach ($path in $paths) {
        $content = Get-Content -LiteralPath $path.FullName -Raw -ErrorAction SilentlyContinue
        foreach ($match in [regex]::Matches($content, "https://[A-Za-z0-9-]+\.crm\.dynamics\.com")) {
            $url = $match.Value
            if ($url -notmatch "<|>|your-org|org\.crm" -and -not $urls.Contains($url)) {
                $urls.Add($url)
            }
        }
    }

    return $urls | Sort-Object @{ Expression = { if ($_ -match "contoso") { 0 } else { 1 } } }, { $_ }
}

function Test-IsPlaceholderEnvironment {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return $Value -match "<|>" -or $Value -match "https://(your-)?org\.crm\.dynamics\.com/?$"
}

if ($Help) {
    Write-Host "Sync Dataverse plugin steps and images from code metadata."
    Write-Host ""
    Write-Host "Run from the repository root:"
    Write-Host "  .\Scripts\Sync-PluginRegistration.ps1 -Environment https://org.crm.dynamics.com"
    Write-Host "  .\Scripts\Sync-PluginRegistration.ps1 -Environment https://org.crm.dynamics.com -Apply"
    Write-Host "  .\Scripts\Sync-PluginRegistration.ps1 -Environment https://org.crm.dynamics.com -PushAssembly"
    Write-Host ""
    Write-Host "Defaults:"
    Write-Host "  - Builds Ops.Plugins in Release unless -NoBuild is passed."
    Write-Host "  - Dry-runs unless -Apply is passed."
    Write-Host "  - Uploads the built DLL before comparing/applying when -Apply is passed."
    Write-Host "  - -PushAssembly can be used with a dry-run to upload first, then inspect the resulting plan."
    Write-Host "  - Finds the pluginassembly by DLL assembly name unless -PluginAssemblyId or -AssemblyName is passed."
    Write-Host "  - Resolves fixed Run in User's Context labels from .local\run-in-user-context.json unless -UserMap is passed."
    exit 0
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    if ([string]::IsNullOrWhiteSpace($Environment) -and [string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw "Pass -Environment https://<org>.crm.dynamics.com or -ConnectionString <value-or-env-var>."
    }

    if (Test-IsPlaceholderEnvironment $Environment) {
        $cachedUrls = @(Get-CachedEnvironmentUrls)
        $message = "Replace the placeholder -Environment value '$Environment' with a real Dataverse URL."
        if ($cachedUrls.Length -gt 0) {
            $message += [Environment]::NewLine + "Cached suggestion: .\Scripts\Sync-PluginRegistration.ps1 -Environment $($cachedUrls[0])"
            if ($Apply) { $message += " -Apply" }
            if ($cachedUrls.Length -gt 1) {
                $message += [Environment]::NewLine + "Other cached URLs: " + (($cachedUrls | Select-Object -Skip 1) -join ", ")
            }
        }

        [Console]::Error.WriteLine($message)
        exit 1
    }

    if (-not $NoBuild) {
        dotnet build "Ops.Plugins/Ops.Plugins.csproj" -c Release
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        dotnet build "Ops.Plugins.Registration/Ops.Plugins.Registration.csproj" -c Release
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    $appArgs = @(
        "--assembly", $Assembly
    )

    if (-not [string]::IsNullOrWhiteSpace($Environment)) {
        $appArgs += @("--environment", $Environment)
    }

    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        $appArgs += @("--connectionString", $ConnectionString)
    }

    if ($PSBoundParameters.ContainsKey("PluginAssemblyId") -and $PluginAssemblyId -ne [Guid]::Empty) {
        $appArgs += @("--pluginAssemblyId", $PluginAssemblyId.ToString())
    }

    if (-not [string]::IsNullOrWhiteSpace($AssemblyName)) {
        $appArgs += @("--assemblyName", $AssemblyName)
    }

    if (-not [string]::IsNullOrWhiteSpace($UserMap)) {
        $appArgs += @("--userMap", $UserMap)
    }

    if ($PushAssembly) {
        $appArgs += "--pushAssembly"
    }

    if ($Apply) {
        $appArgs += "--apply"
    }

    if ($VerboseOutput) {
        $appArgs += "--verbose"
    }

    dotnet run --project "Ops.Plugins.Registration/Ops.Plugins.Registration.csproj" -c Release --no-build --no-restore -- @appArgs
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0 -and -not $Apply) {
        Write-ApplyCommand
    }

    exit $exitCode
}
finally {
    Pop-Location
}
