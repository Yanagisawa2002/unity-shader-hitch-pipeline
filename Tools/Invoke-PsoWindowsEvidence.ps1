[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Player,
    [string[]]$PlayerArguments = @(),
    [Parameter(Mandatory)][string]$PresentMon,
    [string]$Output = 'PsoArtifacts/WindowsEvidence',
    [string]$WarmupReceipt = '', [string]$BenchmarkReceipt = '',
    [string]$Markers = '', [string]$BuildManifest = '',
    [ValidateRange(1,3600)][int]$CaptureSeconds = 120,
    [ValidateRange(1,3600)][int]$PlayerTimeoutSeconds = 110,
    [ValidateRange(0,60)][int]$WprPreRollSeconds = 0,
    [ValidateRange(0,60)][int]$PrePlayerWaitSeconds = 0,
    [double]$TargetFrameMilliseconds = 0,
    [switch]$CaptureEtw, [switch]$CaptureUnavailableContinueEngine,
    [switch]$ProbeOnly, [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$playerPath = (Resolve-Path -LiteralPath $Player).Path
$presentMonPath = (Resolve-Path -LiteralPath $PresentMon).Path
$outputRoot = [IO.Path]::GetFullPath($Output)
if ((Test-Path -LiteralPath $outputRoot) -and @(Get-ChildItem -LiteralPath $outputRoot -Force).Count) { throw 'Evidence output must be empty; refusing stale files or overwrites.' }
foreach ($receipt in @($WarmupReceipt,$BenchmarkReceipt,$Markers)) {
    if ($receipt -and (Test-Path -LiteralPath $receipt)) { throw "Refusing stale receipt: $receipt" }
}
if ($CaptureSeconds -le $PlayerTimeoutSeconds -and -not $ProbeOnly) { throw 'CaptureSeconds must exceed PlayerTimeoutSeconds.' }
if ($PrePlayerWaitSeconds -gt 0 -and $CaptureSeconds -le ($PlayerTimeoutSeconds + $PrePlayerWaitSeconds + 1)) { throw 'Capture must include the common pre-player wait plus the full Player timeout.' }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$sessionName = 'ShaderHitchPipeline-' + [Guid]::NewGuid().ToString('N')
$errors = [Collections.Generic.List[string]]::new()
$commands = [Collections.Generic.List[object]]::new()
$analyzer = Join-Path $PSScriptRoot 'pso_windows_evidence.py'
$csv = Join-Path $outputRoot 'presentmon.csv'; $etl = Join-Path $outputRoot 'gpu.etl'
$wpr = (Get-Command wpr -ErrorAction SilentlyContinue).Source
$pm = $null; $ownedPlayer = $null; $wprStarted = $false
$started = ''; $ended = ''; $processId = $null; $playerExit = $null; $pmExit = $null; $analyzerExit = $null
function FileRecord([string]$Path) {
    $exists = $Path -and (Test-Path -LiteralPath $Path -PathType Leaf)
    return [ordered]@{path=$Path; exists=[bool]$exists; bytes=$(if ($exists) {(Get-Item -LiteralPath $Path).Length} else {0}); sha256=$(if ($exists) {(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()} else {''})}
}
function StartOwned([string]$File, [string[]]$Arguments, [string]$Prefix, [bool]$VisiblePlayer = $false) {
    # ArgumentList performs Windows argv quoting, including trailing backslashes.
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    # Actual presentation requires a visible, non-minimized swap chain. Only the
    # measured Player uses Normal; capture and analysis helpers stay hidden.
    $info.WindowStyle = if ($VisiblePlayer) { [Diagnostics.ProcessWindowStyle]::Normal } else { [Diagnostics.ProcessWindowStyle]::Hidden }
    $info.WorkingDirectory = Split-Path -Parent $File
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $proc = [Diagnostics.Process]::new(); $proc.StartInfo = $info; [void]$proc.Start()
    return @{process=$proc; stdout=$proc.StandardOutput.ReadToEndAsync(); stderr=$proc.StandardError.ReadToEndAsync(); prefix=$Prefix}
}
function SaveOwned($Owned) {
    if ($null -eq $Owned) { return }
    $Owned.stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $outputRoot ($Owned.prefix+'.stdout.log')) -Encoding utf8NoBOM
    $Owned.stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $outputRoot ($Owned.prefix+'.stderr.log')) -Encoding utf8NoBOM
}
function WprCommand([string[]]$Arguments, [string]$Label) {
    if (-not $wpr) { return @{exitCode=$null; text='wpr.exe unavailable'} }
    $owned = StartOwned $wpr $Arguments $Label
    if (-not $owned.process.WaitForExit(30000)) { $owned.process.Kill(); $owned.process.WaitForExit(); $errors.Add("WPR $Label timed out; recording state unknown") }
    SaveOwned $owned
    $result = @{exitCode=$owned.process.ExitCode; text=($owned.stdout.Result + $owned.stderr.Result)}
    $commands.Add(@{tool='wpr'; arguments=$Arguments; exitCode=$result.exitCode; output=$result.text})
    return $result
}
$pmArgs = @('--process_name',[IO.Path]::GetFileName($playerPath),'--output_file',$csv,'--qpc_time','--timed',"$CaptureSeconds",'--terminate_after_timed','--terminate_on_proc_exit','--no_console_stats','--session_name',$sessionName,'--v2_metrics')
$interference = @(Get-Process | Select-Object ProcessName,Id)
try {
    if (@(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($playerPath)) -ErrorAction SilentlyContinue).Count) { throw 'Player with same name already running; refusing ambiguous capture.' }
    if ($CaptureEtw) {
        $status = WprCommand @('-status') 'wpr-status'
        if ($status.text -match 'WPR is not recording') {
            $start = WprCommand @('-start','GPU','-filemode') 'wpr-start'
            $wprStarted = $start.exitCode -eq 0
            if (-not $wprStarted) { $errors.Add("WPR start failed (exit $($start.exitCode)): $($start.text)") }
        } else { $errors.Add("WPR status active or unknown; recording untouched (exit $($status.exitCode)): $($status.text)") }
    }
    if ($wprStarted -and $WprPreRollSeconds -gt 0) {
        # Bounded, declared recorder pre-roll; all target work remains captured.
        # This cannot relax the workload, budget, clock or receipt acceptance gates.
        $commands.Add(@{tool='capture-preroll'; seconds=$WprPreRollSeconds; startedUtc=[DateTime]::UtcNow.ToString('o')})
        Start-Sleep -Seconds $WprPreRollSeconds
    }
    # PresentMon is probed independently even if WPR is denied.
    $pm = StartOwned $presentMonPath $pmArgs 'presentmon'
    Start-Sleep -Milliseconds 1000
    if ($pm.process.HasExited) {
        $pmExit = $pm.process.ExitCode; SaveOwned $pm
        $errors.Add("PresentMon start failed (exit $pmExit): $($pm.stdout.Result) $($pm.stderr.Result)")
    }
    if (-not $ProbeOnly -and ($errors.Count -eq 0 -or $CaptureUnavailableContinueEngine)) {
        if ($PrePlayerWaitSeconds -gt 0) {
            $commands.Add(@{tool='common-pre-player-wait'; seconds=$PrePlayerWaitSeconds; startedUtc=[DateTime]::UtcNow.ToString('o')})
            Start-Sleep -Seconds $PrePlayerWaitSeconds
        }
        $started = [DateTime]::UtcNow.ToString('o')
        $ownedPlayer = StartOwned $playerPath $PlayerArguments 'player' $true
        $processId = $ownedPlayer.process.Id
        if (-not $ownedPlayer.process.WaitForExit($PlayerTimeoutSeconds * 1000)) {
            $ownedPlayer.process.Kill($true); $ownedPlayer.process.WaitForExit()
            $errors.Add("Owned Player exceeded timeout of $PlayerTimeoutSeconds seconds")
        }
        $ended = [DateTime]::UtcNow.ToString('o'); $playerExit = $ownedPlayer.process.ExitCode; SaveOwned $ownedPlayer
        if ($playerExit -ne 0) { $errors.Add("Player exit $playerExit") }
        if (-not $pm.process.HasExited) { [void]$pm.process.WaitForExit(10000) }
    }
} catch { $errors.Add($_.Exception.ToString()) }
finally {
    if ($ownedPlayer -and -not $ownedPlayer.process.HasExited) { $ownedPlayer.process.Kill($true); $ownedPlayer.process.WaitForExit(); SaveOwned $ownedPlayer }
    if ($pm) {
        if (-not $pm.process.HasExited) {
            $stop = StartOwned $presentMonPath @('--terminate_existing_session','--session_name',$sessionName,'--no_csv','--no_console_stats') 'presentmon-stop'
            if (-not $stop.process.WaitForExit(5000)) { $stop.process.Kill(); $stop.process.WaitForExit() }
            SaveOwned $stop
            if (-not $pm.process.WaitForExit(5000)) { $pm.process.Kill(); $pm.process.WaitForExit(); $errors.Add('PresentMon did not stop cleanly') }
        }
        $pmExit = $pm.process.ExitCode; SaveOwned $pm
    }
    if ($wprStarted) {
        $stop = WprCommand @('-stop',$etl) 'wpr-stop'
        if ($stop.exitCode -ne 0) { $errors.Add("WPR stop failed (exit $($stop.exitCode)): $($stop.text)") }
    }
}
if (-not $ProbeOnly -and $processId -and (Test-Path -LiteralPath $csv)) {
    $analysisArgs = @($analyzer,'--presentmon-csv',$csv,'--process-id',"$processId",'--local-utc-offset-minutes',"$([int][TimeZoneInfo]::Local.GetUtcOffset([DateTime]::Now).TotalMinutes)",'--output',$outputRoot)
    if ($WarmupReceipt) { $analysisArgs += @('--warmup-receipt',$WarmupReceipt) }
    if ($Markers) { $analysisArgs += @('--markers',$Markers) }
    if ($TargetFrameMilliseconds -gt 0) { $analysisArgs += @('--target-ms',$TargetFrameMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture)) }
    & $Python @analysisArgs *> (Join-Path $outputRoot 'analyzer.log')
    $analyzerExit = $LASTEXITCODE
    if ($analyzerExit -ne 0) { $errors.Add("Analyzer exit $analyzerExit") }
}
if (-not $ProbeOnly) {
    if ($pmExit -ne 0) { $errors.Add("PresentMon capture exit $pmExit") }
    foreach ($required in @($WarmupReceipt,$BenchmarkReceipt,$Markers,$BuildManifest,$csv)) {
        if (-not $required -or -not (Test-Path -LiteralPath $required -PathType Leaf)) { $errors.Add("Required artifact missing: $required") }
    }
}
if ($CaptureEtw -and -not (Test-Path -LiteralPath $etl)) { $errors.Add('Required GPU ETL unavailable') }
$buildFiles = if ($ProbeOnly) { @() } else { @(Get-ChildItem -LiteralPath (Split-Path -Parent $playerPath) -Recurse -File | Where-Object { $_.FullName -notlike "$outputRoot*" } | Sort-Object FullName | ForEach-Object { FileRecord $_.FullName }) }
$artifacts = [ordered]@{presentMonCsv=(FileRecord $csv); presentMonSummary=(FileRecord (Join-Path $outputRoot 'presentmon-summary.json')); etl=(FileRecord $etl); warmupReceipt=(FileRecord $WarmupReceipt); benchmarkReceipt=(FileRecord $BenchmarkReceipt); markers=(FileRecord $Markers); buildManifest=(FileRecord $BuildManifest)}
$logIndex = [Array]::IndexOf($PlayerArguments, '-logFile')
if ($logIndex -ge 0 -and $logIndex + 1 -lt $PlayerArguments.Count) { $artifacts.playerLog = FileRecord $PlayerArguments[$logIndex + 1] }
if ($WarmupReceipt -and (Test-Path -LiteralPath $WarmupReceipt)) {
    try { $warmupData = Get-Content -Raw -LiteralPath $WarmupReceipt | ConvertFrom-Json; $artifacts.warmupPlan = FileRecord $warmupData.planFile }
    catch { $errors.Add("Warmup receipt JSON could not be read: $_") }
}
$logs = @(Get-ChildItem -LiteralPath $outputRoot -Filter '*.log' | ForEach-Object { FileRecord $_.FullName })
$identity = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$manifest = [ordered]@{
    schemaVersion=2; generatedUtc=[DateTime]::UtcNow.ToString('o'); sessionId=$sessionName
    completed=($errors.Count -eq 0 -and -not $ProbeOnly -and $analyzerExit -eq 0); probeOnly=[bool]$ProbeOnly; errors=@($errors)
    cache=@{process='process-cold'; driver='unknown-preserved'; globalCachesModified=$false}
    privilege=@{isAdministrator=$identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}
    target=@{player=$playerPath; playerSha256=(FileRecord $playerPath).sha256; arguments=$PlayerArguments; processId=$processId; startedUtc=$started; endedUtc=$ended; exitCode=$playerExit; buildFiles=$buildFiles}
    tools=@{presentMon=(FileRecord $presentMonPath); presentMonFileVersion=(Get-Item -LiteralPath $presentMonPath).VersionInfo.FileVersion; presentMonArguments=$pmArgs; presentMonExitCode=$pmExit; wpr=(FileRecord $wpr); wprCommands=@($commands); etwProfile=$(if ($CaptureEtw) {'GPU (built-in, file mode)'} else {'disabled'}); analyzer=(FileRecord $analyzer); analyzerExitCode=$analyzerExit; wrapper=(FileRecord $PSCommandPath)}
    environment=@{operatingSystem=(Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber); processor=(Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name); gpuDevices=@(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,PNPDeviceID); processSnapshotBefore=$interference; interference='Uncontrolled external applications; process snapshot retained; clocks and vendor cache state not controlled'}
    artifacts=$artifacts; logs=$logs
}
$manifest | ConvertTo-Json -Depth 15 | Set-Content (Join-Path $outputRoot 'windows-evidence-manifest.json') -Encoding utf8NoBOM
if ($errors.Count) { throw ($errors -join [Environment]::NewLine) }
