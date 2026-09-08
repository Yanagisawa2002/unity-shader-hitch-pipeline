[CmdletBinding()]
param(
    [switch]$AllowPerformanceExecution,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.2f1\Editor\Unity.exe",
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\UnityProject"),
    [string]$Python = "python",
    [string]$Ffmpeg = "ffmpeg",
    # Five seconds gives the capture guard time to surface and close any delayed
    # Windows firewall consent UI before the measured 12-second slice begins.
    # Frames is a preallocation/compatibility floor; the scenario gate owns the
    # complete 12-second wall-clock interval in every mode.
    [int]$Frames = 3600,
    [double]$BenchmarkDelaySeconds = 5.0,
    [double]$CaptureSeconds = 20.0,
    [double]$BaselineMinimumMilliseconds = 80.0,
    [double]$ScheduledThresholdMilliseconds = 16.67,
    [int[]]$AutoTuneWorkerCounts = @(1, 2, 4, 8),
    [int[]]$AutoTuneBatchSizes = @(1, 4, 16, 64),
    [int]$AutoTuneRepetitions = 2,
    [switch]$SkipAutoTune,
    [switch]$NoVisualCapture,
    [switch]$NoGif,
    [string]$ComposeExistingRun = ""
)

. (Join-Path $PSScriptRoot 'PsoExecutionPolicy.ps1')
Assert-PsoRuntimeExecutionAllowed -AllowPerformanceExecution:$AllowPerformanceExecution

$ErrorActionPreference = "Stop"
$runner = Join-Path $PSScriptRoot "Invoke-PsoShowcase.ps1"
$arguments = @{
    AllowPerformanceExecution = $AllowPerformanceExecution.IsPresent
    Unity = $Unity
    ProjectPath = $ProjectPath
    Python = $Python
    Ffmpeg = $Ffmpeg
    Preset = "DeadlineRun"
    Frames = $Frames
    HitchThresholdMilliseconds = $ScheduledThresholdMilliseconds
    BenchmarkDelaySeconds = $BenchmarkDelaySeconds
    CaptureSeconds = $CaptureSeconds
    VisualDurationSeconds = 12.0
    WarmupVisualDurationSeconds = 12.0
    VisualBaselineMinimumMilliseconds = $BaselineMinimumMilliseconds
    VisualScheduledMaximumMilliseconds = $ScheduledThresholdMilliseconds
    AutoTuneWorkerCounts = $AutoTuneWorkerCounts
    AutoTuneBatchSizes = $AutoTuneBatchSizes
    AutoTuneRepetitions = $AutoTuneRepetitions
    SkipAutoTune = $SkipAutoTune.IsPresent
    NoVisualCapture = $NoVisualCapture.IsPresent
    NoGif = $NoGif.IsPresent
    ComposeExistingDeadlineRun = $ComposeExistingRun
}

& $runner @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Deadline Run pipeline failed with exit code $LASTEXITCODE."
}
