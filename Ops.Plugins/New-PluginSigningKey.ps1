[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

$ErrorActionPreference = 'Stop'

$targetPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)

if (Test-Path -LiteralPath $targetPath) {
    Write-Host "Strong-name key already exists: $targetPath"
    exit 0
}

$targetDirectory = Split-Path -Parent $targetPath
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
"@
}

Write-Host "Creating local strong-name key: $targetPath"
& $sn.FullName -k $targetPath

if ($LASTEXITCODE -ne 0) {
    throw "sn.exe failed with exit code $LASTEXITCODE while creating $targetPath."
}
