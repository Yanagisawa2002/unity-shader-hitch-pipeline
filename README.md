# Shader Hitch Pipeline

Current external evidence: [official Boat Attack](Docs/EXTERNAL_BOAT_ATTACK_2026-09-14.md)
and [official URP four-scene native comparisons](Docs/EXTERNAL_URP_SAMPLE_2026-09-14.md).
The [September 13 fixes](Docs/REVIEW_FIXES_20260913.md) and historical measurements retain their original conditions.

**Schedule graphics-state warmup around content loading, with explicit coverage and lifecycle contracts.**

The engineering goal is to control loading-time pressure while retaining total
completion cost, tail latency and useful state coverage. Start with the
[policy adoption guide](Docs/POLICY_ADOPTION.md) and its
[compilable Core example](DotNet/ShaderHitchPipeline.PolicyExample/README.md).
The example uses a simulated sink; it establishes no native warmup or performance result.

This Unity 6 package captures native `GraphicsStateCollection` states, validates
trace/plan/build identity, and coordinates phase warmup. Unity and the graphics
driver perform shader compilation and pipeline creation.

The working package is **0.3.0, unreleased**. The September 8 integration preserves
the existing vNext production and streaming implementation and adds a faithful
external scene adapter, lifecycle fixes, explicit policy alternatives, and CPU-only
PR checks. Boat Attack now has native content/training/plan validation and two
separately frozen four-arm comparisons. A repeated driver-attestation completion
stall was diagnosed and repaired; **no scheduling gain or useful-coverage improvement
is established**. URP completed its original four-scene route and a separate
16-process comparison with mixed scene-specific results and no overall gain claim.
Other workloads retain their individual validation boundaries. The existing
`scheduled` policy remains the default; `observed-budget` and `fixed-progressive`
require explicit selection.

## What the historical evidence establishes

The original showcase reported warmup-window P95 of **127.0 → 12.9 ms** against
Unity all-at-once, with total warmup **643.5 → 774.6 ms (about 20% longer)** and a
**178.9 ms outlier**. After warmup, workload P95 was **4.169 ms all-at-once versus
4.210 ms scheduled**. The **88.2%** reduction versus cold first use demonstrates
the benefit of prewarming; it does not isolate the scheduler's contribution.

These values belong to the [historical report retained at f352210](Docs/History/f352210/BENCHMARK_METHODOLOGY.md),
not to this repair. That report snapshot is not retroactive proof of an exact
Player binary/source identity. Later vNext media and reports describe different
cells and retain their own provenance.

[![Retained historical warmup/workload comparison](Docs/portfolio/overview.svg)](Docs/portfolio/README.md)

The [later independent follow-up](Docs/PerformanceNextResults.md) retains an
inconclusive hotset result, an unequal-work capture pair, and the original measured
source/build identities. Neither those outcomes nor the outlier are claimed to be
fixed by unmeasured code changes.

## External workloads

[Official Boat Attack](Integrations/BoatAttack/README.md) uses the original full
flythrough and static benchmark routes, with declared correctness repairs and
first-load capture. Sixteen post-repair processes completed; the earlier negative
16-process sequence and all failed attempts remain recorded. This demonstrates
a concrete runtime-overhead fix, not a general PSO score or a policy promotion.

[Official URP 3D Sample](Docs/EXTERNAL_URP_SAMPLE_2026-09-14.md) pins
template 17.1.5 and its Terminal, Garden, Oasis and Cockpit scenes. Its own original
BenchmarkScene supplies the automatic full scene/timeline route. Sixteen matched
processes passed content/native-work/ownership and identity checks, with all
first-load samples, the 381 ms outlier and a separate diagnostic retained.
This external-scene result is independent of Megacity large acceptance.

