# Architecture

## Data flow

```text
Representative player runs
        │  BeginTrace / EndTrace
        ▼
session.json + .graphicsstate ── SHA-256 ──► Inbox
                                                │
                           environment partition + Append/dedupe
                                                ▼
                                 profile plan + merge receipt
                                                │
                                staged/verified installation
                                                ▼
                       StreamingAssets/ShaderHitchPipeline
                                                │
                              build target/API gate + receipt
                                                ▼
          required-hot-set first-present gate + deferred scheduler
                                                │
                      warmup/cache-miss/benchmark receipts
                                                ▼
     Player A/B/C media + PresentMon/WPR + public hardware matrix
```

## Engine-neutral core and adapter boundary

`Packages/.../Core` is compiled with `noEngineReferences: true`. The same source
files are linked directly into `DotNet/ShaderHitchPipeline.Core`, targeting
`netstandard2.0`. Core owns versioned trace/plan/receipt contracts, structural
plan validation, statistics, hard-budget admission, and deadline/cost/hot-set
scheduling. It imports neither `UnityEngine` nor `UnityEditor`.

`IPsoTraceBackend`, `IPsoWarmupBackend`, and `IPsoWarmupBatch` are the opaque
artifact boundary. Unity's implementation wraps `GraphicsStateCollection`;
another engine or a native D3D12/Vulkan implementation can retain its own binary
artifact while reusing state counts, hashes, plans, scheduling, and evidence
semantics. Schema v3 binds every plan to `adapterId` and `adapterVersion` so an
artifact can never be silently consumed by the wrong backend.

## Unity runtime boundary

`PsoTraceSession` owns the portable trace lifecycle and delegates capture to
`PsoUnityGraphicsStateTraceBackend`. It stamps the artifact with runtime
platform, graphics API, quality, adapter id, and SHA-256 before atomically
writing its manifest.

`PsoWarmupOrchestrator` loads only a verified plan and rejects environment drift before loading any collection. Activated phases remain independent work items. At every completed batch, `PsoDeadlineCostScheduler` can choose a different phase. Work with threatened deadline slack wins first; otherwise the comparison is hot-set tier, expected-use value per estimated remaining millisecond, stable priority, effective deadline, and phase name. The product exposes phases, not Unity-specific shader objects, to application code.

Every phase artifact is loaded and environment-checked before interactive
measurement begins. Activation therefore transfers an already resident backend;
it performs no collection-file read. In benchmark mode, phase execution state
is preconstructed, while phase logs, receipt materialization/writes, and all
post-`CAPTURE_READY` scenario markers are deferred until sampling has finished
or the Player is shutting down. Timed sample lists and scheduler-candidate
storage are reserved up front so instrumentation growth is not confused with
driver work.

`PsoAdaptiveBatchPolicy` uses two bounded probes (cold and steady), a fixed+slope
online cost model, residual safety factor, frame EWMA, dynamic ceiling, cooldown,
and circuit breaker. Deadline demand is computed separately from the safe batch;
deadline pressure can report infeasibility but can never enlarge a batch beyond
the hard-admission result. One opaque warmup job is in flight at a time.

No scheduler can preempt a Unity/driver call after admission. The original
64-state first batch took 165.36 ms; a subsequent one-state experiment still
took 119.05 ms and produced a 131.59 ms frame. For `preinteractiveBootstrap`
startup phases, the orchestrator therefore completes the entire required hot set
inside `BeforeSceneLoad`. The receipt preserves this as
`preinteractiveBootstrapMilliseconds`; the hard interactive budget begins only
after the gate. This is a latency-domain boundary, not sample deletion. Deferred
phases use strict admission; if even the minimum predicted batch is unsafe, the
correct result is deferral/infeasibility rather than a deadline override.

The `scheduled` strategy hard-gates required startup phases. Deferred phases use
true `WarmUpProgressively` batches where the installed Unity implementation can
complete them predictably; Unity 6000.1 uses a deadline-gated native asynchronous
bulk submission because its experimental small progressive jobs can remain
pending for tens of seconds. The `throughput` strategy invokes Unity's
post-present all-at-once `WarmUp` as the control. Receipts distinguish
startup-gate latency, scheduler-admission validity, observed interactive frame
violations, minimum-batch infeasibility, and throughput's explicit lack of a
guarantee.

Cache-miss evidence is plan-scoped rather than batch-scoped. The orchestrator appends every phase collection into an independent baseline collection and calls `BeginTrace` on it before warmup. Planned states encountered by either warmup strategy deduplicate against that baseline; only genuinely new runtime states increase the observed count. This avoids Unity's per-`WarmUp` `traceCacheMisses` scope, which would misclassify earlier progressive batches, and it also supports multiple schedulable phases without competing global traces. A non-empty feedback file contains baseline plus additions and is explicitly marked for deduplicating merge.

`PsoBenchmarkController` runs from command-line configuration. A cold run bypasses warmup; prewarmed modes wait for the active startup phase and apply the same post-ready stabilization delay. A `PsoDeadlineScenario` installs a process-local measurement gate: `Arm` opens it and `CompleteMeasurement` closes it. The benchmark therefore retains the real wall-clock slice even when 720 frames would finish early, and reports its actual sample count and duration. Sampling continues while a later phase is activated, scheduled, revealed, and completed. Deferred readiness is queried by phase rather than from global orchestrator idleness, so unrelated concurrent predictions cannot manufacture a deadline miss.

