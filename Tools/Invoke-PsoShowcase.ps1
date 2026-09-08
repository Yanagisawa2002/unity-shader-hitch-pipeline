[CmdletBinding()]
param(
    [switch]$AllowPerformanceExecution,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.2f1\Editor\Unity.exe",
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\UnityProject"),
    [string]$Python = "python",
    [string]$Ffmpeg = "ffmpeg",
    [int]$Frames = 1080,
    [double]$HitchThresholdMilliseconds = 8.33,
    [double]$BenchmarkDelaySeconds = 3.0,
    [double]$CaptureSeconds = 11.0,
    [double]$VisualLeadSeconds = 0.35,
    [double]$RequestVisualLeadSeconds = 0.0,
    [double]$VisualDurationSeconds = 2.35,
    [double]$WarmupVisualDurationSeconds = 2.35,
    [ValidateSet("Grid", "DeadlineRun", "MegacityMetro")]
    [string]$Preset = "Grid",
    [double]$VisualBaselineMinimumMilliseconds = 80.0,
    [double]$VisualScheduledMaximumMilliseconds = 16.67,
    [ValidateSet("Normal", "AboveNormal", "High")]
    [string]$PlayerPriorityClass = "High",
    [ValidateSet("Idle", "BelowNormal", "Normal")]
    [string]$FfmpegPriorityClass = "BelowNormal",
    [int[]]$AutoTuneWorkerCounts = @(1, 2, 4, 8),
    [int[]]$AutoTuneBatchSizes = @(4, 16, 64),
    [int]$AutoTuneRepetitions = 2,
    [switch]$SkipAutoTune,
    [switch]$NoVisualCapture,
    [switch]$NoGif,
    [string]$ComposeExistingDeadlineRun = "",
    [string]$ScheduledReplayPlayer = "",
    [int]$ReplayWorkerCount = 4,
    [int]$ReplayBatchSize = 4,
    [int]$ReplayBootstrapBatchSize = 1,
    [double]$ReplayBudgetSafetyMarginMilliseconds = 2.0,
    [double]$ReplayBudgetCostSafetyMultiplier = 1.5,
    [int]$ReplayBudgetCooldownFrames = 8,
    [ValidateSet("auto", "progressive", "native-async-bulk")]
    [string]$ReplayDeadlineBackendMode = "auto"
)

. (Join-Path $PSScriptRoot 'PsoExecutionPolicy.ps1')
Assert-PsoRuntimeExecutionAllowed -AllowPerformanceExecution:$AllowPerformanceExecution

$ErrorActionPreference = "Stop"
$project = [System.IO.Path]::GetFullPath($ProjectPath)
$composeExisting =
    -not [string]::IsNullOrWhiteSpace($ComposeExistingDeadlineRun)
$stamp = if ($composeExisting) {
    Split-Path -Leaf ([System.IO.Path]::GetFullPath($ComposeExistingDeadlineRun))
} else {
    [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
}
$isMegacityMetro = -not $composeExisting -and $Preset -eq "MegacityMetro"
$isScheduledReplay =
    -not [string]::IsNullOrWhiteSpace($ScheduledReplayPlayer)
$isDeadlineRun =
    $composeExisting -or $Preset -eq "DeadlineRun" -or $isMegacityMetro
$captureLogicalProcessorCount = [Environment]::ProcessorCount
$useCaptureCpuIsolation =
    $isMegacityMetro -and
    -not $NoVisualCapture -and
    $captureLogicalProcessorCount -ge 4 -and
    $captureLogicalProcessorCount -le 62
$recorderAffinityMask = if ($useCaptureCpuIsolation) {
    [int64]([uint64]1 -shl ($captureLogicalProcessorCount - 1))
} else {
    [int64]0
}
$playerAffinityMask = if ($useCaptureCpuIsolation) {
    $allLogicalProcessorsMask =
        ([uint64]1 -shl $captureLogicalProcessorCount) - [uint64]1
    [int64]($allLogicalProcessorsMask -bxor [uint64]$recorderAffinityMask)
} else {
    [int64]0
}
$liveCaptureEncoderThreads = if ($useCaptureCpuIsolation) { 1 } else { 2 }
$liveCaptureBackend = if ($isMegacityMetro) {
    "Windows Desktop Duplication (ddagrab)"
} else {
    "Windows GDI (gdigrab)"
}
$scenarioSlug = if ($isMegacityMetro) { "megacity-metro" } else { "deadline-run" }
$scenarioPhase = if ($isMegacityMetro) {
    "megacity-metro-reveal"
} else {
    "deadline-run-reveal"
}
$scenarioDisplayName = if ($isMegacityMetro) {
    "Megacity Metro"
} else {
    "Deadline Run"
}
if ($isDeadlineRun) {
    $minimumScenarioFrames = if ($isMegacityMetro) { 720 } else { 3600 }
    if ($Frames -lt $minimumScenarioFrames) {
        throw "$scenarioDisplayName requires at least $minimumScenarioFrames retained " +
            "benchmark frames."
    }
}
if ($isDeadlineRun -and -not $NoVisualCapture) {
    # Megacity emits CAPTURE_READY only after two full camera circuits.
    # From that marker, 4 s reaches the fixed arm phase; another 12 s allows
    # one retry if recorder startup resets quiescence, followed by the 12 s run.
    $minimumDeadlineCaptureSeconds = if ($isMegacityMetro) {
        28.5
    } else {
        $BenchmarkDelaySeconds + 12.25
    }
    if ($CaptureSeconds -lt $minimumDeadlineCaptureSeconds) {
        throw "DeadlineRun capture must be at least " +
            "$minimumDeadlineCaptureSeconds seconds (conditioned pre-roll + " +
            "complete 12-second run)."
    }
}
if ($isDeadlineRun) {
    if ($isMegacityMetro) {
        $profileId = "megacity-metro-d3d12-$stamp"
        $runRoot = Join-Path $project "PsoArtifacts\MegacityMetroRuns\$stamp"
        $player = Join-Path $project "Builds\Windows\MegacityMetroPso\MegacityMetroPso.exe"
        $builderMethod =
            "Yanagisawa.ShaderHitchPipeline.MegacityMetro.Editor.PsoMegacityMetroBenchmarkAutomation.BuildWindowsPlayer"
        $cacheBusterArgument = "-pso-megacity-cache-buster"
        $runLabel = "megacity-metro"
    } else {
        $profileId = "deadline-run-d3d12-$stamp"
        $runRoot = if ($composeExisting) {
            [System.IO.Path]::GetFullPath($ComposeExistingDeadlineRun)
        } else {
            Join-Path $project "PsoArtifacts\DeadlineRunRuns\$stamp"
        }
        $player = Join-Path $project "Builds\Windows\DeadlineRun.exe"
        $builderMethod =
            "Yanagisawa.ShaderHitchPipeline.DeadlineRun.Editor.DeadlineRunBuilder.BuildWindowsPlayer"
        $cacheBusterArgument = "-pso-scenario-cache-buster"
        $runLabel = "deadline-run"
    }
} else {
    $profileId = "showcase-d3d12-$stamp"
    $runRoot = Join-Path $project "PsoArtifacts\ShowcaseRuns\$stamp"
    $player = Join-Path $project "Builds\Windows\ShaderHitchShowcase.exe"
    $builderMethod =
        "Yanagisawa.ShaderHitchPipeline.Showcase.Editor.PsoShowcaseBuilder.BuildWindowsPlayer"
    $cacheBusterArgument = "-pso-showcase-cache-buster"
    $runLabel = "showcase"
}
if ($isScheduledReplay) {
    if (-not $isMegacityMetro) {
        throw "Scheduled replay currently requires -Preset MegacityMetro."
    }
    if ($NoVisualCapture) {
        throw "Scheduled replay exists to reproduce the live capture path."
    }
    $player = [System.IO.Path]::GetFullPath($ScheduledReplayPlayer)
    if (-not (Test-Path -LiteralPath $player -PathType Leaf)) {
        throw "Scheduled replay Player is missing: $player"
    }
}
$baseline = Join-Path $runRoot "Benchmarks\baseline.benchmark.json"
$naive = Join-Path $runRoot "Benchmarks\naive.benchmark.json"
$scheduled = Join-Path $runRoot "Benchmarks\scheduled.benchmark.json"
$profileRoot = Join-Path $runRoot "Profiles"
$plan = Join-Path $profileRoot "$profileId\plan.json"
$evidence = Join-Path $runRoot "Evidence"
$captures = Join-Path $runRoot "Captures"

if (-not $NoVisualCapture) {
    $null = Get-Command $Ffmpeg -ErrorAction Stop
    if ($isMegacityMetro) {
        $ddagrabHelp = (& $Ffmpeg -hide_banner -h filter=ddagrab 2>&1) -join "`n"
        if ($LASTEXITCODE -ne 0 -or $ddagrabHelp -notmatch "Filter ddagrab") {
            throw "Megacity visual capture requires an FFmpeg build with ddagrab."
        }
    }
}
if (-not $NoVisualCapture -and -not $NoGif) {
    & $Python -c "from PIL import Image"
    if ($LASTEXITCODE -ne 0) {
        throw "GIF output requires Pillow. Run: python -m pip install -r Tools/requirements.txt"
    }
}

if ($composeExisting) {
    if (-not (Test-Path -LiteralPath $runRoot -PathType Container)) {
        throw "Existing Deadline Run directory is missing: $runRoot"
    }
} else {
    if (Test-Path -LiteralPath $runRoot) {
        throw "Run directory already exists: $runRoot"
    }
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
}

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
        -PassThru `
        -WindowStyle Hidden
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "$Label failed with exit code $($process.ExitCode)."
    }
}

function ConvertTo-InvariantNumber([double]$Value) {
    return $Value.ToString("0.######", [Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-EvidencePath([string]$Path, [string]$EvidenceDirectory) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }
    return [System.IO.Path]::GetRelativePath(
        $EvidenceDirectory,
        [System.IO.Path]::GetFullPath($Path)).Replace("\", "/")
}

function ConvertTo-EvidenceCapture([object]$Capture, [string]$EvidenceDirectory) {
    return [ordered]@{
        Mode = $Capture.Mode
        Video = ConvertTo-EvidencePath $Capture.Video $EvidenceDirectory
        MarkerFile = ConvertTo-EvidencePath $Capture.MarkerFile $EvidenceDirectory
        WindowTitle = $Capture.WindowTitle
        CaptureRegion = $Capture.CaptureRegion
        CaptureReadyRealtimeSeconds = $Capture.CaptureReadyRealtimeSeconds
        WorkloadStartRealtimeSeconds = $Capture.WorkloadStartRealtimeSeconds
        MarkerWorkloadStartSecondsInVideo = $Capture.MarkerWorkloadStartSecondsInVideo
        WorkloadStartSecondsInVideo = $Capture.WorkloadStartSecondsInVideo
        ContentRequestRealtimeSeconds = $Capture.ContentRequestRealtimeSeconds
        MarkerContentRequestSecondsInVideo = $Capture.MarkerContentRequestSecondsInVideo
        ContentRequestSecondsInVideo = $Capture.ContentRequestSecondsInVideo
        ContentRevealRealtimeSeconds = $Capture.ContentRevealRealtimeSeconds
        MarkerContentRevealSecondsInVideo = $Capture.MarkerContentRevealSecondsInVideo
        ContentRevealSecondsInVideo = $Capture.ContentRevealSecondsInVideo
        ContentCompleteRealtimeSeconds = $Capture.ContentCompleteRealtimeSeconds
        MarkerContentCompleteSecondsInVideo = $Capture.MarkerContentCompleteSecondsInVideo
        ContentCompleteSecondsInVideo = $Capture.ContentCompleteSecondsInVideo
        RunCompleteRealtimeSeconds = $Capture.RunCompleteRealtimeSeconds
        MarkerRunCompleteSecondsInVideo = $Capture.MarkerRunCompleteSecondsInVideo
        RunCompleteSecondsInVideo = $Capture.RunCompleteSecondsInVideo
        DeferredReadyRealtimeSeconds = $Capture.DeferredReadyRealtimeSeconds
        MarkerDeferredReadySecondsInVideo = $Capture.MarkerDeferredReadySecondsInVideo
        DeferredReadySecondsInVideo = $Capture.DeferredReadySecondsInVideo
        FirstRevealSecondsInVideo = $Capture.FirstRevealSecondsInVideo
        FirstPresentedUiSecondsInVideo = $Capture.FirstPresentedUiSecondsInVideo
        AlignmentSource = $Capture.AlignmentSource
        PlayerPriorityClass = $Capture.PlayerPriorityClass
        FfmpegPriorityClass = $Capture.FfmpegPriorityClass
        CaptureBackend = $Capture.CaptureBackend
        CpuIsolationEnabled = $Capture.CpuIsolationEnabled
        LogicalProcessorCount = $Capture.LogicalProcessorCount
        PlayerProcessorAffinityMask = $Capture.PlayerProcessorAffinityMask
        RecorderProcessorAffinityMask = $Capture.RecorderProcessorAffinityMask
        RecorderThreads = $Capture.RecorderThreads
        FirewallPromptDismissedBeforeCapture =
            $Capture.FirewallPromptDismissedBeforeCapture
        FirewallPromptDismissedCount = $Capture.FirewallPromptDismissedCount
        FirewallNotificationUiResetCount =
            $Capture.FirewallNotificationUiResetCount
        FirewallConfigurationChanged = $Capture.FirewallConfigurationChanged
        ScenarioReport = if ([string]::IsNullOrWhiteSpace($Capture.ScenarioReport)) {
            ""
        } else {
            ConvertTo-EvidencePath $Capture.ScenarioReport $EvidenceDirectory
        }
    }
}

function Set-OwnedProcessPriority(
    [System.Diagnostics.Process]$Process,
    [string]$PriorityClass,
    [string]$Label
) {
    if ($Process.HasExited) {
        return
    }
    try {
        $requested = [System.Enum]::Parse(
            [System.Diagnostics.ProcessPriorityClass],
            $PriorityClass)
        $Process.PriorityClass = $requested
        $Process.Refresh()
        if ($Process.PriorityClass -ne $requested) {
            throw "requested $requested, observed $($Process.PriorityClass)"
        }
    } catch {
        if ($Process.HasExited) {
            return
        }
        throw "Unable to set $Label process priority to $PriorityClass`: " +
            $_.Exception.Message
    }
}

