[CmdletBinding()]
param(
    [switch] $Apply,

    [switch] $KeepMarkdown,

    [switch] $KeepRegistration,

    [switch] $KeepRegistrationTests,

    [switch] $KeepRegistrationSyncScript,

    [switch] $KeepRenameScript,

    [switch] $RemoveSigningScript,

    [switch] $RemoveDeployScript,

    [switch] $KeepLocalTooling,

    [string[]] $ExtraRemovePath = @(),

    [switch] $Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Help) {
    Write-Host "Strip advanced starter content from a copied project folder."
    Write-Host ""
    Write-Host "Preview:"
    Write-Host "  .\Scripts\Strip-StarterContent.ps1"
    Write-Host ""
    Write-Host "Apply the default manual-registration starter strip:"
    Write-Host "  .\Scripts\Strip-StarterContent.ps1 -Apply"
    Write-Host ""
    Write-Host "Keep selected advanced pieces:"
    Write-Host "  .\Scripts\Strip-StarterContent.ps1 -Apply -KeepMarkdown"
    Write-Host "  .\Scripts\Strip-StarterContent.ps1 -Apply -KeepRegistration"
    Write-Host "  .\Scripts\Strip-StarterContent.ps1 -Apply -KeepRegistration -KeepMarkdown"
    Write-Host ""
    Write-Host "Default removals:"
    Write-Host "  - Repo-local assistant/tooling folders: .claude, .codex, .local, and .mcp.json."
    Write-Host "  - Root and project markdown files, except Scripts/README.md."
    Write-Host "  - Ops.Plugins.Registration and its solution reference."
    Write-Host "  - Registration tests."
    Write-Host "  - Sync-PluginRegistration.ps1."
    Write-Host "  - Rename-SolutionPrefix.ps1 after it has done its job."
    exit 0
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Get-RelativePath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $root = $repoRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $rootUri = [System.Uri]::new($root)
    $pathUri = [System.Uri]::new($Path)
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string] $RelativePath)

    $combined = Join-Path $repoRoot $RelativePath
    return [System.IO.Path]::GetFullPath($combined)
}

function Assert-InRepo {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $repoRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ($fullPath -ne $repoRoot -and -not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the repository: $fullPath"
    }

    return $fullPath
}

function Add-Removal {
    param(
        [System.Collections.Generic.List[string]] $List,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )

    $fullPath = Assert-InRepo -Path (Resolve-RepoPath -RelativePath $RelativePath)
    if (Test-Path -LiteralPath $fullPath) {
        $List.Add($fullPath) | Out-Null
    }
}

$removePaths = [System.Collections.Generic.List[string]]::new()

if (-not $KeepLocalTooling) {
    Add-Removal -List $removePaths -RelativePath ".claude"
    Add-Removal -List $removePaths -RelativePath ".codex"
    Add-Removal -List $removePaths -RelativePath ".local"
    Add-Removal -List $removePaths -RelativePath ".mcp.json"
}

if (-not $KeepMarkdown) {
    $markdownFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -Force -File -Filter "*.md" |
        Where-Object {
            $relative = Get-RelativePath -Path $_.FullName
            $relative -ne "Scripts\README.md" -and
            $relative -notlike ".git\*" -and
            $relative -notlike ".vs\*"
        }

    foreach ($file in $markdownFiles) {
        $removePaths.Add($file.FullName) | Out-Null
    }
}

if (-not $KeepRegistration) {
    Add-Removal -List $removePaths -RelativePath "Ops.Plugins.Registration"
}

if (-not $KeepRegistrationTests -and -not $KeepRegistration) {
    Add-Removal -List $removePaths -RelativePath "Ops.Plugins.Testing\Registration"
}

if (-not $KeepRegistrationSyncScript -and -not $KeepRegistration) {
    Add-Removal -List $removePaths -RelativePath "Scripts\Sync-PluginRegistration.ps1"
}

if (-not $KeepRenameScript) {
    Add-Removal -List $removePaths -RelativePath "Scripts\Rename-SolutionPrefix.ps1"
}

if ($RemoveSigningScript) {
    Add-Removal -List $removePaths -RelativePath "Scripts\New-PluginSigningKey.ps1"
}

if ($RemoveDeployScript) {
    Add-Removal -List $removePaths -RelativePath "Scripts\Deploy-PluginAssembly.ps1"
}

foreach ($path in $ExtraRemovePath) {
    Add-Removal -List $removePaths -RelativePath $path
}

$removePaths = @($removePaths | Sort-Object -Unique)
$prunedRemovePaths = [System.Collections.Generic.List[string]]::new()
foreach ($path in $removePaths) {
    $isCoveredByParent = $false
    foreach ($keptPath in $prunedRemovePaths) {
        $parentPrefix = $keptPath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        if ($path.StartsWith($parentPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $isCoveredByParent = $true
            break
        }
    }

    if (-not $isCoveredByParent) {
        $prunedRemovePaths.Add($path) | Out-Null
    }
}

$removePaths = @($prunedRemovePaths)

$solutionPath = Join-Path $repoRoot "Ops.Plugins.slnx"
$willUpdateSolution = $false
$updatedSolutionText = $null
if (-not $KeepRegistration -and (Test-Path -LiteralPath $solutionPath)) {
    $solutionText = [System.IO.File]::ReadAllText($solutionPath)
    $updatedSolutionText = [regex]::Replace(
        $solutionText,
        '(?m)^\s*<Project Path="[^"]*\.Plugins\.Registration[/\\][^"]*\.Plugins\.Registration\.csproj" />\r?\n?',
        ''
    )
    $willUpdateSolution = $updatedSolutionText -ne $solutionText
}

Write-Host "Repository: $repoRoot"
if (-not $Apply) {
    Write-Host "Preview only. Re-run with -Apply to remove files and update the solution."
}

Write-Host ""
Write-Host "Paths to remove: $($removePaths.Count)"
foreach ($path in $removePaths) {
    Write-Host ("  " + (Get-RelativePath -Path $path))
}

Write-Host ""
if ($willUpdateSolution) {
    Write-Host "Solution update:"
    Write-Host "  Remove Ops.Plugins.Registration project reference from Ops.Plugins.slnx"
} else {
    Write-Host "Solution update: none"
}

if (-not $Apply) {
    exit 0
}

foreach ($path in $removePaths) {
    $fullPath = Assert-InRepo -Path $path
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

if ($willUpdateSolution) {
    [System.IO.File]::WriteAllText($solutionPath, $updatedSolutionText, $utf8NoBom)
}

Write-Host ""
Write-Host "Strip complete."
