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
                        adaptive progressive runtime warmup
                                                │
                      warmup/cache-miss/benchmark receipts
                                                ▼
                actual Player MP4/GIF + JSON/Markdown/PNG evidence
```

## Runtime boundary

`PsoTraceSession` is the only capture primitive. It stamps a collection with runtime platform, graphics API, and quality, starts Unity tracing, and atomically writes the collection and its manifest.

`PsoWarmupOrchestrator` loads only a verified plan. It rejects environment drift before loading any collection, schedules `WarmUpProgressively`, and adjusts each batch according to an exponentially weighted frame-time signal. The product exposes phases, not Unity-specific shader objects, to application code.

`PsoBenchmarkController` runs from command-line configuration. A cold run bypasses warmup; an optimized run waits for plan completion and applies the same post-ready stabilization delay. It retains every frame sample so percentile claims can be audited or re-plotted.

The showcase evidence runner captures the owned Player's visible client region without modifying the render loop, rejects blank video, detects the first visible tile transition in both recordings, and composes a synchronized side-by-side MP4/GIF. This visual path is evidence layered over the benchmark; it is not used to fabricate or replay frame timings.

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
- Performance regression: preserve reports and return a non-zero evidence-gate exit code.

## Evolution boundary

Unity's graphics-state API is experimental. All direct calls live in `PsoTraceSession`, `PsoWarmupOrchestrator`, and `PsoInboxProcessor`. The serialized documents, command line, phase model, evidence schema, and build policy do not expose Unity's nested variant/state structures. This limits future Unity API migration to three adapters rather than every consuming project.
