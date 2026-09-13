#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Output,
    [Parameter(Mandatory = $true)][string]$BudgetPath,
    [ValidateRange(0, 100000)][double]$EstimatedAdditionalPeakGiB = 80,
    [Parameter(Mandatory = $true)][scriptblock]$Action
)

# Synchronous stage boundary, not a queue or benchmark runner. The action must
# wait for all its children. No process is killed and no cache is removed here.
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputRoot) { throw "Retain existing evidence; choose a new directory: $outputRoot" }
New-Item -ItemType Directory -Path $outputRoot | Out-Null

function Get-Workloads {
    param([ref]$IdleServers)
    $candidates = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(Unity|UnityShaderCompiler|bee_backend|il2cpp|MSBuild|dotnet|VBCSCompiler|csc|cl|link|lld-link|clang.*|PresentMon.*|Megacity.*|Forest.*|SUMMIT.*|DataLayout.*|ShaderHitch.*)(\.exe)?$'
    })
    # Resident Roslyn/MSBuild servers can outlive the build that created them.
    # Only exempt recognized services with zero CPU growth during observation.
    # Active clients and all other build/workload processes still block the stage.
    $servers = @{}
    foreach ($candidate in $candidates) {
        if ($candidate.Name -match '^(dotnet|VBCSCompiler)(\.exe)?$' -and
            $candidate.CommandLine -match '(VBCSCompiler\.(dll|exe)|MSBuild\.dll.* /nodemode:1\b)') {
            try {
                $instance = [Diagnostics.Process]::GetProcessById($candidate.ProcessId)
                $servers[$candidate.ProcessId] = @{ instance = $instance; cpu = $instance.TotalProcessorTime.Ticks }
            } catch { } # An uninspectable live process remains a blocker.
        }
    }
    if ($servers.Count) { Start-Sleep -Seconds 3 }
    $idle = @()
    $blocking = @()
    foreach ($candidate in $candidates) {
        if ($servers.ContainsKey($candidate.ProcessId)) {
            $service = $servers[$candidate.ProcessId]
            try {
                $service.instance.Refresh()
                if ($service.instance.HasExited) { continue }
                $cpuAfter = $service.instance.TotalProcessorTime.Ticks
                if ($cpuAfter -eq $service.cpu) {
                    $idle += @{ processId = $candidate.ProcessId; name = $candidate.Name;
                        executablePath = $candidate.ExecutablePath; created = $candidate.CreationDate;
                        observationSeconds = 3; cpuTicksBefore = $service.cpu; cpuTicksAfter = $cpuAfter }
                    continue
                }
            } catch { } finally { $service.instance.Dispose() }
        }
        $blocking += $candidate | Select-Object ProcessId, ParentProcessId, Name, ExecutablePath
    }
    $IdleServers.Value = $idle
    $blocking
}

function Get-Volumes {
    # Account for both the host/build volume and the default temporary volume.
    @(@($BudgetPath, $outputRoot, [IO.Path]::GetTempPath()) | ForEach-Object {
        [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($_))
    } | Sort-Object -Unique | ForEach-Object {
        $drive = [IO.DriveInfo]::new($_)
        @{ volume = $_; freeGiB = $drive.AvailableFreeSpace / 1GB }
    })
}

$record = [ordered]@{
    schemaVersion = 1
    startedUtc = [DateTime]::UtcNow.ToString('o')
    mutex = 'Local\CodexR9700VNextUnityGpu'
    mutexAcquired = $false
    mutexAbandoned = $false
    mutexReleased = $false
    minimumFreeGiBReserve = 20
    estimatedAdditionalPeakGiB = $EstimatedAdditionalPeakGiB
    requiredFreeGiBAtStart = 20 + $EstimatedAdditionalPeakGiB
    budgetScope = 'Peak is a caller estimate, free space is measured; this does not establish a native peak.'
    action = $Action.ToString()
    actionStarted = $false
    status = 'preflight'
    error = $null
}
$mutex = [Threading.Mutex]::new($false, $record.mutex)
try {
    try { $record.mutexAcquired = $mutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] {
        $record.mutexAcquired = $true
        $record.mutexAbandoned = $true
    }
    if (-not $record.mutexAcquired) { throw 'The shared build/workload mutex is occupied.' }
    $idleBefore = @()
    $record.processesBefore = @(Get-Workloads -IdleServers ([ref]$idleBefore))
    $record.idleBuildServersBefore = $idleBefore
    $record.volumesBefore = @(Get-Volumes)
    if ($record.processesBefore.Count) { throw 'A build/workload process is already running. Leave it untouched.' }
    foreach ($volume in $record.volumesBefore) {
        if ($volume.freeGiB -lt $record.requiredFreeGiBAtStart) {
            throw "Capacity gate: $($volume.volume) has $($volume.freeGiB) GiB; stage requires $($record.requiredFreeGiBAtStart) GiB (estimate plus 20 GiB reserve)."
        }
    }
    $record.actionStarted = $true
    # Native commands update the global automatic variable. A local value would
    # shadow failures inside the caller's action as well as this final check.
    $global:LASTEXITCODE = 0
    & $Action
    $record.actionNativeExitCode = $global:LASTEXITCODE
    if ($record.actionNativeExitCode -ne 0) { throw "Stage command failed with exit code $($record.actionNativeExitCode)." }
    $idleAfter = @()
    # Keep the snapshot that decided acceptance even if a short-lived conflicting
    # process exits before the separate finally/release snapshot.
    $record.processesAtActionCompletion = @(Get-Workloads -IdleServers ([ref]$idleAfter))
    $record.idleBuildServersAtActionCompletion = $idleAfter
    if ($record.processesAtActionCompletion.Count) { throw 'A workload is still present. Do not start another stage; inspect the retained process snapshot.' }
    $record.status = 'completed'
} catch {
    $record.status = if ($record.actionStarted) { 'failed' } else { 'rejected' }
    $record.error = $_.Exception.Message
    throw
} finally {
    try {
        $idleAfter = @()
        $record.processesAfter = @(Get-Workloads -IdleServers ([ref]$idleAfter))
        $record.idleBuildServersAfter = $idleAfter
        $record.volumesAfter = @(Get-Volumes)
    }
    finally {
        if ($record.mutexAcquired) {
            $mutex.ReleaseMutex()
            $record.mutexReleased = $true
        }
        $mutex.Dispose()
        $record.endedUtc = [DateTime]::UtcNow.ToString('o')
        $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot 'stage.json') -Encoding utf8
    }
}
