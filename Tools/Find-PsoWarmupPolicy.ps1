[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Player,
    [Parameter(Mandatory = $true)]
    [string]$Plan,
    [string]$Output = "PsoArtifacts\PolicySearch",
    [int[]]$WorkerCounts = @(1, 2, 4, 8),
    [int[]]$BatchSizes = @(4, 16, 64),
    [ValidateRange(1, 1000000)]
    [int]$BootstrapBatchSize = 1,
    [ValidateRange(0.0, 10000.0)]
    [double]$BudgetSafetyMarginMilliseconds = 2.0,
    [ValidateRange(1.0, 100.0)]
    [double]$BudgetCostSafetyMultiplier = 1.5,
    [ValidateRange(0, 1000000)]
    [int]$BudgetCooldownFrames = 8,
    [ValidateRange(0, 1024)]
    [int]$ReservedForegroundProcessorCount = 2,
    [ValidateRange(1, 20)]
    [int]$Repetitions = 2,
    [ValidateRange(960, 100000)]
    [int]$SampleFrames = 1080,
    [ValidateRange(10, 3600)]
    [int]$TrialTimeoutSeconds = 120,
    [ValidateRange(0.01, 10000.0)]
    [double]$TargetFrameMilliseconds = 16.67,
    [string]$Scenario = "",
    [string]$ScenarioPhase = "",
    [ValidateRange(0.0, 3600.0)]
    [double]$BenchmarkDelaySeconds = 3.0,
    [ValidateRange(64, 16384)]
    [int]$ScreenWidth = 1280,
    [ValidateRange(64, 16384)]
    [int]$ScreenHeight = 720,
    [ValidateSet("auto", "progressive", "native-async-bulk")]
    [string]$DeadlineBackendMode = "auto"
)

$ErrorActionPreference = "Stop"
$playerPath = (Resolve-Path -LiteralPath $Player).Path
$planPath = (Resolve-Path -LiteralPath $Plan).Path
$outputRoot = [System.IO.Path]::GetFullPath($Output)
$logicalProcessorCount = [Environment]::ProcessorCount
$maximumSelectableWorkerCount = [Math]::Max(
    1,
    $logicalProcessorCount - $ReservedForegroundProcessorCount)
$scenarioGated = -not [string]::IsNullOrWhiteSpace($Scenario)
$scenarioPhaseSet = -not [string]::IsNullOrWhiteSpace($ScenarioPhase)
if ($scenarioGated -ne $scenarioPhaseSet) {
    throw "Scenario and ScenarioPhase must either both be set or both be empty."
}
$policyPlayerAffinityMask = [int64]0
if ($env:OS -eq "Windows_NT" -and
    $logicalProcessorCount -ge 4 -and
    $logicalProcessorCount -le 62) {
    $allLogicalProcessorsMask =
        ([uint64]1 -shl $logicalProcessorCount) - [uint64]1
    $reservedRecorderMask =
        [uint64]1 -shl ($logicalProcessorCount - 1)
    $policyPlayerAffinityMask =
        [int64]($allLogicalProcessorsMask -bxor $reservedRecorderMask)
}
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

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    if ($Values.Count -eq 0) {
        return 0.0
    }
    $ordered = @($Values | Sort-Object)
    $position = ($ordered.Count - 1) * $Percentile
    $lower = [Math]::Floor($position)
    $upper = [Math]::Ceiling($position)
    if ($lower -eq $upper) {
        return [double]$ordered[$lower]
    }
    $weight = $position - $lower
    return [double]$ordered[$lower] * (1.0 - $weight) +
        [double]$ordered[$upper] * $weight
}