function Set-OwnedProcessAffinity(
    [System.Diagnostics.Process]$Process,
    [int64]$ProcessorAffinityMask,
    [string]$Label
) {
    if ($ProcessorAffinityMask -le 0 -or $Process.HasExited) {
        return
    }
    try {
        $requested = [IntPtr]::new($ProcessorAffinityMask)
        $Process.ProcessorAffinity = $requested
        $Process.Refresh()
        if ($Process.ProcessorAffinity.ToInt64() -ne $ProcessorAffinityMask) {
            throw (
                "requested 0x{0:X}, observed 0x{1:X}" -f
                $ProcessorAffinityMask,
                $Process.ProcessorAffinity.ToInt64())
        }
    } catch {
        if ($Process.HasExited) {
            return
        }
        throw "Unable to set $Label processor affinity`: " +
            $_.Exception.Message
    }
}

function Initialize-PsoFirewallPromptGuard {
    if ($null -ne ("PsoFirewallPromptGuardNative" -as [type])) {
        return
    }
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class PsoFirewallPromptGuardNative
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr window,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    public static long[] GetVisibleWindows(int processId)
    {
        var windows = new List<long>();
        EnumWindows(delegate(IntPtr window, IntPtr parameter)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == (uint)processId && IsWindowVisible(window))
                windows.Add(window.ToInt64());
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    public static bool CloseWindow(long window)
    {
        const uint CloseMessage = 0x0010;
        return PostMessage(
            new IntPtr(window),
            CloseMessage,
            IntPtr.Zero,
            IntPtr.Zero);
    }
}
'@ | Out-Null
}

function Get-VisibleFirewallPromptWindows {
    Initialize-PsoFirewallPromptGuard
    $handles = [System.Collections.Generic.List[long]]::new()
    $servers = Get-CimInstance Win32_Process -Filter "Name='PickerHost.exe'" |
        Where-Object {
            $_.CommandLine -match 'FirewallNotificationDialogServer'
        }
    foreach ($server in $servers) {
        foreach ($handle in [PsoFirewallPromptGuardNative]::GetVisibleWindows(
            [int]$server.ProcessId)) {
            $handles.Add([long]$handle)
        }
    }
    return $handles.ToArray()
}

