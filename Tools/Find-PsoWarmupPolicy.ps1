[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Player,
    [Parameter(Mandatory = $true)]
    [string]$Plan,
    [string]$Output = "PsoArtifacts\PolicySearch",
    [int[]]$WorkerCounts = @(1, 2, 4, 8),
    [int[]]$BatchSizes = @(4, 16, 64),
    [ValidateRange(1, 20)]
    [int]$Repetitions = 2,
    [ValidateRange(30, 100000)]
    [int]$SampleFrames = 30,
    [ValidateRange(10, 3600)]
    [int]$TrialTimeoutSeconds = 120,
    [ValidateRange(0.01, 10000.0)]
    [double]$TargetFrameMilliseconds = 16.67
)

$ErrorActionPreference = "Stop"
$playerPath = (Resolve-Path -LiteralPath $Player).Path
$planPath = (Resolve-Path -LiteralPath $Plan).Path
$outputRoot = [System.IO.Path]::GetFullPath($Output)
if ($WorkerCounts.Count -eq 0 -or $BatchSizes.Count -eq 0) {
    throw "WorkerCounts and BatchSizes must each contain at least one value."
}
if ($WorkerCounts.Where({ $_ -lt 1 }).Count -gt 0 -or
    $BatchSizes.Where({ $_ -lt 1 }).Count -gt 0) {
    throw "Worker counts and batch sizes must be positive."
}

$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$runRoot = Join-Path $outputRoot $stamp
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

function Get-Median([double[]]$Values) {
    if ($Values.Count -eq 0) {
        return 0.0
    }
    $ordered = @($Values | Sort-Object)
    $middle = [Math]::Floor($ordered.Count / 2)
    if (($ordered.Count % 2) -eq 1) {
        return [double]$ordered[$middle]
    }
    return ([double]$ordered[$middle - 1] + [double]$ordered[$middle]) / 2.0
}

function Test-Dominates([pscustomobject]$Left, [pscustomobject]$Right) {
    $notWorse =
        $Left.MedianWarmupMilliseconds -le $Right.MedianWarmupMilliseconds -and
        $Left.MedianWorstWarmupFrameMilliseconds -le
            $Right.MedianWorstWarmupFrameMilliseconds -and
        $Left.MedianWarmupHitchFrames -le $Right.MedianWarmupHitchFrames
    $strictlyBetter =
        $Left.MedianWarmupMilliseconds -lt $Right.MedianWarmupMilliseconds -or
        $Left.MedianWorstWarmupFrameMilliseconds -lt
            $Right.MedianWorstWarmupFrameMilliseconds -or
        $Left.MedianWarmupHitchFrames -lt $Right.MedianWarmupHitchFrames
    return $notWorse -and $strictlyBetter
}

$runs = [System.Collections.Generic.List[object]]::new()
function Add-FailedTrial(
    [int]$Sequence,
    [int]$Repetition,
    [int]$Workers,
    [int]$Batch,
    [string]$Error
) {
    $runs.Add([pscustomobject]@{
        Sequence = $Sequence
        Repetition = $Repetition
        WorkerCount = $Workers
        BatchSize = $Batch
        Succeeded = $false
        Error = $Error
        WarmupMilliseconds = 0.0
        WorstWarmupFrameMilliseconds = 0.0
        WarmupHitchFrames = 0
        DeadlineMissed = $true
        RuntimeP99Milliseconds = 0.0
        RuntimeHitchFrames = 0
        WarmupReceipt = ""
        BenchmarkReceipt = ""
    })
}

