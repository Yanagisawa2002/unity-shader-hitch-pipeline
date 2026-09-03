[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Player,
    [string[]]$PlayerArguments = @(),
    [Parameter(Mandatory = $true)]
    [string]$PresentMon,
    [string]$Output = "PsoArtifacts\WindowsEvidence",
    [string]$WarmupReceipt = "",
    [ValidateRange(1, 3600)]
    [int]$CaptureSeconds = 30,
    [ValidateRange(1, 7200)]
    [int]$PlayerTimeoutSeconds = 120,
    [double]$TargetFrameMilliseconds = 0.0,
    [switch]$CaptureEtw,
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
$playerPath = (Resolve-Path -LiteralPath $Player).Path
$presentMonPath = (Resolve-Path -LiteralPath $PresentMon).Path
$outputRoot = [System.IO.Path]::GetFullPath($Output)
$warmupPath = if ([string]::IsNullOrWhiteSpace($WarmupReceipt)) {
    ""
} else {
    [System.IO.Path]::GetFullPath($WarmupReceipt)
}
$analyzer = Join-Path $PSScriptRoot "pso_windows_evidence.py"
if (-not (Test-Path -LiteralPath $analyzer)) {
    throw "PresentMon analyzer is missing: $analyzer"
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$sessionName = "ShaderHitchPipeline-" + [Guid]::NewGuid().ToString("N")
$presentMonCsv = Join-Path $outputRoot "presentmon.csv"
$presentMonStdout = Join-Path $outputRoot "presentmon.stdout.log"
$presentMonStderr = Join-Path $outputRoot "presentmon.stderr.log"
$playerStdout = Join-Path $outputRoot "player.stdout.log"
$playerStderr = Join-Path $outputRoot "player.stderr.log"
$etlPath = Join-Path $outputRoot "gpu.etl"
$manifestPath = Join-Path $outputRoot "windows-evidence-manifest.json"

function ConvertTo-ArgumentLine([string[]]$Arguments) {
    return ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + $_.Replace('"', '\"') + '"'
        } else {
            $_
        }
    }) -join ' '
}

