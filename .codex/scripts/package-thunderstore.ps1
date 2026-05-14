param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$projectPath = Join-Path $repoRoot "Emberglass.csproj"
$manifestPath = Join-Path $repoRoot "manifest.json"
$readmePath = Join-Path $repoRoot "README.md"
$changelogPath = Join-Path $repoRoot "CHANGELOG.md"
$iconPath = Join-Path $repoRoot "icon.png"
$dllPath = Join-Path $repoRoot "bin\$Configuration\net6.0\Emberglass.dll"

function Read-ProjectVersion {
    [xml]$project = Get-Content -Path $projectPath
    $version = $project.Project.PropertyGroup.Version | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Unable to determine Emberglass version from $projectPath."
    }

    return $version.Trim()
}

function Assert-RequiredFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required package file is missing: $Path"
    }
}

function Get-ZipEntryLines {
    param([string]$ZipPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)

    try {
        return $archive.Entries |
            Sort-Object FullName |
            ForEach-Object { "{0} {1}" -f $_.FullName, $_.Length }
    }
    finally {
        $archive.Dispose()
    }
}

$version = Read-ProjectVersion
$packageRoot = Join-Path $repoRoot "dist\thunderstore-rehearsal"
$stageRoot = Join-Path $packageRoot "Emberglass-$version"
$zipPath = Join-Path $packageRoot "Emberglass-$version.zip"

Assert-RequiredFile $manifestPath
Assert-RequiredFile $readmePath
Assert-RequiredFile $changelogPath
Assert-RequiredFile $iconPath
Assert-RequiredFile $dllPath

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

Copy-Item -LiteralPath $manifestPath -Destination $stageRoot
Copy-Item -LiteralPath $readmePath -Destination $stageRoot
Copy-Item -LiteralPath $changelogPath -Destination $stageRoot
Copy-Item -LiteralPath $iconPath -Destination $stageRoot
Copy-Item -LiteralPath $dllPath -Destination $stageRoot

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $zipPath -Force

$hash = Get-FileHash -Algorithm SHA256 -Path $zipPath

Write-Output "zip=$zipPath"
Write-Output "sha256=$($hash.Hash)"
Get-ZipEntryLines -ZipPath $zipPath
