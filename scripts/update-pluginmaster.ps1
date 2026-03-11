[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PluginmasterPath,

    [Parameter(Mandatory = $true)]
    [string]$MetadataPath,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyVersion,

    [Parameter(Mandatory = $false)]
    [string]$InternalName = "XSZRemoteChatBridge",

    [Parameter(Mandatory = $false)]
    [string]$Changelog = "",

    [Parameter(Mandatory = $false)]
    [string]$DistributionRepo = "ShoOtaku/DalamudPlugins",

    [Parameter(Mandatory = $false)]
    [string]$DistributionBranch = "main",

    [Parameter(Mandatory = $false)]
    [string]$PluginPathInRepo = "plugins/XSZRemoteChatBridge/latest.zip"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $MetadataPath)) {
    throw "Metadata file not found: $MetadataPath"
}

$pluginmasterDir = Split-Path -Path $PluginmasterPath -Parent
if (-not [string]::IsNullOrWhiteSpace($pluginmasterDir) -and -not (Test-Path -Path $pluginmasterDir)) {
    New-Item -ItemType Directory -Path $pluginmasterDir -Force | Out-Null
}

$rawPluginmaster = "[]"
if (Test-Path -Path $PluginmasterPath) {
    $rawPluginmaster = Get-Content -Path $PluginmasterPath -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($rawPluginmaster)) {
        $rawPluginmaster = "[]"
    }
}

$pluginArray = New-Object System.Collections.ArrayList
$parsedPluginmaster = $rawPluginmaster | ConvertFrom-Json
if ($null -ne $parsedPluginmaster) {
    if ($parsedPluginmaster -is [System.Array]) {
        foreach ($item in $parsedPluginmaster) {
            [void]$pluginArray.Add($item)
        }
    }
    else {
        [void]$pluginArray.Add($parsedPluginmaster)
    }
}

$metadata = Get-Content -Path $MetadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
$tags = @()
if ($null -ne $metadata.Tags) {
    foreach ($tag in $metadata.Tags) {
        $tagText = [string]$tag
        if (-not [string]::IsNullOrWhiteSpace($tagText)) {
            $tags += $tagText
        }
    }
}
if ($tags.Count -eq 0) {
    $tags = @("utility")
}

$downloadUrl = "https://raw.githubusercontent.com/$DistributionRepo/$DistributionBranch/$PluginPathInRepo"
$downloadUrl = $downloadUrl.Replace("\", "/")
$repoUrl = "https://github.com/$DistributionRepo"
$nowEpoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$existing = $null
$existingIndex = -1
for ($i = 0; $i -lt $pluginArray.Count; $i++) {
    if ([string]$pluginArray[$i].InternalName -eq $InternalName) {
        $existing = $pluginArray[$i]
        $existingIndex = $i
        break
    }
}

$author = if (-not [string]::IsNullOrWhiteSpace([string]$metadata.Author)) { [string]$metadata.Author } elseif ($null -ne $existing) { [string]$existing.Author } else { "XSZYYS" }
$name = if (-not [string]::IsNullOrWhiteSpace([string]$metadata.Name)) { [string]$metadata.Name } else { $InternalName }
$description = if (-not [string]::IsNullOrWhiteSpace([string]$metadata.Description)) { [string]$metadata.Description } elseif ($null -ne $existing) { [string]$existing.Description } else { "" }
$punchline = if (-not [string]::IsNullOrWhiteSpace([string]$metadata.Punchline)) { [string]$metadata.Punchline } elseif ($null -ne $existing) { [string]$existing.Punchline } else { $name }
$applicableVersion = if (-not [string]::IsNullOrWhiteSpace([string]$metadata.ApplicableVersion)) { [string]$metadata.ApplicableVersion } else { "any" }
$dalamudApiLevel = if ($null -ne $metadata.DalamudApiLevel) { [int]$metadata.DalamudApiLevel } elseif ($null -ne $existing) { [int]$existing.DalamudApiLevel } else { 14 }
$iconUrl = if (-not [string]::IsNullOrWhiteSpace([string]$metadata.IconUrl)) { [string]$metadata.IconUrl } elseif ($null -ne $existing) { [string]$existing.IconUrl } else { "" }
$acceptsFeedback = if ($null -ne $existing -and $null -ne $existing.AcceptsFeedback) { [bool]$existing.AcceptsFeedback } else { $true }
$downloadCount = if ($null -ne $existing -and $null -ne $existing.DownloadCount) { [int]$existing.DownloadCount } else { 0 }
$finalChangelog = if (-not [string]::IsNullOrWhiteSpace($Changelog)) { $Changelog } elseif ($null -ne $existing) { [string]$existing.Changelog } else { "" }

$entry = [ordered]@{
    Author              = $author
    Name                = $name
    InternalName        = $InternalName
    AssemblyVersion     = $AssemblyVersion
    Description         = $description
    ApplicableVersion   = $applicableVersion
    RepoUrl             = $repoUrl
    Tags                = $tags
    CategoryTags        = $tags
    DalamudApiLevel     = $dalamudApiLevel
    IconUrl             = $iconUrl
    Punchline           = $punchline
    AcceptsFeedback     = $acceptsFeedback
    DownloadLinkInstall = $downloadUrl
    DownloadLinkTesting = $downloadUrl
    DownloadLinkUpdate  = $downloadUrl
    DownloadCount       = $downloadCount
    LastUpdate          = $nowEpoch
    Changelog           = $finalChangelog
}

$entryObject = [PSCustomObject]$entry
if ($existingIndex -ge 0) {
    $pluginArray[$existingIndex] = $entryObject
}
else {
    [void]$pluginArray.Add($entryObject)
}

$json = ConvertTo-Json -InputObject $pluginArray -Depth 20
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($PluginmasterPath, "$json`n", $utf8NoBom)

Write-Host "pluginmaster updated: $PluginmasterPath"
Write-Host "InternalName=$InternalName AssemblyVersion=$AssemblyVersion"
