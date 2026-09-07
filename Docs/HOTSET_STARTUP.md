# Trace-derived startup selection

The opt-in hotset policy selects independently warmable collection units. The
existing startup defaults and online deferred cost fitting remain unchanged.
`required && prewarmAtStartup` in the caller's validated plan is authoritative:
neither a policy file nor a zero/unknown budget can remove those phases. Selected
startup work completes behind a first-render gate. The budget is an estimate of
startup work, not a bound on latency or on an opaque driver call.

## Evidence and selection

`PsoHotsetUnit` carries owner-independent `id`, `contentId`, `contentRevision`,
`compatibilityNamespace`, collection integrity hash, phase mapping, state-entry
count, estimated whole-unit work, and resident bytes (`-1` means unknown).
Streaming owners may reference the same content ID; this policy does not own
asset lifetime or invent cross-collection deduplication. Provide already deduped
atomic units, or accept the conservative collection-entry accounting.

`PsoHotsetTrace` names a route, unique capture, explicit training/held-out split,
current content identity, and actual use events: phase ordinal, elapsed first/use
time, and positive frequency. Unity's native collection records state membership,
not a per-PSO frequency counter. Instrument application/render unit use and keep
that provenance explicit. Do not label renderer visits as hardware cache hits.

For each unit, the selector sums each training route's average capture benefit:
`log(1 + frequency) / ((1 + firstUseMs / 1000) * (1 + earliestPhaseOrdinal))`.
It orders optional units by benefit divided by estimated whole-unit milliseconds,
then ordinal ID. This is a deterministic greedy policy, not a knapsack optimum.
Required units spend the budget first; oversized candidates are skipped without
blocking smaller candidates. Repeated captures do not overweight a route; all
duplicate capture IDs are rejected. Unknown optional costs, absent evidence,
negative/NaN use data, stale identity, and wrong splits defer optional work.

Replay rejects training/held-out route or capture overlap. It counts use coverage,
first-use unit misses, unknown uses and unused startup state entries. Supplied
actual deferred completion times may cover later first use. These are modeled
coverage misses, distinct from the native post-plan trace's cache-miss count and
measured frame hitches. Frame times include instrumentation and engine/OS noise;
a coverage miss is not automatically a driver-caused hitch. State-entry totals can overcount cross-collection overlap.

## Runtime entry point and compatibility

After normal plan load/current-build validation, use:

```csharp
PsoWarmupOrchestrator.StartupHotsetCompatibilityValidator = ValidateCurrentIdentityAndCostScope;
orchestrator.LoadPlan(planPath, outputRoot, true);
orchestrator.ActivateStartupHotsetAndGate(policyDocument);
```

Call before the first scene render. The command-line equivalent is
`-pso-startup-hotset <policy.json>`; install the validator before the command-line
bootstrap. Without a validator, or if it rejects/throws, only caller-required
startup phases are selected. `ConfigureStartupHotset` must precede activation.
An explicit phase argument cannot bypass required startup phases when this policy
is enabled. Selection receipts are written beside normal warmup receipts.

The integration bridge must evaluate the plan's collection compatibility against
the actual current environment, compare each trace namespace with
`PsoCompatibility.CollectionKey(currentEnvironment)`, and require applicable cost
evidence. Collection integrity alone is not build attestation. Driver or cost
context changes must not silently reuse optional work estimates. The baseline
worker exposes a conservative callback so the compatibility implementation can
install this bridge after merge without introducing a dependency on absent APIs.
`PsoStartupHotset.Prepare` is the pure policy adapter; production and fixture paths
use `PrepareValidated`, which requires a successful compatibility callback.

## Bounded real Player experiment

`Tools/Invoke-PsoHotsetFixture.ps1` builds a separate Unity 6000.5.2 D3D12
Development Player. Four groups each render four real local-keyword variants.
Every arm uses the same explicit `Camera.Render` backbuffer path, allowing real
GPU rendering when the OS window is occluded. Discovery captures a native
GraphicsStateCollection per visible group and waits for actual renderer callbacks;
no callback is manually fabricated. A separate
process calibrates actual bulk work per collection. Two additional processes
record renderer visits using `OnWillRenderObject`; group identity and native
state membership come from discovery. Each stage lasts 24 frames at a 120 Hz
producer target; it is not a frame-time hard bound.

