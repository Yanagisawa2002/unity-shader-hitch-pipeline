[CmdletBinding()]
param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.2f1\Editor\Unity.exe",
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\UnityProject"),
    [string]$Python = "python",
    [string]$Ffmpeg = "ffmpeg",
    [int]$Frames = 240,
    [double]$HitchThresholdMilliseconds = 8.33,
    [double]$CaptureSeconds = 6.0,
    [double]$VisualLeadSeconds = 0.5,
    [double]$VisualDurationSeconds = 2.8,
    [double]$WarmupVisualDurationSeconds = 1.5,
    [int[]]$AutoTuneWorkerCounts = @(1, 2, 4, 8),
    [int[]]$AutoTuneBatchSizes = @(4, 16, 64),
    [int]$AutoTuneRepetitions = 2,
    [switch]$SkipAutoTune,
    [switch]$NoVisualCapture,
    [switch]$NoGif
)

$ErrorActionPreference = "Stop"
$project = [System.IO.Path]::GetFullPath($ProjectPath)
$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$profileId = "showcase-d3d12-$stamp"
$runRoot = Join-Path $project "PsoArtifacts\ShowcaseRuns\$stamp"
$player = Join-Path $project "Builds\Windows\ShaderHitchShowcase.exe"
$baseline = Join-Path $runRoot "Benchmarks\baseline.benchmark.json"
$naive = Join-Path $runRoot "Benchmarks\naive.benchmark.json"
$scheduled = Join-Path $runRoot "Benchmarks\scheduled.benchmark.json"
$profileRoot = Join-Path $runRoot "Profiles"
$plan = Join-Path $profileRoot "$profileId\plan.json"
$evidence = Join-Path $runRoot "Evidence"
$captures = Join-Path $runRoot "Captures"

if (-not $NoVisualCapture) {
    $null = Get-Command $Ffmpeg -ErrorAction Stop
}
if (-not $NoGif) {
    & $Python -c "from PIL import Image"
    if ($LASTEXITCODE -ne 0) {
        throw "GIF output requires Pillow. Run: python -m pip install -r Tools/requirements.txt"
    }
}

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
        WorkloadStartSecondsInVideo = $Capture.WorkloadStartSecondsInVideo
        FirstRevealSecondsInVideo = $Capture.FirstRevealSecondsInVideo
        FirstPresentedUiSecondsInVideo = $Capture.FirstPresentedUiSecondsInVideo
    }
}

