# Adopt warmup scheduling around content loading

The scheduler's value is the tradeoff between pressure during loading, completion
time, deadline misses and useful coverage. Unity and the driver still compile
and create the native graphics state. Cold versus prewarmed behavior establishes
the value of prewarming; it does not isolate the scheduler.

## Read the historical tradeoff directly

The retained [historical experiment](History/f352210/BENCHMARK_METHODOLOGY.md)
reported warmup-window P95 of 127.0 to 12.9 ms relative to all-at-once, total warmup
of 643.5 to 774.6 ms, and a 178.9 ms outlier. Post-warmup workload P95 was 4.169 ms
all-at-once versus 4.210 ms scheduled. These values are separate dimensions of
one historical cell, not proof that the current implementation eliminates hitches.

## Choose explicit policy behavior

| Application need | Existing option | Cost or condition to retain |
|---|---|---|
| A declared non-interactive loading interval | `throughput` | Whole-collection submission; no interactive latency guarantee. |
| Retain the existing scheduled behavior | `scheduled` (default) | Existing policy remains default; no new measured advantage claimed. |
| Isolate a simple progressive baseline | `fixed-progressive` | Fixed batch count; requires native progressive support. |
| Evaluate deadline/cost-aware admission | `observed-budget` | Opt-in; estimates can become stale, and native jobs are not preemptible. |

The fixed-progressive preset disables deadline, adaptive-cost, hotset and cost priorities.
The [policy and lifecycle API example](../DotNet/ShaderHitchPipeline.PolicyExample/README.md)
targets .NET 8, compiles against the real Core, and runs in the existing functional
CI allowlist. Its simulated sink shows retained resources, a pending fence,
deferral, cancellation and replacement revisions. It displays configurations
without executing policies; it does no native warmup or measurements.

In Unity, configure after loading the plan and before activation, then bind
content ownership to the [runtime scheduling APIs](SCHEDULING_OPTIONS.md):

| Integration event | Real API and host obligation |
|---|---|
| Select policy | Call `ConfigureScheduling(PsoSchedulingOptions.ObservedBudget())` after `LoadPlan` and before any activation. |
| Dependencies ready | Call `ActivatePhase(name)`. `true` accepts new demand, not completed warmup. `false` can also mean already active/completed, pending unload, missing phase or failure; inspect state rather than treating every false result as retryable. |
| Pending unload | If `IsPhaseUnloading(name)` is true, retain the dependencies and retry activation after retirement. The existing scheduler pump observes its own native fences. |
| Cancel demand | `CancelPhase(name)` keeps already submitted work and its resident owner. Cancellation neither preempts the batch nor establishes completion. |
| Evict resident phase | Call `UnloadPhase(name)`. Keep external shader/material leases until their submitted work has fenced; an unloaded request alone is not a resource-release signal. |
| Confirm warmup | Inspect `IsPhaseComplete(name)`/`GetPhaseStatus(name)`. A finished batch or a permutation count cannot substitute for the backend reporting complete warmup. |

The existing [native scene sink](../Integrations/MegacityMetroNative/Package/Runtime/PsoNativeSceneObserver.cs)
maps these real results to `Accepted`, `Deferred` and `Unavailable`. A pending,
running or completed resident can accept shared demand; a missing/failed plan is
unavailable. `Deferred` keeps a lifecycle request waiting so the host can retry.

Use `PsoContentPhaseLifecycle` when requests can be cancelled, superseded or
deferred. It accounts for demand; it does not retain external assets or drain
native fences. Its `Generation` identifies a content request and is distinct
from a scheduler activation number. Cancelling a deferred request never cancels
another owner's batch. A cancelled handle is already retired, so a later
`Unload(handle)` does not evict a resident phase; its owning sink/orchestrator must
request eviction separately when needed.

A ready event alone is not first-render coverage. Keep resources retained through
submitted native fences. Call `TryArmFeedbackTrace` after **all plan baseline
collections** resolve completely; leave feedback unavailable until it succeeds.
Only subsequent draws belong to that trace window. An observed zero-miss delta
does not prove coverage of content that was never drawn.
[Integration flow](INTEGRATION.md) and [Addressables ownership](../Integrations/Addressables/README.md)
describe the actual engine/resource boundary.

## Evaluate the scheduler's incremental contribution

Use cold, all-at-once, fixed progressive and the candidate policy with the same
engine/dependency version, player/shader build, content, resolved collections,
route, activations and cache protocol. Keep complete-frame tail
latency, warmup-window pressure, total completion, missed/infeasible deadlines,
cancelled/unused work and coverage separate.

The [pinned native Megacity adapter](../Integrations/MegacityMetroNative/README.md)
retains an official application. Its Unity 6000.1 bulk backend cannot supply a
fixed-progressive arm. A different capable engine/dependency cell must use the
same versions for all arms and retain the adapted-application label. Comparing
across those versions does not isolate policy value.

New policies and native-scene integration remain **Unmeasured**. The synthetic
example is API teaching data, not a new benchmark or coverage/speed result. The next evidence
milestone is this same-cell comparison during real asynchronous loading, with
full-frame observations and first-draw limitations preserved.
