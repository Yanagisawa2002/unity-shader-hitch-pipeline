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
| `-pso-warmup-bootstrap-batch <n>` | Override cold/steady probe size; defaults to one. |
| `-pso-warmup-budget-safety-margin-ms <n>` | Reserve frame headroom before admitting opaque work. |
| `-pso-warmup-budget-cost-safety-multiplier <n>` | Inflate the online cost bound; must be at least one. |
| `-pso-warmup-budget-cooldown-frames <n>` | Frames withheld after an observed violation. |
| `-pso-disable-preinteractive-bootstrap` | Disable the required-startup first-present gate; intended for deferred-policy experiments. |
| `-max-async-pso-job-count <n>` | Unity's native async PSO worker limit; retained in the receipt. |

Startup phases with `preinteractiveBootstrap: true` complete their required hot
set in `BeforeSceneLoad`. The full gate duration remains in the receipt. The hard
frame budget applies to interactive frames after that boundary; it does not
pretend an opaque driver call can be preempted.

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
  -Scenario megacity-metro `
  -ScenarioPhase megacity-metro-reveal `
  -WorkerCounts 1,2,4,8 `
  -BatchSizes 1 `
  -Repetitions 2 `
  -Output PsoArtifacts/PolicySearch
```

Every candidate has a watchdog. With `-Scenario` and `-ScenarioPhase`, the
Player uses `-pso-scenario-quit-on-complete` internally and the generic sampler
is gated from scenario `Arm` through the 12-second completion boundary.
`BatchSizes` are dynamic maxima, never forced cold batches; minimum and
bootstrap remain one. Qualification requires every repetition to record zero
16.67 ms misses, no measured-window GC, the exact hard budget,
scheduler-admission validity, and foreground CPU headroom. If none
qualifies, `recommendation.json` says
`provisional-no-budget-safe-candidate`. This is hardware-local evidence, not a
portable default: the graphics driver owns its disk cache, and the script never
deletes it.

`-DeadlineBackendMode auto|progressive|native-async-bulk` makes the compatibility
choice explicit in the search receipt. `auto` keeps progressive batches on
Unity versions where they are dependable and selects deadline-gated native
asynchronous bulk warmup for Unity 6000.1.

## PresentMon and WPR evidence

Run from an elevated shell or an account in **Performance Log Users**:

```powershell
pwsh Tools/Invoke-PsoWindowsEvidence.ps1 `
  -Player Builds/Windows/YourGame.exe `
  -PlayerArguments @(
    "-pso-warmup-strategy", "scheduled",
    "-pso-benchmark", "-pso-benchmark-mode", "scheduled"
  ) `
  -PresentMon C:\Tools\PresentMon.exe `
  -WarmupReceipt C:\Evidence\scheduled.warmup.json `
  -Output C:\Evidence\Windows `
  -CaptureEtw
```

The wrapper refuses to disturb an existing WPR session. It emits raw PresentMon
v2 CSV, optional built-in `GPU` profile ETL, parsed p50/p95/p99/max CPU-present,
display, CPU and GPU metrics, exact commands and SHA-256 hashes. Insufficient
trace privilege is a failed manifest with the original exit code.

## Public hardware/scene matrix

```powershell
python Tools/pso_matrix.py aggregate `
  --definition Matrix/targets.json `
  --runs Matrix/runs `
  --output Matrix/results
```

A cell requires five process-cold repetitions, PresentMon for every run, and at
least one WPR GPU ETL. NVIDIA, AMD, Intel, controlled-showcase, and pinned
Megacity Metro cells are independent; no vendor result is inferred from another.

## Complete Windows showcase

```powershell
python -m pip install -r Tools/requirements.txt
ffmpeg -version
pwsh Tools/Invoke-PsoShowcase.ps1
```

This one command performs a cache-isolated two-phase training build, cold trace/benchmark/capture, merge and install, final build, worker/batch search, all-at-once and scheduled deferred-phase benchmark/capture, `CONTENT_REQUEST` / `CONTENT_REVEAL` / `CONTENT_COMPLETE` validation, pixel-anchored synchronization, blank-frame validation, and report generation. Defaults retain 1080 frames and 11 seconds of visible capture. The first-use chapter keeps a 0.35 s reveal lead; the request chapter begins one presented frame after the event (`-RequestVisualLeadSeconds 0`). Players default to `High` priority and ffmpeg to `BelowNormal`, identically across modes and recorded in alignment metadata.

Use `-SkipAutoTune` for a faster non-search run, `-NoVisualCapture` for receipt-only automation, or `-NoGif` to omit animated outputs. Priority controls can be overridden with `-PlayerPriorityClass` and `-FfmpegPriorityClass`. `-pso-showcase-marker` and `-pso-showcase-cache-buster` are internal sample arguments; consuming games do not need them.

## Deadline Run (do not invoke as a smoke test)

```powershell
pwsh Tools/Invoke-PsoDeadlineRun.ps1
```

This is the complete build/train/merge/install/search/three-player/capture job,
not a scene preview command. Defaults retain up to 3600 raw frames (15 seconds at
the 240 FPS target), capture 20 seconds to safely contain the two-second pre-roll
and delayed 12-second flight, and compose exactly 12 seconds from
`WORKLOAD_START`. The extra measured second guarantees that receipt-only runs can
write `RUN_COMPLETE` before the benchmark Player exits. It fails unless cold
reaches 80 ms in the reveal window and scheduled has zero frames at or above
16.67 ms. Use `-NoVisualCapture` only when receipts are the intended output; it
does not waive the acceptance gate.

The shared runner rejects `-Preset DeadlineRun` before launch when fewer than
3600 benchmark frames are requested or when visible capture cannot contain the
pre-roll plus the complete 12-second slice. Use the dedicated wrapper unless
deliberately overriding its other controls.

The runner passes `-pso-scenario-marker` and an invocation-specific shader cache
key internally. Application integrations normally use `PsoDeadlineScenario`
directly and do not need either argument.

## Megacity Metro external validation (do not invoke as a smoke test)

```powershell
pwsh Tools/Invoke-PsoMegacityMetro.ps1 `
  -ProjectPath C:\Work\megacity-metro
```

The target checkout must match the revision pinned in
`Integrations/MegacityMetro/pin.json` and must already contain the adapter. The
command performs a fresh cold Release build/capture, a Development trace build,
phase-filtered merge and install, another Release build, two-repetition worker
search, and visible all-at-once/scheduled captures. It then validates the three
12-second scenario receipts and composes the two-up MP4/GIF plus a three-way
technical MP4.

The formal cell pins Unity 6000.1.0f1, D3D12, Medium quality, 1280x720 native
output, render scale 1.0, a 150 m street-canyon far plane, VSync off, and a
120 Hz producer. Before every measurement it traverses two full camera circuits,
freezes Initialization/Simulation while retaining Presentation, starts desktop
capture, proves three continuous seconds below 14 ms, and arms at the fixed
4.0-second circuit phase. Any diagnostic camera/HUD/frame-rate override, timed
GC, incomplete native timing coverage, camera drift, cache miss, or scheduled
frame at or above 16.67 ms fails the formal receipt.
