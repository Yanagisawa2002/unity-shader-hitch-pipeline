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
                 deadline + cost + hot-set runtime scheduler
                                                │
                      warmup/cache-miss/benchmark receipts
                                                ▼
           actual Player A/B/C MP4/GIF + JSON/Markdown/PNG evidence
```

## Runtime boundary

`PsoTraceSession` is the only capture primitive. It stamps a collection with runtime platform, graphics API, and quality, starts Unity tracing, and atomically writes the collection and its manifest.

`PsoWarmupOrchestrator` loads only a verified plan and rejects environment drift before loading any collection. Activated phases remain independent work items. At every completed batch, `PsoDeadlineCostScheduler` can choose a different phase. Work with threatened deadline slack wins first; otherwise the comparison is hot-set tier, expected-use value per estimated remaining millisecond, stable priority, effective deadline, and phase name. The product exposes phases, not Unity-specific shader objects, to application code.

`PsoAdaptiveBatchPolicy` retains the frame-time EWMA but also learns wall-clock milliseconds per completed state. It computes the batch needed to meet a finite deadline and expands a tier-0 hot set only when frame headroom permits. One Unity warmup job is in flight at a time, so the scheduler controls pressure instead of creating an unbounded second concurrency layer over Unity's own async PSO workers.

The `scheduled` strategy uses `WarmUpProgressively`; the `throughput` strategy invokes Unity's all-at-once `WarmUp` once per phase as a truthful baseline. Both write the same receipt schema: clean warmup-window frame distribution, batch sizes/durations, learned cost, minimum slack, deadline result, and exact environment. The measurement window starts only after a real warmup job is scheduled, so scene/bootstrap frames cannot contaminate the maximum.

Cache-miss evidence is plan-scoped rather than batch-scoped. The orchestrator appends every phase collection into an independent baseline collection and calls `BeginTrace` on it before warmup. Planned states encountered by either warmup strategy deduplicate against that baseline; only genuinely new runtime states increase the observed count. This avoids Unity's per-`WarmUp` `traceCacheMisses` scope, which would misclassify earlier progressive batches, and it also supports multiple schedulable phases without competing global traces. A non-empty feedback file contains baseline plus additions and is explicitly marked for deduplicating merge.

`PsoBenchmarkController` runs from command-line configuration. A cold run bypasses warmup; all prewarmed modes wait for plan completion and apply the same post-ready stabilization delay. It retains every frame sample so percentile claims can be audited or re-plotted.

The showcase evidence runner captures the owned Player's visible client region without modifying the render loop and rejects blank video. A marker bounds each pixel search: a reveal can never be detected before that Player's `WORKLOAD_START`. Four persistent luma frames identify the first visible tile, after which cold, all-at-once, and scheduled videos are aligned into a triptych. A separate triptych retains the real warmup period. This visual path is evidence layered over the benchmark; it is not used to fabricate or replay frame timings.

`Find-PsoWarmupPolicy.ps1` runs a configured worker-count × batch-size matrix, rotates candidate order across repetitions, enforces a per-trial watchdog, and emits raw trials, aggregates, a Pareto frontier, and one recommendation. After deadline/frame guards, elapsed times within 2% of the fastest eligible candidate form a noise-equivalent band; hitch count and worst-frame pressure then win before raw elapsed time. The showcase applies the selected worker count to both all-at-once and scheduled runs so their Unity concurrency is controlled. Driver caches are never deleted by the tool; controlled hardware images remain necessary for true cold policy searches.

## Editor boundary

`PsoInboxProcessor` treats trace files as untrusted inputs. It verifies schema, successful capture, non-empty coverage, child paths, file hashes, embedded collection metadata, and the selected environment profile. Collections from another API, platform, or quality never enter the same plan.

Within a phase, Unity's `GraphicsStateCollection.Append` performs semantic merging and deduplication. The receipt records states before, after, and added for every input. The plan stores each collection hash plus a deterministic content hash that excludes only its own `planSha256` field.

`PsoPlanInstaller` uses a staging directory and validates the staged copy before swapping it under `Assets/StreamingAssets`. `PsoBuildValidator` then checks the embedded plan against the build target and enabled graphics APIs.

## Failure policy

- Empty trace: reject; it normally indicates that no visible GPU frames were submitted.
- Hash mismatch or path traversal: reject.
- Mixed environment: isolate; never coerce or merge.
- Missing required collection: reject plan and build.
- Runtime environment mismatch: fail warmup and write a receipt.
- Partial shutdown: complete finished jobs when safe and write a partial receipt.
- Policy-search timeout: terminate only the owned Player, retain the failed trial, continue.
- Performance regression: report every missed 120 Hz budget, allow at most 0.5% isolated non-severe presentation jitter, require zero severe-stall regression and zero plan-trace misses, preserve reports, and return a non-zero evidence-gate exit code.

## Evolution boundary

Unity's graphics-state API is experimental. All direct calls live in `PsoTraceSession`, `PsoWarmupOrchestrator`, and `PsoInboxProcessor`. The serialized documents, command line, phase model, evidence schema, and build policy do not expose Unity's nested variant/state structures. This limits future Unity API migration to three adapters rather than every consuming project.
