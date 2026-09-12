[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetRepository,
    [switch]$AllowRevisionMismatch
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$target = [System.IO.Path]::GetFullPath($TargetRepository)
$integration = Join-Path $root "Integrations\MegacityMetro"
$pin = Get-Content -Raw -LiteralPath (Join-Path $integration "pin.json") |
    ConvertFrom-Json

if (-not (Test-Path -LiteralPath $target -PathType Container)) {
    throw "Megacity target does not exist: $target"
}
if (-not (Test-Path -LiteralPath (Join-Path $target ".git"))) {
    throw "Megacity target is not a Git checkout: $target"
}
if (-not (Test-Path -LiteralPath (Join-Path $target "Assets") -PathType Container)) {
    throw "Megacity target has no Assets directory: $target"
}

$observedRevision = (& git -C $target rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Could not resolve the Megacity revision."
}
if (-not $AllowRevisionMismatch -and $observedRevision -ne [string]$pin.commit) {
    throw "Megacity revision mismatch. Expected $($pin.commit), observed $observedRevision."
}

$source = Join-Path $integration "Assets\ShaderHitchPipelineBenchmark"
$destination = Join-Path $target "Assets\ShaderHitchPipelineBenchmark"
if (Test-Path -LiteralPath $destination) {
    throw "Adapter destination already exists: $destination"
}

Copy-Item -LiteralPath $source -Destination $destination -Recurse
Write-Host "Installed Megacity adapter at $destination"
Write-Host "Pinned revision: $observedRevision"
Write-Host "Next: add the Shader Hitch Pipeline package, select a reveal set, and bind it from the Unity Tools menu."
