[CmdletBinding()]
param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.2f1\Editor\Unity.exe",
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\UnityProject"),
    [string]$Python = "python",
    [int]$Frames = 240,
    [double]$HitchThresholdMilliseconds = 8.33,
    [switch]$NoGif
)

$ErrorActionPreference = "Stop"
$project = [System.IO.Path]::GetFullPath($ProjectPath)
$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$profileId = "showcase-d3d12-$stamp"
$runRoot = Join-Path $project "PsoArtifacts\ShowcaseRuns\$stamp"
$player = Join-Path $project "Builds\Windows\ShaderHitchShowcase.exe"
$baseline = Join-Path $runRoot "Benchmarks\baseline.benchmark.json"
$optimized = Join-Path $runRoot "Benchmarks\optimized.benchmark.json"
$profileRoot = Join-Path $runRoot "Profiles"
$plan = Join-Path $profileRoot "$profileId\plan.json"
$evidence = Join-Path $runRoot "Evidence"

if (Test-Path -LiteralPath $runRoot) {
    throw "Run directory already exists: $runRoot"
}
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

function ConvertTo-ArgumentLine([string[]]$Arguments) {
    return ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + $_.Replace('"', '\"') + '"'
        } else {
            $_
        }
    }) -join ' '
}

function Invoke-UnityEditor([string[]]$Arguments, [string]$Label) {
    Write-Host "[$Label] Unity Editor"
    $all = @("-batchmode", "-nographics", "-projectPath", ".") + $Arguments
    $process = Start-Process `
        -FilePath $Unity `
        -ArgumentList (ConvertTo-ArgumentLine $all) `
        -WorkingDirectory $project `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "$Label failed with exit code $($process.ExitCode)."
    }
}

function Invoke-ShowcasePlayer(
    [string]$Mode,
    [string]$Report,
    [string]$Log,
    [switch]$Trace,
    [switch]$DisableWarmup
) {
    Write-Host "[$Mode] Visible GPU player"
    $arguments = @(
        "-screen-fullscreen", "0",
        "-screen-width", "1280",
        "-screen-height", "720",
        "-pso-output", $runRoot,
        "-pso-benchmark",
        "-pso-benchmark-mode", $Mode,
        "-pso-benchmark-frames", $Frames.ToString(),
        "-pso-benchmark-discard-frames", "0",
        "-pso-benchmark-delay-seconds", "1.0",
        "-pso-hitch-threshold-ms", $HitchThresholdMilliseconds.ToString(
            [Globalization.CultureInfo]::InvariantCulture),
        "-pso-benchmark-report", $Report,
        "-logFile", $Log
    )
    if ($Trace) {
        $arguments += @(
            "-pso-trace",
            "-pso-trace-phase", "startup",
            "-pso-session", "cold-baseline-$stamp"
        )
    }
    if ($DisableWarmup) {
        $arguments += "-pso-disable-warmup"
    }
    $process = Start-Process `
        -FilePath $player `
        -ArgumentList (ConvertTo-ArgumentLine $arguments) `
        -WorkingDirectory (Split-Path -Parent $player) `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$Mode player failed with exit code $($process.ExitCode)."
    }
}

Invoke-UnityEditor @(
    "-executeMethod",
    "Yanagisawa.ShaderHitchPipeline.Showcase.Editor.PsoShowcaseBuilder.BuildWindowsPlayer",
    "-pso-training-build",
    "-quit",
    "-logFile", "Logs/showcase-training-$stamp.log"
) "training-build"

Invoke-ShowcasePlayer `
    -Mode "baseline" `
    -Report $baseline `
    -Log (Join-Path $runRoot "baseline-player.log") `
    -Trace `
    -DisableWarmup

Invoke-UnityEditor @(
    "-executeMethod", "Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.ProcessInbox",
    "-pso-inbox", (Join-Path $runRoot "Inbox"),
    "-pso-profile-output", $profileRoot,
    "-pso-profile", $profileId,
    "-quit",
    "-logFile", "Logs/showcase-merge-$stamp.log"
) "merge"

Invoke-UnityEditor @(
    "-executeMethod", "Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.InstallPlan",
    "-pso-profile-output", $profileRoot,
    "-pso-profile", $profileId,
    "-quit",
    "-logFile", "Logs/showcase-install-$stamp.log"
) "install"

Invoke-UnityEditor @(
    "-executeMethod",
    "Yanagisawa.ShaderHitchPipeline.Showcase.Editor.PsoShowcaseBuilder.BuildWindowsPlayer",
    "-quit",
    "-logFile", "Logs/showcase-final-$stamp.log"
) "final-build"

Invoke-ShowcasePlayer `
    -Mode "optimized" `
    -Report $optimized `
    -Log (Join-Path $runRoot "optimized-player.log")

$reportArguments = @(
    (Join-Path $PSScriptRoot "pso_report.py"),
    "--baseline", $baseline,
    "--optimized", $optimized,
    "--plan", $plan,
    "--output", $evidence
)
if ($NoGif) {
    $reportArguments += "--no-gif"
}
& $Python @reportArguments
if ($LASTEXITCODE -ne 0) {
    throw "A/B evidence gate failed with exit code $LASTEXITCODE."
}

Write-Host "Completed PSO showcase pipeline."
Write-Host "Evidence: $evidence"
