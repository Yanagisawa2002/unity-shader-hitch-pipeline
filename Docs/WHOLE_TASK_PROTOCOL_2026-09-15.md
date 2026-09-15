# Shader whole-task opportunity gate

Status: discovery protocol, written before any new Player run. This is not a
performance result or a frozen winning candidate. Source branch starts at
PR 4 head `9c7fc7b75fa7ac1bca2e407821414cf5941e9b8c`; PR 4 stays unmerged.

## Actual caller and complete task

Use the official `com.unity.template.urp-sample@17.1.5` archive already bound by
`Integrations/Urp3DSample/source-lock.json`. Preserve the original BenchmarkScene,
Terminal, Garden, Oasis and Cockpit assets, cameras, benchmark code and full
Timelines. The caller loads each scene, renders its existing five-second warmup,
resets its Timeline, renders the complete route, and publishes native CSV.
The adapter exits only after all four Finished states and CSV publication.
This is a real official scene rendering workload with its original benchmark
route; it is not an interactive player study or a standardized PSO benchmark.

Upstream materials, scene objects and URP render passes produce the draws.
The graphics driver consumes shaders plus raster/depth/blend/render-target state
to create PSOs; the renderer consumes them on the GPU. Training produces native
graphics-state collections on the CPU, which are merged into a build-specific
plan and loaded for native warmup. There is no task-required GPU result readback.
Frames and native CSV are the output; CPU Update, render callback, GPU completion
and display presentation are different boundaries.

The old raw evidence and assets are not tracked. Initial scoped searches did not
find a reusable original host/archive; `C:/Users/EdwinLiu` does not exist here.
The current machine is Intel Core Ultra 7 265K / RTX 4090, not the old R9700 cell.
Reuse the existing task-owned Unity 6000.5.9f1 (`b57deb96f08d`) and its IL2CPP module
read-only from `D:/CodexWork/babel-dot-20260915/Artifacts/dot-20260915/unity`.
The 17.1.5 content / 6000.5.9f1 engine pairing is a new compatibility cell. Import,
package resolution, shader API compatibility and original-content identity must
be checked before it is eligible. No old measurement is relabeled for this cell.
If migration changes scene content or rendering semantics, retain its exact diff
and reject this pairing unless equivalent task output can be established.

## Discovery before optimization

1. Fetch the fixed archive only after the hardware queue gates open. Verify its
   exact length, SHA-256 and every original member using the existing preparer.
2. Import/build one new Development D3D12/IL2CPP Player. Preserve all attempts,
   logs, compiler failures, preparation durations, source/asset indexes and memory.
3. First launch: observe the complete original route with the pipeline disabled
   and `-pso-external-observer-only`; enable the opt-in whole-task profiler.
   This first process has no claimed driver-cold status. The first run is a
   diagnostic, with its instrumentation overhead declared.
4. Run two further complete original-route diagnostics with the identical Player
   and retained caches. Keep every long frame, failure, startup gap and incomplete
   collection. Do not choose another favorable scene after seeing these results.
5. Inspect actual shader/PSO marker samples, thread names, nested stacks, waits,
   frame metadata and loading/initialization activity. A same-frame event or native
   state-entry growth alone is insufficient causal attribution. Parallel/nested
   marker work cannot be added into wall time. Missing markers are unavailable.
6. Only if actual shader/PSO creation blocks a render/main-thread critical path
   and produces repeatable harmful intervals, proceed to a targeted candidate.
   Otherwise deliver NO-GO for a scheduling-benefit claim, with the remaining
   blind intervals and a concrete condition required for a later experiment.

The full binary profiler is diagnostic evidence, not a timing arm. Startup before
the profiler bootstrap and any profile import retention limit remain explicit.
The per-frame recorder retains supported markers across the whole route; the
binary's emitted Unity frame metadata establishes exact joins where available.
No inference of absence is made from a marker unavailable at bootstrap.

## Conditional comparison and freeze

The three required alternatives are the original app's native warmup traversal,
a reasonable native once-per-available-collection prewarm using the installed
Unity API, and this project's scheduler. The original path and pipeline-disabled
path with common retention/trace overhead are separate controls. Do not call
the project's throughput policy an independent external library. Unity's native
GraphicsStateCollection is the applicable engine alternative.

After discovery, freeze the exact candidate, Player/plan/input identities, native
API semantics, per-scene workloads, independent process count/order and resource
budget in a new file before formal runs. Minimum confirmation: four complete
processes per arm in balanced order, separate from tuning/training. All four
official scenes stay in every process, including the highest-cost scene and all
failures. Scene diversity is not automatically a small/medium/large size sweep;
report actual draws/state counts and identify untested scales explicitly.

Primary outcome: complete-route CPU Update excess time above 33.333 ms, with
all per-process maxima and counts above 16.67/33.333/50/200/500 ms. Pair it with
launch-to-first-render and launch-to-normal-exit, original loading/warmup spans,
per-scene first traversal and warmed-route distributions, memory and native
prewarm costs. Fixed Timeline end time is not a standalone speedup metric.
No CPU metric is labeled GPU/presentation. Native FrameTimingManager data stays
unvalidated unless checked against an independent device/presentation source.

Include import/build/training/merge/plan generation once, process startup and
prewarm on every relevant launch, resident native collections/retained shaders,
allocation, and capture publication. Declare amortization over 1, 5 and 20
launches; do not silently remove required preparation. Analyze uncertainty over
processes, not inner frames. Preserve all raw process data, including negative
arms; do not trim maxima or replace failed runs.

Cache condition for every stage: new process, application/OS/driver caches
retained and driver state unknown. First launch in this checkout is only a known
application lifecycle event. No global cache purge, cache-buster shader variants,
driver/power/affinity changes or unsupported vendor environment variables.
If only the initial unreproducible cache state has an opportunity, a fair
independent cache-cold comparison is unavailable; do not manufacture one.

## Hardware and stop conditions

Every heavy command must use `Invoke-PsoWholeTaskStage.ps1`, which checks the
three predecessor handoffs and then calls the existing native mutex/capacity/
process gate. An independent preflight samples CPU and GPU five times, rejecting
any CPU >20% or GPU >10%. Missing telemetry fails closed. Disk budgets retain
the existing 20 GiB reserve and 25 GiB early-stop threshold. Process-local TEMP
and package caches are under this checkout; these process-local environment
overrides are restored on exit. Existing package/driver caches and global
environment/configuration files are not edited.

Expected additional peaks: download/extract 6 GiB, import/build 80 GiB, profiler
20 GiB. These are conservative budgets, not measured peaks. All child processes
must finish or be stopped by their owning monitor before release. Workload,
capacity, licensing, content, backend or source-identity failures are retained.
Do not terminate external processes. Do not write another task's status/handoff.

If no benefit or necessary experimental condition exists, deliver source audit,
correctness/diagnostic evidence, commands, comparison limits, NO-GO and a bounded
resume condition. Release hardware only after all owned work ends; publish the
final handoff before notifying the coordinator. Commit/push this branch and open
a reviewable PR without merging PR 4 or editing résumé/company/other task files.

## Sources for the diagnostic contract

- [Unity shader prewarming](https://docs.unity3d.com/6000.1/Documentation/Manual/shader-prewarm.html)
  documents shader-program and PSO creation markers and the native collection API.
- [ProfilerRecorder](https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html)
  explains metric enumeration, frame aggregation and unmanaged ownership.
- [RawFrameDataView](https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Profiling.RawFrameDataView.html)
  exposes thread samples for offline diagnosis. Runtime APIs and actual coverage
  still require validation in the selected Editor.
