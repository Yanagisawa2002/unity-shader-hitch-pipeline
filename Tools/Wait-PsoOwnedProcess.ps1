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
}
$roots = @(@($BudgetPath, $EvidenceDirectory, [IO.Path]::GetTempPath()) | ForEach-Object {
    [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($_))
} | Sort-Object -Unique)
$nextProgress = $started
$descendants = @{}
try {
    while (-not $Process.HasExited) {
        $now = [DateTime]::UtcNow
        $volumes = @($roots | ForEach-Object {
            @{ volume = $_; freeGiB = ([IO.DriveInfo]::new($_)).AvailableFreeSpace / 1GB }
        })
        $sample = @{ utc = $now.ToString('o'); processId = $Process.Id; volumes = $volumes }
        $sample | ConvertTo-Json -Depth 4 -Compress | Add-Content -LiteralPath $samplesPath -Encoding utf8
        $processes = @(Get-CimInstance Win32_Process)
        $ownerIds = [Collections.Generic.HashSet[int]]::new()
        [void]$ownerIds.Add($Process.Id)
        foreach ($candidate in $processes) {
            $key = "$($candidate.ProcessId):$($candidate.CreationDate.ToUniversalTime().Ticks)"
            if ($descendants.ContainsKey($key)) { [void]$ownerIds.Add($candidate.ProcessId) }
        }
        do {
            $added = $false
            foreach ($candidate in $processes) {
                if ($ownerIds.Contains($candidate.ParentProcessId) -and $ownerIds.Add($candidate.ProcessId)) {
                    $key = "$($candidate.ProcessId):$($candidate.CreationDate.ToUniversalTime().Ticks)"
                    $descendants[$key] = $candidate | Select-Object ProcessId, ParentProcessId, Name, ExecutablePath, CreationDate
                    $added = $true
                }
            }
        } while ($added)
        @{ utc = $now.ToString('o'); descendants = @($descendants.Values) } |
            ConvertTo-Json -Depth 5 -Compress | Add-Content -LiteralPath $processSamplesPath -Encoding utf8
        if (@($volumes | Where-Object { $_.freeGiB -lt 25 }).Count) {
            $record.stopReason = 'Capacity approached the 20 GiB reserve (25 GiB early-stop threshold).'
        } elseif (($now - $started).TotalSeconds -ge $MaximumSeconds) {
            $record.stopReason = 'Declared stage timeout.'
        }
        if ($record.stopReason) { break }
        if ($now -ge $nextProgress) {
            $tail = if ($LogPath -and (Test-Path -LiteralPath $LogPath)) {
                (Get-Content -LiteralPath $LogPath -Tail 2) -join ' | '
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
