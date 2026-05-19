param(
    [string]$ServerModsPath = "BepInEx/config/Server",
    [string]$ShareMetadataPath = "BepInEx/config/Emberglass/ShareMetadata.json",
    [string]$ReceiptRoot = ".codex/runs/vshare-provenance",
    [string]$ReleaseMetadataPath = "",
    [switch]$RequireAttestation,
    [switch]$SkipAttestation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-PluginMetadata {
    if (-not (Test-Path -LiteralPath $ShareMetadataPath)) {
        return @{}
    }

    $payload = Get-Content -LiteralPath $ShareMetadataPath -Raw | ConvertFrom-Json
    $entries = @{}
    if ($null -eq $payload -or $null -eq $payload.Plugins) {
        return $entries
    }

    foreach ($property in $payload.Plugins.PSObject.Properties) {
        $entries[$property.Name] = $property.Value
    }

    return $entries
}

function Split-StagedName {
    param([string]$BaseName)

    if ([string]::IsNullOrWhiteSpace($BaseName)) {
        return $null
    }

    $parts = if ($BaseName.Contains("__")) {
        @($BaseName -split "__" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    else {
        @($BaseName -split "_" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    $parts = @($parts)

    if ($parts.Count -lt 3) {
        return $null
    }

    [pscustomobject]@{
        Owner = $parts[0]
        Repo = $parts[1]
        Tag = ($parts[2..($parts.Count - 1)] -join "_")
    }
}

function Resolve-Identity {
    param(
        [System.IO.FileInfo]$Asset,
        [hashtable]$Metadata
    )

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($Asset.Name)
    $keys = New-Object System.Collections.Generic.List[string]
    $keys.Add($baseName)

    $fallback = Split-StagedName -BaseName $baseName
    if ($null -ne $fallback -and -not $keys.Contains($fallback.Repo)) {
        $keys.Add($fallback.Repo)
    }

    foreach ($key in $keys) {
        if (-not $Metadata.ContainsKey($key)) {
            continue
        }

        $entry = $Metadata[$key]
        $repoParts = @(([string]$entry.GitHubRepo) -split "/")
        $repoParts = @($repoParts)
        if ($repoParts.Count -ne 2) {
            throw "GitHubRepo for '$key' must be in owner/repo format."
        }

        if ([string]::IsNullOrWhiteSpace([string]$entry.GitHubTag)) {
            throw "Share metadata for '$key' is missing GitHubTag."
        }

        $assetName = [string]$entry.GitHubAssetName
        if ([string]::IsNullOrWhiteSpace($assetName)) {
            $assetName = $Asset.Name
        }

        return [pscustomobject]@{
            Owner = $repoParts[0]
            Repo = $repoParts[1]
            Tag = [string]$entry.GitHubTag
            AssetName = $assetName
        }
    }

    if ($null -eq $fallback) {
        throw "GitHub Release identity could not be resolved for '$($Asset.Name)'."
    }

    [pscustomobject]@{
        Owner = $fallback.Owner
        Repo = $fallback.Repo
        Tag = $fallback.Tag
        AssetName = $Asset.Name
    }
}

function Get-Release {
    param(
        [string]$Owner,
        [string]$Repo,
        [string]$Tag
    )

    if (-not [string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
        $fixture = Get-Content -LiteralPath $ReleaseMetadataPath -Raw | ConvertFrom-Json
        foreach ($release in @($fixture.releases)) {
            if ($release.owner -ieq $Owner -and $release.repo -ieq $Repo -and $release.tag -ieq $Tag) {
                return $release
            }
        }

        throw "Fixture release metadata did not include $Owner/$Repo@$Tag."
    }

    Invoke-RestMethod `
        -Uri "https://api.github.com/repos/$Owner/$Repo/releases/tags/$Tag" `
        -Headers @{
            Accept = "application/vnd.github+json"
            "User-Agent" = "Emberglass-VShareProvenancePreflight"
        }
}

function Normalize-Digest {
    param([string]$Digest)

    if ([string]::IsNullOrWhiteSpace($Digest)) {
        throw "GitHub Release asset metadata did not include a digest."
    }

    $normalized = $Digest.Trim()
    if ($normalized.StartsWith("sha256:", [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring("sha256:".Length).Trim()
    }
    elseif ($normalized.Contains(":")) {
        throw "GitHub Release asset digest did not use SHA-256."
    }

    if ($normalized -notmatch "^[0-9a-fA-F]{64}$") {
        throw "GitHub Release asset digest was not a valid SHA-256 hex string."
    }

    return $normalized.ToUpperInvariant()
}

function Find-ReleaseAsset {
    param(
        [object]$Release,
        [string]$AssetName
    )

    foreach ($asset in @($Release.assets)) {
        if ($asset.name -ieq $AssetName) {
            return $asset
        }
    }

    throw "GitHub Release asset metadata did not include '$AssetName'."
}

function Test-Attestation {
    param(
        [System.IO.FileInfo]$Asset,
        [string]$Repository
    )

    $command = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "gh attestation verification requested, but 'gh' was not found."
    }

    $attestationOutput = gh attestation verify $Asset.FullName --repo $Repository --format json --deny-self-hosted-runners 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh attestation verify failed for '$($Asset.Name)': $attestationOutput"
    }
}

function New-Result {
    param(
        [string]$Status,
        [string]$Level,
        [System.IO.FileInfo]$Asset,
        [string]$ReleaseAssetName,
        [string]$Repository,
        [string]$Tag,
        [string]$LocalSha256,
        [string]$ReleaseSha256,
        [string]$DownloadUrl,
        [string]$ErrorMessage = ""
    )

    [ordered]@{
        status = $Status
        level = $Level
        stagedFileName = $Asset.Name
        releaseAssetName = $ReleaseAssetName
        repository = $Repository
        tag = $Tag
        localSha256 = $LocalSha256
        releaseSha256 = $ReleaseSha256
        downloadUrl = $DownloadUrl
        error = $ErrorMessage
    }
}

if (-not (Test-Path -LiteralPath $ServerModsPath)) {
    throw "Server mods path '$ServerModsPath' does not exist."
}

New-Item -ItemType Directory -Force -Path $ReceiptRoot | Out-Null

$metadata = Get-PluginMetadata
$results = New-Object System.Collections.Generic.List[object]
$assets = Get-ChildItem -LiteralPath $ServerModsPath -File |
    Where-Object { $_.Extension -ieq ".dll" -or $_.Extension -ieq ".zip" }

foreach ($asset in $assets) {
    try {
        $identity = Resolve-Identity -Asset $asset -Metadata $metadata
        $repository = "$($identity.Owner)/$($identity.Repo)"
        $release = Get-Release -Owner $identity.Owner -Repo $identity.Repo -Tag $identity.Tag
        $releaseAsset = Find-ReleaseAsset -Release $release -AssetName $identity.AssetName
        $releaseSha = Normalize-Digest -Digest ([string]$releaseAsset.digest)
        $localSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $asset.FullName).Hash.ToUpperInvariant()

        if ($localSha -ne $releaseSha) {
            $results.Add((New-Result "failed" "release-digest" $asset $identity.AssetName $repository $identity.Tag $localSha $releaseSha ([string]$releaseAsset.browser_download_url) "GitHub Release asset digest mismatch."))
            continue
        }

        if ($RequireAttestation -and -not $SkipAttestation) {
            try {
                Test-Attestation -Asset $asset -Repository $repository
                $results.Add((New-Result "passed" "attestation" $asset $identity.AssetName $repository $identity.Tag $localSha $releaseSha ([string]$releaseAsset.browser_download_url)))
            }
            catch {
                $results.Add((New-Result "failed" "attestation" $asset $identity.AssetName $repository $identity.Tag $localSha $releaseSha ([string]$releaseAsset.browser_download_url) $_.Exception.Message))
            }

            continue
        }

        $results.Add((New-Result "passed" "release-digest" $asset $identity.AssetName $repository $identity.Tag $localSha $releaseSha ([string]$releaseAsset.browser_download_url)))
    }
    catch {
        $results.Add((New-Result "failed" "release-digest" $asset "" "" "" "" "" "" $_.Exception.Message))
    }
}

$assetResults = $results.ToArray()
$status = if (@($assetResults | Where-Object { $_.status -ne "passed" }).Count -eq 0) { "passed" } else { "failed" }
$resolvedShareMetadataPath = if (Test-Path -LiteralPath $ShareMetadataPath) {
    (Resolve-Path -LiteralPath $ShareMetadataPath).Path
}
else {
    $ShareMetadataPath
}

$receipt = [ordered]@{
    status = $status
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    serverModsPath = (Resolve-Path -LiteralPath $ServerModsPath).Path
    shareMetadataPath = $resolvedShareMetadataPath
    strictAttestation = [bool]($RequireAttestation -and -not $SkipAttestation)
    assets = $assetResults
}

$receiptPath = Join-Path $ReceiptRoot ("vshare-provenance-{0}.json" -f [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss"))
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding UTF8

Write-Host "VShare provenance preflight $status. Receipt: $receiptPath"
if ($status -ne "passed") {
    exit 1
}
