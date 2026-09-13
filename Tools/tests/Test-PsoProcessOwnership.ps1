# Synthetic process records exercise the production classifier. No real process
# APIs, native workloads, sleeps or performance measurements are used.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../PsoProcessOwnership.ps1')
$epoch = [DateTime]::Parse('2026-09-12T15:00:00Z').ToUniversalTime()
function New-Record([int]$Id, [int]$Parent, [int]$Seconds) {
    [pscustomobject]@{ProcessId=$Id;ParentProcessId=$Parent;CreationDate=$epoch.AddSeconds($Seconds);Name='fixture';ExecutablePath='fixture'}
}
function Assert-Ids($Snapshot, [int[]]$Expected) {
    $actual = @($Snapshot.ProcessIds | Sort-Object)
    if (($actual -join ',') -ne (($Expected | Sort-Object) -join ',')) {
        throw "Unexpected owned IDs: $($actual -join ','); expected $($Expected -join ',')"
    }
}
$history = @{}
# Unsorted children need transitive discovery. The unrelated old process keeps
# a ParentProcessId later reused by our compiler, reproducing the native finding.
$first = @((New-Record 400 300 40), (New-Record 900 200 -60),
    (New-Record 300 200 30), (New-Record 200 100 20),
    (New-Record 800 200 10), (New-Record 700 100 -10), (New-Record 100 1 0))
$snapshot = Get-PsoOwnedProcessSnapshot -RootProcessId 100 -RootStartedUtc $epoch -Processes $first -ObservedDescendants $history
Assert-Ids $snapshot @(100,200,300,400)
if ($history.Count -ne 3) { throw 'Unrelated reused-parent records entered retained history.' }
# An observed child remains owned after its parent exits. A new process reusing
# the exited parent's PID and its children remain external.
$second = @((New-Record 300 200 30), (New-Record 400 300 40),
    (New-Record 500 300 50), (New-Record 200 1 60), (New-Record 600 200 70))
$snapshot = Get-PsoOwnedProcessSnapshot -RootProcessId 100 -RootStartedUtc $epoch -Processes $second -ObservedDescendants $history
Assert-Ids $snapshot @(100,300,400,500)
# Equal creation times are not sufficient to prove a parent edge.
$snapshot = Get-PsoOwnedProcessSnapshot -RootProcessId 100 -RootStartedUtc $epoch -Processes @((New-Record 200 100 0)) -ObservedDescendants @{}
Assert-Ids $snapshot @(100)
$snapshot = Get-PsoOwnedProcessSnapshot -RootProcessId 100 -RootStartedUtc $epoch -Processes @() -ObservedDescendants @{}
Assert-Ids $snapshot @(100)
Write-Output 'PSO_PROCESS_OWNERSHIP_OK scenarios=4 measurementStatus=Unmeasured'
