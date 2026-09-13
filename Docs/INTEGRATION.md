# Integration guide

The September 8 default workflow is **compile and deterministic CPU validation**;
runtime capture/search/Player steps below remain separate, explicitly invoked
stages. Use `Tools/Invoke-PsoValidation.ps1` for this branch's safe entry.
New policy and native external-scene work is **Unmeasured**.

For an unmodified official workload, use the [native Megacity scene adapter](../Integrations/MegacityMetroNative/README.md).
Its source/asset lock, additive UPM overlay, declared Menu/Main build helper and
async content lifecycle are separate from the historical generated reveal.
The [scheduling contract](SCHEDULING_OPTIONS.md) documents cancellation, late shader
resolution and unavailable feedback. New builds must declare the actual
`BuildPlayerOptions` before building so trace/plan/build identity can be checked.

## Package installation

Add the package directory through Package Manager, or reference a Git tag when this repository is hosted under your personal account. The package contains runtime and editor assemblies; generated profiles remain in the consuming project's ignored artifact directories.

## Capture design

Choose phases at content boundaries: `startup`, `city`, `combat`, `weather-rain`, or similar. A phase should be early enough to prewarm ahead of use and small enough to fit its loading-screen budget. Put the must-not-hitch path in hot-set tier 0 and colder speculative work in higher tiers; do not label the entire application hot.

Capture representative material keywords, vertex layouts, render targets, MSAA modes, depth/stencil formats, and render states. Modern PSO identity is broader than a shader keyword list, which is why a legacy `ShaderVariantCollection` alone is not sufficient on D3D12/Metal/Vulkan.

Run each coverage scenario more than once. The merge is idempotent: duplicate inputs are accepted but add zero states, and the receipt makes that visible.

## Optional runtime evidence pipeline

These are separately invoked runtime stages from the existing evidence workflow.
The PR compile/CPU workflow does not execute them.

1. Build a development training player with `-pso-training-build`.
2. Execute deterministic representative scenarios with `-pso-trace`.
3. Process the inbox and archive the merge receipt.
4. Review state/variant growth. Unexpected large growth often signals keyword or render-state explosion.
5. Install the selected profile and build the candidate player normally.
6. Classify required startup hot sets versus genuinely deferred/speculative phases.
7. Search worker/dynamic-maximum policy for deferred phases on each controlled target image.
8. Run cold, Unity all-at-once, and hard-gated/scheduled benchmarks on the same pool.
9. Capture PresentMon v2 CSV for every matrix run and at least one WPR GPU ETL per cell.
10. Store raw receipts, plan/build hashes, tool manifest, CSV/ETL hashes, media, and matrix row.

## Application hooks

No code is required for startup warmup when a valid plan is embedded. By
default, a startup phase's required hot set completes before the first scene
frame. That time is reported as startup latency; it is not part of the
interactive frame-budget claim. If startup latency is too large, split the plan
into a smaller true hot set and deferred phases rather than weakening the gate.
For later phases:

```csharp
if (PsoWarmupOrchestrator.Instance != null)
    PsoWarmupOrchestrator.Instance.ActivatePhase("combat");
```

Deadlines are relative to activation, so activate before the content boundary
rather than on first render. Deadline pressure never overrides strict admission.
If a minimum batch cannot fit the modeled headroom, the receipt reports
infeasibility and work remains deferred. A deadline of zero means no deadline;
it does not mean immediate.

For manual trace orchestration:

```csharp
PsoTraceController controller = PsoTraceController.EnsureInstance();
controller.Configure(sessionId, outputDirectory, shouldSendToEditor: false);
controller.BeginPhase("combat", "nightly", "high-quality");
// Run the representative path.
PsoSessionManifest manifest = controller.EndPhase();
```

## Profile refresh policy

Recapture when shader source, shader stripping rules, render-pipeline version, render-target topology, graphics API, runtime platform, or quality configuration changes materially. Treat a plan as build input with provenance, not a permanent asset.

Feed plan-feedback collections into a later representative run only after review. The file is deliberately pre-seeded with the current plan (`collectionContainsBaseline: true`); `cacheMissGraphicsStates` is the observed-minus-baseline delta, and the normal merge deduplicates all known states. A nonzero delta is evidence that runtime content escaped the current plan, but blindly accepting every incidental editor/debug state can bloat startup work.

Policy-search recommendations are intentionally not universal. Candidate batch
sizes are ceilings, while cold bootstrap stays one. Keep the raw matrix and
rerun after GPU, driver, API, engine, or materially different shader-build
changes. The tool never deletes implementation-owned driver caches.

For public claims, use `Matrix/targets.json`. Megacity Metro is pinned to an
exact upstream commit and must use a fixed scene route and quality preset. Record
GPU name, PCI vendor, driver, OS, Unity version, pipeline revision, cache state,
and all artifact hashes. A missing NVIDIA/Intel machine or Windows ETW privilege
is a pending cell, not permission to extrapolate AMD results.

## Reusable deadline scene contract

For a predictable future reveal, wrap the scene boundary with
`PsoDeadlineScenario` instead of duplicating command-line mode handling and
warmup state checks. Supply monotonic realtime, call `AdvanceTrainingTrace`
during representative training, `Arm` when the measured route begins, and
`Tick` once per frame. The contract emits `CONTENT_REQUEST`, activates the
phase, observes readiness, and emits `CONTENT_REVEAL` at the unchanged deadline.
Call `CompleteMeasurement` at the exact end of the declared wall-clock slice.
`Arm`/`CompleteMeasurement` also gate `PsoBenchmarkController`, preventing a
high-FPS frame-count sample from ending before the scenario does. The scene
remains responsible for what becomes visible.

The Megacity integration under `Integrations/MegacityMetro` pins upstream commit
`07652ee74a1f322c2c3e607020f07be720175680`. Its editor binder assigns the
selected SubScene authoring renderer set to a dedicated layer before Entities
Graphics baking, so runtime reveal changes the camera mask rather than
performing ECS structural changes, instantiation, or streaming. Install it into
a separate Megacity checkout; do not copy Unity sample assets into this
repository.

Accepted local engine/visual run `20260903-054644` used the complete pinned
6,947-renderer corridor inventory with a declared 150 m street-canyon
PSO-isolation view. On Unity 6000.1.0f1 / D3D12 / Medium / 1280x720 / AMD Radeon
AI PRO R9700, cold reveal reached 227.658 ms and deadline scheduled stayed at
8.581 ms maximum with zero 16.67 ms misses, zero timed collections, zero budget
violations, and zero cache misses. This single accepted visual run does not
replace the separate five-run PresentMon/WPR requirement for promoting a public
hardware-matrix cell.