function Invoke-Ffmpeg([string[]]$Arguments, [string]$Label, [string]$Log) {
    Write-Host "[$Label] ffmpeg"
    $process = Start-Process `
        -FilePath $Ffmpeg `
        -ArgumentList (ConvertTo-ArgumentLine $Arguments) `
        -WorkingDirectory $project `
        -Wait `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardError $Log
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
    $topMost = [IntPtr](-1)
    $noSize = [uint32]0x0001
    $showWindow = [uint32]0x0040
    [void][PsoShowcaseCaptureNative]::SetWindowPos(
        $handle,
        $topMost,
        32,
        32,
        0,
        0,
        ($noSize -bor $showWindow))
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

function Get-FirstRevealSeconds(
    [string]$Capture,
    [pscustomobject]$CaptureRegion,
    [string]$Mode,
    [double]$MinimumSeconds
) {
    $cropX = [Math]::Floor($CaptureRegion.Width * 0.25)
    $cropY = [Math]::Floor($CaptureRegion.Height * 0.2222)
    $cropWidth = [Math]::Floor($CaptureRegion.Width * 0.5)
    $cropHeight = [Math]::Floor($CaptureRegion.Height * 0.1333)
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

    $referenceCount = [Math]::Min(24, [Math]::Floor($samples.Count / 4))
    if ($referenceCount -lt 8) {
        throw "$Mode reveal detection has too little pre-roll for a background reference."
    }
    $referenceLuma = @(
        $samples | Select-Object -First $referenceCount |
            ForEach-Object { $_.AverageLuma } | Sort-Object
    )
    $backgroundLuma = [double]$referenceLuma[
        [Math]::Floor($referenceLuma.Count / 2)]
    # The live presentation scanline is intentionally bright but narrow. Require a
    # persistent multi-frame luma increase so it cannot be mistaken for tile reveal.
    $revealLimit = $backgroundLuma + 2.0
    $backgroundEnd = $referenceCount - 1

    for ($index = $backgroundEnd + 1; $index -lt $samples.Count - 3; $index++) {
        if ($samples[$index].Time -ge $MinimumSeconds -and
            $samples[$index].AverageLuma -gt $revealLimit -and
            $samples[$index + 1].AverageLuma -gt $revealLimit -and
            $samples[$index + 2].AverageLuma -gt $revealLimit -and
            $samples[$index + 3].AverageLuma -gt $revealLimit) {
            return $samples[$index].Time
        }
    }
    throw "$Mode reveal detection did not find the first visible tile."
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
    foreach ($line in (Get-Content -LiteralPath $statisticsLog)) {
        if ($line -match '^\[Parsed_metadata[^]]*\] frame:\s*\d+\s+pts:\s*\d+\s+pts_time:([0-9.]+)') {
            $sampleTime = [double]::Parse(
                $Matches[1],
                [Globalization.CultureInfo]::InvariantCulture)
        } elseif ($null -ne $sampleTime -and
                  $line -match 'lavfi\.signalstats\.YMAX=([0-9.]+)') {
            $samples.Add([pscustomobject]@{
                Time = $sampleTime
                MaximumLuma = [double]::Parse(
                    $Matches[1],
                    [Globalization.CultureInfo]::InvariantCulture)
            })
            $sampleTime = $null
        }
    }
    for ($index = 0; $index -lt $samples.Count - 1; $index++) {
        if ($samples[$index].MaximumLuma -ge 160.0 -and
            $samples[$index + 1].MaximumLuma -ge 160.0) {
            return $samples[$index].Time
        }
    }
    throw "$Mode UI detection did not find two presented title frames."
}

function Invoke-ShowcasePlayer(
    [string]$Mode,
    [string]$Report,
    [string]$Log,
    [string]$Capture,
    [string]$Strategy = "",
    [int]$AsyncJobCount = 0,
    [int]$InitialBatchSize = 0,
    [string]$WarmupReceipt = "",
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
    if (-not [string]::IsNullOrWhiteSpace($Strategy)) {
        $arguments += @("-pso-warmup-strategy", $Strategy)
    }
    if ($AsyncJobCount -gt 0) {
        $arguments += @("-max-async-pso-job-count", $AsyncJobCount.ToString())
    }
    if ($InitialBatchSize -gt 0) {
        $arguments += @("-pso-warmup-initial-batch", $InitialBatchSize.ToString())
    }
    if (-not [string]::IsNullOrWhiteSpace($WarmupReceipt)) {
        $arguments += @("-pso-warmup-receipt", $WarmupReceipt)
    }
    $markerFile = ""
    if (-not [string]::IsNullOrWhiteSpace($Capture)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $Capture) -Force | Out-Null
        $markerFile = $Capture + ".markers.txt"
        $arguments += @(
            "-pso-benchmark-no-quit",
            "-pso-showcase-marker", $markerFile
        )
    }
    $process = Start-Process `
        -FilePath $player `
        -ArgumentList (ConvertTo-ArgumentLine $arguments) `
        -WorkingDirectory (Split-Path -Parent $player) `
        -PassThru

    if ([string]::IsNullOrWhiteSpace($Capture)) {
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
        $captureReady = Wait-ShowcaseMarker `
            -MarkerFile $markerFile `
            -Marker "CAPTURE_READY realtime" `
            -PlayerProcess $process
        $windowTitle = Wait-PlayerWindow $process
        $captureRect = Get-PlayerClientCaptureRect $process
        $previousCursor = New-Object PsoShowcaseCaptureNative+Point
        $restoreCursor = [PsoShowcaseCaptureNative]::GetCursorPos(
            [ref]$previousCursor)
        [void][PsoShowcaseCaptureNative]::SetCursorPos(0, 0)
        Start-Sleep -Milliseconds 150
        $captureStartUtc = [DateTime]::UtcNow
        Invoke-Ffmpeg `
            -Label "$Mode-window-capture" `
            -Log ($Capture + ".ffmpeg.log") `
            -Arguments @(
                "-y",
                "-f", "gdigrab",
                "-draw_mouse", "0",
                "-framerate", "60",
                "-thread_queue_size", "1024",
                "-offset_x", $captureRect.X.ToString(),
                "-offset_y", $captureRect.Y.ToString(),
                "-video_size", "$($captureRect.Width)x$($captureRect.Height)",
                "-i", "desktop",
                "-t", (ConvertTo-InvariantNumber $CaptureSeconds),
                "-an",
                "-c:v", "libx264",
                "-preset", "fast",
                "-crf", "16",
                "-pix_fmt", "yuv420p",
                "-movflags", "+faststart",
                $Capture
            )
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
    $captureStartAfterReady = ($captureStartUtc - $captureReady.SeenUtc).TotalSeconds
    $workloadStartInVideo =
        $workloadStart.RealtimeSeconds -
        $captureReady.RealtimeSeconds -
        $captureStartAfterReady
    if ($workloadStartInVideo -lt 0.0 -or $workloadStartInVideo -gt $CaptureSeconds) {
        throw "$Mode workload marker is outside the captured video: $workloadStartInVideo s"
    }

    $firstRevealSeconds = Get-FirstRevealSeconds `
        -Capture $Capture `
        -CaptureRegion $captureRect `
        -Mode $Mode `
        -MinimumSeconds $workloadStartInVideo
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

    $captureMetadata = [pscustomobject]@{
        Mode = $Mode
        Video = [System.IO.Path]::GetFullPath($Capture)
        MarkerFile = [System.IO.Path]::GetFullPath($markerFile)
        WindowTitle = $windowTitle
        CaptureRegion = $captureRect
        CaptureReadyRealtimeSeconds = $captureReady.RealtimeSeconds
        WorkloadStartRealtimeSeconds = $workloadStart.RealtimeSeconds
        WorkloadStartSecondsInVideo = $workloadStartInVideo
        FirstRevealSecondsInVideo = $firstRevealSeconds
        FirstPresentedUiSecondsInVideo = $firstPresentedUiSeconds
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
    [string]$OutputDirectory
) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $effectiveLeadSeconds = [Math]::Min(
        $VisualLeadSeconds,
        [Math]::Min(
            $BaselineCapture.FirstRevealSecondsInVideo,
            [Math]::Min(
                $NaiveCapture.FirstRevealSecondsInVideo,
                $ScheduledCapture.FirstRevealSecondsInVideo)))
    if ($effectiveLeadSeconds -lt 0.25) {
        # A very short pre-roll mostly contains swapchain/DWM settling. Start at the
        # first visible tile instead; runs with enough clean pre-roll retain the
        # requested half-second lead.
        $effectiveLeadSeconds = 0.0
    }
    # Seek two nominal frames before the detected transition. Source timestamps are
    # rounded to milliseconds, so a one-frame guard can still land just after the
    # prior frame and make one side appear a batch ahead.
    $seekGuardSeconds = 2.0 / 60.0
    $baselineTrim = [Math]::Max(
        0.0,
        $BaselineCapture.FirstRevealSecondsInVideo -
        $effectiveLeadSeconds -
        $seekGuardSeconds)
    $naiveTrim = [Math]::Max(
        0.0,
        $NaiveCapture.FirstRevealSecondsInVideo -
        $effectiveLeadSeconds -
        $seekGuardSeconds)
    $scheduledTrim = [Math]::Max(
        0.0,
        $ScheduledCapture.FirstRevealSecondsInVideo -
        $effectiveLeadSeconds -
        $seekGuardSeconds)
    $actualMp4 = Join-Path $OutputDirectory "actual-comparison.mp4"
    $actualGif = Join-Path $OutputDirectory "actual-comparison.gif"
    $actualPng = Join-Path $OutputDirectory "actual-comparison.png"
    $warmupMp4 = Join-Path $OutputDirectory "actual-warmup-comparison.mp4"
    $warmupGif = Join-Path $OutputDirectory "actual-warmup-comparison.gif"
    $alignment = Join-Path $OutputDirectory "visual-alignment.json"

    Invoke-Ffmpeg `
        -Label "actual-side-by-side" `
        -Log ($actualMp4 + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", (ConvertTo-InvariantNumber $baselineTrim),
            "-i", $BaselineCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $naiveTrim),
            "-i", $NaiveCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $scheduledTrim),
            "-i", $ScheduledCapture.Video,
            "-filter_complex",
            "[0:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[cold];[1:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[naive];[2:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[ours];[cold][naive][ours]hstack=inputs=3[comparison]",
            "-map", "[comparison]",
            "-t", (ConvertTo-InvariantNumber $VisualDurationSeconds),
            "-an",
            "-c:v", "libx264",
            "-preset", "slow",
            "-crf", "17",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            $actualMp4
        )

    Invoke-Ffmpeg `
        -Label "actual-warmup-triptych" `
        -Log ($warmupMp4 + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", (ConvertTo-InvariantNumber $BaselineCapture.FirstPresentedUiSecondsInVideo),
            "-i", $BaselineCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $NaiveCapture.FirstPresentedUiSecondsInVideo),
            "-i", $NaiveCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $ScheduledCapture.FirstPresentedUiSecondsInVideo),
            "-i", $ScheduledCapture.Video,
            "-filter_complex",
            "[0:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[cold];[1:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[naive];[2:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[ours];[cold][naive][ours]hstack=inputs=3[comparison]",
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
        schemaVersion = 2
        generatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        pathBase = "alignmentFileDirectory"
        synchronizationMarker = "FIRST_VISIBLE_TILE_PIXEL"
        requestedLeadSeconds = $VisualLeadSeconds
        effectiveLeadSeconds = $effectiveLeadSeconds
        seekGuardSeconds = $seekGuardSeconds
        durationSeconds = $VisualDurationSeconds
        warmupDurationSeconds = $WarmupVisualDurationSeconds
        baseline = ConvertTo-EvidenceCapture $BaselineCapture $OutputDirectory
        naive = ConvertTo-EvidenceCapture $NaiveCapture $OutputDirectory
        scheduled = ConvertTo-EvidenceCapture $ScheduledCapture $OutputDirectory
        baselineTrimSeconds = $baselineTrim
        naiveTrimSeconds = $naiveTrim
        scheduledTrimSeconds = $scheduledTrim
        outputVideo = ConvertTo-EvidencePath $actualMp4 $OutputDirectory
        outputGif = if ($NoGif) {
            ""
        } else {
            ConvertTo-EvidencePath $actualGif $OutputDirectory
        }
        outputPoster = ConvertTo-EvidencePath $actualPng $OutputDirectory
        warmupVideo = ConvertTo-EvidencePath $warmupMp4 $OutputDirectory
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
    Write-Host "Warmup pressure comparison: $warmupMp4"
}

Invoke-UnityEditor @(
    "-executeMethod",
    "Yanagisawa.ShaderHitchPipeline.Showcase.Editor.PsoShowcaseBuilder.BuildWindowsPlayer",
    "-pso-showcase-cache-buster", $stamp,
    "-pso-training-build",
    "-quit",
    "-logFile", "Logs/showcase-training-$stamp.log"
) "training-build"

$baselineCapturePath = if ($NoVisualCapture) {
    ""
} else {
    Join-Path $captures "baseline.mp4"
}
$baselineCapture = Invoke-ShowcasePlayer `
    -Mode "baseline" `
    -Report $baseline `
    -Log (Join-Path $runRoot "baseline-player.log") `
    -Capture $baselineCapturePath `
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
    "-pso-showcase-cache-buster", $stamp,
    "-quit",
    "-logFile", "Logs/showcase-final-$stamp.log"
) "final-build"