function Test-Dominates([pscustomobject]$Left, [pscustomobject]$Right) {
    $notWorse =
        $Left.MedianBudgetViolations -le $Right.MedianBudgetViolations -and
        $Left.MedianWarmupMilliseconds -le $Right.MedianWarmupMilliseconds -and
        $Left.MedianWorstWarmupFrameMilliseconds -le
            $Right.MedianWorstWarmupFrameMilliseconds -and
        $Left.MedianWarmupHitchFrames -le $Right.MedianWarmupHitchFrames -and
        $Left.MedianRuntimeHitchFrames -le $Right.MedianRuntimeHitchFrames -and
        $Left.MedianRuntimeP99Milliseconds -le
            $Right.MedianRuntimeP99Milliseconds
    $strictlyBetter =
        $Left.MedianBudgetViolations -lt $Right.MedianBudgetViolations -or
        $Left.MedianWarmupMilliseconds -lt $Right.MedianWarmupMilliseconds -or
        $Left.MedianWorstWarmupFrameMilliseconds -lt
            $Right.MedianWorstWarmupFrameMilliseconds -or
        $Left.MedianWarmupHitchFrames -lt $Right.MedianWarmupHitchFrames -or
        $Left.MedianRuntimeHitchFrames -lt $Right.MedianRuntimeHitchFrames -or
        $Left.MedianRuntimeP99Milliseconds -lt
            $Right.MedianRuntimeP99Milliseconds
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
        HardFrameBudgetMet = $false
        SchedulerAdmissionBudgetMet = $false
        BudgetViolations = 0
        MinimumBatchBudgetViolations = 0
        DeadlineMissed = $true
        ScenarioGated = $scenarioGated
        ScenarioGcClean = $false
        CacheMissFree = $false
        RuntimeP99Milliseconds = 0.0
        RuntimeHitchFrames = 0
        WorkerHeadroomMet = $Workers -le $maximumSelectableWorkerCount
        WarmupReceipt = ""
        BenchmarkReceipt = ""
        ScenarioReceipt = ""
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
            $scenarioReceipt = Join-Path $candidateRoot "scenario.json"
            $log = Join-Path $candidateRoot "player.log"
            New-Item -ItemType Directory -Path $candidateRoot -Force | Out-Null

            $arguments = @(
                "-screen-fullscreen", "0",
                "-screen-width", $ScreenWidth.ToString(),
                "-screen-height", $ScreenHeight.ToString(),
                "-pso-output", $candidateRoot,
                "-pso-warmup-plan", $planPath,
                "-pso-warmup-strategy", "scheduled",
                "-pso-warmup-receipt", $warmupReceipt,
                "-pso-warmup-initial-batch", $batch.ToString(),
                "-pso-warmup-minimum-batch", "1",
                "-pso-warmup-maximum-batch", $batch.ToString(),
                "-pso-warmup-bootstrap-batch", $BootstrapBatchSize.ToString(),
                "-pso-warmup-budget-safety-margin-ms",
                    $BudgetSafetyMarginMilliseconds.ToString(
                        [Globalization.CultureInfo]::InvariantCulture),
                "-pso-warmup-budget-cost-safety-multiplier",
                    $BudgetCostSafetyMultiplier.ToString(
                        [Globalization.CultureInfo]::InvariantCulture),
                "-pso-warmup-budget-cooldown-frames",
                    $BudgetCooldownFrames.ToString(),
                "-max-async-pso-job-count", $workers.ToString(),
                "-pso-deadline-backend-mode", $DeadlineBackendMode,
                "-pso-benchmark",
                "-pso-benchmark-mode", "scheduled",
                "-pso-benchmark-frames", $SampleFrames.ToString(),
                "-pso-benchmark-discard-frames", "0",
                "-pso-benchmark-delay-seconds",
                    $BenchmarkDelaySeconds.ToString(
                        [Globalization.CultureInfo]::InvariantCulture),
                "-pso-hitch-threshold-ms",
                    $TargetFrameMilliseconds.ToString(
                        [Globalization.CultureInfo]::InvariantCulture),
                "-pso-benchmark-report", $benchmarkReceipt,
                "-logFile", $log
            )
            if ($scenarioGated) {
                $arguments += @(
                    "-pso-scenario-report", $scenarioReceipt,
                    "-pso-scenario-quit-on-complete",
                    "-pso-benchmark-no-quit"
                )
            }
            Write-Host "[$sequence] workers=$workers batch=$batch repetition=$($repetition + 1)"
            $process = Start-Process `
                -FilePath $playerPath `
                -ArgumentList (ConvertTo-ArgumentLine $arguments) `
                -WorkingDirectory (Split-Path -Parent $playerPath) `
                -PassThru `
                -WindowStyle Hidden
            if (-not $process.HasExited) {
                $process.PriorityClass =
                    [System.Diagnostics.ProcessPriorityClass]::High
                if ($policyPlayerAffinityMask -gt 0) {
                    $process.ProcessorAffinity =
                        [IntPtr]::new($policyPlayerAffinityMask)
                }
            }
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
                -not (Test-Path -LiteralPath $benchmarkReceipt) -or
                ($scenarioGated -and
                    -not (Test-Path -LiteralPath $scenarioReceipt))) {
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Candidate did not emit every required receipt."
                continue
            }

            $warmup = Get-Content -Raw -LiteralPath $warmupReceipt | ConvertFrom-Json
            $benchmark = Get-Content -Raw -LiteralPath $benchmarkReceipt | ConvertFrom-Json
            $scenarioEvidence = if ($scenarioGated) {
                Get-Content -Raw -LiteralPath $scenarioReceipt | ConvertFrom-Json
            } else {
                $null
            }
            if (-not $warmup.completed -or -not $benchmark.completed -or
                ($scenarioGated -and -not $scenarioEvidence.completed)) {
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Candidate emitted an incomplete receipt."
                continue
            }
            $phases = @($warmup.phases)
            $scenarioPhaseEvidence = if ($scenarioGated) {
                @($phases | Where-Object {
                    $_.phase -eq $ScenarioPhase
                } | Select-Object -First 1)
            } else {
                @()
            }
            if ($scenarioGated -and (
                    -not [bool]$benchmark.scenarioMeasurementGated -or
                    [Math]::Abs(
                        [double]$benchmark.measurementDurationSeconds - 12.0
                    ) -gt 0.25 -or
                    [string]$scenarioEvidence.scenario -ne $Scenario -or
                    [string]$scenarioEvidence.phase -ne $ScenarioPhase -or
                    $scenarioPhaseEvidence.Count -ne 1)) {
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Scenario/benchmark gate evidence is inconsistent."
                continue
            }
            $rawScenarioSampleObjects = if ($scenarioGated) {
                @($scenarioEvidence.frameSamples)
            } else {
                @()
            }
            $scenarioSampleObjects = if ($scenarioGated) {
                @($rawScenarioSampleObjects | Where-Object {
                    [double]$_.elapsedSeconds -ge 0.0 -and
                    [double]$_.elapsedSeconds -le 12.25
                })
            } else {
                @()
            }
            $runtimeSamples = if ($scenarioGated) {
                @($scenarioSampleObjects | ForEach-Object {
                    [double]$_.milliseconds
                })
            } else {
                @($benchmark.frameTimeSamplesMilliseconds | ForEach-Object {
                    [double]$_
                })
            }
            if ($runtimeSamples.Count -eq 0) {
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Candidate emitted no measured runtime samples."
                continue
            }
            if ($scenarioGated -and (
                    $scenarioSampleObjects.Count -ne
                        $rawScenarioSampleObjects.Count -or
                    [double]$scenarioSampleObjects[0].elapsedSeconds -gt 0.25 -or
                    [double]$scenarioSampleObjects[-1].elapsedSeconds -lt 12.0)) {
                Add-FailedTrial `
                    -Sequence $sequence `
                    -Repetition ($repetition + 1) `
                    -Workers $workers `
                    -Batch $batch `
                    -Error "Scenario samples do not cover the complete 12-second gate."
                continue
            }
            $warmupHitches = ($phases | ForEach-Object {
                [int]$_.warmupFrameTimes.hitchFrameCount
            } | Measure-Object -Sum).Sum
            $worstFrame = ($phases | ForEach-Object {
                [double]$_.warmupFrameTimes.maximumMilliseconds
            } | Measure-Object -Maximum).Maximum
            $scenarioDeadlineMissed = $scenarioGated -and (
                [bool]$scenarioEvidence.deadlineMissed -or
                -not [bool]$scenarioEvidence.deferredReady)
            $deadlineMissed =
                ($phases | Where-Object { $_.deadlineMissed }).Count -gt 0 -or
                $scenarioDeadlineMissed
            $scenarioGcClean = -not $scenarioGated -or
                $Scenario -ne "megacity-metro" -or
                ([int]$scenarioEvidence.schemaVersion -ge 6 -and
                    [int]$scenarioEvidence.managedGcCollectionsDuringRun -eq 0)
            $cacheMissEvidence = $warmup.cacheMissTrace
            $cacheMissFree = $null -ne $cacheMissEvidence -and
                [bool]$cacheMissEvidence.requested -and
                [bool]$cacheMissEvidence.armed -and
                [string]::IsNullOrWhiteSpace(
                    [string]$cacheMissEvidence.error) -and
                [int]$cacheMissEvidence.baselineGraphicsStates -gt 0 -and
                [int]$cacheMissEvidence.observedGraphicsStates -eq
                    [int]$cacheMissEvidence.baselineGraphicsStates -and
                [int]$cacheMissEvidence.cacheMissGraphicsStates -eq 0
            $hardBudgetMet = @($phases | Where-Object {
                -not $_.hardFrameBudgetMet
            }).Count -eq 0
            $admissionBudgetMet = @($phases | Where-Object {
                -not $_.schedulerAdmissionBudgetMet
            }).Count -eq 0
            $budgetViolations = ($phases | ForEach-Object {
                [int]$_.budgetViolationCount
            } | Measure-Object -Sum).Sum
            $minimumBudgetViolations = ($phases | ForEach-Object {
                [int]$_.minimumBatchBudgetViolationCount
            } | Measure-Object -Sum).Sum
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
                HardFrameBudgetMet = [bool]$hardBudgetMet
                SchedulerAdmissionBudgetMet = [bool]$admissionBudgetMet
                BudgetViolations = [int]$budgetViolations
                MinimumBatchBudgetViolations = [int]$minimumBudgetViolations
                DeadlineMissed = [bool]$deadlineMissed
                ScenarioGated = $scenarioGated
                ScenarioGcClean = [bool]$scenarioGcClean
                CacheMissFree = [bool]$cacheMissFree
                RuntimeP99Milliseconds = Get-Percentile $runtimeSamples 0.99
                RuntimeHitchFrames = @($runtimeSamples | Where-Object {
                    $_ -ge $TargetFrameMilliseconds
                }).Count
                WorkerHeadroomMet =
                    [int]$workers -le $maximumSelectableWorkerCount
                WarmupReceipt = $warmupReceipt
                BenchmarkReceipt = $benchmarkReceipt
                ScenarioReceipt = if ($scenarioGated) {
                    $scenarioReceipt
                } else {
                    ""
                }
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
        MedianBudgetViolations = Get-Median @(
            $items | ForEach-Object { [double]$_.BudgetViolations })
        AnyMinimumBatchBudgetViolation = @($items | Where-Object {
            $_.MinimumBatchBudgetViolations -gt 0
        }).Count -gt 0
        AllHardFrameBudgetsMet = @($items | Where-Object {
            -not $_.HardFrameBudgetMet
        }).Count -eq 0
        AllSchedulerAdmissionsMet = @($items | Where-Object {
            -not $_.SchedulerAdmissionBudgetMet
        }).Count -eq 0
        MedianRuntimeP99Milliseconds = Get-Median @(
            $items | ForEach-Object { [double]$_.RuntimeP99Milliseconds })
        MedianRuntimeHitchFrames = Get-Median @(
            $items | ForEach-Object { [double]$_.RuntimeHitchFrames })
        MaximumRuntimeHitchFrames = [double](
            $items |
                Measure-Object -Property RuntimeHitchFrames -Maximum
        ).Maximum
        AllWorkerHeadroomMet = @($items | Where-Object {
            -not $_.WorkerHeadroomMet
        }).Count -eq 0
        AllScenarioGcClean = @($items | Where-Object {
            -not $_.ScenarioGcClean
        }).Count -eq 0
        AllCacheMissFree = @($items | Where-Object {
            -not $_.CacheMissFree
        }).Count -eq 0
        AnyDeadlineMissed = ($items | Where-Object { $_.DeadlineMissed }).Count -gt 0
        ParetoOptimal = $false
    })
}