function Dismiss-NewFirewallPromptBeforeCapture(
    [long[]]$PreexistingHandles,
    [int]$ObservationMilliseconds = 3000
) {
    $preexisting = [System.Collections.Generic.HashSet[long]]::new()
    foreach ($handle in $PreexistingHandles) {
        [void]$preexisting.Add($handle)
    }
    $dismissed = [System.Collections.Generic.HashSet[long]]::new()
    $deadline = [DateTime]::UtcNow.AddMilliseconds($ObservationMilliseconds)
    do {
        foreach ($handle in @(Get-VisibleFirewallPromptWindows)) {
            if (-not $preexisting.Contains($handle)) {
                if (-not [PsoFirewallPromptGuardNative]::CloseWindow($handle)) {
                    throw "Unable to close the new Windows firewall prompt before capture."
                }
                [void]$dismissed.Add($handle)
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    $remaining = @(
        Get-VisibleFirewallPromptWindows |
            Where-Object { -not $preexisting.Contains([long]$_) }
    )
    if ($remaining.Count -gt 0) {
        throw "A Windows firewall prompt still obscures the Player capture region."
    }
    return [pscustomobject]@{
        DismissedCount = $dismissed.Count
        FirewallConfigurationChanged = $false
    }
}

function Reset-FirewallNotificationDialogServer([string]$Label) {
    $servers = @(
        Get-CimInstance Win32_Process -Filter "Name='PickerHost.exe'" |
            Where-Object {
                $_.CommandLine -match 'FirewallNotificationDialogServer'
            }
    )
    foreach ($server in $servers) {
        $ownedServer = Get-Process -Id $server.ProcessId -ErrorAction Stop
        Stop-Process -Id $server.ProcessId -Force -ErrorAction Stop
        [void]$ownedServer.WaitForExit(2000)
        if (-not $ownedServer.HasExited) {
            throw "$Label could not stop firewall notification UI process " +
                "$($server.ProcessId)."
        }
    }
    if ($servers.Count -gt 0) {
        Write-Host (
            "[$Label] Reset $($servers.Count) firewall notification UI " +
            "server(s); firewall rules were not changed.")
    }
    return $servers.Count
}

function Invoke-Ffmpeg(
    [string[]]$Arguments,
    [string]$Label,
    [string]$Log,
    [int64]$ProcessorAffinityMask = 0
) {
    Write-Host "[$Label] ffmpeg"
    $process = Start-Process `
        -FilePath $Ffmpeg `
        -ArgumentList (ConvertTo-ArgumentLine $Arguments) `
        -WorkingDirectory $project `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardError $Log
    Set-OwnedProcessPriority $process $FfmpegPriorityClass $Label
    Set-OwnedProcessAffinity $process $ProcessorAffinityMask $Label
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "$Label failed with exit code $($process.ExitCode). See $Log"
    }
}

function Wait-ShowcaseMarker(
    [string]$MarkerFile,
    [string]$Marker,
    [System.Diagnostics.Process]$PlayerProcess,
    [int]$TimeoutSeconds = 60
) {
    $pattern = [regex]::Escape($Marker) + '=([0-9]+(?:\.[0-9]+)?)'
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $MarkerFile) {
            try {
                $contents = [System.IO.File]::ReadAllText($MarkerFile)
                $matches = [regex]::Matches($contents, $pattern)
                if ($matches.Count -gt 0) {
                    $value = [double]::Parse(
                        $matches[$matches.Count - 1].Groups[1].Value,
                        [Globalization.CultureInfo]::InvariantCulture)
                    return [pscustomobject]@{
                        RealtimeSeconds = $value
                        SeenUtc = [DateTime]::UtcNow
                    }
                }
            } catch [System.IO.IOException] {
                # Unity may briefly hold the log while flushing the marker.
            }
        }
        $PlayerProcess.Refresh()
        if ($PlayerProcess.HasExited) {
            throw "Player exited before marker '$Marker' appeared. See $MarkerFile"
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for marker '$Marker'. See $MarkerFile"
}

function Wait-PlayerWindow(
    [System.Diagnostics.Process]$PlayerProcess,
    [int]$TimeoutSeconds = 30
) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $PlayerProcess.Refresh()
        if ($PlayerProcess.HasExited) {
            throw "Player exited before its capture window was available."
        }
        if ($PlayerProcess.MainWindowHandle -ne [IntPtr]::Zero -and
            -not [string]::IsNullOrWhiteSpace($PlayerProcess.MainWindowTitle)) {
            return $PlayerProcess.MainWindowTitle
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for the player capture window."
}

function Get-PlayerClientCaptureRect([System.Diagnostics.Process]$PlayerProcess) {
    if ($null -eq ("PsoShowcaseCaptureNative" -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class PsoShowcaseCaptureNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
}
'@ | Out-Null
    }

    $PlayerProcess.Refresh()
    $handle = $PlayerProcess.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        throw "Player capture window handle is unavailable."
    }

    # Keep the owned Player unobscured and on the primary desktop. Desktop-region
    # capture is required because D3D12 flip-model content is black through title=.
    # Megacity restores its saved borderless resolution after process startup, so
    # normalize the late window to the same 1280x720 client area requested on the
    # command line before asking Desktop Duplication for a crop.
    $topMost = [IntPtr](-1)
    $showWindow = [uint32]0x0040
    $restoreWindow = 9
    [void][PsoShowcaseCaptureNative]::ShowWindow($handle, $restoreWindow)
    $initialClient = New-Object PsoShowcaseCaptureNative+Rect
    $initialWindow = New-Object PsoShowcaseCaptureNative+Rect
    if (-not [PsoShowcaseCaptureNative]::GetClientRect(
            $handle,
            [ref]$initialClient) -or
        -not [PsoShowcaseCaptureNative]::GetWindowRect(
            $handle,
            [ref]$initialWindow)) {
        throw "Unable to measure the Player window before capture normalization."
    }
    $nonClientWidth = [Math]::Max(
        0,
        ($initialWindow.Right - $initialWindow.Left) -
            ($initialClient.Right - $initialClient.Left))
    $nonClientHeight = [Math]::Max(
        0,
        ($initialWindow.Bottom - $initialWindow.Top) -
            ($initialClient.Bottom - $initialClient.Top))
    [void][PsoShowcaseCaptureNative]::SetWindowPos(
        $handle,
        $topMost,
        32,
        32,
        (1280 + $nonClientWidth),
        (720 + $nonClientHeight),
        $showWindow)
    [void][PsoShowcaseCaptureNative]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 250

    $rect = New-Object PsoShowcaseCaptureNative+Rect
    $origin = New-Object PsoShowcaseCaptureNative+Point
    if (-not [PsoShowcaseCaptureNative]::GetClientRect($handle, [ref]$rect)) {
        throw "GetClientRect failed for the Player window."
    }
    if (-not [PsoShowcaseCaptureNative]::ClientToScreen($handle, [ref]$origin)) {
        throw "ClientToScreen failed for the Player window."
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $desktopWidth = [PsoShowcaseCaptureNative]::GetSystemMetrics(0)
    $desktopHeight = [PsoShowcaseCaptureNative]::GetSystemMetrics(1)
    $width = [Math]::Min($width, $desktopWidth - $origin.X)
    $height = [Math]::Min($height, $desktopHeight - $origin.Y)
    # yuv420p requires even dimensions. DDA clips a crop at the display edge,
    # which otherwise turns an odd 1017-line region into an x264 startup error.
    $width -= $width % 2
    $height -= $height % 2
    if ($origin.X -lt 0 -or $origin.Y -lt 0 -or $width -lt 64 -or $height -lt 64) {
        throw "Invalid Player client capture region: $($origin.X),$($origin.Y) ${width}x${height}"
    }
    return [pscustomobject]@{
        X = $origin.X
        Y = $origin.Y
        Width = $width
        Height = $height
    }
}

function Stop-OwnedPlayer([System.Diagnostics.Process]$PlayerProcess) {
    $PlayerProcess.Refresh()
    if ($PlayerProcess.HasExited) {
        return
    }

    [void]$PlayerProcess.CloseMainWindow()
    if (-not $PlayerProcess.WaitForExit(5000)) {
        $PlayerProcess.Kill()
        $PlayerProcess.WaitForExit()
    }
}

function Assert-BenchmarkReceipt([string]$Report, [string]$Mode) {
    if (-not (Test-Path -LiteralPath $Report)) {
        throw "$Mode benchmark did not create $Report"
    }
    $receipt = Get-Content -Raw -LiteralPath $Report | ConvertFrom-Json
    if (-not $receipt.completed) {
        throw "$Mode benchmark was not completed: $($receipt.error)"
    }
}

function Assert-CapturePixels(
    [string]$Capture,
    [double]$SampleAtSeconds,
    [string]$Mode
) {
    $statisticsLog = $Capture + ".signalstats.log"
    Invoke-Ffmpeg `
        -Label "$Mode-pixel-validation" `
        -Log $statisticsLog `
        -Arguments @(
            "-y",
            "-ss", (ConvertTo-InvariantNumber $SampleAtSeconds),
            "-i", $Capture,
            "-frames:v", "1",
            "-vf", "signalstats,metadata=print",
            "-f", "null",
            "NUL"
        )
    $contents = Get-Content -Raw -LiteralPath $statisticsLog
    $maximumMatch = [regex]::Match(
        $contents,
        'lavfi\.signalstats\.YMAX=([0-9]+(?:\.[0-9]+)?)')
    $minimumMatch = [regex]::Match(
        $contents,
        'lavfi\.signalstats\.YMIN=([0-9]+(?:\.[0-9]+)?)')
    if (-not $maximumMatch.Success -or -not $minimumMatch.Success) {
        throw "$Mode capture pixel validation did not emit signal statistics."
    }
    $maximum = [double]::Parse(
        $maximumMatch.Groups[1].Value,
        [Globalization.CultureInfo]::InvariantCulture)
    $minimum = [double]::Parse(
        $minimumMatch.Groups[1].Value,
        [Globalization.CultureInfo]::InvariantCulture)
    if ($maximum -lt 100.0 -or ($maximum - $minimum) -lt 50.0) {
        throw "$Mode capture is blank or obscured (YMIN=$minimum, YMAX=$maximum)."
    }
}

function Assert-NoObscuringSystemDialog(
    [string]$Capture,
    [pscustomobject]$CaptureRegion,
    [string]$Mode
) {
    # The Deadline Run pre-roll has a deliberately saturated blue corridor. A
    # Windows Security / firewall dialog creates a large, bright, nearly neutral
    # rectangle in this central region. Gate the encoded pixels as a second line
    # of defence after the native-window guard.
    $cropWidth = [Math]::Floor($CaptureRegion.Width * 0.3125)
    $cropHeight = [Math]::Floor($CaptureRegion.Height * 0.5556)
    $cropX = [Math]::Floor(($CaptureRegion.Width - $cropWidth) * 0.5)
    $cropY = [Math]::Floor($CaptureRegion.Height * 0.2083)
    $cropWidth -= $cropWidth % 2
    $cropHeight -= $cropHeight % 2
    $statisticsLog = $Capture + ".obstructionstats.log"
    Invoke-Ffmpeg `
        -Label "$Mode-obstruction-validation" `
        -Log $statisticsLog `
        -Arguments @(
            "-y",
            "-t", "1.0",
            "-i", $Capture,
            "-vf",
                "crop=$cropWidth`:$cropHeight`:$cropX`:$cropY,signalstats,metadata=print",
            "-f", "null",
            "NUL"
        )

    $luma = [System.Collections.Generic.List[double]]::new()
    $saturation = [System.Collections.Generic.List[double]]::new()
    foreach ($line in (Get-Content -LiteralPath $statisticsLog)) {
        if ($line -match 'lavfi\.signalstats\.YAVG=([0-9]+(?:\.[0-9]+)?)') {
            $luma.Add([double]::Parse(
                $Matches[1],
                [Globalization.CultureInfo]::InvariantCulture))
        } elseif ($line -match 'lavfi\.signalstats\.SATAVG=([0-9]+(?:\.[0-9]+)?)') {
            $saturation.Add([double]::Parse(
                $Matches[1],
                [Globalization.CultureInfo]::InvariantCulture))
        }
    }
    if ($luma.Count -lt 30 -or $saturation.Count -lt 30) {
        throw "$Mode obstruction validation received too few video frames."
    }
    $sortedLuma = @($luma | Sort-Object)
    $sortedSaturation = @($saturation | Sort-Object)
    $medianLuma = $sortedLuma[[Math]::Floor($sortedLuma.Count / 2)]
    $medianSaturation =
        $sortedSaturation[[Math]::Floor($sortedSaturation.Count / 2)]
    if ($medianLuma -gt 80.0 -and $medianSaturation -lt 15.0) {
        throw "$Mode capture contains a likely Windows Security/firewall dialog " +
            "(central median YAVG=$medianLuma, SATAVG=$medianSaturation)."
    }
}

function Get-FirstRevealSeconds(
    [string]$Capture,
    [pscustomobject]$CaptureRegion,
    [string]$Mode,
    [double]$MinimumSeconds,
    [bool]$MegacityMetro = $false
) {
    if ($MegacityMetro) {
        # The pinned 150 m street-canyon cell places the complete 16 x 3 neon
        # band across the central reveal corridor. Keep both the HUD and most of
        # the moving sky outside this crop so the unchanged four-frame / 2.0-luma
        # gate tracks the controlled reveal rather than slow background drift.
        $cropX = [Math]::Floor($CaptureRegion.Width * 0.30)
        $cropY = [Math]::Floor($CaptureRegion.Height * 0.40)
        $cropWidth = [Math]::Floor($CaptureRegion.Width * 0.40)
        $cropHeight = [Math]::Floor($CaptureRegion.Height * 0.20)
    } else {
        $cropX = [Math]::Floor($CaptureRegion.Width * 0.25)
        $cropY = [Math]::Floor($CaptureRegion.Height * 0.2222)
        $cropWidth = [Math]::Floor($CaptureRegion.Width * 0.5)
        $cropHeight = [Math]::Floor($CaptureRegion.Height * 0.1333)
    }
    $cropWidth -= $cropWidth % 2
    $cropHeight -= $cropHeight % 2
    $statisticsLog = $Capture + ".revealstats.log"

    Invoke-Ffmpeg `
        -Label "$Mode-reveal-detection" `
        -Log $statisticsLog `
        -Arguments @(
            "-y",
            "-i", $Capture,
            "-vf", "crop=$cropWidth`:$cropHeight`:$cropX`:$cropY,signalstats,metadata=print",
            "-f", "null",
            "NUL"
        )

    $samples = [System.Collections.Generic.List[object]]::new()
    $sampleTime = $null
    foreach ($line in (Get-Content -LiteralPath $statisticsLog)) {
        if ($line -match '^\[Parsed_metadata[^]]*\] frame:\s*\d+\s+pts:\s*\d+\s+pts_time:([0-9.]+)') {
            $sampleTime = [double]::Parse(
                $Matches[1],
                [Globalization.CultureInfo]::InvariantCulture)
        } elseif ($null -ne $sampleTime -and
                  $line -match 'lavfi\.signalstats\.YAVG=([0-9.]+)') {
            $samples.Add([pscustomobject]@{
                Time = $sampleTime
                AverageLuma = [double]::Parse(
                    $Matches[1],
                    [Globalization.CultureInfo]::InvariantCulture)
            })
            $sampleTime = $null
        }
    }
    if ($samples.Count -lt 10) {
        throw "$Mode reveal detection received too few video frames."
    }

    # Use the frames immediately preceding the marker window as the visual
    # reference. A long, continuously moving third-party scene can drift in
    # average luma from the beginning of the recording even when no reveal has
    # happened. The local reference proves a discontinuity at CONTENT_REVEAL
    # without depending on whether the new content is lighter or darker.
    $referenceLuma = @(
        $samples |
            Where-Object {
                $_.Time -ge [Math]::Max(0.0, $MinimumSeconds - 0.55) -and
                $_.Time -le [Math]::Max(0.0, $MinimumSeconds - 0.05)
            } |
            ForEach-Object { $_.AverageLuma } |
            Sort-Object
    )
    if ($referenceLuma.Count -lt 8) {
        $referenceCount = [Math]::Min(24, [Math]::Floor($samples.Count / 4))
        $referenceLuma = @(
            $samples | Select-Object -First $referenceCount |
                ForEach-Object { $_.AverageLuma } | Sort-Object
        )
    }
    if ($referenceLuma.Count -lt 8) {
        throw "$Mode reveal detection has too little pre-roll for a background reference."
    }
    $backgroundLuma = [double]$referenceLuma[
        [Math]::Floor($referenceLuma.Count / 2)]
    # Require a persistent multi-frame absolute change so both luminous and
    # shadowed scene reveals are detectable, while a one-frame capture artifact
    # cannot be mistaken for the actual content transition.
    $minimumLumaDelta = 2.0

    for ($index = 0; $index -lt $samples.Count - 3; $index++) {
        if ($samples[$index].Time -ge $MinimumSeconds -and
            [Math]::Abs($samples[$index].AverageLuma - $backgroundLuma) -gt $minimumLumaDelta -and
            [Math]::Abs($samples[$index + 1].AverageLuma - $backgroundLuma) -gt $minimumLumaDelta -and
            [Math]::Abs($samples[$index + 2].AverageLuma - $backgroundLuma) -gt $minimumLumaDelta -and
            [Math]::Abs($samples[$index + 3].AverageLuma - $backgroundLuma) -gt $minimumLumaDelta) {
            return $samples[$index].Time
        }
    }
    throw "$Mode reveal detection did not find the first visible content reveal."
}

function Get-FirstPresentedUiSeconds(
    [string]$Capture,
    [pscustomobject]$CaptureRegion,
    [string]$Mode
) {
    $cropHeight = [Math]::Min(140, $CaptureRegion.Height)
    $statisticsLog = $Capture + ".uistats.log"
    Invoke-Ffmpeg `
        -Label "$Mode-ui-detection" `
        -Log $statisticsLog `
        -Arguments @(
            "-y",
            "-i", $Capture,
            "-vf", "crop=$($CaptureRegion.Width)`:$cropHeight`:0`:0,signalstats,metadata=print",
            "-f", "null",
            "NUL"
    )

    $samples = [System.Collections.Generic.List[object]]::new()
    $sampleTime = $null
    $minimumLuma = $null
    foreach ($line in (Get-Content -LiteralPath $statisticsLog)) {
        if ($line -match '^\[Parsed_metadata[^]]*\] frame:\s*\d+\s+pts:\s*\d+\s+pts_time:([0-9.]+)') {
            $sampleTime = [double]::Parse(
                $Matches[1],
                [Globalization.CultureInfo]::InvariantCulture)
            $minimumLuma = $null
        } elseif ($null -ne $sampleTime -and
                  $line -match 'lavfi\.signalstats\.YMIN=([0-9.]+)') {
            $minimumLuma = [double]::Parse(
                $Matches[1],
                [Globalization.CultureInfo]::InvariantCulture)
        } elseif ($null -ne $sampleTime -and
                  $null -ne $minimumLuma -and
                  $line -match 'lavfi\.signalstats\.YMAX=([0-9.]+)') {
            $maximumLuma = [double]::Parse(
                $Matches[1],
                [Globalization.CultureInfo]::InvariantCulture)
            $samples.Add([pscustomobject]@{
                Time = $sampleTime
                MinimumLuma = $minimumLuma
                MaximumLuma = $maximumLuma
                LumaRange = $maximumLuma - $minimumLuma
            })
            $sampleTime = $null
            $minimumLuma = $null
        }
    }
    for ($index = 0; $index -lt $samples.Count - 1; $index++) {
        if ($samples[$index].MaximumLuma -ge 100.0 -and
            $samples[$index].LumaRange -ge 60.0 -and
            $samples[$index + 1].MaximumLuma -ge 100.0 -and
            $samples[$index + 1].LumaRange -ge 60.0) {
            return $samples[$index].Time
        }
    }
    throw "$Mode UI detection did not find two high-contrast title frames."
}

