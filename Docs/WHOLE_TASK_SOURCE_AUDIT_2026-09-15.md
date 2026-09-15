# Source audit: what the external route can establish

Audit source: PR 4 head `9c7fc7b75fa7ac1bca2e407821414cf5941e9b8c`.
Fresh remote metadata confirms PR 4 OPEN / Draft and main
`6218483755b40f6fd3742754b9e71c53b74d3bd4`. This checkout has its own branch;
it does not merge that PR or modify a previous evidence directory.

## Confirmed source findings

| Finding | Source and consequence |
|---|---|
| The official application consumes real render state | `Integrations/Urp3DSample/README.md`, `source-lock.json`, and `PsoUrpSampleCapture.cs` bind the original four complete Timeline routes, live camera associations and original CSV. This is an authored external rendering workload, not an arbitrary generated PSO count. |
| The original route itself has warmup | Each scene renders during five seconds of original Warming, then its Timeline restarts. This existing behavior is a relevant alternative and can already absorb first-use work. |
| The old disabled arm includes pipeline observation cost | `PsoExternalPhaseBridge.RetainLoadedShaders` retains encountered shaders in every arm; `Loaded` requests phases, while `renderCold` prevents warmup. A baseline-only loaded plan can also start feedback tracing. These are common costs, not a pristine original application path. |
| External observers lacked shader compilation attribution | They record CPU Update intervals, native route/camera events and memory. The shader marker recorder in `PsoBenchmarkController` requires its separate benchmark CLI flag and is not part of these original-route captures. CPU gaps alone do not identify PSO compilation. |
| The driver attestation fix is already present | `PsoWindowsDriverIdentity`, `PsoDriverAttestationCache` and the report preserve initial byte attestation and reuse checked snapshots. Repeated diagnostic cost dropped from about 563 ms to 0.27–0.42 ms in the old hardware cell. This change is inherited, not a new prewarm gain. |
| Reproduction assumed one old Editor path and a linked worktree | `Invoke-PsoExternalEditor.ps1` hardcoded Unity 6000.1.0f1 and read `.git` as text. The fresh standalone clone has a `.git` directory. The new branch compares resolved Git common directories and accepts an explicitly checked Editor path/version. |
| The old formal comparator assumes the old engine/backend | `pso_urp_comparison.py` freezes 6000.1 native-async-bulk and common retention. It must not silently freeze a 6000.5 or observer-only run under that description. The new diagnostic flag is rejected by its formal gates. A future new-cell comparison requires a new explicit freeze. |

## Existing results and limits

The complete old Boat Attack and URP reports were reviewed. The repaired Boat
Attack cohort completed 16 processes and had no sampled Update above 200 ms;
its whole-time medians do not show a scheduler gain. The URP cohort completed
16 processes, but retained about 212–221 ms early frames in every process and
one unexplained 381.3446 ms frame. Its 0.029-second scheduled/disabled median
whole-route difference is not compelling evidence on a fixed-duration route.

Those are historical one-cell results (R9700, retained caches). This turn has
read the checked-in receipts; it has not independently rehashed unavailable old
raw captures or binaries. Native collection growth is not verified compile misses
or a useful-coverage fraction. No current-machine result is inferred from them.

## New diagnostic implementation

- Explicit observer-only control bypasses the phase bridge and requires the
  existing warmup-disable flag, which makes orchestrator bootstrap return early.
- Opt-in `PsoWholeTaskProfiler` records actual available profiler metrics, a
  binary profile and Unity frame metadata before the original scene load. Its
  data and allocation overhead are diagnostic only. Missing markers/units are
  unavailable; nested/parallel work is not wall-time stall duration.
- `PsoWholeTaskProfilerExport` extracts thread samples and native flow records
  from the binary. Missing frame metadata and import coverage limits stay visible.
- `pso_whole_task_analysis.py` checks the complete CPU interval partition,
  startup/tail, all long frames, marker availability and optional binary joins.
  It never automatically promotes correlation into a performance claim.
- `Invoke-PsoWholeTaskStage.ps1` validates all predecessor terminal releases,
  then takes the original named mutex and independently checks load/capacity.
  It runs one explicit command and installs no queue/automation.

These changes enable a test; source inspection or successful compilation does
not establish original-content acceptance, a shader bottleneck or net savings.
The discovery protocol defines the conditions for proceeding or returning NO-GO.

## Remaining validation

Pending the hardware slot: archive bytes, complete host import, migration diff,
IL2CPP build, original scene/camera/route/CSV correctness, native profiler API
behavior and coverage, actual compile/wait attribution and resource costs.
Small/medium/large scaling, independent cache-cold repeats, verified GPU time and
presentation are unavailable until explicitly measured. No default changes.
