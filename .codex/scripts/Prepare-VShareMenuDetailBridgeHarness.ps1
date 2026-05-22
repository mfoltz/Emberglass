[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServerInstallPath = $(if ($env:VSHARE_MENU_DETAIL_SERVER_INSTALL) { $env:VSHARE_MENU_DETAIL_SERVER_INSTALL } else { "C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServerCodex" }),

    [string]$EclipseAsset = "",

    [string]$RetroCameraAsset = "",

    [string]$ReceiptRoot = "",

    [switch]$SkipBloodcraftConfig
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
}

if ([string]::IsNullOrWhiteSpace($EclipseAsset)) {
    $EclipseAsset = Join-Path $scriptRoot "..\artifacts\vshare-provenance-assets\Eclipse.dll"
}

if ([string]::IsNullOrWhiteSpace($RetroCameraAsset)) {
    $RetroCameraAsset = Join-Path $scriptRoot "..\artifacts\vshare-provenance-assets\RetroCamera.dll"
}

if ([string]::IsNullOrWhiteSpace($ReceiptRoot)) {
    $ReceiptRoot = Join-Path $scriptRoot "..\runs\vshare-menu-detail-bridge\server-prep"
}

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolved = @(Resolve-Path -LiteralPath $Path -ErrorAction Stop)
    if ($resolved.Count -ne 1) {
        throw "Expected one file for '$Path', found $($resolved.Count)."
    }

    $item = Get-Item -LiteralPath $resolved[0].Path
    if ($item.PSIsContainer) {
        throw "Expected file path, got directory '$Path'."
    }

    return $item
}

function Set-IniValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Section,

        [Parameter(Mandatory = $true)]
        [string]$Key,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $lines = @()
    if (Test-Path -LiteralPath $Path) {
        $lines = @(Get-Content -LiteralPath $Path)
    }

    $sectionHeader = "[$Section]"
    $sectionIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq $sectionHeader) {
            $sectionIndex = $i
            break
        }
    }

    if ($sectionIndex -lt 0) {
        if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -ne "") {
            $lines += ""
        }

        $lines += $sectionHeader
        $lines += "$Key = $Value"
        $lines | Set-Content -LiteralPath $Path -Encoding utf8
        return
    }

    $insertIndex = $lines.Count
    for ($i = $sectionIndex + 1; $i -lt $lines.Count; $i++) {
        $trimmed = $lines[$i].Trim()
        if ($trimmed.StartsWith("[") -and $trimmed.EndsWith("]")) {
            $insertIndex = $i
            break
        }

        $escapedKey = [regex]::Escape($Key)
        if ($trimmed -match "^$escapedKey\s*=") {
            $lines[$i] = "$Key = $Value"
            $lines | Set-Content -LiteralPath $Path -Encoding utf8
            return
        }
    }

    $before = @()
    $after = @()
    if ($insertIndex -gt 0) {
        $before = $lines[0..($insertIndex - 1)]
    }

    if ($insertIndex -lt $lines.Count) {
        $after = $lines[$insertIndex..($lines.Count - 1)]
    }

    @($before + "$Key = $Value" + $after) | Set-Content -LiteralPath $Path -Encoding utf8
}

function Copy-ProvenanceAsset {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Source,

        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    $destinationPath = Join-Path $DestinationDirectory $Source.Name
    Copy-Item -LiteralPath $Source.FullName -Destination $destinationPath -Force
    $hash = Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256

    return [ordered]@{
        name = $Source.Name
        sourcePath = $Source.FullName
        stagedPath = (Resolve-Path -LiteralPath $destinationPath).Path
        sha256 = $hash.Hash
        length = (Get-Item -LiteralPath $destinationPath).Length
    }
}

$serverRoot = (Resolve-Path -LiteralPath $ServerInstallPath -ErrorAction Stop).Path
$serverConfigDirectory = Join-Path $serverRoot "BepInEx\config"
$serverShareDirectory = Join-Path $serverConfigDirectory "Server"
$emberglassConfigDirectory = Join-Path $serverConfigDirectory "Emberglass"
$shareMetadataPath = Join-Path $emberglassConfigDirectory "ShareMetadata.json"
$bloodcraftConfigPath = Join-Path $serverConfigDirectory "io.zfolmt.Bloodcraft.cfg"

$eclipseFile = Resolve-ExistingFile -Path $EclipseAsset
$retroCameraFile = Resolve-ExistingFile -Path $RetroCameraAsset

if ($PSCmdlet.ShouldProcess($serverRoot, "stage VShare menu-detail bridge harness assets")) {
    New-Item -ItemType Directory -Path $serverShareDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $emberglassConfigDirectory -Force | Out-Null

    $stagedAssets = @(
        Copy-ProvenanceAsset -Source $eclipseFile -DestinationDirectory $serverShareDirectory
        Copy-ProvenanceAsset -Source $retroCameraFile -DestinationDirectory $serverShareDirectory
    )

    $metadata = [ordered]@{
        Plugins = [ordered]@{
            Eclipse = [ordered]@{
                GitHubRepo = "mfoltz/Eclipse"
                GitHubTag = "v1.3.17-pre"
                GitHubAssetName = "Eclipse.dll"
                ClientSafe = $true
                HotloadAllowed = $true
                LocalSha256 = ""
                Tags = @("client")
                Categories = @()
            }
            RetroCamera = [ordered]@{
                GitHubRepo = "mfoltz/RetroCamera"
                GitHubTag = "v1.5.4"
                GitHubAssetName = "RetroCamera.dll"
                ClientSafe = $true
                HotloadAllowed = $true
                LocalSha256 = ""
                Tags = @("client")
                Categories = @()
            }
        }
    }
    $metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $shareMetadataPath -Encoding utf8

    if (-not $SkipBloodcraftConfig) {
        New-Item -ItemType Directory -Path $serverConfigDirectory -Force | Out-Null
        Set-IniValue -Path $bloodcraftConfigPath -Section "General" -Key "Eclipsed" -Value "true"
        Set-IniValue -Path $bloodcraftConfigPath -Section "Leveling" -Key "LevelingSystem" -Value "true"
    }

    $runId = Get-Date -Format "yyyyMMdd-HHmmss"
    $receiptDirectory = Join-Path $ReceiptRoot $runId
    New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null

    $receipt = [ordered]@{
        schema = "vshare-menu-detail-bridge-server-prep.v1"
        createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        serverInstallPath = $serverRoot
        shareDirectory = (Resolve-Path -LiteralPath $serverShareDirectory).Path
        shareMetadataPath = (Resolve-Path -LiteralPath $shareMetadataPath).Path
        bloodcraftConfigPath = $bloodcraftConfigPath
        bloodcraftConfigUpdated = -not $SkipBloodcraftConfig.IsPresent
        stagedAssets = $stagedAssets
    }

    $receiptPath = Join-Path $receiptDirectory "receipt.json"
    $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8

    Write-Host "Prepared VShare menu-detail bridge server harness inputs:"
    Write-Host "  $serverRoot"
    Write-Host "Receipt:"
    Write-Host "  $receiptPath"
}
