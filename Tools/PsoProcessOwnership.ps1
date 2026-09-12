# Pure snapshot classification. No process enumeration, execution or termination.
function Get-PsoOwnedProcessSnapshot {
    param(
        [Parameter(Mandatory = $true)][int]$RootProcessId,
        [Parameter(Mandatory = $true)][DateTime]$RootStartedUtc,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Processes,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$ObservedDescendants
    )

    $ownerIds = [Collections.Generic.HashSet[int]]::new()
    [void]$ownerIds.Add($RootProcessId)
    $rootTicks = $RootStartedUtc.ToUniversalTime().Ticks
    $ownerStarts = @{ $RootProcessId = $rootTicks }
    foreach ($candidate in $Processes) {
        $created = ([DateTime]$candidate.CreationDate).ToUniversalTime().Ticks
        $key = "$($candidate.ProcessId):$created"
        # An observed child can outlive its parent, but a reused PID alone does
        # not preserve ownership. Every retained identity includes its start time.
        if ($created -gt $rootTicks -and $ObservedDescendants.Contains($key)) {
            [void]$ownerIds.Add([int]$candidate.ProcessId)
            $ownerStarts[[int]$candidate.ProcessId] = $created
        }
    }
    do {
        $added = $false
        foreach ($candidate in $Processes) {
            $parentId = [int]$candidate.ParentProcessId
            $created = ([DateTime]$candidate.CreationDate).ToUniversalTime().Ticks
            # A process may retain the PID of a parent that exited long before
            # our compiler reused that PID. Require a real temporal parent edge.
            if ($ownerIds.Contains($parentId) -and $created -gt $ownerStarts[$parentId] -and
                $ownerIds.Add([int]$candidate.ProcessId)) {
                $key = "$($candidate.ProcessId):$created"
                $ObservedDescendants[$key] = $candidate | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CreationDate
                $ownerStarts[[int]$candidate.ProcessId] = $created
                $added = $true
            }
        }
    } while ($added)
    [pscustomobject]@{ ProcessIds = $ownerIds }
}
