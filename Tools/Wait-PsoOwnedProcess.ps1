#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$BudgetPath,
    [string]$LogPath = '',
    [ValidateRange(1, 86400)][int]$MaximumSeconds = 7200
)

# Called only for the Process returned by this stage's Start-Process, while the
# caller holds Local\CodexR9700VNextUnityGpu. Never search for a process to kill.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PsoProcessOwnership.ps1')
$samplesPath = Join-Path $EvidenceDirectory 'capacity.jsonl'
$processSamplesPath = Join-Path $EvidenceDirectory 'descendants.jsonl'
$resultPath = Join-Path $EvidenceDirectory 'process.json'
if ((Test-Path -LiteralPath $samplesPath) -or (Test-Path -LiteralPath $resultPath)) {
    throw 'Use a new process evidence directory.'
}
$started = [DateTime]::UtcNow
$record = [ordered]@{
    processId = $Process.Id
    processStartedUtc = $Process.StartTime.ToUniversalTime().ToString('o')
    monitorStartedUtc = $started.ToString('o')
    minimumFreeGiBReserve = 20
    stopThresholdGiB = 25
    maximumSeconds = $MaximumSeconds
    stopReason = $null
    closeRequested = $false
    killedOwnedTree = $false
    exitCode = $null
    conflictingWorkloads = @()
}
$roots = @(@($BudgetPath, $EvidenceDirectory, [IO.Path]::GetTempPath()) | ForEach-Object {
    [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($_))
} | Sort-Object -Unique)
$nextProgress = $started
$descendants = @{}
$moduleEvidenceSaved = $false
try {
    while (-not $Process.HasExited) {
        $now = [DateTime]::UtcNow
        $volumes = @($roots | ForEach-Object {
            @{ volume = $_; freeGiB = ([IO.DriveInfo]::new($_)).AvailableFreeSpace / 1GB }
        })
        $sample = @{ utc = $now.ToString('o'); processId = $Process.Id; volumes = $volumes }
        $sample | ConvertTo-Json -Depth 4 -Compress | Add-Content -LiteralPath $samplesPath -Encoding utf8
        $processes = @(Get-CimInstance Win32_Process)
        if (-not $moduleEvidenceSaved -and $Process.ProcessName -match '^(BoatAttack|UrpExternal)') {
            try {
                $modules = @($Process.Modules | Where-Object { $_.ModuleName -in @('GameAssembly.dll','UnityPlayer.dll') } | ForEach-Object {
                    @{ name = $_.ModuleName; path = $_.FileName; baseAddress = $_.BaseAddress.ToInt64(); bytes = $_.ModuleMemorySize }
                })
                if ($modules.Count -eq 2) {
                    $modules | ConvertTo-Json | Set-Content (Join-Path $EvidenceDirectory 'native-module-ranges.json') -Encoding utf8
                    $moduleEvidenceSaved = $true
                }
            } catch { } # Optional symbolization evidence; never changes workload eligibility.
        }
        $ownership = Get-PsoOwnedProcessSnapshot -RootProcessId $Process.Id -RootStartedUtc $record.processStartedUtc `
            -Processes $processes -ObservedDescendants $descendants
        $ownerIds = $ownership.ProcessIds
        @{ utc = $now.ToString('o'); descendants = @($descendants.Values) } |
            ConvertTo-Json -Depth 5 -Compress | Add-Content -LiteralPath $processSamplesPath -Encoding utf8
        # A non-cooperating external native workload can start after the stage's
        # mutex/process preflight. Stop our own process, never the external one.
        $conflicts = @($processes | Where-Object {
            $nativeWorkload = $_.Name -match '^(Unity|UnityShaderCompiler|bee_backend|il2cpp|cl|link|lld-link|clang.*|PresentMon.*|ffmpeg|obs64|BoatAttack.*|UrpExternal.*|Megacity.*|Forest.*|SUMMIT.*|DataLayout.*|ShaderHitch.*)(\.exe)?$'
            $managedClient = $_.Name -match '^(dotnet|MSBuild|VBCSCompiler|csc)(\.exe)?$' -and
                $_.CommandLine -notmatch '(VBCSCompiler\.(dll|exe)|MSBuild\.dll.* /nodemode:1\b)'
            ($nativeWorkload -or $managedClient) -and -not $ownerIds.Contains([int]$_.ProcessId)
        } | Select-Object ProcessId, ParentProcessId, Name, ExecutablePath, CreationDate)
        $ownedProgress = @($processes | Where-Object { $ownerIds.Contains([int]$_.ProcessId) } | ForEach-Object {
            @{ processId = $_.ProcessId; name = $_.Name; created = $_.CreationDate;
                cpuSeconds = ([double]$_.KernelModeTime + [double]$_.UserModeTime) / 10000000;
                privateBytes = [double]$_.PrivatePageCount; readBytes = [double]$_.ReadTransferCount;
                writeBytes = [double]$_.WriteTransferCount }
        })
        @{ utc = $now.ToString('o'); scope = 'Build/process liveness, not PSO performance'; ownedProcesses = $ownedProgress } |
            ConvertTo-Json -Depth 5 -Compress | Add-Content (Join-Path $EvidenceDirectory 'process-progress.jsonl') -Encoding utf8
        if (@($volumes | Where-Object { $_.freeGiB -lt 25 }).Count) {
            $record.stopReason = 'Capacity approached the 20 GiB reserve (25 GiB early-stop threshold).'
        } elseif ($conflicts.Count) {
            $record.conflictingWorkloads = $conflicts
            $record.stopReason = 'An external native workload started during this stage; stop only the owned process.'
        } elseif (($now - $started).TotalSeconds -ge $MaximumSeconds) {
            $record.stopReason = 'Declared stage timeout.'
        }
        if ($record.stopReason) { break }
        if ($now -ge $nextProgress) {
            $tail = if ($LogPath -and (Test-Path -LiteralPath $LogPath)) {
                # Raw logs remain private evidence. Do not echo license tokens,
                # signed URLs or complete tool/process commands to progress output.
                $safe = @(Get-Content -LiteralPath $LogPath -Tail 20 | Where-Object {
                    $_ -match '^(\[\d+/\d+|\[PSO |\[ShaderHitchPipeline\]|Build completed|Building |Compiling |Linking )' -and
                    $_ -notmatch '(?i)(licens|serial number|https?://|command line|arguments:)'
                } | Select-Object -Last 1)
                if ($safe.Count) { $safe[0].Substring(0, [Math]::Min(220, $safe[0].Length)) }
                else { 'Raw log retained; no public progress marker in recent lines' }
            } else { 'Waiting for log creation' }
            Write-Output ("NATIVE_STAGE pid={0} freeGiB={1:N2} log={2}" -f $Process.Id,
                ($volumes.freeGiB | Measure-Object -Minimum).Minimum, $tail)
            $nextProgress = $now.AddSeconds(45)
        }
        [void]$Process.WaitForExit(5000)
    }
} catch {
    $record.stopReason = 'Monitor error: ' + $_.Exception.Message
    throw
} finally {
    if (-not $Process.HasExited) {
        # Cooperative process close first; if it cannot exit, terminate only the
        # process object started by this stage and its descendants. No cache cleanup.
        $record.closeRequested = $Process.CloseMainWindow()
        if (-not $Process.WaitForExit(10000)) {
            $Process.Kill($true)
            $record.killedOwnedTree = $true
            $Process.WaitForExit()
        }
    }
    $record.exitCode = $Process.ExitCode
    $record.observedDescendants = @($descendants.Values)
    $record.finishedUtc = [DateTime]::UtcNow.ToString('o')
    try {
        $record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding utf8
    } finally {
        # Closing the handle also prevents an exited process from lingering in a
        # subsequent Win32_Process snapshot as an apparent live workload.
        $Process.Dispose()
    }
}
if ($record.stopReason) { throw $record.stopReason }
if ($record.exitCode -ne 0) { throw "Owned native process exited $($record.exitCode)." }
