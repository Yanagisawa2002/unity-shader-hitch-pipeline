[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Baseline,
    [Parameter(Mandatory = $true)]
    [string]$Optimized,
    [string]$Plan,
    [string]$Output = "Evidence/Latest",
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "pso_report.py"
$arguments = @(
    $scriptPath,
    "--baseline", (Resolve-Path -LiteralPath $Baseline).Path,
    "--optimized", (Resolve-Path -LiteralPath $Optimized).Path,
    "--output", [System.IO.Path]::GetFullPath($Output)
)
if (-not [string]::IsNullOrWhiteSpace($Plan)) {
    $arguments += @("--plan", (Resolve-Path -LiteralPath $Plan).Path)
}

& $Python @arguments
if ($LASTEXITCODE -ne 0) {
    throw "PSO A/B comparison failed with exit code $LASTEXITCODE."
}
