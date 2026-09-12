# Scheduler functional contract

The runtime implementation and these tests share `Core/PsoWarmupScheduler.cs`. This is a deterministic functional test executable, **not an external benchmark, performance framework, or benchmark workload**. Its only backend is a mock; its only clock is a caller-controlled number. It accepts no arguments and contains no Unity, process-launch, profiling, elapsed-time measurement, calibration, or policy-search entry.

Run the allowed check from the repository root:

```powershell
dotnet run --disable-build-servers -p:UseSharedCompilation=false --project DotNet/ShaderHitchPipeline.Scheduler.Tests/ShaderHitchPipeline.Scheduler.Tests.csproj
```

New policy performance is **Unmeasured / 待验证**. The historical 88.2% warmup-versus-cold result is not evidence for this scheduler or this source revision. No Player, native warmup, external scene, benchmark, calibration, profiler, or real timing comparison was run for this change.

## Integration API

`PsoWarmupOrchestrator.LoadPlan` validates the plan, compatibility and file hashes without creating native phase collections. Call `ActivatePhase(name)` **after** the phase's shader/material dependencies are loaded. The native collection is loaded at activation and must resolve exactly the plan's graphics-state count. A partial resolution is rejected; it cannot report successful warmup. A custom loader must also attest its content identity and retain dependency owners until the last in-flight fence finishes. An observer of a scene unload cannot, by itself, keep external shader/Addressables owners alive; use the existing retained-owner streaming integration for that ownership guarantee.

Existing `ActivatePhase` and `IsPhaseComplete` remain available. Added methods:

- `ConfigureScheduling(PsoSchedulingOptions)` after `LoadPlan`, before any activation; options are validated and copied.
- `CancelPhase(name)` retires the current activation. A new activation can queue immediately behind its predecessor's non-preemptible fence. The cancelled activation remains cancelled in history.
- `UnloadPhase(name)` also retires the resident collection. Release waits for a successful fence. A reload during that pending unload returns `false`; retry after `GetPhaseStatus(name).HasInFlightBatch` becomes false and retirement completes.
- `GetPhaseStatus(name)` returns the latest activation, or null before activation. It exposes state, actual permutation progress, backend completion, deadline outcome, admission reason, and pending ownership. Status is updated by the scheduler pump; it does not sample counters itself.
- `IsPhaseUnloading(name)` reports pending resident retirement independently of terminal state. A cancelled activation keeps its cancellation history during unload. Phase activation numbers survive full release/re-registration within the loaded plan.
- `RefreshSchedulingEnvironment(snapshot)` rejects incompatible collections and invalidates cost observations on a cost identity change. Hosts must call it for content/build/device/driver/execution-context changes. Quality/API changes are also detected by the Unity update adapter.
- `SetNonInteractiveWindow(active, budgetMilliseconds)` supplies a **per-admission estimate cap** during a caller-owned loading window. It is not a total loading-window duration or a driver time limit.
- `DrainRetiredSchedulers(block: false)` pumps ownership retained after orchestrator destruction. Explicit `block: true` is a shutdown fence with no latency bound. A failed/unproven fence stays retained for retry; pending work is not silently marked complete.

`TryArmFeedbackTrace()` is an explicit full-plan trace-baseline attempt, also attempted after startup activation. Every source must resolve its declared count before the combined collection begins tracing. Failure leaves `FeedbackTraceArmed=false` and `FeedbackTraceError` populated; no partial baseline is used. Zero fields in an unavailable trace are **not zero misses or coverage proof**. A later successful arming covers only subsequent draws; it cannot recover earlier first-use events. Phase cancellation/unload never prunes the plan baseline to improve miss counts.

`WriteReceipt` retains the original warmup receipt and writes a separate `<receiptPath>.scheduling.json` feedback contract. This records the effective policy switches, distinct activation numbers, lifecycle/fence state, invalidation generations, and absence of any latency guarantee. The plan schema is unchanged. Only `IsWarmedUp` attests successful backend completion: `completedWarmupCount` is a permutation count and cannot prove partial graphics-state coverage. Incomplete receipts retain the raw permutation count and report no attested completed graphics-state count.

## Policy entry points

The existing `scheduled` strategy remains the default; it keeps legacy adaptive batch selection with correctness fixes. `observed-budget` is opt-in. It uses a bounded window of observed costs, a positive-residual envelope, current frame headroom, repeated dispatch costs in remaining-work estimates, and inclusive deadline-demand prefixes for competing phases. Already missed deadlines do not monopolize viable deadlines; bounded waiting gives older admitted work another turn. Infeasible demand is reported and preserved. Prediction-based admission is not a statistical confidence bound or proof of completion by a deadline.

The `fixed-progressive` strategy is a simple constant batch count, capped by remaining work and the plan maximum, in activation order. It still refuses known insufficient frame headroom and observes frame pressure, but by default disables deadline, hot-set, density, and adaptive-cost behavior. Its batch size is `-pso-fixed-batch-size` (default 4). A backend requiring native bulk is **Unsupported** for this policy; it is never silently substituted. In particular, the default Unity 6000.1 backend selection prefers bulk. A separately requested progressive backend remains a different explicit configuration, with latency unverified.

CLI configuration values are `-pso-warmup-strategy scheduled|observed-budget|fixed-progressive|throughput`. Independent disabling flags are `-pso-disable-deadlines`, `-pso-disable-adaptive-cost`, `-pso-disable-hotset-priority`, and `-pso-disable-cost-priority`. `PsoSchedulingOptions` also allows these features to be enabled independently for future controlled configurations. Fixed-progressive does not run the startup bulk gate. New policy selection is never automatic.

Native bulk admission uses the estimated **entire** batch cost; there is no CPU-core-count speedup assumption or one-state dispatch proxy. Missing a deadline does not permanently forbid work that otherwise fits the budget. The observed-budget policy pauses interactive admission after a minimum opaque batch overrun. That hazard survives cost expiry and requires an explicit loading window. The job already in flight cannot be cancelled or made shorter.

Cost identity changes invalidate in-flight samples. Expired observations return to a bootstrap-sized admission and clear the fitted slope. New conservative-window slopes are not exported as the legacy `adaptive-online-v1` cost-cache model. Polling observations include time spent inside the synchronous submit call and the completion polling interval; they are upper observations, not isolated driver service times.

## Sources and verification scope

`sources.lock.json` pins the official Unity reference source to a full commit and file hashes, and lists the Unity 6.0/6.5 API documentation and native example. The reference-only license was reviewed; upstream implementation code is not vendored or incorporated. These are API references, not standardized workloads. The external application scenario has a separate source lock and workload contract under `Integrations/MegacityMetroNative`.

The functional cases cover finite inputs, tail batches, observed uncertainty, stale identity, deadline/headroom conflict and joint demand, hot-set policy switches, starvation, cancellation/reactivation, unload, failed submission/fences/disposal, missing progress, startup partial completion, and partial dependency resolution. Real Unity API usage is checked by compiling against installed reference DLLs, without starting the Editor or Player. Native latency, graphics correctness, first-draw coverage, device behavior, external-scene behavior, and any speedup remain unverified. Build and runtime commands remain independent entry points; the CPU validation allowlist and preparation tool never invoke them.