$sequence = 0
for ($repetition = 0; $repetition -lt $Repetitions; $repetition++) {
    # Rotate both axes between repetitions so a warmed driver cache cannot always
    # favor the same candidate order.
    $orderedWorkers = @(
        for ($index = 0; $index -lt $WorkerCounts.Count; $index++) {
            $WorkerCounts[($index + $repetition) % $WorkerCounts.Count]
        }
    )
    $batchOffset = ($Repetitions - 1 - $repetition) % $BatchSizes.Count
    $orderedBatches = @(
        for ($index = 0; $index -lt $BatchSizes.Count; $index++) {
            $BatchSizes[($index + $batchOffset) % $BatchSizes.Count]
        }
    )

    foreach ($workers in $orderedWorkers) {
        foreach ($batch in $orderedBatches) {
            $sequence++
            $candidateName = "w$workers-b$batch-r$($repetition + 1)"
            $candidateRoot = Join-Path $runRoot $candidateName
            $warmupReceipt = Join-Path $candidateRoot "warmup.json"
            $benchmarkReceipt = Join-Path $candidateRoot "benchmark.json"
            $log = Join-Path $candidateRoot "player.log"
            New-Item -ItemType Directory -Path $candidateRoot -Force | Out-Null

            $arguments = @(
                "-screen-fullscreen", "0",
                "-screen-width", "640",
                "-screen-height", "360",
                "-pso-output", $candidateRoot,
                "-pso-warmup-plan", $planPath,
                "-pso-warmup-strategy", "scheduled",
                "-pso-warmup-receipt", $warmupReceipt,
                "-pso-warmup-initial-batch", $batch.ToString(),
                "-pso-warmup-minimum-batch", $batch.ToString(),
                "-pso-warmup-maximum-batch", $batch.ToString(),
                "-max-async-pso-job-count", $workers.ToString(),
                "-pso-benchmark",
                "-pso-benchmark-mode", "scheduled",
                "-pso-benchmark-frames", $SampleFrames.ToString(),
                "-pso-benchmark-discard-frames", "0",
                "-pso-benchmark-delay-seconds", "0",
                "-pso-hitch-threshold-ms",
                    $TargetFrameMilliseconds.ToString(
                        [Globalization.CultureInfo]::InvariantCulture),
                "-pso-benchmark-report", $benchmarkReceipt,
                "-logFile", $log
            )
            Write-Host "[$sequence] workers=$workers batch=$batch repetition=$($repetition + 1)"
            $process = Start-Process `
                -FilePath $playerPath `
                -ArgumentList (ConvertTo-ArgumentLine $arguments) `
                -WorkingDirectory (Split-Path -Parent $playerPath) `
                -PassThru `
                -WindowStyle Hidden
            if (-not $process.WaitForExit($TrialTimeoutSeconds * 1000)) {
                [void]$process.CloseMainWindow()
                if (-not $process.WaitForExit(3000)) {
                    $process.Kill()
                    $process.WaitForExit()
                }
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Timed out after $TrialTimeoutSeconds seconds."
                Write-Warning "$candidateName timed out; continuing search."
                continue
            }
            if ($process.ExitCode -ne 0) {
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Player exit code $($process.ExitCode). See $log"
                continue
            }
            if (-not (Test-Path -LiteralPath $warmupReceipt) -or
                -not (Test-Path -LiteralPath $benchmarkReceipt)) {
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Candidate did not emit both receipts."
                continue
            }

            $warmup = Get-Content -Raw -LiteralPath $warmupReceipt | ConvertFrom-Json
            $benchmark = Get-Content -Raw -LiteralPath $benchmarkReceipt | ConvertFrom-Json
            if (-not $warmup.completed -or -not $benchmark.completed) {
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Candidate emitted an incomplete receipt."
                continue
            }
            $phases = @($warmup.phases)
            $warmupHitches = ($phases | ForEach-Object {
                [int]$_.warmupFrameTimes.hitchFrameCount
            } | Measure-Object -Sum).Sum
            $worstFrame = ($phases | ForEach-Object {
                [double]$_.warmupFrameTimes.maximumMilliseconds
            } | Measure-Object -Maximum).Maximum
            $deadlineMissed = ($phases | Where-Object { $_.deadlineMissed }).Count -gt 0
            $runs.Add([pscustomobject]@{
                Sequence = $sequence
                Repetition = $repetition + 1
                WorkerCount = [int]$workers
                BatchSize = [int]$batch
                Succeeded = $true
                Error = ""
                WarmupMilliseconds = [double]$warmup.elapsedMilliseconds
                WorstWarmupFrameMilliseconds = [double]$worstFrame
                WarmupHitchFrames = [int]$warmupHitches
                DeadlineMissed = [bool]$deadlineMissed
                RuntimeP99Milliseconds = [double]$benchmark.frameTimes.p99Milliseconds
                RuntimeHitchFrames = [int]$benchmark.frameTimes.hitchFrameCount
                WarmupReceipt = $warmupReceipt
                BenchmarkReceipt = $benchmarkReceipt
            })
        }
    }
}

$successfulRuns = @($runs | Where-Object { $_.Succeeded })
if ($successfulRuns.Count -eq 0) {
    throw "Every policy-search trial failed. Inspect $runRoot."
}
$aggregates = [System.Collections.Generic.List[object]]::new()
foreach ($group in ($successfulRuns | Group-Object WorkerCount, BatchSize)) {
    $items = @($group.Group)
    $allCandidateRuns = @($runs | Where-Object {
        $_.WorkerCount -eq $items[0].WorkerCount -and
        $_.BatchSize -eq $items[0].BatchSize
    })
    $aggregates.Add([pscustomobject]@{
        WorkerCount = [int]$items[0].WorkerCount
        BatchSize = [int]$items[0].BatchSize
        TrialCount = $items.Count
        FailedTrialCount = @($allCandidateRuns | Where-Object {
            -not $_.Succeeded
        }).Count
        MedianWarmupMilliseconds = Get-Median @(
            $items | ForEach-Object { [double]$_.WarmupMilliseconds })
        MedianWorstWarmupFrameMilliseconds = Get-Median @(
            $items | ForEach-Object { [double]$_.WorstWarmupFrameMilliseconds })
        MedianWarmupHitchFrames = Get-Median @(
            $items | ForEach-Object { [double]$_.WarmupHitchFrames })
        MedianRuntimeP99Milliseconds = Get-Median @(
            $items | ForEach-Object { [double]$_.RuntimeP99Milliseconds })
        AnyDeadlineMissed = ($items | Where-Object { $_.DeadlineMissed }).Count -gt 0
        ParetoOptimal = $false
    })
}

foreach ($candidate in $aggregates) {
    $dominated = $false
    foreach ($other in $aggregates) {
        if ($candidate -ne $other -and (Test-Dominates $other $candidate)) {
            $dominated = $true
            break
        }
    }
    $candidate.ParetoOptimal = -not $dominated
}

$frameLimit = $TargetFrameMilliseconds * 1.05
$eligible = @($aggregates | Where-Object {
    $_.ParetoOptimal -and
    $_.FailedTrialCount -eq 0 -and
    -not $_.AnyDeadlineMissed -and
    $_.MedianWorstWarmupFrameMilliseconds -le $frameLimit
})
if ($eligible.Count -eq 0) {
    $eligible = @($aggregates | Where-Object {
        $_.ParetoOptimal -and
        $_.FailedTrialCount -eq 0 -and
        -not $_.AnyDeadlineMissed
    })
}
if ($eligible.Count -eq 0) {
    $eligible = @($aggregates | Where-Object { $_.ParetoOptimal })
}
$bestEligibleWarmup = [double](
    $eligible |
        Measure-Object -Property MedianWarmupMilliseconds -Minimum
).Minimum
$nearFastWarmupLimit = $bestEligibleWarmup * 1.02
$nearFast = @($eligible | Where-Object {
    [double]$_.MedianWarmupMilliseconds -le $nearFastWarmupLimit
})
$recommended = $nearFast | Sort-Object `
    FailedTrialCount,
    MedianWarmupHitchFrames,
    MedianWorstWarmupFrameMilliseconds,
    MedianWarmupMilliseconds,
    WorkerCount,
    BatchSize | Select-Object -First 1

$runs | Export-Csv -LiteralPath (Join-Path $runRoot "trials.csv") -NoTypeInformation
$aggregates | Export-Csv `
    -LiteralPath (Join-Path $runRoot "candidates.csv") `
    -NoTypeInformation

$recommendation = [ordered]@{
    schemaVersion = 2
    generatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    player = $playerPath
    plan = $planPath
    workerCount = [int]$recommended.WorkerCount
    batchSize = [int]$recommended.BatchSize
    medianWarmupMilliseconds = [double]$recommended.MedianWarmupMilliseconds
    medianWorstWarmupFrameMilliseconds =
        [double]$recommended.MedianWorstWarmupFrameMilliseconds
    medianWarmupHitchFrames = [double]$recommended.MedianWarmupHitchFrames
    targetFrameMilliseconds = $TargetFrameMilliseconds
    nearFastTolerancePercent = 2.0
    selectionRule =
        "Pareto frontier; meet deadline and 105% frame guard when possible; within 2% of the fastest eligible warmup, minimize hitch count and worst-frame pressure before elapsed time."
}
$recommendationPath = Join-Path $runRoot "recommendation.json"
$recommendation | ConvertTo-Json -Depth 6 | Set-Content `
    -LiteralPath $recommendationPath `
    -Encoding utf8NoBOM

$searchReceipt = [ordered]@{
    schemaVersion = 2
    generatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    player = $playerPath
    plan = $planPath
    targetFrameMilliseconds = $TargetFrameMilliseconds
    repetitions = $Repetitions
    workerCounts = $WorkerCounts
    batchSizes = $BatchSizes
    trials = $runs
    candidates = $aggregates
    recommendation = $recommendation
    caveats = @(
        "Candidates are rotated between repetitions to reduce run-order bias.",
        "Driver-level PSO caches are implementation-owned and cannot be portably cleared by this tool.",
        "Re-run on every target GPU, driver image, graphics API, and materially different shader build."
    )
}
$searchReceipt | ConvertTo-Json -Depth 10 | Set-Content `
    -LiteralPath (Join-Path $runRoot "policy-search.json") `
    -Encoding utf8NoBOM

$markdownRows = @($aggregates | Sort-Object `
    MedianWarmupMilliseconds,
    MedianWorstWarmupFrameMilliseconds | ForEach-Object {
        $pareto = if ($_.ParetoOptimal) { "yes" } else { "no" }
        $deadline = if ($_.AnyDeadlineMissed) { "missed" } else { "met" }
        "| $($_.WorkerCount) | $($_.BatchSize) | " +
        "$([double]$_.MedianWarmupMilliseconds) ms | " +
        "$([double]$_.MedianWorstWarmupFrameMilliseconds) ms | " +
        "$([double]$_.MedianWarmupHitchFrames) | $deadline | $pareto |"
    })
$markdown = @"
# PSO warmup policy search

Recommended: **$($recommended.WorkerCount) workers / batch $($recommended.BatchSize)**

| Workers | Batch | Median warmup | Median worst frame | Warmup hitch frames | Deadline | Pareto |
|---:|---:|---:|---:|---:|---|---|
$($markdownRows -join [Environment]::NewLine)

The search rotates candidate order and reports the Pareto frontier. It treats
elapsed times within 2% of the fastest eligible candidate as a noise-equivalent
band, then prefers fewer hitches and a lower worst warmup frame. Driver-level
pipeline caches are implementation-owned and are not deleted by this tool; repeat
the search on every controlled target GPU/driver image and material shader build.
"@
$markdown | Set-Content `
    -LiteralPath (Join-Path $runRoot "policy-search.md") `
    -Encoding utf8NoBOM

Write-Host "Recommended workers=$($recommended.WorkerCount), batch=$($recommended.BatchSize)"
Write-Host "Recommendation: $recommendationPath"
