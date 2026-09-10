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
    @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(Unity|UnityShaderCompiler|bee_backend|il2cpp|MSBuild|dotnet|VBCSCompiler|cl|link|lld-link|clang.*|PresentMon.*|Megacity.*|SUMMIT.*|DataLayout.*|ShaderHitch.*)(\.exe)?$'
    } | Select-Object ProcessId, ParentProcessId, Name, ExecutablePath)
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
    $record.processesBefore = @(Get-Workloads)
    $record.volumesBefore = @(Get-Volumes)
    if ($record.processesBefore.Count) { throw 'A build/workload process is already running. Leave it untouched.' }
    foreach ($volume in $record.volumesBefore) {
        if ($volume.freeGiB -lt $record.requiredFreeGiBAtStart) {
            throw "Capacity gate: $($volume.volume) has $($volume.freeGiB) GiB; stage requires $($record.requiredFreeGiBAtStart) GiB (estimate plus 20 GiB reserve)."
        }
    }
    $record.actionStarted = $true
    $LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Stage command failed with exit code $LASTEXITCODE." }
    $record.processesAfter = @(Get-Workloads)
    if ($record.processesAfter.Count) { throw 'A workload is still present. Do not start another stage; inspect the retained process snapshot.' }
    $record.status = 'completed'
} catch {
    $record.status = if ($record.actionStarted) { 'failed' } else { 'rejected' }
    $record.error = $_.Exception.Message
    throw
} finally {
    try { $record.volumesAfter = @(Get-Volumes) }
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
