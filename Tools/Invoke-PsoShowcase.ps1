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
$optimized = Join-Path $runRoot "Benchmarks\optimized.benchmark.json"
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
    [string]$Mode
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

    $minimumLuma = ($samples.AverageLuma | Measure-Object -Minimum).Minimum
    $backgroundLimit = $minimumLuma + 0.25
    $revealLimit = $minimumLuma + 0.8
    $backgroundEnd = -1
    for ($index = 0; $index -le $samples.Count - 4; $index++) {
        if ($samples[$index].AverageLuma -le $backgroundLimit -and
            $samples[$index + 1].AverageLuma -le $backgroundLimit -and
            $samples[$index + 2].AverageLuma -le $backgroundLimit) {
            $backgroundEnd = $index + 2
            break
        }
    }
    if ($backgroundEnd -lt 0) {
        throw "$Mode reveal detection did not find an empty-scene reference."
    }

    for ($index = $backgroundEnd + 1; $index -lt $samples.Count - 1; $index++) {
        if ($samples[$index].AverageLuma -gt $revealLimit -and
            $samples[$index + 1].AverageLuma -gt $revealLimit) {
            return $samples[$index].Time
        }
    }
    throw "$Mode reveal detection did not find the first visible tile."
}

function Invoke-ShowcasePlayer(
    [string]$Mode,
    [string]$Report,
    [string]$Log,
    [string]$Capture,
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
    }
    $captureMetadata | ConvertTo-Json -Depth 4 | Set-Content `
        -LiteralPath ($Capture + ".capture.json") `
        -Encoding utf8NoBOM
    return $captureMetadata
}

function New-ActualVisualEvidence(
    [pscustomobject]$BaselineCapture,
    [pscustomobject]$OptimizedCapture,
    [string]$OutputDirectory
) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $effectiveLeadSeconds = [Math]::Min(
        $VisualLeadSeconds,
        [Math]::Min(
            $BaselineCapture.FirstRevealSecondsInVideo,
            $OptimizedCapture.FirstRevealSecondsInVideo))
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
    $optimizedTrim = [Math]::Max(
        0.0,
        $OptimizedCapture.FirstRevealSecondsInVideo -
        $effectiveLeadSeconds -
        $seekGuardSeconds)
    $actualMp4 = Join-Path $OutputDirectory "actual-comparison.mp4"
    $actualGif = Join-Path $OutputDirectory "actual-comparison.gif"
    $alignment = Join-Path $OutputDirectory "visual-alignment.json"

    Invoke-Ffmpeg `
        -Label "actual-side-by-side" `
        -Log ($actualMp4 + ".ffmpeg.log") `
        -Arguments @(
            "-y",
            "-ss", (ConvertTo-InvariantNumber $baselineTrim),
            "-i", $BaselineCapture.Video,
            "-ss", (ConvertTo-InvariantNumber $optimizedTrim),
            "-i", $OptimizedCapture.Video,
            "-filter_complex",
            "[0:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[left];[1:v]fps=60,scale=640:360:flags=lanczos,setsar=1,setpts=PTS-STARTPTS[right];[left][right]hstack=inputs=2[comparison]",
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

    if (-not $NoGif) {
        Invoke-Ffmpeg `
            -Label "actual-gif" `
            -Log ($actualGif + ".ffmpeg.log") `
            -Arguments @(
                "-y",
                "-i", $actualMp4,
                "-filter_complex",
                "[0:v]fps=20,scale=960:-2:flags=lanczos,split[frames][palette_source];[palette_source]palettegen=max_colors=128:stats_mode=diff[palette];[frames][palette]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle[gif]",
                "-map", "[gif]",
                "-loop", "0",
                $actualGif
            )
    }

    [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        synchronizationMarker = "FIRST_VISIBLE_TILE_PIXEL"
        requestedLeadSeconds = $VisualLeadSeconds
        effectiveLeadSeconds = $effectiveLeadSeconds
        seekGuardSeconds = $seekGuardSeconds
        durationSeconds = $VisualDurationSeconds
        baseline = $BaselineCapture
        optimized = $OptimizedCapture
        baselineTrimSeconds = $baselineTrim
        optimizedTrimSeconds = $optimizedTrim
        outputVideo = $actualMp4
        outputGif = if ($NoGif) { "" } else { $actualGif }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $alignment -Encoding utf8NoBOM

    Write-Host "Actual player comparison: $actualMp4"
    if (-not $NoGif) {
        Write-Host "Actual player GIF: $actualGif"
    }
}

Invoke-UnityEditor @(
    "-executeMethod",
    "Yanagisawa.ShaderHitchPipeline.Showcase.Editor.PsoShowcaseBuilder.BuildWindowsPlayer",
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
    "-quit",
    "-logFile", "Logs/showcase-final-$stamp.log"
) "final-build"

$optimizedCapturePath = if ($NoVisualCapture) {
    ""
} else {
    Join-Path $captures "optimized.mp4"
}
$optimizedCapture = Invoke-ShowcasePlayer `
    -Mode "optimized" `
    -Report $optimized `
    -Log (Join-Path $runRoot "optimized-player.log") `
    -Capture $optimizedCapturePath

if (-not $NoVisualCapture) {
    New-ActualVisualEvidence `
        -BaselineCapture $baselineCapture `
        -OptimizedCapture $optimizedCapture `
        -OutputDirectory $evidence
}

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