[Megacity Metro native scene integration](Integrations/MegacityMetroNative/README.md)
pins Unity's official application at `07652ee74a1f322c2c3e607020f07be720175680`.
Its [new large-acceptance attempt](Docs/EXTERNAL_MEGACITY_ACCEPTANCE_2026-09-14.md)
has a complete IL2CPP Player and a repaired Menu-camera observation failure.
The repair's new link was interrupted by external Unity work; Main, six-SubScene
content and Megacity policy comparisons remain unaccepted.
It retains the original Menu/Main scenes, SubScenes, materials, gameplay, camera
and ECS simulation. The optional package observes asynchronous SubScene requests;
it creates no render workload. This is an **external application scene**, not a
cross-engine standard PSO benchmark.

The source verifier pins license/dependency/scene bytes, detects changed working
files and missing LFS assets, and prepares a manifest overlay without starting
Unity. A shared contract records cold, all-at-once, scheduled and observed-budget
inputs, plus a separate fixed-progressive backend requirement.
Missing build/plan/route/cache evidence stays null.
The pinned 6000.1 bulk backend cannot stand in for a fixed-progressive baseline.

The [older Megacity experiment](Integrations/MegacityMetro/README.md) retains its
48 generated material instances and scene controls as a separate historical
PSO-isolation cell. Its results do not transfer to the native scene adapter.

## Safe first check

From a short checkout such as `C:/src/pso`, with .NET 8+ and Python 3.12:

```powershell
python -m pip install -r Tools/requirements-validation.txt
pwsh Tools/Invoke-PsoValidation.ps1
```

This entry runs an explicit allowlist of deterministic CPU checks. Optional
[Unity API reference compilation](Docs/NON_PERFORMANCE_VALIDATION.md) validates
source against installed Editor DLLs without starting Editor or Player. PR CI
uses the same CPU entry on Windows and Linux. Historical runtime/capture/search
scripts remain separate, explicitly invoked commands. Safe validation and CI
do not call them.

## Production integration

Install `Packages/com.yanagisawa.shader-hitch-pipeline` as a local UPM package.
The optional native scene and Addressables dependencies remain outside the base
package. Generated traces, installed plans, build identity and local preparations
remain separate from source assets.

- [Trace → plan → build → runtime → feedback](Docs/INTEGRATION.md): separate capture sessions and compatible collection merging, declared actual build inputs, plan installation and missed-state feedback.
- [Compatibility](Docs/COMPATIBILITY.md): engine patch, shader/content/build identity, platform/API/quality, device and cost environment invalidation.
- [Streaming ownership](Integrations/Addressables/README.md): retain material/shader assets until submitted jobs fence; cancel queued demand and reject stale revisions.
- [Scheduling options](Docs/SCHEDULING_OPTIONS.md): fixed-progressive and observed-budget selection, independent priority/adaptation controls, cancellation, reactivation and status.
- [Non-performance validation](Docs/NON_PERFORMANCE_VALIDATION.md): exact allowed checks, compiler limits and external preparation.

A finite deadline is relative to phase activation. Budget admission is a model
of whether to submit work; it cannot bound an opaque non-preemptible driver job.
Cancellation is distinct from completion. Native SubScene readiness can occur
after its first draw, so observing a load-ready event does not prove first-frame
PSO coverage. Runtime evidence must retain full frames, warmup windows,
deadline/infeasibility outcomes, cancelled/unused work and coverage separately.

## Repository

```text
Packages/com.yanagisawa.shader-hitch-pipeline/   Core, runtime, editor, samples
DotNet/                                       CPU checks and reference-only compile
Integrations/MegacityMetroNative/              Pinned original-scene observer
Integrations/MegacityMetro/                    Historical controlled reveal
Integrations/Addressables/                     Optional streaming integration
Tools/                                        Validation, preparation, separate historical runners
Docs/                                         Contracts and explicitly sourced history
```

## Ownership

Copyright © 2026 Edwin Liu. See [LICENSE.md](LICENSE.md) and
[PROVENANCE.md](PROVENANCE.md). No employer content is included. Megacity assets
are not redistributed; the pinned upstream [license notice](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/LICENCE.md)
identifies the Unity Companion License. The repository's limited benchmark
reproduction permission does not replace third-party rights.
