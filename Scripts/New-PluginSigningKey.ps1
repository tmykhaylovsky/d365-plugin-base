[CmdletBinding()]
param(
    [string] $ProjectPath,

    [string] $Path,

    [string] $KeyFileName = 'PluginKey.snk',

    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

function Resolve-ProjectPath {
    param([string] $RequestedProjectPath)

    if ($RequestedProjectPath) {
        $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RequestedProjectPath)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Project file not found: $resolved"
        }

        return $resolved
    }

    $projects = @(Get-ChildItem -LiteralPath $repoRoot -Filter *.csproj -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git|\.vs)\\' })

    $signingProjects = @($projects | Where-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw
        $text -match '<SignAssembly>\s*true\s*</SignAssembly>' -or
        $text -match '<AssemblyOriginatorKeyFile>.*</AssemblyOriginatorKeyFile>'
    })

    if ($signingProjects.Count -eq 1) {
        return $signingProjects[0].FullName
    }

    if ($projects.Count -eq 1) {
        return $projects[0].FullName
    }

    throw 'Could not infer a single plugin project. Pass -ProjectPath explicitly.'
}

function Set-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [xml] $Project,

        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement] $PropertyGroup,

        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    $changed = $false
    $property = $PropertyGroup.SelectSingleNode($Name)
    if (-not $property) {
        $property = $Project.CreateElement($Name)
        [void] $PropertyGroup.AppendChild($property)
        $changed = $true
    }

    if ($property.InnerText -ne $Value) {
        $property.InnerText = $Value
        $changed = $true
    }

    return $changed
}

function Ensure-ProjectSigningProperties {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ResolvedProjectPath,

        [Parameter(Mandatory = $true)]
        [string] $ProjectRelativeKeyPath
    )

    [xml] $project = Get-Content -LiteralPath $ResolvedProjectPath -Raw
    $propertyGroup = @($project.Project.PropertyGroup | Where-Object { $_ }) | Select-Object -First 1

    if (-not $propertyGroup) {
        $propertyGroup = $project.CreateElement('PropertyGroup')
        [void] $project.Project.PrependChild($propertyGroup)
    }

    $changed = $false
    $changed = (Set-ProjectProperty -Project $project -PropertyGroup $propertyGroup -Name 'SignAssembly' -Value 'true') -or $changed
    $changed = (Set-ProjectProperty -Project $project -PropertyGroup $propertyGroup -Name 'AssemblyOriginatorKeyFile' -Value $ProjectRelativeKeyPath) -or $changed

    if ($changed) {
        $project.Save($ResolvedProjectPath)
        Write-Host "Updated signing properties in: $ResolvedProjectPath"
    } else {
        Write-Host "Signing properties already configured in: $ResolvedProjectPath"
    }
}

$resolvedProjectPath = Resolve-ProjectPath -RequestedProjectPath $ProjectPath
$projectDirectory = Split-Path -Parent $resolvedProjectPath

if ($Path) {
    $targetPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
} else {
    $targetPath = Join-Path $projectDirectory $KeyFileName
}

$targetDirectory = Split-Path -Parent $targetPath
$projectDirectoryRoot = $projectDirectory.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$projectDirectoryUri = New-Object System.Uri($projectDirectoryRoot)
$targetPathUri = New-Object System.Uri($targetPath)
$projectRelativeKeyPath = [System.Uri]::UnescapeDataString($projectDirectoryUri.MakeRelativeUri($targetPathUri).ToString()) -replace '\\', '/'

if ((Test-Path -LiteralPath $targetPath) -and -not $Force) {
    Write-Host "Strong-name key already exists: $targetPath"
    Ensure-ProjectSigningProperties -ResolvedProjectPath $resolvedProjectPath -ProjectRelativeKeyPath $projectRelativeKeyPath
    exit 0
}

if ($targetDirectory -and -not (Test-Path -LiteralPath $targetDirectory)) {
    New-Item -ItemType Directory -Path $targetDirectory | Out-Null
}

$sdkRoots = @(
    ${env:ProgramFiles(x86)},
    $env:ProgramFiles
) | Where-Object { $_ } | ForEach-Object { Join-Path $_ 'Microsoft SDKs' } | Where-Object { Test-Path -LiteralPath $_ }

$sn = $sdkRoots |
    ForEach-Object { Get-ChildItem -LiteralPath $_ -Filter sn.exe -Recurse -ErrorAction SilentlyContinue } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $sn) {
    throw @"
Could not find sn.exe to create $targetPath.

Install the .NET Framework SDK component for Visual Studio, or run this from a Visual Studio Developer PowerShell:

  sn -k "$targetPath"

The generated .snk file is a passwordless strong-name key pair. Use an organization-controlled key for production if you need that governance.
"@
}

if ((Test-Path -LiteralPath $targetPath) -and $Force) {
    Remove-Item -LiteralPath $targetPath -Force
}

Write-Host "Creating passwordless strong-name key: $targetPath"
& $sn.FullName -k $targetPath

if ($LASTEXITCODE -ne 0) {
    throw "sn.exe failed with exit code $LASTEXITCODE while creating $targetPath."
}

Ensure-ProjectSigningProperties -ResolvedProjectPath $resolvedProjectPath -ProjectRelativeKeyPath $projectRelativeKeyPath