The showcase has an eight-state startup seed and a separate 376-tile deferred `combat` phase. At `CONTENT_REQUEST`, throughput activates the whole phase in one batch while the deadline scheduler admits measured work; both receive 1.50 s before `CONTENT_REVEAL`, and every capture must reach `CONTENT_COMPLETE`. Two new states become visible per target-240-Hz presentation, so the same content rate is exercised without a showcase-only activation batch.

The evidence runner captures the owned Player's visible client region without modifying the render loop and rejects blank video. Four persistent luma frames identify `CONTENT_REVEAL` in pixels; all other events are placed from monotonic Player marker intervals. One chapter aligns cold and scheduled on first visible content, and another begins on the first presented frame after the mid-game request to compare all-at-once with scheduled. This visual path is evidence layered over retained benchmark and warmup receipts; it does not fabricate or replay frame timings.

`Find-PsoWarmupPolicy.ps1` runs a worker-count × dynamic-maximum matrix with the
first-present gate disabled so it measures the deferred scheduler. When given a
scenario and phase, every candidate runs the complete 12-second wall-clock
slice and exits only after both the scenario and generic sampler have finalized
their receipts. Minimum and cold bootstrap remain one. Qualification requires
every repetition to record zero runtime deadline misses, no measured-window GC,
the exact warmup budget, valid scheduler admission, and reserved foreground CPU
headroom; otherwise the output is visibly provisional. Driver caches are never
deleted by the tool.

`Invoke-PsoWindowsEvidence.ps1` starts PresentMon before its owned Player, can
add WPR's built-in GPU profile, and records exact commands, tool hashes, OS/GPU/
driver identity, exit codes, raw CSV/ETL hashes, and privilege failures. The CSV
analyzer selects the dominant swap chain and correlates UTC warmup windows when
timestamps permit. ETW rights are required; an access-denied run becomes a
failed manifest, never synthetic evidence. `pso_matrix.py` then aggregates only
explicit NVIDIA, AMD, and Intel runs across the controlled showcase and pinned
Megacity Metro scene.

## Editor boundary

`PsoInboxProcessor` treats trace files as untrusted inputs. It verifies schema, successful capture, non-empty coverage, child paths, file hashes, embedded collection metadata, and the selected environment profile. Collections from another API, platform, or quality never enter the same plan.

Within a phase, `PsoGraphicsStateCollectionCompatibility` performs semantic
merging and deduplication. Unity 6000.5+ delegates to
`GraphicsStateCollection.Append`; Unity 6000.0–6000.4 enumerates the public
variant/state API because the collection still lives under
`UnityEngine.Experimental.Rendering` and has no `Append` method. The receipt
records states before, after, and added for every input. The plan stores each
collection hash plus a deterministic content hash that excludes only its own
`planSha256` field.

`PsoPlanInstaller` uses a staging directory and validates the staged copy before swapping it under `Assets/StreamingAssets`. `PsoBuildValidator` then checks the embedded plan against the build target and enabled graphics APIs.

## Failure policy

- Empty trace: reject; it normally indicates that no visible GPU frames were submitted.
- Hash mismatch or path traversal: reject.
- Mixed environment: isolate; never coerce or merge.
- Missing required collection: reject plan and build.
- Runtime environment mismatch: fail warmup and write a receipt.
- Unsafe predicted batch: defer; a threatened deadline never overrides admission.
- Minimum-batch observed violation: mark the budget unachievable for that run.
- Required startup opaque work: finish before first presentation and report its latency.
- PresentMon/WPR privilege failure: retain a failed manifest and leave the matrix cell pending.
- Partial shutdown: complete finished jobs when safe and write a partial receipt.
- Policy-search timeout: terminate only the owned Player, retain the failed trial, continue.
- Performance regression: report every missed 120 Hz budget, allow at most 0.5% isolated non-severe presentation jitter, require zero severe-stall regression and zero plan-trace misses, preserve reports, and return a non-zero evidence-gate exit code.

## Evolution boundary

Unity's graphics-state API is experimental. Direct runtime calls live in
`PsoUnityGraphicsStateTraceBackend` and `PsoUnityGraphicsStateWarmupBackend`;
Unity-specific merge/install work remains in the Editor assembly. The core and
schema expose no Unity nested variant/state types, limiting an engine/API change
to the adapter instead of every consuming project.

## Scenario and visual-evidence boundary

`PsoDeadlineScenario` is the reusable event contract between a scene and the
pipeline. The scene supplies monotonic time and owns its camera/content; the
contract changes the trace phase, emits request/reveal/complete markers,
activates the planned phase, and exposes readiness/deadline state. It never
sleeps, advances a fake clock, creates presentation frames, or owns scene assets.

The evidence stack has three intentionally separate layers:

1. **PSO Hitch Showcase** remains the deterministic microbenchmark and CI gate.
2. **Deadline Run** is a 12-second actual-picture hero scene with world-space
   continuity evidence and a strict 80 ms cold / zero 16.67 ms scheduled gate.
3. **Megacity Metro** is a pinned external adapter proving the same contract in
   a third-party large scene without redistributing its assets.

Only retained receipts can promote either visual scene to a result. Source code,
a staged video, or an unexecuted acceptance configuration is not evidence.
