# Scheduling and content lifecycle — Unmeasured

For the application decision and a compilable CPU-only example, start with
[policy adoption](POLICY_ADOPTION.md). Configuration choices are distinct from
native backend capability and from measured policy improvements.

`PsoWarmupOrchestrator` delegates execution and ownership to
`Core/PsoWarmupScheduler`. The same scheduler is exercised by deterministic
mock-backend/virtual-clock tests. New strategy options remain opt-in and carry
no comparative performance claim. The versioned plan schema remains v3.

| Policy | Selection | Contract |
|---|---|---|
| Existing scheduled | Default, or `-pso-warmup-strategy scheduled` | Retains the prior scheduled policy selection. |
| Unity throughput | `-pso-warmup-strategy throughput` | Whole collection job; no interactive budget guarantee. |
| Fixed progressive | `-pso-warmup-strategy fixed-progressive -pso-fixed-batch-size 8` | Fixed native progressive count, with deadline/cost/hotset priority disabled. Rejects a bulk-only backend. |
| Observed budget | `-pso-warmup-strategy observed-budget` | Opt-in conservative admission with cost age/environment invalidation; no hard driver latency bound. |

`-pso-disable-deadlines`, `-pso-disable-adaptive-cost`,
`-pso-disable-hotset-priority` and `-pso-disable-cost-priority` independently
disable policy components. All comparison arms must retain the same content,
shader/build identity, resolved collections, activation events and cache protocol.
Changing Editor/backend capability creates a separate externally locked cell.

Applications can configure a loaded plan before any activation:

```csharp
orchestrator.ConfigureScheduling(PsoSchedulingOptions.ObservedBudget());
orchestrator.ActivatePhase("city"); // only after the phase's shader dependencies are loaded
PsoPhaseStatus status = orchestrator.GetPhaseStatus("city");
orchestrator.CancelPhase("city");
// A later activation has its own history, even if a cancelled job still owns a fence.
orchestrator.ActivatePhase("city");
orchestrator.UnloadPhase("city");
```

An activation attempted while unload still retains a native fence returns false.
`IsPhaseUnloading(name)` reports this pending ownership even when a cancelled
activation keeps its original terminal state. Retry after retirement;
`Core/PsoContentPhaseLifecycle` uses an explicit `Deferred`
activation result to keep dependency-ready loads pending without converting that
normal wait into permanent failure. Cancelling a deferred request prevents its
later retry from resurrecting demand. `ActivatePhase` can also return false for
an already active/completed resident or for an unavailable phase; inspect status
before deciding to retry. A request's `Generation` is separate from the scheduler's
phase activation number.
Activation numbers remain unique for a phase across unload/re-registration within
the same loaded plan; a new native collection does not restart its feedback identity.

The host can explicitly declare an existing non-interactive loading window with
`SetNonInteractiveWindow(true, budgetMilliseconds)` and must close it with
`SetNonInteractiveWindow(false)`. This does not authorize deleting work or
claiming that a non-preemptible driver job meets that estimated budget.

`RefreshSchedulingEnvironment(snapshot)` separates collection compatibility
failure from cost-only invalidation. Incompatible content/API/quality retires
work; a cost-context change invalidates old observations, including an in-flight
sample from the previous context. Configured estimates remain priors.

## Ownership and evidence

`GetPhaseStatus` exposes Pending/Running/Completed/Cancelled/Unloaded/Faulted,
activation generation, real backend progress, deadline feasibility/miss, latest
admission reason and retained fences. Cancellation is not completion; unloading
is not proof of shader coverage. The scheduler retains its backend owner through
a successful completion fence; the host must independently retain external
shader/material leases. `PsoContentPhaseLifecycle` accounts for demand and does
not own those assets or drain fences. A cancelled request handle is already
retired, so a later `Unload(handle)` does not request phase eviction; use the
owning sink/orchestrator for that separate action. Backend failure and no-progress
outcomes stay visible.

The native progress counter counts **permutations**, not necessarily graphics
states. Only `IsWarmedUp` attests complete warmup of the loaded collection;
incomplete phases must not advertise their permutation count as warmed graphics
states. This says nothing about application states absent from that collection
or content that has not yet drawn.

Each warmup receipt has a `.scheduling.json` sidecar containing policy flags,
separate activation histories, cancellation/fault/fence state and an explicit
Unmeasured/no-hard-latency claim. The new JSON Schema validates those fields and
retains all historical warmup receipt labels. A terminal status cannot hide an
unfenced job or an impossible deadline.

## Deferred shader resolution and feedback

Plan parsing/file hash validation does not establish that deferred shader assets
are loaded. Resolve the native collection when the phase is activated, then
require its complete graphics-state count to match the attested plan. A partial
collection is an actionable error, never a smaller successful workload.

Plan-wide feedback must wait until every baseline collection resolves completely.
Call `TryArmFeedbackTrace` after the relevant dependencies are available. A false
result keeps feedback unavailable with a reason; the numeric default zero is not
evidence of zero misses. Only the later armed interval is observed. The native
scene observer cannot retroactively cover first draws that preceded readiness.

See [the native application adapter](../Integrations/MegacityMetroNative/README.md)
for generation-safe request/ready/cancel/unload wiring, and the separate
[Addressables owner](../Integrations/Addressables/README.md) for asset retention.
No scheduler code change establishes that the historical 178.9 ms outlier has
been eliminated. Runtime correctness and timing remain untested in this repair.
