[CmdletBinding()]
param(
    [switch]$AllowPerformanceExecution,
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [string]$Unity =
        "C:\Program Files\Unity\Hub\Editor\6000.1.0f1\Editor\Unity.exe",
    [string]$Python = "python",
    [string]$Ffmpeg = "ffmpeg",
    # The real scene is GPU-heavy. Frames is now a compatibility/capacity floor;
    # the scenario gate retains the complete 12-second wall-clock interval.
    # Forty seconds leaves one extra 12-second circuit when post-capture
    # quiescence misses its first fixed-phase arm point.
    [int]$Frames = 720,
    [double]$BenchmarkDelaySeconds = 5.0,
    [double]$CaptureSeconds = 40.0,
    [double]$BaselineMinimumMilliseconds = 80.0,
    [double]$ScheduledThresholdMilliseconds = 16.67,
    [int[]]$AutoTuneWorkerCounts = @(1, 2, 4, 8),
    # Unity 6000.1's accepted backend is one native asynchronous bulk dispatch;
    # progressive batch ceilings are not a degree of freedom in this cell.
    [int[]]$AutoTuneBatchSizes = @(1),
    [int]$AutoTuneRepetitions = 2,
    [switch]$SkipAutoTune,
    [switch]$NoVisualCapture,
    [switch]$NoGif
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
    Preset = "MegacityMetro"
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
}

& $runner @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Megacity Metro pipeline failed with exit code $LASTEXITCODE."
}
