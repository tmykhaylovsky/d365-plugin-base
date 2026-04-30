param(
    [ValidateSet("System Admin", "System Admin 2", "Calling User")]
    [string] $Label,

    [Guid] $SystemUserId,

    [string] $FullName,

    [Guid] $SystemAdminId,

    [string] $SystemAdminFullName,

    [Guid] $SystemAdmin2Id,

    [string] $SystemAdmin2FullName,

    [string] $CallingUserFullName = "Calling User",

    [string] $Path = ".local\run-in-user-context.json"
)

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$targetPath = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $repoRoot $Path }
$targetDirectory = Split-Path -Parent $targetPath

function Read-RunInUserRows {
    param([string] $JsonPath)

    $content = Get-Content -Raw -Path $JsonPath
    $parsed = $content | ConvertFrom-Json
    if ($parsed.PSObject.Properties.Name -contains "value" -and $parsed.PSObject.Properties.Name -contains "Count") {
        $parsed = $parsed.value
    }

    return @($parsed | ForEach-Object {
        [pscustomobject]@{
            label = $_.label
            systemuserid = $_.systemuserid
            fullname = $_.fullname
        }
    })
}

function New-RunInUserRow {
    param(
        [string] $RowLabel,
        [AllowNull()] $RowSystemUserId,
        [string] $RowFullName
    )

    [pscustomobject]@{
        label = $RowLabel
        systemuserid = $RowSystemUserId
        fullname = $RowFullName
    }
}

function New-DefaultRunInUserRows {
    return @(
        (New-RunInUserRow -RowLabel "Calling User" -RowSystemUserId $null -RowFullName "Calling User"),
        (New-RunInUserRow -RowLabel "System Admin" -RowSystemUserId $null -RowFullName ""),
        (New-RunInUserRow -RowLabel "System Admin 2" -RowSystemUserId $null -RowFullName "")
    )
}

function Get-SortOrder {
    param([string] $RowLabel)

    if ($RowLabel -eq "Calling User") { return 0 }
    if ($RowLabel -eq "System Admin") { return 1 }
    if ($RowLabel -eq "System Admin 2") { return 2 }
    return 9
}

function Set-RunInUserRow {
    param(
        [object[]] $Rows,
        [string] $RowLabel,
        [AllowNull()] $RowSystemUserId,
        [string] $RowFullName,
        [bool] $SetSystemUserId,
        [bool] $SetFullName
    )

    $updated = @()
    $found = $false

    foreach ($row in $Rows) {
        if ($row.label -ne $RowLabel) {
            $updated += $row
            continue
        }

        $found = $true
        $updated += New-RunInUserRow `
            -RowLabel $RowLabel `
            -RowSystemUserId $(if ($SetSystemUserId) { $RowSystemUserId } else { $row.systemuserid }) `
            -RowFullName $(if ($SetFullName) { $RowFullName } else { $row.fullname })
    }

    if (-not $found) {
        $updated += New-RunInUserRow `
            -RowLabel $RowLabel `
            -RowSystemUserId $(if ($SetSystemUserId) { $RowSystemUserId } else { $null }) `
            -RowFullName $(if ($SetFullName) { $RowFullName } else { "" })
    }

    return $updated
}

if (-not (Test-Path $targetDirectory)) {
    New-Item -ItemType Directory -Path $targetDirectory | Out-Null
}

if (Test-Path $targetPath) {
    $rows = @(Read-RunInUserRows $targetPath)
}
else {
    $templatePath = Join-Path $repoRoot "Ops.Plugins.Registration\run-in-user-context.template.json"
    if (Test-Path -LiteralPath $templatePath) {
        $rows = @(Read-RunInUserRows $templatePath)
    }
    else {
        $rows = @(New-DefaultRunInUserRows)
    }
}

$updatesRequested = $false

$rows = Set-RunInUserRow `
    -Rows $rows `
    -RowLabel "Calling User" `
    -RowSystemUserId $null `
    -RowFullName $CallingUserFullName `
    -SetSystemUserId $true `
    -SetFullName $true

if (-not [string]::IsNullOrWhiteSpace($Label)) {
    $updatesRequested = $true
    $rowSystemUserId = $null
    $setSystemUserId = $Label -eq "Calling User" -or ($PSBoundParameters.ContainsKey("SystemUserId") -and $SystemUserId -ne [Guid]::Empty)
    if ($Label -ne "Calling User" -and $setSystemUserId) {
        $rowSystemUserId = $SystemUserId.ToString("D")
    }

    $rows = Set-RunInUserRow `
        -Rows $rows `
        -RowLabel $Label `
        -RowSystemUserId $rowSystemUserId `
        -RowFullName $(if ($PSBoundParameters.ContainsKey("FullName")) { $FullName } elseif ($Label -eq "Calling User") { "Calling User" } else { "" }) `
        -SetSystemUserId $setSystemUserId `
        -SetFullName ($PSBoundParameters.ContainsKey("FullName") -or $Label -eq "Calling User")
}

if ($PSBoundParameters.ContainsKey("SystemAdminId") -and $SystemAdminId -ne [Guid]::Empty) {
    $updatesRequested = $true
    $rows = Set-RunInUserRow -Rows $rows -RowLabel "System Admin" -RowSystemUserId $SystemAdminId.ToString("D") -RowFullName $SystemAdminFullName -SetSystemUserId $true -SetFullName $PSBoundParameters.ContainsKey("SystemAdminFullName")
}
elseif ($PSBoundParameters.ContainsKey("SystemAdminFullName")) {
    $updatesRequested = $true
    $rows = Set-RunInUserRow -Rows $rows -RowLabel "System Admin" -RowSystemUserId $null -RowFullName $SystemAdminFullName -SetSystemUserId $false -SetFullName $true
}

if ($PSBoundParameters.ContainsKey("SystemAdmin2Id") -and $SystemAdmin2Id -ne [Guid]::Empty) {
    $updatesRequested = $true
    $rows = Set-RunInUserRow -Rows $rows -RowLabel "System Admin 2" -RowSystemUserId $SystemAdmin2Id.ToString("D") -RowFullName $SystemAdmin2FullName -SetSystemUserId $true -SetFullName $PSBoundParameters.ContainsKey("SystemAdmin2FullName")
}
elseif ($PSBoundParameters.ContainsKey("SystemAdmin2FullName")) {
    $updatesRequested = $true
    $rows = Set-RunInUserRow -Rows $rows -RowLabel "System Admin 2" -RowSystemUserId $null -RowFullName $SystemAdmin2FullName -SetSystemUserId $false -SetFullName $true
}

if (-not $updatesRequested) {
    throw "Pass -Label with -SystemUserId/-FullName, or use -SystemAdminId/-SystemAdmin2Id with optional full names."
}

$rows |
    Sort-Object @{ Expression = { Get-SortOrder $_.label } }, label |
    ConvertTo-Json -Depth 4 |
    Set-Content -Path $targetPath -Encoding UTF8

Write-Host "Updated $targetPath"
