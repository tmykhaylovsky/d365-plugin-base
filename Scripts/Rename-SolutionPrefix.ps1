[CmdletBinding()]
param(
    [string] $OldPrefix = 'Ops',

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string] $NewPrefix,

    [switch] $Apply,

    [switch] $ReplaceStandalonePrefix,

    [string[]] $ExcludePath = @(
        '.git',
        '.vs',
        '.claude',
        'bin',
        'obj',
        'Debug',
        'Release',
        'packages',
        'TestResults'
    ),

    [string[]] $TextExtension = @(
        '.cs',
        '.csproj',
        '.projitems',
        '.shproj',
        '.sln',
        '.slnx',
        '.props',
        '.targets',
        '.json',
        '.config',
        '.xml',
        '.md',
        '.txt',
        '.ps1',
        '.psm1',
        '.yml',
        '.yaml',
        '.editorconfig',
        '.gitignore'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($OldPrefix -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw "OldPrefix must be a plain identifier such as 'Ops'. Do not include a trailing dot."
}

if ($OldPrefix -eq $NewPrefix) {
    throw 'OldPrefix and NewPrefix are the same.'
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$oldEscaped = [regex]::Escape($OldPrefix)

if ($ReplaceStandalonePrefix) {
    $pattern = "(?<![A-Za-z0-9_])$oldEscaped(?![A-Za-z0-9_])"
    $mode = 'standalone identifier and dot-prefixed namespace matches'
} else {
    $pattern = "(?<![A-Za-z0-9_])$oldEscaped(?=\.)"
    $mode = 'dot-prefixed namespace/project matches only'
}

function Get-RelativePath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $root = $repoRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $rootUri = New-Object System.Uri($root)
    $pathUri = New-Object System.Uri($Path)
    $relative = [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
    return $relative -replace '\\', '/'
}

function Test-IsExcluded {
    param([Parameter(Mandatory = $true)][string] $Path)

    $relative = Get-RelativePath -Path $Path
    foreach ($exclude in $ExcludePath) {
        $normalized = ($exclude -replace '\\', '/').Trim('/')
        if (-not $normalized) {
            continue
        }

        if ($relative -eq $normalized -or $relative.StartsWith("$normalized/")) {
            return $true
        }

        if ($relative -match "(^|/)$([regex]::Escape($normalized))(/|$)") {
            return $true
        }
    }

    return $false
}

function Test-IsTextFile {
    param([Parameter(Mandatory = $true)][System.IO.FileInfo] $File)

    if ($TextExtension -contains $File.Extension) {
        return $true
    }

    return $TextExtension -contains $File.Name
}

function Get-MatchPreview {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )

    $lineNumber = 0
    $lines = $Text -split "`r?`n"
    foreach ($line in $lines) {
        $lineNumber++
        if ($line -match $pattern) {
            $trimmed = $line.Trim()
            if ($trimmed.Length -gt 140) {
                $trimmed = $trimmed.Substring(0, 140) + '...'
            }

            "  $RelativePath`:$lineNumber $trimmed"
        }
    }
}

Write-Host "Repository: $repoRoot"
Write-Host "Rename mode: $mode"
if (-not $Apply) {
    Write-Host 'Preview only. Re-run with -Apply to write changes.'
}

$contentChanges = New-Object System.Collections.Generic.List[object]
$renameChanges = New-Object System.Collections.Generic.List[object]

$files = Get-ChildItem -LiteralPath $repoRoot -Recurse -Force -File |
    Where-Object { -not (Test-IsExcluded -Path $_.FullName) -and (Test-IsTextFile -File $_) }

foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $matches = [regex]::Matches($text, $pattern)
    if ($matches.Count -eq 0) {
        continue
    }

    $relativePath = Get-RelativePath -Path $file.FullName
    $contentChanges.Add([pscustomobject]@{
        File = $file.FullName
        RelativePath = $relativePath
        MatchCount = $matches.Count
        UpdatedText = [regex]::Replace($text, $pattern, $NewPrefix)
        Preview = @(Get-MatchPreview -Text $text -RelativePath $relativePath)
    }) | Out-Null
}

$itemsToRename = Get-ChildItem -LiteralPath $repoRoot -Recurse -Force |
    Where-Object { -not (Test-IsExcluded -Path $_.FullName) -and $_.Name -match $pattern } |
    Sort-Object { $_.FullName.Length } -Descending

foreach ($item in $itemsToRename) {
    $newName = [regex]::Replace($item.Name, $pattern, $NewPrefix)
    $parentPath = if ($item -is [System.IO.DirectoryInfo]) {
        $item.Parent.FullName
    } else {
        $item.DirectoryName
    }
    $destination = Join-Path $parentPath $newName
    if (Test-Path -LiteralPath $destination) {
        throw "Cannot rename '$($item.FullName)' because '$destination' already exists."
    }

    $renameChanges.Add([pscustomobject]@{
        Path = $item.FullName
        RelativePath = Get-RelativePath -Path $item.FullName
        NewName = $newName
        NewRelativePath = Get-RelativePath -Path $destination
    }) | Out-Null
}

Write-Host ''
Write-Host "Content files to update: $($contentChanges.Count)"
foreach ($change in $contentChanges) {
    Write-Host "[$($change.MatchCount)] $($change.RelativePath)"
    foreach ($preview in $change.Preview) {
        Write-Host $preview
    }
}

Write-Host ''
Write-Host "Files/folders to rename: $($renameChanges.Count)"
foreach ($change in $renameChanges) {
    Write-Host "  $($change.RelativePath) -> $($change.NewRelativePath)"
}

if (-not $Apply) {
    exit 0
}

foreach ($change in $contentChanges) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($change.File, $change.UpdatedText, $utf8NoBom)
}

foreach ($change in $renameChanges) {
    Rename-Item -LiteralPath $change.Path -NewName $change.NewName
}

Write-Host ''
Write-Host 'Rename complete.'
