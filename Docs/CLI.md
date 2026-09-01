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

## Player benchmark

| Argument | Meaning |
|---|---|
| `-pso-benchmark` | Enable automated frame sampling. |
| `-pso-benchmark-mode baseline\|optimized` | Labels the receipt and controls readiness behavior. |
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
  -Optimized Artifacts/optimized.benchmark.json `
  -Plan Artifacts/plan.json `
  -Output Evidence/Latest
```

The Python implementation returns exit code 2 if environments differ, the optimized run regresses beyond tolerance, or there is no material improvement.
