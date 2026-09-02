# Command-line reference

Arguments accept either `-key value` or `-key=value`.

## Player trace

| Argument | Meaning |
|---|---|
| `-pso-trace` | Start tracing before the first scene loads. |
| `-pso-trace-phase <name>` | Phase label; defaults to `startup`. |
| `-pso-session <id>` | Stable run/session identifier. |
| `-pso-output <directory>` | Artifact root; defaults below `Application.persistentDataPath`. |
| `-pso-trace-auto-stop-seconds <n>` | End trace after a real-time duration. |
| `-pso-send-to-editor` | Also use Unity PlayerConnection delivery. File output is still retained. |

## Player warmup

| Argument | Meaning |
|---|---|
| `-pso-disable-warmup` | Deliberately bypass warmup for capture/cold A/B. |
| `-pso-warmup-plan <file>` | Override the default StreamingAssets plan. |
| `-pso-warmup-phase <name>` | Activate one explicit phase instead of all startup phases. |
| `-pso-warmup-strategy scheduled\|throughput` | Deadline/cost scheduler, or Unity all-at-once baseline. |
| `-pso-warmup-receipt <file>` | Write the warmup receipt to one exact path. |
| `-pso-warmup-initial-batch <n>` | Override the plan's initial progressive batch. |
| `-pso-warmup-minimum-batch <n>` | Override the minimum progressive batch. |
| `-pso-warmup-maximum-batch <n>` | Override the maximum progressive batch. |
| `-max-async-pso-job-count <n>` | Unity's native async PSO worker limit; retained in the receipt. |

## Player benchmark

| Argument | Meaning |
|---|---|
| `-pso-benchmark` | Enable automated frame sampling. |
| `-pso-benchmark-mode baseline\|naive\|scheduled` | Labels the receipt; every non-disabled mode waits for warmup. |
| `-pso-benchmark-frames <n>` | Raw sample count; default 600. |
| `-pso-benchmark-discard-frames <n>` | Discard frames after readiness before sampling. |
| `-pso-benchmark-delay-seconds <n>` | Identical stabilization delay after cold/optimized readiness. |
| `-pso-hitch-threshold-ms <n>` | Hitch classification threshold; default 33.33 ms. |
| `-pso-benchmark-warmup-timeout-seconds <n>` | Fail instead of hanging if optimized warmup never completes; default 300. |
| `-pso-benchmark-report <file>` | Exact JSON receipt path. |
| `-pso-benchmark-no-quit` | Keep the player running after writing the report. |

## Editor batch methods

```powershell
Unity.exe -batchmode -nographics -projectPath . `
  -executeMethod Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.ProcessInbox `
  -pso-inbox C:\PsoArtifacts\Inbox `
  -pso-profile-output C:\PsoArtifacts\Profiles `
  -pso-profile windows-d3d12-high `
  -quit
```

Use `PsoBatch.InstallPlan` with the same profile arguments to stage the plan. Use `PsoBatch.ValidateInstalledPlan` for a CI-only validation job.

`-pso-training-build` is an explicit build-gate bypass for the first capture player. It is intended only for the build that has no plan yet.

## Evidence generator

```powershell
pwsh Tools/Compare-PsoRuns.ps1 `
  -Baseline Artifacts/baseline.benchmark.json `
  -Naive Artifacts/naive.benchmark.json `
  -Optimized Artifacts/optimized.benchmark.json `
  -NaiveWarmup Artifacts/naive.warmup.json `
  -OptimizedWarmup Artifacts/scheduled.warmup.json `
  -Plan Artifacts/plan.json `
  -Output Evidence/Latest
```

The Python implementation returns exit code 2 if environments differ, scheduled regresses against cold or the all-at-once control beyond tolerance, there is no material improvement, warmup is incomplete, plan hashes differ, or the plan-scoped feedback trace reports a miss. It always reports the 8.33 ms presentation-budget count. The A/B/C parity policy permits at most 0.5% isolated non-severe capture/OS jitter but gives ≥16.67 ms severe stalls zero regression allowance; exact zero-jitter parity is retained separately.

## Worker and batch search

```powershell
pwsh Tools/Find-PsoWarmupPolicy.ps1 `
  -Player Builds/Windows/YourGame.exe `
  -Plan PsoArtifacts/Profiles/windows-d3d12/plan.json `
  -WorkerCounts 1,2,4,8 `
  -BatchSizes 4,16,64 `
  -Repetitions 2 `
  -Output PsoArtifacts/PolicySearch
```

Every candidate has a watchdog. The output contains trial CSV, aggregate CSV, JSON and Markdown Pareto reports, and `recommendation.json`. Eligible elapsed times within 2% of the fastest are treated as a noise-equivalent band, then fewer hitches and a lower worst warmup frame win before elapsed time. This is hardware-local evidence, not a portable default: the graphics driver owns its disk cache, and the script intentionally never deletes it.

## Complete Windows showcase

```powershell
python -m pip install -r Tools/requirements.txt
ffmpeg -version
pwsh Tools/Invoke-PsoShowcase.ps1
```

This one command performs a cache-isolated training build, cold trace/benchmark/capture, merge and install, final build, worker/batch search, all-at-once and scheduled benchmark/capture, marker-bounded pixel synchronization, blank-frame validation, and report generation. Use `-SkipAutoTune` for a faster non-search run, `-NoVisualCapture` for receipt-only automation, or `-NoGif` to omit animated outputs. `-pso-showcase-marker` and `-pso-showcase-cache-buster` are internal sample arguments; consuming games do not need them.
