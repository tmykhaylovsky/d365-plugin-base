[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [string] $ProjectPath
)

$ErrorActionPreference = 'Stop'

$targetPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
$targetDirectory = Split-Path -Parent $targetPath
$keyFileName = Split-Path -Leaf $targetPath

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
        [string] $KeyFileName,

        [string] $ProjectPath
    )

    if (-not $ProjectPath) {
        $projects = @(Get-ChildItem -LiteralPath $targetDirectory -Filter *.csproj -File -ErrorAction SilentlyContinue)
        if ($projects.Count -eq 1) {
            $ProjectPath = $projects[0].FullName
        } else {
            Write-Host "Skipped project signing properties because no single .csproj was found next to $KeyFileName."
            return
        }
    }

    $resolvedProjectPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ProjectPath)
    if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
        throw "Project file not found: $resolvedProjectPath"
    }

    [xml] $project = Get-Content -LiteralPath $resolvedProjectPath -Raw
    $propertyGroup = @($project.Project.PropertyGroup | Where-Object { $_ }) | Select-Object -First 1

    if (-not $propertyGroup) {
        $propertyGroup = $project.CreateElement('PropertyGroup')
        [void] $project.Project.PrependChild($propertyGroup)
    }

    $changed = $false
    $changed = (Set-ProjectProperty -Project $project -PropertyGroup $propertyGroup -Name 'SignAssembly' -Value 'true') -or $changed
    $changed = (Set-ProjectProperty -Project $project -PropertyGroup $propertyGroup -Name 'AssemblyOriginatorKeyFile' -Value $KeyFileName) -or $changed

    if ($changed) {
        $project.Save($resolvedProjectPath)
        Write-Host "Updated signing properties in: $resolvedProjectPath"
    } else {
        Write-Host "Signing properties already configured in: $resolvedProjectPath"
    }
}

if (Test-Path -LiteralPath $targetPath) {
    Write-Host "Strong-name key already exists: $targetPath"
    Ensure-ProjectSigningProperties -KeyFileName $keyFileName -ProjectPath $ProjectPath
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

The generated .snk file is intentionally ignored by git. Replace it with your organization's key if you have one.
The key is a passwordless strong-name key pair; do not use a password-protected signing file for this project.
"@
}

Write-Host "Creating local passwordless strong-name key: $targetPath"
& $sn.FullName -k $targetPath

if ($LASTEXITCODE -ne 0) {
    throw "sn.exe failed with exit code $LASTEXITCODE while creating $targetPath."
}

Ensure-ProjectSigningProperties -KeyFileName $keyFileName -ProjectPath $ProjectPath