function Invoke-ShowcasePlayer(
    [string]$Mode,
    [string]$Report,
    [string]$Log,
    [string]$Capture,
    [int]$SampleFrames = 0,
    [string]$Strategy = "",
    [int]$AsyncJobCount = 0,
    [int]$InitialBatchSize = 0,
    [int]$MaximumBatchSize = 0,
    [int]$BootstrapBatchSize = 0,
    [double]$BudgetSafetyMarginMilliseconds = 0.0,
    [double]$BudgetCostSafetyMultiplier = 0.0,
    [int]$BudgetCooldownFrames = -1,
    [string]$DeadlineBackendMode = "",
    [string]$WarmupReceipt = "",
    [string]$ScenarioReport = "",
    [switch]$Trace,
    [switch]$DisableWarmup,
    [switch]$SurfaceFirewallPrompt
) {
    Write-Host "[$Mode] Visible GPU player"
    $firewallPromptsBefore = @()
    if (-not [string]::IsNullOrWhiteSpace($Capture) -or
        $SurfaceFirewallPrompt) {
        $firewallPromptsBefore = @(Get-VisibleFirewallPromptWindows)
        if ($firewallPromptsBefore.Count -gt 0) {
            throw "A pre-existing Windows firewall prompt makes visual capture unsafe."
        }
    }
    $requestedSampleFrames = if ($SampleFrames -gt 0) {
        $SampleFrames
    } else {
        $Frames
    }
    $arguments = @(
        "-screen-fullscreen", "0",
        "-screen-width", "1280",
        "-screen-height", "720",
        "-pso-output", $runRoot,
        "-pso-benchmark",
        "-pso-benchmark-mode", $Mode,
        "-pso-benchmark-frames", $requestedSampleFrames.ToString(),
        "-pso-benchmark-discard-frames", "0",
        "-pso-benchmark-delay-seconds",
            (ConvertTo-InvariantNumber $BenchmarkDelaySeconds),
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
    if (-not [string]::IsNullOrWhiteSpace($Strategy)) {
        $arguments += @("-pso-warmup-strategy", $Strategy)
    }
    if ($AsyncJobCount -gt 0) {
        $arguments += @("-max-async-pso-job-count", $AsyncJobCount.ToString())
    }
    if ($InitialBatchSize -gt 0) {
        $arguments += @("-pso-warmup-initial-batch", $InitialBatchSize.ToString())
    }
    if ($MaximumBatchSize -gt 0) {
        $arguments += @("-pso-warmup-maximum-batch", $MaximumBatchSize.ToString())
    }
    if ($BootstrapBatchSize -gt 0) {
        $arguments += @("-pso-warmup-bootstrap-batch", $BootstrapBatchSize.ToString())
    }
    if ($BudgetSafetyMarginMilliseconds -gt 0.0) {
        $arguments += @(
            "-pso-warmup-budget-safety-margin-ms",
            (ConvertTo-InvariantNumber $BudgetSafetyMarginMilliseconds)
        )
    }
    if ($BudgetCostSafetyMultiplier -ge 1.0) {
        $arguments += @(
            "-pso-warmup-budget-cost-safety-multiplier",
            (ConvertTo-InvariantNumber $BudgetCostSafetyMultiplier)
        )
    }
    if ($BudgetCooldownFrames -ge 0) {
        $arguments += @(
            "-pso-warmup-budget-cooldown-frames",
            $BudgetCooldownFrames.ToString()
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($DeadlineBackendMode)) {
        $arguments += @(
            "-pso-deadline-backend-mode",
            $DeadlineBackendMode
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($WarmupReceipt)) {
        $arguments += @("-pso-warmup-receipt", $WarmupReceipt)
    }
    if (-not [string]::IsNullOrWhiteSpace($ScenarioReport)) {
        $arguments += @("-pso-scenario-report", $ScenarioReport)
    }
    $markerFile = ""
    if (-not [string]::IsNullOrWhiteSpace($Capture)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $Capture) -Force | Out-Null
        $markerFile = $Capture + ".markers.txt"
        $arguments += @(
            "-pso-benchmark-no-quit",
            "-pso-scenario-marker", $markerFile,
            "-pso-showcase-marker", $markerFile
        )
    }
    $process = Start-Process `
        -FilePath $player `
        -ArgumentList (ConvertTo-ArgumentLine $arguments) `
        -WorkingDirectory (Split-Path -Parent $player) `
        -PassThru
    Set-OwnedProcessPriority $process $PlayerPriorityClass "$Mode Player"
    if (-not [string]::IsNullOrWhiteSpace($Capture)) {
        Set-OwnedProcessAffinity `
            $process `
            $playerAffinityMask `
            "$Mode Player"
    }

    if ([string]::IsNullOrWhiteSpace($Capture)) {
        if ($SurfaceFirewallPrompt) {
            $null = Wait-PlayerWindow $process
            $null = Get-PlayerClientCaptureRect $process
            $trainingPromptGuard = Dismiss-NewFirewallPromptBeforeCapture `
                -PreexistingHandles $firewallPromptsBefore
            if ($trainingPromptGuard.DismissedCount -gt 0) {
                Write-Host (
                    "[$Mode] Closed $($trainingPromptGuard.DismissedCount) " +
                    "firewall prompt without changing firewall configuration.")
            }
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "$Mode player failed with exit code $($process.ExitCode)."
        }
        Assert-BenchmarkReceipt $Report $Mode
        return
    }

    $restoreCursor = $false
    $previousCursor = $null
    try {
        $captureReadyTimeoutSeconds = if ($isMegacityMetro) { 90 } else { 60 }
        $captureReady = Wait-ShowcaseMarker `
            -MarkerFile $markerFile `
            -Marker "CAPTURE_READY realtime" `
            -PlayerProcess $process `
            -TimeoutSeconds $captureReadyTimeoutSeconds
        $windowTitle = Wait-PlayerWindow $process
        $captureRect = Get-PlayerClientCaptureRect $process
        # Some Windows builds surface the consent UI only after the owned Player
        # becomes foreground/top-most, so dismissal must happen after positioning.
        $firewallPromptGuard = Dismiss-NewFirewallPromptBeforeCapture `
            -PreexistingHandles $firewallPromptsBefore
        $previousCursor = New-Object PsoShowcaseCaptureNative+Point
        $restoreCursor = [PsoShowcaseCaptureNative]::GetCursorPos(
            [ref]$previousCursor)
        [void][PsoShowcaseCaptureNative]::SetCursorPos(0, 0)
        Start-Sleep -Milliseconds 150
        $lateFirewallPrompts = @(Get-VisibleFirewallPromptWindows)
        if ($lateFirewallPrompts.Count -gt 0) {
            throw "A late Windows firewall prompt appeared immediately before capture."
        }
        $captureStartUtc = [DateTime]::UtcNow
        $captureInputArguments = if ($isMegacityMetro) {
            @(
                "-f", "lavfi",
                "-i", (
                    "ddagrab=output_idx=0:draw_mouse=0:framerate=60:" +
                    "video_size=$($captureRect.Width)x$($captureRect.Height):" +
                    "offset_x=$($captureRect.X):offset_y=$($captureRect.Y)")
            )
        } else {
            @(
                "-f", "gdigrab",
                "-draw_mouse", "0",
                "-framerate", "60",
                "-thread_queue_size", "1024",
                "-offset_x", $captureRect.X.ToString(),
                "-offset_y", $captureRect.Y.ToString(),
                "-video_size", "$($captureRect.Width)x$($captureRect.Height)",
                "-i", "desktop"
            )
        }
        $captureTransferArguments = if ($isMegacityMetro) {
            @("-vf", "hwdownload,format=bgra")
        } else {
            @()
        }
        $captureArguments = @("-y") + $captureInputArguments + @(
            "-t", (ConvertTo-InvariantNumber $CaptureSeconds),
            "-an"
        ) + $captureTransferArguments + @(
            "-c:v", "libx264",
            "-threads", $liveCaptureEncoderThreads.ToString(),
            # Keep the evidence recorder below the Player's CPU budget. Final
            # triptych encoding happens after every measured process exits.
            "-preset", "ultrafast",
            "-crf", "18",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            $Capture
        )
        Invoke-Ffmpeg `
            -Label "$Mode-window-capture" `
            -Log ($Capture + ".ffmpeg.log") `
            -ProcessorAffinityMask $recorderAffinityMask `
            -Arguments $captureArguments
    } finally {
        if ($restoreCursor) {
            [void][PsoShowcaseCaptureNative]::SetCursorPos(
                $previousCursor.X,
                $previousCursor.Y)
        }
        Stop-OwnedPlayer $process
    }

    Assert-BenchmarkReceipt $Report $Mode
    $workloadStart = Wait-ShowcaseMarker `
        -MarkerFile $markerFile `
        -Marker "WORKLOAD_START realtime" `
        -PlayerProcess $process `
        -TimeoutSeconds 1
    $contentRequest = Wait-ShowcaseMarker `
        -MarkerFile $markerFile `
        -Marker "CONTENT_REQUEST realtime" `
        -PlayerProcess $process `
        -TimeoutSeconds 1
    $contentReveal = Wait-ShowcaseMarker `
        -MarkerFile $markerFile `
        -Marker "CONTENT_REVEAL realtime" `
        -PlayerProcess $process `
        -TimeoutSeconds 1
    $contentComplete = Wait-ShowcaseMarker `
        -MarkerFile $markerFile `
        -Marker "CONTENT_COMPLETE realtime" `
        -PlayerProcess $process `
        -TimeoutSeconds 1
    $runComplete = if ($isDeadlineRun) {
        Wait-ShowcaseMarker `
            -MarkerFile $markerFile `
            -Marker "RUN_COMPLETE realtime" `
            -PlayerProcess $process `
            -TimeoutSeconds 1
    } else {
        $null
    }
    $deferredReady = if ($Mode -eq "baseline") {
        $null
    } else {
        Wait-ShowcaseMarker `
            -MarkerFile $markerFile `
            -Marker "DEFERRED_READY realtime" `
            -PlayerProcess $process `
            -TimeoutSeconds 1
    }
    $captureStartAfterReady = ($captureStartUtc - $captureReady.SeenUtc).TotalSeconds
    $markerWorkloadStartInVideo =
        $workloadStart.RealtimeSeconds -
        $captureReady.RealtimeSeconds -
        $captureStartAfterReady
    $markerContentRequestInVideo =
        $contentRequest.RealtimeSeconds -
        $captureReady.RealtimeSeconds -
        $captureStartAfterReady
    $markerContentRevealInVideo =
        $contentReveal.RealtimeSeconds -
        $captureReady.RealtimeSeconds -
        $captureStartAfterReady
    $markerContentCompleteInVideo =
        $contentComplete.RealtimeSeconds -
        $captureReady.RealtimeSeconds -
        $captureStartAfterReady
    $markerRunCompleteInVideo = if ($null -eq $runComplete) {
        $null
    } else {
        $runComplete.RealtimeSeconds -
            $captureReady.RealtimeSeconds -
            $captureStartAfterReady
    }
    $markerDeferredReadyInVideo = if ($null -eq $deferredReady) {
        $null
    } else {
        $deferredReady.RealtimeSeconds -
            $captureReady.RealtimeSeconds -
            $captureStartAfterReady
    }
    if ($markerWorkloadStartInVideo -lt 0.0 -or
        $markerWorkloadStartInVideo -gt $CaptureSeconds) {
        throw "$Mode workload marker is outside the captured video: " +
            "$markerWorkloadStartInVideo s"
    }
    if ($markerContentRequestInVideo -le $markerWorkloadStartInVideo -or
        $markerContentRevealInVideo -le $markerContentRequestInVideo -or
        $markerContentCompleteInVideo -le $markerContentRevealInVideo -or
        $markerContentCompleteInVideo -gt $CaptureSeconds) {
        throw "$Mode content marker ordering is invalid in the captured video."
    }

    $firstRevealSeconds = Get-FirstRevealSeconds `
        -Capture $Capture `
        -CaptureRegion $captureRect `
        -Mode $Mode `
        -MinimumSeconds ([Math]::Max(0.0, $markerContentRevealInVideo - 0.20)) `
        -MegacityMetro $isMegacityMetro
    $revealEarlyToleranceSeconds = 0.20
    $revealLateToleranceSeconds = 0.20
    if ($isDeadlineRun -and
        -not [string]::IsNullOrWhiteSpace($ScenarioReport) -and
        (Test-Path -LiteralPath $ScenarioReport -PathType Leaf)) {
        $scenarioReceipt = Get-Content -Raw -LiteralPath $ScenarioReport |
            ConvertFrom-Json
        $revealWindowMaximumMilliseconds =
            [double]$scenarioReceipt.revealWindowMaximumMilliseconds
        if ([double]::IsNaN($revealWindowMaximumMilliseconds) -or
            [double]::IsInfinity($revealWindowMaximumMilliseconds) -or
            $revealWindowMaximumMilliseconds -lt 0.0) {
            throw "$Mode scenario receipt contains an invalid reveal-window maximum."
        }
        # CONTENT_REVEAL is emitted in Update. The newly visible frame cannot reach
        # the desktop until its real PSO/driver work finishes, so the cold visual
        # anchor is expected to trail the CPU marker by the very hitch being shown.
        $revealLateToleranceSeconds = [Math]::Max(
            $revealLateToleranceSeconds,
            ($revealWindowMaximumMilliseconds / 1000.0) + 0.20)
    }
    $revealAlignmentDelta = $firstRevealSeconds - $markerContentRevealInVideo
    if ($revealAlignmentDelta -lt -$revealEarlyToleranceSeconds -or
        $revealAlignmentDelta -gt $revealLateToleranceSeconds) {
        throw "$Mode pixel reveal is not aligned with CONTENT_REVEAL " +
            "(marker=$markerContentRevealInVideo, pixel=$firstRevealSeconds, " +
            "allowed=-$revealEarlyToleranceSeconds/+${revealLateToleranceSeconds}s)."
    }

    # SeenUtc is sampled by the marker-file poller, so its absolute video offset can
    # differ between three separately recorded Players by roughly one poll interval.
    # The first tile reveal is observable in the pixels themselves. Anchor that event
    # to the detected frame, then recover every other marker from the Player's monotonic
    # realtime interval. This makes the split-screen comparison frame-accurate without
    # changing or inventing any workload timing.
    $contentRevealInVideo = $firstRevealSeconds
    $contentRequestInVideo =
        $firstRevealSeconds -
        ($contentReveal.RealtimeSeconds - $contentRequest.RealtimeSeconds)
    $contentCompleteInVideo =
        $firstRevealSeconds +
        ($contentComplete.RealtimeSeconds - $contentReveal.RealtimeSeconds)
    $workloadStartInVideo =
        $firstRevealSeconds +
        ($workloadStart.RealtimeSeconds - $contentReveal.RealtimeSeconds)
    $deferredReadyInVideo = if ($null -eq $deferredReady) {
        $null
    } else {
        $firstRevealSeconds +
            ($deferredReady.RealtimeSeconds - $contentReveal.RealtimeSeconds)
    }
    $runCompleteInVideo = if ($null -eq $runComplete) {
        $null
    } else {
        $firstRevealSeconds +
            ($runComplete.RealtimeSeconds - $contentReveal.RealtimeSeconds)
    }
    if ($workloadStartInVideo -lt 0.0 -or
        $contentRequestInVideo -le $workloadStartInVideo -or
        $contentRevealInVideo -le $contentRequestInVideo -or
        $contentCompleteInVideo -le $contentRevealInVideo -or
        $contentCompleteInVideo -gt $CaptureSeconds) {
        throw "$Mode pixel-anchored content marker ordering is invalid."
    }
    if ($null -ne $runComplete) {
        $measuredRunDuration =
            $runComplete.RealtimeSeconds - $workloadStart.RealtimeSeconds
        $measuredRequestDelay =
            $contentRequest.RealtimeSeconds - $workloadStart.RealtimeSeconds
        $measuredRevealDeadline =
            $contentReveal.RealtimeSeconds - $contentRequest.RealtimeSeconds
        $measuredCompletionDelay =
            $contentComplete.RealtimeSeconds - $contentReveal.RealtimeSeconds
        if ([Math]::Abs($measuredRunDuration - 12.0) -gt 0.25 -or
            [Math]::Abs($measuredRequestDelay - 2.0) -gt 0.25 -or
            [Math]::Abs($measuredRevealDeadline - 2.5) -gt 0.25 -or
            [Math]::Abs($measuredCompletionDelay - 2.0) -gt 0.25 -or
            $runCompleteInVideo -le $contentCompleteInVideo -or
            $runCompleteInVideo -gt $CaptureSeconds) {
            throw "$Mode markers do not prove the required 12-second $scenarioDisplayName timeline."
        }
    }
    $firstPresentedUiSeconds = Get-FirstPresentedUiSeconds `
        -Capture $Capture `
        -CaptureRegion $captureRect `
        -Mode $Mode
    Assert-CapturePixels `
        -Capture $Capture `
        -SampleAtSeconds ([Math]::Min(
            $CaptureSeconds - 0.1,
            $firstRevealSeconds + 0.75)) `
        -Mode $Mode
    if ($isDeadlineRun -and -not $isMegacityMetro) {
        Assert-NoObscuringSystemDialog `
            -Capture $Capture `
            -CaptureRegion $captureRect `
            -Mode $Mode
    }

    $captureMetadata = [pscustomobject]@{
        Mode = $Mode
        Video = [System.IO.Path]::GetFullPath($Capture)
        MarkerFile = [System.IO.Path]::GetFullPath($markerFile)
        WindowTitle = $windowTitle
        CaptureRegion = $captureRect
        CaptureReadyRealtimeSeconds = $captureReady.RealtimeSeconds
        WorkloadStartRealtimeSeconds = $workloadStart.RealtimeSeconds
        MarkerWorkloadStartSecondsInVideo = $markerWorkloadStartInVideo
        WorkloadStartSecondsInVideo = $workloadStartInVideo
        ContentRequestRealtimeSeconds = $contentRequest.RealtimeSeconds
        MarkerContentRequestSecondsInVideo = $markerContentRequestInVideo
        ContentRequestSecondsInVideo = $contentRequestInVideo
        ContentRevealRealtimeSeconds = $contentReveal.RealtimeSeconds
        MarkerContentRevealSecondsInVideo = $markerContentRevealInVideo
        ContentRevealSecondsInVideo = $contentRevealInVideo
        ContentCompleteRealtimeSeconds = $contentComplete.RealtimeSeconds
        MarkerContentCompleteSecondsInVideo = $markerContentCompleteInVideo
        ContentCompleteSecondsInVideo = $contentCompleteInVideo
        RunCompleteRealtimeSeconds = if ($null -eq $runComplete) {
            $null
        } else {
            $runComplete.RealtimeSeconds
        }
        MarkerRunCompleteSecondsInVideo = $markerRunCompleteInVideo
        RunCompleteSecondsInVideo = $runCompleteInVideo
        DeferredReadyRealtimeSeconds = if ($null -eq $deferredReady) {
            $null
        } else {
            $deferredReady.RealtimeSeconds
        }
        MarkerDeferredReadySecondsInVideo = $markerDeferredReadyInVideo
        DeferredReadySecondsInVideo = $deferredReadyInVideo
        FirstRevealSecondsInVideo = $firstRevealSeconds
        FirstPresentedUiSecondsInVideo = $firstPresentedUiSeconds
        AlignmentSource = "CONTENT_REVEAL pixels + Player realtime marker intervals"
        PlayerPriorityClass = $PlayerPriorityClass
        FfmpegPriorityClass = $FfmpegPriorityClass
        CaptureBackend = $liveCaptureBackend
        CpuIsolationEnabled = $useCaptureCpuIsolation
        LogicalProcessorCount = $captureLogicalProcessorCount
        PlayerProcessorAffinityMask = if ($useCaptureCpuIsolation) {
            "0x{0:X}" -f $playerAffinityMask
        } else {
            ""
        }
        RecorderProcessorAffinityMask = if ($useCaptureCpuIsolation) {
            "0x{0:X}" -f $recorderAffinityMask
        } else {
            ""
        }
        RecorderThreads = $liveCaptureEncoderThreads
        FirewallPromptDismissedBeforeCapture =
            $firewallPromptGuard.DismissedCount -gt 0
        FirewallPromptDismissedCount = $firewallPromptGuard.DismissedCount
        FirewallNotificationUiResetCount = $firewallNotificationUiResetCount
        FirewallConfigurationChanged =
            $firewallPromptGuard.FirewallConfigurationChanged
        ScenarioReport = if ([string]::IsNullOrWhiteSpace($ScenarioReport)) {
            ""
        } else {
            [System.IO.Path]::GetFullPath($ScenarioReport)
        }
    }
    $captureMetadata | ConvertTo-Json -Depth 4 | Set-Content `
        -LiteralPath ($Capture + ".capture.json") `
        -Encoding utf8NoBOM
    return $captureMetadata
}

function New-ActualVisualEvidence(
    [pscustomobject]$BaselineCapture,
    [pscustomobject]$NaiveCapture,
    [pscustomobject]$ScheduledCapture,
    [string]$OutputDirectory,
    [string]$Scorecard
) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath $Scorecard -PathType Leaf)) {
        throw "Actual visual scorecard is missing: $Scorecard"
    }

    $effectiveRequestLeadSeconds = [Math]::Min(
        $RequestVisualLeadSeconds,
        [Math]::Min(
            $NaiveCapture.ContentRequestSecondsInVideo,
            $ScheduledCapture.ContentRequestSecondsInVideo))
    $effectiveRevealLeadSeconds = [Math]::Min(
        $VisualLeadSeconds,
        [Math]::Min(
            $BaselineCapture.ContentRevealSecondsInVideo,
            $ScheduledCapture.ContentRevealSecondsInVideo))
    $seekGuardSeconds = 1.0 / 60.0
    $requestPresentationOffsetSeconds = 1.0 / 60.0
    $baselineRevealTrim = [Math]::Max(
        0.0,
        $BaselineCapture.ContentRevealSecondsInVideo -
        $effectiveRevealLeadSeconds -
        $seekGuardSeconds)
    $scheduledRevealTrim = [Math]::Max(
        0.0,
        $ScheduledCapture.ContentRevealSecondsInVideo -
        $effectiveRevealLeadSeconds -
        $seekGuardSeconds)
    $naiveRequestTrim = [Math]::Max(
        0.0,
        $NaiveCapture.ContentRequestSecondsInVideo -
        $effectiveRequestLeadSeconds +
        $requestPresentationOffsetSeconds)
    $scheduledRequestTrim = [Math]::Max(
        0.0,
        $ScheduledCapture.ContentRequestSecondsInVideo -
        $effectiveRequestLeadSeconds +
        $requestPresentationOffsetSeconds)
    $actualMp4 = Join-Path $OutputDirectory "actual-comparison.mp4"
    $actualGif = Join-Path $OutputDirectory "actual-comparison.gif"
    $actualPng = Join-Path $OutputDirectory "actual-comparison.png"
    $firstUseMp4 = Join-Path $OutputDirectory "actual-first-use-comparison.mp4"
    $warmupMp4 = Join-Path $OutputDirectory "actual-warmup-comparison.mp4"
    $warmupGif = Join-Path $OutputDirectory "actual-warmup-comparison.gif"
    $alignment = Join-Path $OutputDirectory "visual-alignment.json"
    $fontFile = "C\:/Windows/Fonts/segoeuib.ttf"
    $scorecardDurationSeconds = 1.8

    Invoke-Ffmpeg `
        -Label "actual-first-use-chapter" `
        -Log ($firstUseMp4 + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", (ConvertTo-InvariantNumber $baselineRevealTrim),
            "-i", $BaselineCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $scheduledRevealTrim),
            "-i", $ScheduledCapture.Video,
            "-filter_complex",
            "[0:v]fps=60,scale=960:540:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[cold];[1:v]fps=60,scale=960:540:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[ours];[cold][ours]hstack=inputs=2,pad=1920:600:0:60:color=0x050b14,drawtext=fontfile='$fontFile':text='1  FIRST-USE REVEAL  -  COLD vs OURS':fontcolor=white:fontsize=30:x=(w-text_w)/2:y=14[comparison]",
            "-map", "[comparison]",
            "-t", (ConvertTo-InvariantNumber $VisualDurationSeconds),
            "-an",
            "-c:v", "libx264",
            "-preset", "slow",
            "-crf", "17",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            $firstUseMp4
        )

    Invoke-Ffmpeg `
        -Label "actual-deferred-warmup-chapter" `
        -Log ($warmupMp4 + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", (ConvertTo-InvariantNumber $naiveRequestTrim),
            "-i", $NaiveCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $scheduledRequestTrim),
            "-i", $ScheduledCapture.Video,
            "-filter_complex",
            "[0:v]fps=60,scale=960:540:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[naive];[1:v]fps=60,scale=960:540:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[ours];[naive][ours]hstack=inputs=2,pad=1920:600:0:60:color=0x050b14,drawtext=fontfile='$fontFile':text='2  MID-GAME CONTENT REQUEST  -  ALL-AT-ONCE vs OURS':fontcolor=white:fontsize=30:x=(w-text_w)/2:y=14[comparison]",
            "-map", "[comparison]",
            "-t", (ConvertTo-InvariantNumber $WarmupVisualDurationSeconds),
            "-an",
            "-c:v", "libx264",
            "-preset", "slow",
            "-crf", "17",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            $warmupMp4
        )

    Invoke-Ffmpeg `
        -Label "actual-two-chapter-comparison" `
        -Log ($actualMp4 + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-i", $firstUseMp4,
            "-i", $warmupMp4,
            "-loop", "1",
            "-framerate", "60",
            "-t", (ConvertTo-InvariantNumber $scorecardDurationSeconds),
            "-i", $Scorecard,
            "-filter_complex",
            "[0:v]fps=60,format=yuv420p,setpts=PTS-STARTPTS[first];[1:v]fps=60,format=yuv420p,setpts=PTS-STARTPTS[second];[2:v]fps=60,scale=1920:600:flags=lanczos,format=yuv420p,setpts=PTS-STARTPTS[score];[first][second][score]concat=n=3:v=1:a=0[comparison]",
            "-map", "[comparison]",
            "-an",
            "-c:v", "libx264",
            "-preset", "slow",
            "-crf", "17",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            $actualMp4
        )

    Invoke-Ffmpeg `
        -Label "actual-poster" `
        -Log ($actualPng + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", "1.0",
            "-i", $actualMp4,
            "-frames:v", "1",
            "-update", "1",
            $actualPng
        )

    if (-not $NoGif) {
        Invoke-Ffmpeg `
            -Label "actual-gif" `
            -Log ($actualGif + ".ffmpeg.log") `
            -Arguments @(
                "-y",
                "-i", $actualMp4,
                "-filter_complex",
                "[0:v]fps=20,scale=1440:-2:flags=lanczos,split[frames][palette_source];[palette_source]palettegen=max_colors=128:stats_mode=diff[palette];[frames][palette]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle[gif]",
                "-map", "[gif]",
                "-loop", "0",
                $actualGif
            )
        Invoke-Ffmpeg `
            -Label "actual-warmup-gif" `
            -Log ($warmupGif + ".ffmpeg.log") `
            -Arguments @(
                "-y",
                "-i", $warmupMp4,
                "-filter_complex",
                "[0:v]fps=20,scale=1440:-2:flags=lanczos,split[frames][palette_source];[palette_source]palettegen=max_colors=128:stats_mode=diff[palette];[frames][palette]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle[gif]",
                "-map", "[gif]",
                "-loop", "0",
                $warmupGif
            )
    }

    [ordered]@{
        schemaVersion = 5
        generatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        pathBase = "alignmentFileDirectory"
        synchronizationMarkers = @(
            "CONTENT_REQUEST",
            "CONTENT_REVEAL",
            "CONTENT_COMPLETE"
        )
        requestedLeadSeconds = $VisualLeadSeconds
        requestedRevealLeadSeconds = $VisualLeadSeconds
        requestedRequestLeadSeconds = $RequestVisualLeadSeconds
        effectiveRequestLeadSeconds = $effectiveRequestLeadSeconds
        effectiveRevealLeadSeconds = $effectiveRevealLeadSeconds
        seekGuardSeconds = $seekGuardSeconds
        requestPresentationOffsetSeconds = $requestPresentationOffsetSeconds
        firstUseChapterDurationSeconds = $VisualDurationSeconds
        deferredChapterDurationSeconds = $WarmupVisualDurationSeconds
        scorecardDurationSeconds = $scorecardDurationSeconds
        playerPriorityClass = $PlayerPriorityClass
        ffmpegPriorityClass = $FfmpegPriorityClass
        baseline = ConvertTo-EvidenceCapture $BaselineCapture $OutputDirectory
        naive = ConvertTo-EvidenceCapture $NaiveCapture $OutputDirectory
        scheduled = ConvertTo-EvidenceCapture $ScheduledCapture $OutputDirectory
        baselineRevealTrimSeconds = $baselineRevealTrim
        scheduledRevealTrimSeconds = $scheduledRevealTrim
        naiveRequestTrimSeconds = $naiveRequestTrim
        scheduledRequestTrimSeconds = $scheduledRequestTrim
        outputVideo = ConvertTo-EvidencePath $actualMp4 $OutputDirectory
        outputGif = if ($NoGif) {
            ""
        } else {
            ConvertTo-EvidencePath $actualGif $OutputDirectory
        }
        outputPoster = ConvertTo-EvidencePath $actualPng $OutputDirectory
        firstUseVideo = ConvertTo-EvidencePath $firstUseMp4 $OutputDirectory
        deferredWarmupVideo = ConvertTo-EvidencePath $warmupMp4 $OutputDirectory
        scorecard = ConvertTo-EvidencePath $Scorecard $OutputDirectory
        warmupGif = if ($NoGif) {
            ""
        } else {
            ConvertTo-EvidencePath $warmupGif $OutputDirectory
        }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $alignment -Encoding utf8NoBOM

    Write-Host "Actual player comparison: $actualMp4"
    if (-not $NoGif) {
        Write-Host "Actual player GIF: $actualGif"
    }
    Write-Host "Deferred warmup comparison: $warmupMp4"
}

function New-DeadlineRunVisualEvidence(
    [pscustomobject]$BaselineCapture,
    [pscustomobject]$NaiveCapture,
    [pscustomobject]$ScheduledCapture,
    [string]$OutputDirectory,
    [string]$Acceptance
) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath $Acceptance -PathType Leaf)) {
        throw "$scenarioDisplayName acceptance receipt is missing: $Acceptance"
    }
    $acceptanceReceipt = Get-Content -Raw -LiteralPath $Acceptance | ConvertFrom-Json
    if (-not $acceptanceReceipt.passed) {
        throw "$scenarioDisplayName media cannot be composed from a failed acceptance receipt."
    }
    $baselineRevealMilliseconds = [double](
        $acceptanceReceipt.metrics.scenarioMaximumMilliseconds.baselineRevealWindow)
    $scheduledWorstMilliseconds = [double](
        $acceptanceReceipt.metrics.maximumMilliseconds.scheduled)
    $scheduledMissedFrames = [int](
        $acceptanceReceipt.metrics.missedFramesAtScheduledThreshold.scheduled)
    $scheduledThresholdMilliseconds = [double](
        $acceptanceReceipt.requirements.scheduledMissThresholdMilliseconds)
    $proofText =
        "COLD REVEAL " +
        $baselineRevealMilliseconds.ToString(
            "F1",
            [Globalization.CultureInfo]::InvariantCulture) +
        " ms  /  OURS WORST " +
        $scheduledWorstMilliseconds.ToString(
            "F2",
            [Globalization.CultureInfo]::InvariantCulture) +
        " ms  /  OURS MISSES >= " +
        $scheduledThresholdMilliseconds.ToString(
            "F2",
            [Globalization.CultureInfo]::InvariantCulture) +
        " ms = " + $scheduledMissedFrames

    $heroDurationSeconds = 12.0
    $seekGuardSeconds = 1.0 / 60.0
    $baselineTrim = [Math]::Max(
        0.0,
        $BaselineCapture.WorkloadStartSecondsInVideo - $seekGuardSeconds)
    $naiveTrim = [Math]::Max(
        0.0,
        $NaiveCapture.WorkloadStartSecondsInVideo - $seekGuardSeconds)
    $scheduledTrim = [Math]::Max(
        0.0,
        $ScheduledCapture.WorkloadStartSecondsInVideo - $seekGuardSeconds)
    foreach ($entry in @(
        [pscustomobject]@{ Mode = "baseline"; Trim = $baselineTrim },
        [pscustomobject]@{ Mode = "naive"; Trim = $naiveTrim },
        [pscustomobject]@{ Mode = "scheduled"; Trim = $scheduledTrim }
    )) {
        if ($entry.Trim + $heroDurationSeconds -gt $CaptureSeconds + 0.000001) {
            throw "$($entry.Mode) capture does not contain the complete 12-second run."
        }
    }

    $heroMp4 = Join-Path $OutputDirectory "actual-comparison.mp4"
    $heroGif = Join-Path $OutputDirectory "actual-comparison.gif"
    $poster = Join-Path $OutputDirectory "actual-comparison.png"
    $technicalMp4 = Join-Path $OutputDirectory "$scenarioSlug-three-way.mp4"
    $alignment = Join-Path $OutputDirectory "visual-alignment.json"
    $fontFile = "C\:/Windows/Fonts/segoeuib.ttf"

    Invoke-Ffmpeg `
        -Label "$scenarioSlug-hero" `
        -Log ($heroMp4 + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", (ConvertTo-InvariantNumber $baselineTrim),
            "-i", $BaselineCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $scheduledTrim),
            "-i", $ScheduledCapture.Video,
            "-filter_complex",
            "[0:v]fps=60,scale=960:540:flags=lanczos,setsar=1,setpts=PTS-STARTPTS,drawbox=x=16:y=14:w=300:h=42:color=0x300706@0.88:t=fill,drawtext=fontfile='$fontFile':text='UNITY DEFAULT / COLD':fontcolor=white:fontsize=22:x=30:y=22[cold];[1:v]fps=60,scale=960:540:flags=lanczos,setsar=1,setpts=PTS-STARTPTS,drawbox=x=16:y=14:w=350:h=42:color=0x063323@0.88:t=fill,drawtext=fontfile='$fontFile':text='OURS / DEADLINE SCHEDULED':fontcolor=white:fontsize=22:x=30:y=22[ours];[cold][ours]hstack=inputs=2,pad=1920:660:0:60:color=0x050b14,drawtext=fontfile='$fontFile':text='$($scenarioDisplayName.ToUpperInvariant())  -  SAME CAMERA / SAME CONTENT / NO INJECTED STALL':fontcolor=white:fontsize=28:x=(w-text_w)/2:y=14,drawbox=x=0:y=600:w=1920:h=60:color=0x07131f@1.0:t=fill,drawtext=fontfile='$fontFile':text='$proofText':fontcolor=0x8fffd5:fontsize=25:x=(w-text_w)/2:y=616[comparison]",
            "-map", "[comparison]",
            "-t", (ConvertTo-InvariantNumber $heroDurationSeconds),
            "-an",
            "-c:v", "libx264",
            "-preset", "slow",
            "-crf", "17",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            $heroMp4
        )

    Invoke-Ffmpeg `
        -Label "$scenarioSlug-three-way" `
        -Log ($technicalMp4 + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", (ConvertTo-InvariantNumber $baselineTrim),
            "-i", $BaselineCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $naiveTrim),
            "-i", $NaiveCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $scheduledTrim),
            "-i", $ScheduledCapture.Video,
            "-filter_complex",
            "[0:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[cold];[1:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[naive];[2:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[ours];[cold][naive][ours]hstack=inputs=3,pad=1920:480:0:60:color=0x050b14,drawtext=fontfile='$fontFile':text='COLD  /  ALL-AT-ONCE  /  DEADLINE + COST + HOT-SET':fontcolor=white:fontsize=28:x=(w-text_w)/2:y=14,drawbox=x=0:y=420:w=1920:h=60:color=0x07131f@1.0:t=fill,drawtext=fontfile='$fontFile':text='$proofText':fontcolor=0x8fffd5:fontsize=25:x=(w-text_w)/2:y=436[comparison]",
            "-map", "[comparison]",
            "-t", (ConvertTo-InvariantNumber $heroDurationSeconds),
            "-an",
            "-c:v", "libx264",
            "-preset", "slow",
            "-crf", "17",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            $technicalMp4
        )

    Invoke-Ffmpeg `
        -Label "$scenarioSlug-poster" `
        -Log ($poster + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", "5.2",
            "-i", $heroMp4,
            "-frames:v", "1",
            "-update", "1",
            $poster
        )

    if (-not $NoGif) {
        Invoke-Ffmpeg `
            -Label "$scenarioSlug-gif" `
            -Log ($heroGif + ".ffmpeg.log") `
            -Arguments @(
                "-y",
                "-i", $heroMp4,
                "-filter_complex",
                "[0:v]fps=20,scale=1440:-2:flags=lanczos,split[frames][palette_source];[palette_source]palettegen=max_colors=128:stats_mode=diff[palette];[frames][palette]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle[gif]",
                "-map", "[gif]",
                "-loop", "0",
                $heroGif
            )
    }

    [ordered]@{
        schemaVersion = 6
        preset = $scenarioSlug
        generatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        pathBase = "alignmentFileDirectory"
        synchronizationSource =
            "CONTENT_REVEAL pixels + Player realtime intervals"
        synchronizationMarkers = @(
            "WORKLOAD_START",
            "CONTENT_REQUEST",
            "CONTENT_REVEAL",
            "CONTENT_COMPLETE",
            "RUN_COMPLETE"
        )
        heroAnchor = "WORKLOAD_START"
        heroDurationSeconds = $heroDurationSeconds
        rawCaptureSeconds = $CaptureSeconds
        seekGuardSeconds = $seekGuardSeconds
        playerPriorityClass = $PlayerPriorityClass
        ffmpegPriorityClass = $FfmpegPriorityClass
        acceptance = ConvertTo-EvidencePath $Acceptance $OutputDirectory
        proofOverlay = [ordered]@{
            source = "passed acceptance receipt"
            text = $proofText
            baselineRevealMilliseconds = $baselineRevealMilliseconds
            scheduledWorstMilliseconds = $scheduledWorstMilliseconds
            scheduledMissedFrames = $scheduledMissedFrames
            scheduledThresholdMilliseconds = $scheduledThresholdMilliseconds
        }
        baseline = ConvertTo-EvidenceCapture $BaselineCapture $OutputDirectory
        naive = ConvertTo-EvidenceCapture $NaiveCapture $OutputDirectory
        scheduled = ConvertTo-EvidenceCapture $ScheduledCapture $OutputDirectory
        baselineTrimSeconds = $baselineTrim
        naiveTrimSeconds = $naiveTrim
        scheduledTrimSeconds = $scheduledTrim
        outputVideo = ConvertTo-EvidencePath $heroMp4 $OutputDirectory
        outputGif = if ($NoGif) {
            ""
        } else {
            ConvertTo-EvidencePath $heroGif $OutputDirectory
        }
        outputPoster = ConvertTo-EvidencePath $poster $OutputDirectory
        technicalVideo = ConvertTo-EvidencePath $technicalMp4 $OutputDirectory
    } | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath $alignment `
        -Encoding utf8NoBOM

    Write-Host "$scenarioDisplayName hero: $heroMp4"
    Write-Host "$scenarioDisplayName three-way evidence: $technicalMp4"
}

if ($isScheduledReplay) {
    $firewallNotificationUiResetCount = 0
    $scheduledScenarioReport =
        Join-Path $runRoot "ScenarioReceipts\scheduled.$scenarioSlug.json"
    $scheduledCapturePath = Join-Path $captures "scheduled.mp4"
    $scheduledWarmupReceipt =
        Join-Path $runRoot "Receipts\scheduled.warmup.json"
    $scheduledCapture = Invoke-ShowcasePlayer `
        -Mode "scheduled" `
        -Report $scheduled `
        -Log (Join-Path $runRoot "scheduled-player.log") `
        -Capture $scheduledCapturePath `
        -ScenarioReport $scheduledScenarioReport `
        -Strategy "scheduled" `
        -AsyncJobCount $ReplayWorkerCount `
        -InitialBatchSize $ReplayBatchSize `
        -MaximumBatchSize $ReplayBatchSize `
        -BootstrapBatchSize $ReplayBootstrapBatchSize `
        -BudgetSafetyMarginMilliseconds `
            $ReplayBudgetSafetyMarginMilliseconds `
        -BudgetCostSafetyMultiplier $ReplayBudgetCostSafetyMultiplier `
        -BudgetCooldownFrames $ReplayBudgetCooldownFrames `
        -DeadlineBackendMode $ReplayDeadlineBackendMode `
        -WarmupReceipt $scheduledWarmupReceipt
    $scenarioReceipt = Get-Content `
        -Raw `
        -LiteralPath $scheduledScenarioReport | ConvertFrom-Json
    $warmupReceipt = Get-Content `
        -Raw `
        -LiteralPath $scheduledWarmupReceipt | ConvertFrom-Json
    $benchmarkReceipt = Get-Content `
        -Raw `
        -LiteralPath $scheduled | ConvertFrom-Json
    $phaseReceipt = @($warmupReceipt.phases | Where-Object {
        $_.phase -eq $scenarioPhase
    } | Select-Object -First 1)
    $replayFailures = [System.Collections.Generic.List[string]]::new()
    if (-not [bool]$scenarioReceipt.completed) {
        $replayFailures.Add("Scenario receipt is incomplete.")
    }
    if ([int]$scenarioReceipt.schemaVersion -lt 6) {
        $replayFailures.Add("Scenario receipt predates circuit/GC evidence.")
    }
    if ([int]$scenarioReceipt.severeFrameCount -ne 0 -or
        [double]$scenarioReceipt.maximumMilliseconds -ge 16.67) {
        $replayFailures.Add("The 12-second scenario contains a 60 FPS miss.")
    }
    if (-not [bool]$scenarioReceipt.deferredReady -or
        [bool]$scenarioReceipt.deadlineMissed) {
        $replayFailures.Add("The deferred hot set was not ready before reveal.")
    }
    if (-not [bool]$scenarioReceipt.cameraCircuitCompletedBeforeMeasurement -or
        -not [bool]$scenarioReceipt.captureConditioned -or
        [Math]::Abs(
            [double]$scenarioReceipt.cameraCircuitPhaseAtArmSeconds - 4.0
        ) -gt 0.1) {
        $replayFailures.Add("Camera/capture preconditioning is invalid.")
    }
    if ([int]$scenarioReceipt.managedGcCollectionsDuringRun -ne 0) {
        $replayFailures.Add("Managed GC occurred inside the measured window.")
    }
    $rawScenarioSamples = @($scenarioReceipt.frameSamples)
    $scenarioSamples = @($rawScenarioSamples | Where-Object {
        [double]$_.elapsedSeconds -ge 0.0 -and
        [double]$_.elapsedSeconds -le 12.25
    })
    $nativeTimingSamples = @($scenarioSamples | Where-Object {
        [bool]$_.nativeTimingAvailable
    })
    if ($scenarioSamples.Count -eq 0 -or
        $scenarioSamples.Count -ne $rawScenarioSamples.Count -or
        [double]$scenarioSamples[0].elapsedSeconds -gt 0.25 -or
        [double]$scenarioSamples[-1].elapsedSeconds -lt 12.0) {
        $replayFailures.Add("Scenario samples do not cover exactly one 12-second gate.")
    }
    if ($scenarioSamples.Count -eq 0 -or
        $nativeTimingSamples.Count -lt
            [Math]::Ceiling($scenarioSamples.Count * 0.95)) {
        $replayFailures.Add("Native FrameTiming coverage is below 95%.")
    }
    if (-not [bool]$benchmarkReceipt.completed -or
        -not [bool]$benchmarkReceipt.scenarioMeasurementGated -or
        [Math]::Abs(
            [double]$benchmarkReceipt.measurementDurationSeconds - 12.0
        ) -gt 0.25) {
        $replayFailures.Add("Benchmark receipt is not gated to the 12-second scenario.")
    }
    if (-not [bool]$warmupReceipt.completed -or $phaseReceipt.Count -ne 1) {
        $replayFailures.Add("Warmup receipt or deferred phase is incomplete.")
    } else {
        $phaseEvidence = $phaseReceipt[0]
        if (-not [bool]$phaseEvidence.completed -or
            [bool]$phaseEvidence.deadlineMissed -or
            -not [bool]$phaseEvidence.hardFrameBudgetMet -or
            -not [bool]$phaseEvidence.hardFrameBudgetFeasible -or
            -not [bool]$phaseEvidence.schedulerAdmissionBudgetMet -or
            [int]$phaseEvidence.budgetViolationCount -ne 0 -or
            -not [bool]$phaseEvidence.backendReportedWarmedUp) {
            $replayFailures.Add("Deferred phase failed hard-budget evidence checks.")
        }
    }
    $cacheMissEvidence = $warmupReceipt.cacheMissTrace
    if ($null -eq $cacheMissEvidence -or
        -not [bool]$cacheMissEvidence.requested -or
        -not [bool]$cacheMissEvidence.armed -or
        -not [string]::IsNullOrWhiteSpace([string]$cacheMissEvidence.error) -or
        [int]$cacheMissEvidence.baselineGraphicsStates -le 0 -or
        [int]$cacheMissEvidence.observedGraphicsStates -ne
            [int]$cacheMissEvidence.baselineGraphicsStates -or
        [int]$cacheMissEvidence.cacheMissGraphicsStates -ne 0) {
        $replayFailures.Add("Plan-scoped feedback trace is invalid or contains misses.")
    }
    $replayReceipt = [ordered]@{
        schemaVersion = 2
        generatedUtc = [DateTime]::UtcNow.ToString("o")
        player = $player
        runRoot = $runRoot
        capture = $scheduledCapture
        scenarioMaximumMilliseconds =
            [double]$scenarioReceipt.maximumMilliseconds
        scheduledMissThresholdMilliseconds = 16.67
        scheduledMissedFrames = [int]$scenarioReceipt.severeFrameCount
        deadlineBackendMode = $ReplayDeadlineBackendMode
        nativeFrameTimingCoverage = if ($scenarioSamples.Count -gt 0) {
            $nativeTimingSamples.Count / [double]$scenarioSamples.Count
        } else {
            0.0
        }
        managedGcCollectionsDuringRun =
            [int]$scenarioReceipt.managedGcCollectionsDuringRun
        benchmarkMeasurementDurationSeconds =
            [double]$benchmarkReceipt.measurementDurationSeconds
        failures = @($replayFailures)
        passed = $replayFailures.Count -eq 0
    }
    $replayReceiptPath = Join-Path $runRoot "scheduled-replay.json"
    $replayReceipt | ConvertTo-Json -Depth 8 | Set-Content `
        -LiteralPath $replayReceiptPath `
        -Encoding utf8NoBOM
    Write-Host "Scheduled replay receipt: $replayReceiptPath"
    if ($replayFailures.Count -gt 0) {
        throw "Scheduled replay failed its hard acceptance gate. See $replayReceiptPath"
    }
    return
}

if ($composeExisting) {
    $baselineCaptureReceipt = Join-Path $captures "baseline.mp4.capture.json"
    $naiveCaptureReceipt = Join-Path $captures "naive.mp4.capture.json"
    $scheduledCaptureReceipt = Join-Path $captures "scheduled.mp4.capture.json"
    $deadlineAcceptance = Join-Path $evidence "$scenarioSlug.acceptance.json"
    foreach ($requiredFile in @(
        $baselineCaptureReceipt,
        $naiveCaptureReceipt,
        $scheduledCaptureReceipt,
        $deadlineAcceptance
    )) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Existing Deadline Run is incomplete: $requiredFile"
        }
    }
    $baselineCapture =
        Get-Content -Raw -LiteralPath $baselineCaptureReceipt | ConvertFrom-Json
    $naiveCapture =
        Get-Content -Raw -LiteralPath $naiveCaptureReceipt | ConvertFrom-Json
    $scheduledCapture =
        Get-Content -Raw -LiteralPath $scheduledCaptureReceipt | ConvertFrom-Json
    New-DeadlineRunVisualEvidence `
        -BaselineCapture $baselineCapture `
        -NaiveCapture $naiveCapture `
        -ScheduledCapture $scheduledCapture `
        -OutputDirectory $evidence `
        -Acceptance $deadlineAcceptance
    return
}

$firewallNotificationUiResetCount = 0
if ($isDeadlineRun) {
    $firewallNotificationUiResetCount +=
        Reset-FirewallNotificationDialogServer -Label "preflight"
}

$baselineCapturePath = if ($NoVisualCapture) {
    ""
} else {
    Join-Path $captures "baseline.mp4"
}
$baselineScenarioReport = if ($isDeadlineRun) {
    Join-Path $runRoot "ScenarioReceipts\baseline.$scenarioSlug.json"
} else {
    ""
}
if ($isDeadlineRun) {
    # Record the genuinely cold visual baseline before any tracing can populate
    # driver caches. This Player is a release build and warmup is explicitly off.
    Invoke-UnityEditor @(
        "-executeMethod",
        $builderMethod,
        $cacheBusterArgument, $stamp,
        "-pso-baseline-build",
        "-quit",
        "-logFile", "Logs/$runLabel-baseline-release-$stamp.log"
    ) "baseline-release-build"
    $baselineCapture = Invoke-ShowcasePlayer `
        -Mode "baseline" `
        -Report $baseline `
        -Log (Join-Path $runRoot "baseline-player.log") `
        -Capture $baselineCapturePath `
        -ScenarioReport $baselineScenarioReport `
        -DisableWarmup

    # Rebuild the identical cache-busted workload as Development solely for
    # GraphicsStateCollection tracing. Its timing is retained as a diagnostic,
    # never substituted for the clean release baseline above.
    Invoke-UnityEditor @(
        "-executeMethod",
        $builderMethod,
        $cacheBusterArgument, $stamp,
        "-pso-training-build",
        "-quit",
        "-logFile", "Logs/$runLabel-training-$stamp.log"
    ) "training-build"
    $trainingTraceReport =
        Join-Path $runRoot "Training\baseline-trace.benchmark.json"
    # The scenario gate, rather than this compatibility frame count, owns the
    # full request -> reveal -> completion timeline. Keep a generous value for
    # older Players while the current Player always closes on the 12 s gate.
    $trainingSampleFrames = if ($isMegacityMetro) {
        [Math]::Max($Frames, 4000)
    } else {
        $Frames
    }
    Invoke-ShowcasePlayer `
        -Mode "baseline" `
        -Report $trainingTraceReport `
        -Log (Join-Path $runRoot "training-trace-player.log") `
        -Capture "" `
        -SampleFrames $trainingSampleFrames `
        -Trace `
        -DisableWarmup
    $firewallNotificationUiResetCount +=
        Reset-FirewallNotificationDialogServer -Label "training-trace-cleanup"
} else {
    Invoke-UnityEditor @(
        "-executeMethod",
        $builderMethod,
        $cacheBusterArgument, $stamp,
        "-pso-training-build",
        "-quit",
        "-logFile", "Logs/$runLabel-training-$stamp.log"
    ) "training-build"
    $baselineCapture = Invoke-ShowcasePlayer `
        -Mode "baseline" `
        -Report $baseline `
        -Log (Join-Path $runRoot "baseline-player.log") `
        -Capture $baselineCapturePath `
        -ScenarioReport $baselineScenarioReport `
        -Trace `
        -DisableWarmup
}

Invoke-UnityEditor @(
    "-executeMethod", "Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.ProcessInbox",
    "-pso-inbox", (Join-Path $runRoot "Inbox"),
    "-pso-profile-output", $profileRoot,
    "-pso-profile", $profileId,
    "-quit",
    "-logFile", "Logs/$runLabel-merge-$stamp.log"
) "merge"

Invoke-UnityEditor @(
    "-executeMethod", "Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.InstallPlan",
    "-pso-profile-output", $profileRoot,
    "-pso-profile", $profileId,
    "-quit",
    "-logFile", "Logs/$runLabel-install-$stamp.log"
) "install"

$finalBuildArguments = @(
    "-executeMethod",
    $builderMethod,
    $cacheBusterArgument, $stamp,
    "-quit",
    "-logFile", "Logs/$runLabel-final-$stamp.log"
)
if (-not $isDeadlineRun) {
    $finalBuildArguments += "-pso-reuse-generated-scene"
}
Invoke-UnityEditor $finalBuildArguments "final-build"

$selectedWorkerCount = 0
$selectedBatchSize = 0
$selectedBootstrapBatchSize = 0
$selectedBudgetSafetyMarginMilliseconds = 0.0
$selectedBudgetCostSafetyMultiplier = 0.0
$selectedBudgetCooldownFrames = -1
$selectedDeadlineBackendMode = "auto"
if (-not $SkipAutoTune) {
    $policySearchOutput = Join-Path $runRoot "PolicySearch"
    $policySearchSampleFrames = if ($isMegacityMetro) { 2400 } else { 1080 }
    & (Join-Path $PSScriptRoot "Find-PsoWarmupPolicy.ps1") -AllowPerformanceExecution:$AllowPerformanceExecution `
        -Player $player `
        -Plan $plan `
        -Output $policySearchOutput `
        -WorkerCounts $AutoTuneWorkerCounts `
        -BatchSizes $AutoTuneBatchSizes `
        -Repetitions $AutoTuneRepetitions `
        -SampleFrames $policySearchSampleFrames `
        -TargetFrameMilliseconds 16.67 `
        -Scenario $(if ($isDeadlineRun) { $scenarioSlug } else { "" }) `
        -ScenarioPhase $(if ($isDeadlineRun) { $scenarioPhase } else { "" }) `
        -BenchmarkDelaySeconds $BenchmarkDelaySeconds `
        -ScreenWidth 1280 `
        -ScreenHeight 720 `
        -DeadlineBackendMode "auto"
    $recommendationFile = Get-ChildItem `
        -LiteralPath $policySearchOutput `
        -Filter "recommendation.json" `
        -File `
        -Recurse | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $recommendationFile) {
        throw "Policy search did not emit recommendation.json."
    }
    $recommendation = Get-Content `
        -Raw `
        -LiteralPath $recommendationFile.FullName | ConvertFrom-Json
    if (-not [bool]$recommendation.hardBudgetQualified) {
        throw "Policy search produced only a provisional, non-budget-safe result."
    }
    if ($recommendation.PSObject.Properties.Name -contains "workerHeadroomMet" -and
        -not [bool]$recommendation.workerHeadroomMet) {
        throw "Policy search recommendation consumes the reserved foreground cores."
    }
    $selectedWorkerCount = [int]$recommendation.workerCount
    $selectedBatchSize = [int]$recommendation.maximumBatchSize
    $selectedBootstrapBatchSize = [int]$recommendation.bootstrapBatchSize
    $selectedBudgetSafetyMarginMilliseconds =
        [double]$recommendation.budgetSafetyMarginMilliseconds
    $selectedBudgetCostSafetyMultiplier =
        [double]$recommendation.budgetCostSafetyMultiplier
    $selectedBudgetCooldownFrames = [int]$recommendation.budgetCooldownFrames
    $selectedDeadlineBackendMode = [string]$recommendation.deadlineBackendMode
}

$naiveCapturePath = if ($NoVisualCapture) {
    ""
} else {
    Join-Path $captures "naive.mp4"
}
$naiveScenarioReport = if ($isDeadlineRun) {
    Join-Path $runRoot "ScenarioReceipts\naive.$scenarioSlug.json"
} else {
    ""
}
$naiveCapture = Invoke-ShowcasePlayer `
    -Mode "naive" `
    -Report $naive `
    -Log (Join-Path $runRoot "naive-player.log") `
    -Capture $naiveCapturePath `
    -ScenarioReport $naiveScenarioReport `
    -Strategy "throughput" `
    -AsyncJobCount $selectedWorkerCount `
    -WarmupReceipt (Join-Path $runRoot "Receipts\naive.warmup.json")

$scheduledCapturePath = if ($NoVisualCapture) {
    ""
} else {
    Join-Path $captures "scheduled.mp4"
}
$scheduledScenarioReport = if ($isDeadlineRun) {
    Join-Path $runRoot "ScenarioReceipts\scheduled.$scenarioSlug.json"
} else {
    ""
}
$scheduledCapture = Invoke-ShowcasePlayer `
    -Mode "scheduled" `
    -Report $scheduled `
    -Log (Join-Path $runRoot "scheduled-player.log") `
    -Capture $scheduledCapturePath `
    -ScenarioReport $scheduledScenarioReport `
    -Strategy "scheduled" `
    -AsyncJobCount $selectedWorkerCount `
    -InitialBatchSize $selectedBatchSize `
    -MaximumBatchSize $selectedBatchSize `
    -BootstrapBatchSize $selectedBootstrapBatchSize `
    -BudgetSafetyMarginMilliseconds $selectedBudgetSafetyMarginMilliseconds `
    -BudgetCostSafetyMultiplier $selectedBudgetCostSafetyMultiplier `
    -BudgetCooldownFrames $selectedBudgetCooldownFrames `
    -DeadlineBackendMode $selectedDeadlineBackendMode `
    -WarmupReceipt (Join-Path $runRoot "Receipts\scheduled.warmup.json")

$reportArguments = @(
    (Join-Path $PSScriptRoot "pso_report.py"),
    "--baseline", $baseline,
    "--naive", $naive,
    "--optimized", $scheduled,
    "--naive-warmup", (Join-Path $runRoot "Receipts\naive.warmup.json"),
    "--optimized-warmup", (Join-Path $runRoot "Receipts\scheduled.warmup.json"),
    "--plan", $plan,
    "--output", $evidence
)
if ($NoGif) {
    $reportArguments += "--no-gif"
}
if ($isDeadlineRun) {
    # The scenario sidecar and generic sampler share one 12-second gate. The
    # sidecar remains authoritative for event-local reveal evidence, while the
    # generic receipt independently proves the complete raw-frame interval.
    $reportArguments += "--scenario-sidecar-authoritative"
}
$scorecard = Join-Path $evidence "actual-scorecard.png"
if (-not $NoVisualCapture) {
    $reportArguments += @("--actual-scorecard", $scorecard)
}
& $Python @reportArguments
if ($LASTEXITCODE -ne 0) {
    throw "A/B evidence gate failed with exit code $LASTEXITCODE."
}

$deadlineAcceptance = ""
if ($isDeadlineRun) {
    $deadlineAcceptance = Join-Path $evidence "$scenarioSlug.acceptance.json"
    & $Python `
        (Join-Path $PSScriptRoot "validate_deadline_run.py") `
        --baseline $baseline `
        --naive $naive `
        --scheduled $scheduled `
        --scheduled-warmup (Join-Path $runRoot "Receipts\scheduled.warmup.json") `
        --baseline-scenario $baselineScenarioReport `
        --naive-scenario $naiveScenarioReport `
        --scheduled-scenario $scheduledScenarioReport `
        --scenario $scenarioSlug `
        --phase $scenarioPhase `
        --baseline-min-ms (ConvertTo-InvariantNumber $VisualBaselineMinimumMilliseconds) `
        --scheduled-threshold-ms (ConvertTo-InvariantNumber $VisualScheduledMaximumMilliseconds) `
        --output $deadlineAcceptance
    if ($LASTEXITCODE -ne 0) {
        throw "$scenarioDisplayName visual acceptance failed with exit code $LASTEXITCODE."
    }
}

if (-not $NoVisualCapture) {
    if ($isDeadlineRun) {
        New-DeadlineRunVisualEvidence `
            -BaselineCapture $baselineCapture `
            -NaiveCapture $naiveCapture `
            -ScheduledCapture $scheduledCapture `
            -OutputDirectory $evidence `
            -Acceptance $deadlineAcceptance
    } else {
        New-ActualVisualEvidence `
            -BaselineCapture $baselineCapture `
            -NaiveCapture $naiveCapture `
            -ScheduledCapture $scheduledCapture `
            -OutputDirectory $evidence `
            -Scorecard $scorecard
    }
}

Write-Host "Completed $Preset PSO showcase pipeline."
Write-Host "Evidence: $evidence"