$selectedWorkerCount = 0
$selectedBatchSize = 0
if (-not $SkipAutoTune) {
    $policySearchOutput = Join-Path $runRoot "PolicySearch"
    & (Join-Path $PSScriptRoot "Find-PsoWarmupPolicy.ps1") `
        -Player $player `
        -Plan $plan `
        -Output $policySearchOutput `
        -WorkerCounts $AutoTuneWorkerCounts `
        -BatchSizes $AutoTuneBatchSizes `
        -Repetitions $AutoTuneRepetitions `
        -TargetFrameMilliseconds 16.67
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
    $selectedWorkerCount = [int]$recommendation.workerCount
    $selectedBatchSize = [int]$recommendation.batchSize
}

$naiveCapturePath = if ($NoVisualCapture) {
    ""
} else {
    Join-Path $captures "naive.mp4"
}
$naiveCapture = Invoke-ShowcasePlayer `
    -Mode "naive" `
    -Report $naive `
    -Log (Join-Path $runRoot "naive-player.log") `
    -Capture $naiveCapturePath `
    -Strategy "throughput" `
    -AsyncJobCount $selectedWorkerCount `
    -WarmupReceipt (Join-Path $runRoot "Receipts\naive.warmup.json")

$scheduledCapturePath = if ($NoVisualCapture) {
    ""
} else {
    Join-Path $captures "scheduled.mp4"
}
$scheduledCapture = Invoke-ShowcasePlayer `
    -Mode "scheduled" `
    -Report $scheduled `
    -Log (Join-Path $runRoot "scheduled-player.log") `
    -Capture $scheduledCapturePath `
    -Strategy "scheduled" `
    -AsyncJobCount $selectedWorkerCount `
    -InitialBatchSize $selectedBatchSize `
    -WarmupReceipt (Join-Path $runRoot "Receipts\scheduled.warmup.json")

if (-not $NoVisualCapture) {
    New-ActualVisualEvidence `
        -BaselineCapture $baselineCapture `
        -NaiveCapture $naiveCapture `
        -ScheduledCapture $scheduledCapture `
        -OutputDirectory $evidence
}

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
& $Python @reportArguments
if ($LASTEXITCODE -ne 0) {
    throw "A/B evidence gate failed with exit code $LASTEXITCODE."
}

Write-Host "Completed PSO showcase pipeline."
Write-Host "Evidence: $evidence"