foreach ($candidate in $aggregates) {
    $dominated = $false
    foreach ($other in $aggregates) {
        # A configuration that consumes the foreground reservation cannot
        # eliminate a deployable candidate from the Pareto frontier.
        if ($candidate.AllWorkerHeadroomMet -and
            -not $other.AllWorkerHeadroomMet) {
            continue
        }
        if (($candidate.AllScenarioGcClean -and
                -not $other.AllScenarioGcClean) -or
            ($candidate.AllHardFrameBudgetsMet -and
                -not $other.AllHardFrameBudgetsMet) -or
            ($candidate.AllSchedulerAdmissionsMet -and
                -not $other.AllSchedulerAdmissionsMet) -or
            ($candidate.AllCacheMissFree -and
                -not $other.AllCacheMissFree) -or
            (-not $candidate.AnyDeadlineMissed -and
                $other.AnyDeadlineMissed)) {
            continue
        }
        if ($candidate -ne $other -and (Test-Dominates $other $candidate)) {
            $dominated = $true
            break
        }
    }
    $candidate.ParetoOptimal = -not $dominated
}

$eligible = @($aggregates | Where-Object {
    $_.ParetoOptimal -and
    $_.FailedTrialCount -eq 0 -and
    $_.AllHardFrameBudgetsMet -and
    $_.AllSchedulerAdmissionsMet -and
    $_.AllWorkerHeadroomMet -and
    $_.AllScenarioGcClean -and
    $_.AllCacheMissFree -and
    -not $_.AnyMinimumBatchBudgetViolation -and
    -not $_.AnyDeadlineMissed -and
    $_.MaximumRuntimeHitchFrames -eq 0 -and
    $_.MedianWorstWarmupFrameMilliseconds -le $TargetFrameMilliseconds
})
$budgetQualified = $eligible.Count -gt 0
if ($eligible.Count -eq 0) {
    $eligible = @($aggregates | Where-Object {
        $_.ParetoOptimal -and
        $_.FailedTrialCount -eq 0 -and
        $_.AllWorkerHeadroomMet
    })
}
if ($eligible.Count -eq 0) {
    $eligible = @($aggregates | Where-Object { $_.AllWorkerHeadroomMet })
}
if ($eligible.Count -eq 0) {
    $eligible = @($aggregates)
}
if (-not $budgetQualified) {
    $minimumViolations = [double](
        $eligible | Measure-Object -Property MedianBudgetViolations -Minimum
    ).Minimum
    $eligible = @($eligible | Where-Object {
        [double]$_.MedianBudgetViolations -eq $minimumViolations
    })
    $withoutMinimumViolation = @($eligible | Where-Object {
        -not $_.AnyMinimumBatchBudgetViolation
    })
    if ($withoutMinimumViolation.Count -gt 0) {
        $eligible = $withoutMinimumViolation
    }
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
    MedianBudgetViolations,
    AnyMinimumBatchBudgetViolation,
    MedianWarmupHitchFrames,
    MedianRuntimeHitchFrames,
    MedianRuntimeP99Milliseconds,
    MedianWorstWarmupFrameMilliseconds,
    MedianWarmupMilliseconds,
    WorkerCount,
    BatchSize | Select-Object -First 1

$runs | Export-Csv -LiteralPath (Join-Path $runRoot "trials.csv") -NoTypeInformation
$aggregates | Export-Csv `
    -LiteralPath (Join-Path $runRoot "candidates.csv") `
    -NoTypeInformation

$recommendation = [ordered]@{
    schemaVersion = 5
    generatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    player = $playerPath
    plan = $planPath
    scenarioGated = $scenarioGated
    scenario = $Scenario
    scenarioPhase = $ScenarioPhase
    deadlineBackendMode = $DeadlineBackendMode
    workerCount = [int]$recommended.WorkerCount
    batchSize = [int]$recommended.BatchSize
    maximumBatchSize = [int]$recommended.BatchSize
    bootstrapBatchSize = $BootstrapBatchSize
    budgetSafetyMarginMilliseconds = $BudgetSafetyMarginMilliseconds
    budgetCostSafetyMultiplier = $BudgetCostSafetyMultiplier
    budgetCooldownFrames = $BudgetCooldownFrames
    logicalProcessorCount = $logicalProcessorCount
    reservedForegroundProcessorCount = $ReservedForegroundProcessorCount
    maximumSelectableWorkerCount = $maximumSelectableWorkerCount
    workerHeadroomMet = [bool]$recommended.AllWorkerHeadroomMet
    hardBudgetQualified = $budgetQualified
    status = if ($budgetQualified) { "qualified" } else { "provisional-no-budget-safe-candidate" }
    medianWarmupMilliseconds = [double]$recommended.MedianWarmupMilliseconds
    medianWorstWarmupFrameMilliseconds =
        [double]$recommended.MedianWorstWarmupFrameMilliseconds
    medianWarmupHitchFrames = [double]$recommended.MedianWarmupHitchFrames
    medianRuntimeHitchFrames = [double]$recommended.MedianRuntimeHitchFrames
    maximumRuntimeHitchFrames = [double]$recommended.MaximumRuntimeHitchFrames
    allScenarioGcClean = [bool]$recommended.AllScenarioGcClean
    allCacheMissFree = [bool]$recommended.AllCacheMissFree
    medianRuntimeP99Milliseconds =
        [double]$recommended.MedianRuntimeP99Milliseconds
    targetFrameMilliseconds = $TargetFrameMilliseconds
    nearFastTolerancePercent = 2.0
    selectionRule =
        "Require every repetition to use the real scenario time gate, record zero runtime deadline misses and plan-trace cache misses, avoid measured-window GC, meet the exact hard frame budget and strict-admission check, and preserve foreground-core headroom; then use the warmup/runtime Pareto frontier and a 2% elapsed-time noise band. If no candidate qualifies, emit a visibly provisional result."
}
$recommendationPath = Join-Path $runRoot "recommendation.json"
$recommendation | ConvertTo-Json -Depth 6 | Set-Content `
    -LiteralPath $recommendationPath `
    -Encoding utf8NoBOM

$searchReceipt = [ordered]@{
    schemaVersion = 5
    generatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    player = $playerPath
    plan = $planPath
    targetFrameMilliseconds = $TargetFrameMilliseconds
    scenarioGated = $scenarioGated
    scenario = $Scenario
    scenarioPhase = $ScenarioPhase
    benchmarkDelaySeconds = $BenchmarkDelaySeconds
    screenWidth = $ScreenWidth
    screenHeight = $ScreenHeight
    deadlineBackendMode = $DeadlineBackendMode
    repetitions = $Repetitions
    workerCounts = $WorkerCounts
    batchSizes = $BatchSizes
    batchSizeMeaning = "dynamic maximum; runtime minimum remains 1 and cold bootstrap is independently bounded"
    bootstrapBatchSize = $BootstrapBatchSize
    budgetSafetyMarginMilliseconds = $BudgetSafetyMarginMilliseconds
    budgetCostSafetyMultiplier = $BudgetCostSafetyMultiplier
    budgetCooldownFrames = $BudgetCooldownFrames
    logicalProcessorCount = $logicalProcessorCount
    reservedForegroundProcessorCount = $ReservedForegroundProcessorCount
    maximumSelectableWorkerCount = $maximumSelectableWorkerCount
    policyPlayerAffinityMask = if ($policyPlayerAffinityMask -gt 0) {
        "0x{0:X}" -f $policyPlayerAffinityMask
    } else {
        ""
    }
    trials = $runs
    candidates = $aggregates
    recommendation = $recommendation
    caveats = @(
        "Candidates are rotated between repetitions to reduce run-order bias.",
        "Runtime p99 and hitch counts break ties after hard warmup-budget qualification.",
        "Scenario-enabled searches measure the real 12-second wall-clock slice, not a frame-count proxy.",
        "Driver-level PSO caches are implementation-owned and cannot be portably cleared by this tool.",
        "A provisional recommendation is not a hard-budget pass.",
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
        $budget = if ($_.AllHardFrameBudgetsMet) { "met" } else { "failed" }
        "| $($_.WorkerCount) | $($_.BatchSize) | " +
        "$([double]$_.MedianWarmupMilliseconds) ms | " +
        "$([double]$_.MedianWorstWarmupFrameMilliseconds) ms | " +
        "$([double]$_.MedianWarmupHitchFrames) | " +
        "$([double]$_.MedianRuntimeP99Milliseconds) ms | " +
        "$([double]$_.MedianRuntimeHitchFrames) | " +
        "$([double]$_.MaximumRuntimeHitchFrames) | " +
        "$(if ($_.AllScenarioGcClean) { 'clean' } else { 'GC observed' }) | " +
        "$(if ($_.AllWorkerHeadroomMet) { 'yes' } else { 'no' }) | " +
        "$([double]$_.MedianBudgetViolations) | $budget | $deadline | $pareto |"
    })
$markdown = @"
# PSO warmup policy search

Recommended: **$($recommended.WorkerCount) workers / dynamic maximum $($recommended.BatchSize)**

Qualification: **$(if ($budgetQualified) { '12-second scenario + hard-budget qualified' } else { 'PROVISIONAL — no candidate met every hard gate' })**

| Workers | Max batch | Median warmup | Median worst frame | Warmup hitch frames | Runtime p99 | Median misses | Max misses | GC | Foreground headroom | Budget violations | Hard budget | Deadline | Pareto |
|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---|---|---|
$($markdownRows -join [Environment]::NewLine)

The candidate batch is a dynamic ceiling, not a forced cold batch. Every run
starts with minimum 1 and an independently bounded cold probe. Scenario-enabled
searches retain the real 12-second wall-clock slice and require zero misses in
every repetition. The search also requires the exact frame budget and reserves
$ReservedForegroundProcessorCount logical processors for foreground/render work;
elapsed times within 2% are then treated as noise-equivalent. Driver-level pipeline caches are
implementation-owned and are not deleted by this tool; repeat the search on every
controlled target GPU/driver image and material shader build.
"@
$markdown | Set-Content `
    -LiteralPath (Join-Path $runRoot "policy-search.md") `
    -Encoding utf8NoBOM

Write-Host "Recommended workers=$($recommended.WorkerCount), batch=$($recommended.BatchSize)"
Write-Host "Recommendation: $recommendationPath"
