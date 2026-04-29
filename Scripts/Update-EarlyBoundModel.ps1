param(
    [string] $SettingsTemplateFile = "Ops.Plugins.Model/builderSettings.json",
    [string] $OutDirectory = "Ops.Plugins.Model",
    [string] $Environment,
    [ValidateSet("Off", "Critical", "Error", "Warning", "Information", "Verbose", "ActivityTracing", "All")]
    [string] $LogLevel,
    [switch] $NoProjItemsUpdate,
    [switch] $Help
)

function Format-PowerShellArgument {
    param([string] $Value)

    if ($null -eq $Value) { return "''" }
    if ($Value -match '^[A-Za-z0-9_./:\\-]+$') { return $Value }
    return "'" + $Value.Replace("'", "''") + "'"
}

function Write-CommandPreview {
    param([string[]] $Arguments)

    $command = @("pac")
    foreach ($argument in $Arguments) {
        $command += (Format-PowerShellArgument $argument)
    }

    Write-Host "Running:"
    Write-Host ("  " + ($command -join " "))
    Write-Host ""
}

function ConvertTo-ProjItemsInclude {
    param(
        [string] $ModelDirectory,
        [string] $FilePath
    )

    $basePath = [System.IO.Path]::GetFullPath($ModelDirectory)
    if (-not $basePath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $basePath += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [Uri]::new($basePath)
    $fileUri = [Uri]::new([System.IO.Path]::GetFullPath($FilePath))
    $relativePath = [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString()).Replace('/', '\')
    return '$(MSBuildThisFileDirectory)' + $relativePath
}

function Sync-ProjItems {
    param(
        [string] $ModelDirectory,
        [string] $ProjItemsPath
    )

    if (-not (Test-Path -LiteralPath $ProjItemsPath)) {
        throw "Could not find shared project items file: $ProjItemsPath"
    }

    [xml] $projectXml = Get-Content -LiteralPath $ProjItemsPath -Raw
    $namespaceUri = $projectXml.Project.NamespaceURI
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($projectXml.NameTable)
    $namespaceManager.AddNamespace("msb", $namespaceUri)

    $itemGroup = $projectXml.SelectSingleNode("/msb:Project/msb:ItemGroup[msb:Compile]", $namespaceManager)
    if ($null -eq $itemGroup) {
        $itemGroup = $projectXml.CreateElement("ItemGroup", $namespaceUri)
        [void] $projectXml.Project.AppendChild($itemGroup)
    }

    foreach ($compileNode in @($itemGroup.SelectNodes("msb:Compile", $namespaceManager))) {
        [void] $itemGroup.RemoveChild($compileNode)
    }

    $sourceFiles = Get-ChildItem -LiteralPath $ModelDirectory -Recurse -File -Filter *.cs |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        } |
        Sort-Object @{ Expression = {
            $relative = (ConvertTo-ProjItemsInclude -ModelDirectory $ModelDirectory -FilePath $_.FullName).
                Replace('$(MSBuildThisFileDirectory)', '').
                Replace('\', '/')
            if ($relative -notmatch '/') { "0/$relative" } else { "1/$relative" }
        } }

    foreach ($sourceFile in $sourceFiles) {
        $compileNode = $projectXml.CreateElement("Compile", $namespaceUri)
        $includeAttribute = $projectXml.CreateAttribute("Include")
        $includeAttribute.Value = ConvertTo-ProjItemsInclude -ModelDirectory $ModelDirectory -FilePath $sourceFile.FullName
        [void] $compileNode.Attributes.Append($includeAttribute)
        [void] $itemGroup.AppendChild($compileNode)
    }

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.NewLineChars = "`r`n"
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)

    $writer = [System.Xml.XmlWriter]::Create($ProjItemsPath, $settings)
    try {
        $projectXml.Save($writer)
    }
    finally {
        $writer.Dispose()
    }

    Write-Host "Updated $ProjItemsPath with $($sourceFiles.Count) generated source file(s)."
}

if ($Help) {
    Write-Host "Regenerate Dataverse early-bound model classes using Ops.Plugins.Model/builderSettings.json."
    Write-Host ""
    Write-Host "Run from the repository root:"
    Write-Host "  .\Scripts\Update-EarlyBoundModel.ps1"
    Write-Host "  .\Scripts\Update-EarlyBoundModel.ps1 -Environment https://org.crm.dynamics.com"
    Write-Host "  .\Scripts\Update-EarlyBoundModel.ps1 -LogLevel Information"
    Write-Host ""
    Write-Host "Defaults:"
    Write-Host "  - Runs pac modelbuilder build with -OutDirectory and -SettingsTemplateFile."
    Write-Host "  - Uses the active PAC auth profile unless -Environment is passed."
    Write-Host "  - Updates Ops.Plugins.Model.projitems after generation unless -NoProjItemsUpdate is passed."
    exit 0
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    if ($null -eq (Get-Command pac -ErrorAction SilentlyContinue)) {
        throw "Power Platform CLI 'pac' was not found. Install it and authenticate with pac auth create before regenerating the model."
    }

    $settingsPath = Resolve-Path -LiteralPath $SettingsTemplateFile -ErrorAction SilentlyContinue
    if ($null -eq $settingsPath) {
        throw "Could not find settings template file: $SettingsTemplateFile"
    }

    $outDirectoryPath = Resolve-Path -LiteralPath $OutDirectory -ErrorAction SilentlyContinue
    if ($null -eq $outDirectoryPath) {
        throw "Could not find output directory: $OutDirectory"
    }

    $pacArgs = @(
        "modelbuilder",
        "build",
        "--outdirectory", $outDirectoryPath.Path,
        "--settingsTemplateFile", $settingsPath.Path
    )

    if (-not [string]::IsNullOrWhiteSpace($Environment)) {
        $pacArgs += @("--environment", $Environment)
    }

    if (-not [string]::IsNullOrWhiteSpace($LogLevel)) {
        $pacArgs += @("--logLevel", $LogLevel)
    }

    Write-CommandPreview -Arguments $pacArgs

    & pac @pacArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (-not $NoProjItemsUpdate) {
        $projectName = Split-Path -Leaf $outDirectoryPath.Path
        $projItemsPath = Join-Path $outDirectoryPath.Path "$projectName.projitems"
        Sync-ProjItems -ModelDirectory $outDirectoryPath.Path -ProjItemsPath $projItemsPath
    }
}
finally {
    Pop-Location
}
