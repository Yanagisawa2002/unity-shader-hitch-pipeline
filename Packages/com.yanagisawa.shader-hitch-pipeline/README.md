# Shader Hitch Pipeline package

Schedule graphics-state warmup around asynchronous content loading, with explicit
policy choices, coverage and resource ownership. Unity and the graphics driver
perform native compilation and pipeline creation; this package captures states,
validates identity and coordinates when work is submitted.

The working package is **0.3.0, unreleased**. New policies and lifecycle/native
scene integration are **Unmeasured**. The existing `scheduled` policy remains
default; `observed-budget` and `fixed-progressive` require explicit selection.

## Start with the scheduling tradeoff

The retained historical experiment reported warmup-window P95 of
**127.0 → 12.9 ms**, with total warmup **643.5 → 774.6 ms** and a **178.9 ms
outlier**. Post-warmup workload P95 was 4.169 ms all-at-once versus 4.210 ms
scheduled. These are historical observations, not current-source results.
The cold-start reduction establishes the value of prewarming rather than the
scheduler's incremental contribution.

[Policy adoption and source report](../../Docs/POLICY_ADOPTION.md) ·
[Actual Core API example with a simulated sink](../../DotNet/ShaderHitchPipeline.PolicyExample/README.md)

## Install and integrate

Use **Tools > Shader Hitch Pipeline > Control Center** for inbox processing and
plan installation. Import **PSO Hitch Showcase** or **Deadline Run** to inspect
the existing demonstrations. They are project-specific scenes; their recorded
results do not become a general PSO benchmark or evidence for changed source.

The normal integration is trace → validated plan → attested build → dependency-ready
phase activation → runtime feedback. Configure the policy before activation.
Keep shader/material resources alive until submitted native work finishes, even
after a load request is cancelled or unloaded. A later load needs its own
activation identity; a stale callback must not revive earlier demand.
The portable request lifecycle tracks demand, not external asset leases or native
fence completion. A batch finishing is also distinct from complete collection
warmup. The CPU example models those events separately without executing them
in Unity.

- [Trace, build and feedback](../../Docs/INTEGRATION.md)
- [Policies and cancellation/reload lifecycle](../../Docs/SCHEDULING_OPTIONS.md)
- [Optional Addressables resource owner](../../Integrations/Addressables/README.md)
- [Compatibility and cost invalidation](../../Docs/COMPATIBILITY.md)
- [CPU checks and Unity API reference compilation](../../Docs/NON_PERFORMANCE_VALIDATION.md)

## Compare the policy, not just cold versus warm

The external application contract has four arms: cold, all-at-once, fixed
progressive and the candidate policy. Preserve identical engine/dependency
versions, player/shader build, content, resolved collections, routes, activation
events and cache protocol. Report full-frame tails, warmup-window
pressure, completion time, deadline outcomes, unused work and useful coverage.

[Native Megacity integration](../../Integrations/MegacityMetroNative/README.md)
uses an official application with pinned source/dependencies. The pinned Unity
6000.1 bulk backend cannot stand in for fixed progressive. Using a more capable
engine version creates a different cell; all comparison arms must share it.

Admission estimates do not impose a hard upper bound on non-preemptible driver
jobs. Load-ready events do not retroactively prove first-draw state coverage.
Missing or unarmed feedback remains unavailable, never a zero-miss claim.

## Supported API boundary

The package has Unity compatibility seams for D3D12, Metal and Vulkan; actual
runtime validation remains version/platform-specific. Capture distinct profiles
for platform, API, quality and materially changed shader/content/build identity.
Consult the compatibility guide for the exact Unity namespace/signature path
instead of inferring universal tested support from an API name.

Copyright © 2026 Edwin Liu. All rights reserved.