function Get-OptionalHash([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or
        -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return ""
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-FileRecord([string]$Path) {
    $exists = -not [string]::IsNullOrWhiteSpace($Path) -and
        (Test-Path -LiteralPath $Path -PathType Leaf)
    return [ordered]@{
        path = if ($exists) { [System.IO.Path]::GetFullPath($Path) } else { $Path }
        exists = $exists
        bytes = if ($exists) { (Get-Item -LiteralPath $Path).Length } else { 0 }
        sha256 = Get-OptionalHash $Path
    }
}

$processName = [System.IO.Path]::GetFileNameWithoutExtension($playerPath)
$existing = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    throw "Refusing an ambiguous capture: $processName is already running."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
$presentMonVersion = (& $presentMonPath --help 2>&1 | Select-Object -First 1).ToString()
$wprStatusBefore = (& wpr -status 2>&1) -join [Environment]::NewLine

if ($CaptureEtw -and $wprStatusBefore -notmatch 'WPR is not recording') {
    throw "WPR already owns a recording session. Stop it before requesting -CaptureEtw."
}

$presentMonArguments = @(
    "--process_name", ([System.IO.Path]::GetFileName($playerPath)),
    "--output_file", $presentMonCsv,
    "--date_time",
    "--timed", $CaptureSeconds.ToString(),
    "--terminate_after_timed",
    "--terminate_on_proc_exit",
    "--no_console_stats",
    "--stop_existing_session",
    "--session_name", $sessionName,
    "--v2_metrics"
)

$presentMonProcess = $null
$playerProcess = $null
$wprStarted = $false
$failure = ""
$playerStartedUtc = ""
$playerEndedUtc = ""
$presentMonExitCode = $null
$playerExitCode = $null
$analyzerExitCode = $null

try {
    if ($CaptureEtw) {
        & wpr -start GPU -filemode
        if ($LASTEXITCODE -ne 0) {
            throw "WPR failed to start the built-in GPU profile (exit $LASTEXITCODE)."
        }
        $wprStarted = $true
    }

    $presentMonProcess = Start-Process `
        -FilePath $presentMonPath `
        -ArgumentList (ConvertTo-ArgumentLine $presentMonArguments) `
        -WorkingDirectory $outputRoot `
        -RedirectStandardOutput $presentMonStdout `
        -RedirectStandardError $presentMonStderr `
        -PassThru `
        -WindowStyle Hidden
    Start-Sleep -Milliseconds 500
    if ($presentMonProcess.HasExited) {
        $presentMonExitCode = $presentMonProcess.ExitCode
        $detail = if (Test-Path -LiteralPath $presentMonStderr) {
            Get-Content -Raw -LiteralPath $presentMonStderr
        } else {
            ""
        }
        throw "PresentMon could not begin capture (exit $presentMonExitCode). $detail"
    }

    $playerStartedUtc = [DateTime]::UtcNow.ToString("o")
    $playerProcess = Start-Process `
        -FilePath $playerPath `
        -ArgumentList (ConvertTo-ArgumentLine $PlayerArguments) `
        -WorkingDirectory (Split-Path -Parent $playerPath) `
        -RedirectStandardOutput $playerStdout `
        -RedirectStandardError $playerStderr `
        -PassThru `
        -WindowStyle Hidden
    if (-not $playerProcess.WaitForExit($PlayerTimeoutSeconds * 1000)) {
        [void]$playerProcess.CloseMainWindow()
        if (-not $playerProcess.WaitForExit(3000)) {
            $playerProcess.Kill()
            $playerProcess.WaitForExit()
        }
        throw "Player exceeded the $PlayerTimeoutSeconds second evidence timeout."
    }
    $playerEndedUtc = [DateTime]::UtcNow.ToString("o")
    $playerExitCode = $playerProcess.ExitCode
    if ($playerExitCode -ne 0) {
        throw "Player exited with code $playerExitCode."
    }

    if (-not $presentMonProcess.WaitForExit(15000)) {
        & $presentMonPath `
            --terminate_existing_session `
            --session_name $sessionName `
            --no_csv `
            --no_console_stats | Out-Null
        [void]$presentMonProcess.WaitForExit(5000)
    }
    if ($presentMonProcess.HasExited) {
        $presentMonExitCode = $presentMonProcess.ExitCode
    }
    if ($null -eq $presentMonExitCode -or $presentMonExitCode -ne 0) {
        throw "PresentMon capture failed or did not terminate cleanly."
    }
    if (-not (Test-Path -LiteralPath $presentMonCsv -PathType Leaf)) {
        throw "PresentMon emitted no CSV: $presentMonCsv"
    }
} catch {
    $failure = $_.Exception.ToString()
} finally {
    if ($playerProcess -ne $null -and -not $playerProcess.HasExited) {
        $playerProcess.Kill()
        $playerProcess.WaitForExit()
        $playerEndedUtc = [DateTime]::UtcNow.ToString("o")
        $playerExitCode = $playerProcess.ExitCode
    }
    if ($presentMonProcess -ne $null -and -not $presentMonProcess.HasExited) {
        & $presentMonPath `
            --terminate_existing_session `
            --session_name $sessionName `
            --no_csv `
            --no_console_stats | Out-Null
        if (-not $presentMonProcess.WaitForExit(5000)) {
            $presentMonProcess.Kill()
            $presentMonProcess.WaitForExit()
        }
        $presentMonExitCode = $presentMonProcess.ExitCode
    }
    if ($wprStarted) {
        & wpr -stop $etlPath
        if ($LASTEXITCODE -ne 0 -and [string]::IsNullOrWhiteSpace($failure)) {
            $failure = "WPR failed to stop and save the ETL (exit $LASTEXITCODE)."
        }
    }
}

if ([string]::IsNullOrWhiteSpace($failure) -and
    -not [string]::IsNullOrWhiteSpace($warmupPath) -and
    -not (Test-Path -LiteralPath $warmupPath -PathType Leaf)) {
    $failure = "Warmup receipt was not emitted: $warmupPath"
}

if ([string]::IsNullOrWhiteSpace($failure)) {
    $offsetMinutes = [int][TimeZoneInfo]::Local.GetUtcOffset([DateTime]::Now).TotalMinutes
    $analyzerArguments = @(
        $analyzer,
        "--presentmon-csv", $presentMonCsv,
        "--local-utc-offset-minutes", $offsetMinutes.ToString(),
        "--output", $outputRoot
    )
    if (-not [string]::IsNullOrWhiteSpace($warmupPath)) {
        $analyzerArguments += @("--warmup-receipt", $warmupPath)
    }
    if ($TargetFrameMilliseconds -gt 0.0) {
        $analyzerArguments += @(
            "--target-ms",
            $TargetFrameMilliseconds.ToString(
                [Globalization.CultureInfo]::InvariantCulture)
        )
    }
    & $Python @analyzerArguments
    $analyzerExitCode = $LASTEXITCODE
    if ($analyzerExitCode -ne 0) {
        $failure = "PresentMon analyzer failed with exit $analyzerExitCode."
    }
}

$gpuDevices = @(Get-CimInstance Win32_VideoController | ForEach-Object {
    [ordered]@{
        name = $_.Name
        driverVersion = $_.DriverVersion
        pnpDeviceId = $_.PNPDeviceID
        adapterRamBytes = [long]$_.AdapterRAM
    }
})
$operatingSystem = Get-CimInstance Win32_OperatingSystem
$processor = Get-CimInstance Win32_Processor | Select-Object -First 1
$manifest = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    completed = [string]::IsNullOrWhiteSpace($failure)
    error = $failure
    privilege = [ordered]@{
        isAdministrator = $isAdministrator
        note = "PresentMon and WPR require ETW session rights (administrator or Performance Log Users)."
    }
    target = [ordered]@{
        player = $playerPath
        playerSha256 = Get-OptionalHash $playerPath
        arguments = $PlayerArguments
        startedUtc = $playerStartedUtc
        endedUtc = $playerEndedUtc
        exitCode = $playerExitCode
    }
    tools = [ordered]@{
        presentMon = $presentMonPath
        presentMonSha256 = Get-OptionalHash $presentMonPath
        presentMonVersion = $presentMonVersion
        presentMonArguments = $presentMonArguments
        presentMonExitCode = $presentMonExitCode
        wprStatusBefore = $wprStatusBefore
        etwProfile = if ($CaptureEtw) { "GPU (built-in, file mode)" } else { "disabled" }
        analyzer = $analyzer
        analyzerSha256 = Get-OptionalHash $analyzer
        analyzerExitCode = $analyzerExitCode
    }
    environment = [ordered]@{
        computerName = [Environment]::MachineName
        operatingSystem = $operatingSystem.Caption
        operatingSystemVersion = $operatingSystem.Version
        processor = $processor.Name
        logicalProcessorCount = [Environment]::ProcessorCount
        gpuDevices = $gpuDevices
    }
    artifacts = [ordered]@{
        presentMonCsv = Get-FileRecord $presentMonCsv
        presentMonSummary = Get-FileRecord (Join-Path $outputRoot "presentmon-summary.json")
        etl = Get-FileRecord $(if ($CaptureEtw) { $etlPath } else { "" })
        warmupReceipt = Get-FileRecord $warmupPath
        playerStdout = Get-FileRecord $playerStdout
        playerStderr = Get-FileRecord $playerStderr
        presentMonStdout = Get-FileRecord $presentMonStdout
        presentMonStderr = Get-FileRecord $presentMonStderr
    }
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content `
    -LiteralPath $manifestPath `
    -Encoding utf8NoBOM

if (-not [string]::IsNullOrWhiteSpace($failure)) {
    throw $failure
}

Write-Host "PresentMon summary: $(Join-Path $outputRoot 'presentmon-summary.json')"
if ($CaptureEtw) {
    Write-Host "GPU ETL: $etlPath"
}
Write-Host "Evidence manifest: $manifestPath"
