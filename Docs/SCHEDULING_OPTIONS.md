# Scheduling and content lifecycle — Unmeasured

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
is not proof of shader coverage. Resources remain retained until a successful
completion fence. Backend failure and no-progress outcomes stay visible.

The native progress counter counts **permutations**, not necessarily graphics
states. Only `IsWarmedUp` proves complete phase coverage; incomplete phases must
not advertise their permutation count as warmed graphics states.

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
