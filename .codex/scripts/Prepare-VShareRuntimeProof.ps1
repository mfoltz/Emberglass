[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RequiredEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required environment variable '$Name' was not set."
    }

    return $value
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

$serverInstallPath = Get-RequiredEnvironmentValue -Name "VSHARE_PROOF_SERVER_INSTALL"
$eclipseDllPath = Get-RequiredEnvironmentValue -Name "VSHARE_PROOF_ECLIPSE_DLL"
$eclipseTag = Get-RequiredEnvironmentValue -Name "VSHARE_PROOF_ECLIPSE_TAG"
$expectedSha256 = Get-RequiredEnvironmentValue -Name "VSHARE_PROOF_ECLIPSE_SHA256"

if (-not (Test-Path -LiteralPath $eclipseDllPath)) {
    throw "Eclipse proof DLL not found: $eclipseDllPath"
}

$actualSha256 = (Get-FileHash -LiteralPath $eclipseDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256.ToLowerInvariant()) {
    throw "Eclipse proof DLL digest mismatch. Expected $expectedSha256, got $actualSha256."
}

$serverSharePath = Join-Path $serverInstallPath "BepInEx\config\Server"
$emberglassConfigPath = Join-Path $serverInstallPath "BepInEx\config\Emberglass"
$bloodcraftConfigPath = Join-Path $serverInstallPath "BepInEx\config\io.zfolmt.Bloodcraft.cfg"
$shareMetadataPath = Join-Path $emberglassConfigPath "ShareMetadata.json"

New-Item -ItemType Directory -Path $serverSharePath -Force | Out-Null
New-Item -ItemType Directory -Path $emberglassConfigPath -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Path $bloodcraftConfigPath -Parent) -Force | Out-Null

Copy-Item -LiteralPath $eclipseDllPath -Destination (Join-Path $serverSharePath "Eclipse.dll") -Force
Set-IniValue -Path $bloodcraftConfigPath -Section "General" -Key "UseEmberglassEclipseBridge" -Value "true"

$shareMetadata = [ordered]@{
    Plugins = [ordered]@{
        Eclipse = [ordered]@{
            GitHubRepo = "mfoltz/Eclipse"
            GitHubTag = $eclipseTag
            ClientSafe = $true
            HotloadAllowed = $true
            LocalSha256 = $actualSha256
            Tags = @("client")
            Categories = @()
        }
    }
}

$shareMetadata |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $shareMetadataPath -Encoding utf8

Write-Host "Prepared VShare runtime proof staging:"
Write-Host "  Server share DLL: $(Join-Path $serverSharePath "Eclipse.dll")"
Write-Host "  Share metadata: $shareMetadataPath"
Write-Host "  Bloodcraft bridge config: $bloodcraftConfigPath"
Write-Host "  Eclipse SHA256: $actualSha256"