| Split | Route | Actual visible group sequence |
|---|---|---|
| Training | train-a | 0, 1, 1, 2, 1 |
| Training | train-b | 0, 1, 2, 1, 1 |
| Held-out | held-a | 0, 2, 1, 3, 1 |
| Held-out | held-b | 0, 3, 2, 3, 2 |

The seed is the literal immutable array, not a relabeled common workload. Group
0 is required. The fixed budget rule is required measured work plus half the sum
of optional measured work. The plan, training and policy are frozen before any
held-out process starts. Held-out results never tune the budget or selection.

The controls are required-only, trace-budget hotset, and natural Unity bulk for
all collections. Required-only and hotset share exactly the same deferred path:
after frame four, submit one state per job and poll asynchronously. No control
receives an injected stall, forced cold batch, or altered route. Bulk may win.
The matrix uses `PrepareValidated`; a separate `orchestrator-smoke` process checks
the production `LoadPlan` → `ActivateStartupHotsetAndGate` path and verifies every
selected phase, including required u0, completed before rendering.

The fixture's explicit experiment cost scope binds the actual binary build GUID,
shader hash, GPU, actual `Win32_VideoController` driver version captured by the
runner for every process, Unity, API, quality, screen, producer target, vSync and
backend scheduling. Numerical comparison permits only 1e-12 relative serialization
roundoff for measured estimates; collection/build identity remains exact. It
intentionally permits only declared route/mode/output
axes and rejects unknown arguments. Across-route prediction is a training-model
generalization experiment, not reuse of a production exact-context cost cache.
Driver cache state is uncontrolled; even process-cold calibration after discovery
may use a warmed driver cache. Nothing is globally cleared.

All Unity builds and GPU runs must execute while holding the shared mutex:

```powershell
& '<control>/Invoke-SerializedValidation.ps1' -Action {
    & '<worktree>/Tools/Invoke-PsoHotsetFixture.ps1' -BuildOnly -OutputRoot '<build-log-dir>'
}
& '<control>/Invoke-SerializedValidation.ps1' -Action {
    & '<worktree>/Tools/Invoke-PsoHotsetFixture.ps1' -SkipBuild -Repetitions 3 -OutputRoot '<new-formal-run-dir>'
}
```

`-Repetitions 1` is the worker's six-cell correctness smoke. Three repetitions
are the integrator's 18-cell formal matrix, with route/replicate-rotated arm order.
Use a fresh output directory for each entire experiment; do not rerun discovery
or calibration into already frozen held-out evidence.

Each receipt retains engine first-render latency, startup work, deferred work and
submissions, raw subsequent frame times/hitches, modeled coverage, native
post-plan cache misses, extra warmed entries, collection file bytes and engine
allocation observations. `deferredWarmupMilliseconds` is summed observed job
elapsed time including frame-quantized completion polling, not CPU/GPU execution
time. Completion timestamps likewise conservatively record when the Player
observed completion. Startup warmup milliseconds measure blocking call elapsed;
startup API smoke elapsed additionally includes selection/receipt work. Driver PSO memory and OS first-present are unavailable
(`-1` and explicit availability), not zero. Buffered UTC/QPC/frame markers enable
external alignment. First-render is an engine proxy; actual OS first-present and
the separate five-process system capture gate remain integrator responsibilities.
This tiny fixture cannot establish a production advantage or a frame-time bound.

## Shared serializer regression

The actual v3 discovery/calibration fixture produced a valid hash over its written
JSON, but Unity's first read changed only
`phases[1].estimatedMillisecondsPerState` from `0.10332000000000001` to `0.10332`.
The new serialized document therefore had a different hash. The original plan,
first/second read results and exact hashes are retained in
`Docs/Evidence/hotset-json-roundtrip/`; `PsoHotsetJsonRoundtripProbe.Run` reproduces
the issue in the Editor. This is a shared hash/write/read contract regression,
not justification to disable integrity checks. The fixture normalizes before
hashing and immediately verifies the saved file; integration must fix the common
numerical serialization contract and add the preserved case to its tests.
