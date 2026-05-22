[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ProjectPath = Join-Path $RepoRoot "Emberglass.csproj"

dotnet build $ProjectPath `
    --configuration Release `
    --no-restore `
    -p:DeployToServer=false `
    -p:DeployToClient=false
