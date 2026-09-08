# Changelog

## 0.3.0 — Unreleased

September 8 repair: new policy and external-scene changes are **Unmeasured**.
Retained measurements in the earlier entries below refer to their historical
source/cells. The repair adds generation-aware content lifecycle, atomic
publication, finite/portable-path validation, opt-in scheduling alternatives,
fence/cancellation feedback and deferred shader-resolution integrity. Existing
policy defaults are not replaced by an unmeasured alternative.

- Added Deadline Run: a 12-second tunnel-to-combat
  scene, 320-combination real reveal, world-space continuity evidence, two-up hero and
  three-way technical compositor, and an 80 ms cold / zero 16.67 ms scheduled
  acceptance receipt. Accepted D3D12 run `20260903-031014` measured 1,039.290 ms
  cold versus 5.093 ms scheduled with zero scheduled misses.
- Added reusable `PsoDeadlineScenario` request/deadline/reveal orchestration and
  a pinned, asset-free Megacity Metro external adapter at commit
  `07652ee74a1f322c2c3e607020f07be720175680`.
- Added a Unity 6000.0–6000.4 compatibility seam for the experimental
  `GraphicsStateCollection` namespace, merge path, and warmup signatures while
  preserving the Unity 6000.5+ implementation.
- Removed evidence-path allocation noise by preallocating timed samples and
  scheduler buffers and caching the Deadline Run/Megacity HUD presentation state.
- Preloaded every phase artifact and preconstructed phase execution state before
  measurement; deferred receipt finalization, benchmark logs, post-ready marker
  writes, and JSON output so the evidence path does no timed disk I/O or bulk
  completion-time allocation.
- Added a scenario measurement gate so the generic sampler records the complete
  wall-clock slice instead of ending after an arbitrary frame count. Policy
  search now consumes that real slice and requires zero misses in every repeat.
- Added Megacity fixed-phase closed-camera preconditioning, post-recorder
  quiescence, native FrameTiming coverage, managed-GC counters, and camera-origin
  parity gates; non-training runs no longer poll Unity's native trace state.
  Accepted external run `20260903-054644` measured 227.658 ms cold versus
  8.581 ms scheduled, zero scheduled misses, zero timed GC, zero budget
  violations, and zero cache misses in the pinned third-party city scene.
- Replaced Megacity's allocating IMGUI value formatting with precomputed
  `GUIContent` lookup tables and an allocation-free latched hitch value, and
  moved reveal detection to the actual central neon corridor.

- Diagnosed the 178.9 ms tail as an opaque first-warmup backend cost; a one-state
  cold probe still reproduced a 131.59 ms frame, so the evidence gate now rejects
  the false assumption that batch sizing can preempt the driver.
- Added a required-hot-set first-present gate. Its full duration is reported as
  startup latency, while the hard interactive budget begins only after completion.
- Added strict batch admission, separate cold/steady probes, fixed+slope online
  cost fitting, safety margin, cooldown, circuit breaker, infeasibility outcomes,
  raw admissions, and exact observed budget violations.
- Corrected policy search so candidate batch sizes are dynamic ceilings; minimum
  remains one and no large candidate can become the forced cold batch.
- Extracted contracts, structural plan validation, statistics, backend interfaces,
  hard-budget policy, and deadline/cost/hot-set scheduler into a Unity-free Core
  assembly with a directly linked `netstandard2.0` build and smoke test.
- Added schema-v3 trace/plan/receipt JSON Schemas and explicit adapter identity.
- Added PresentMon v2 CSV analysis, optional WPR GPU ETL capture, privilege/error
  manifests, raw hashes, and warmup-window correlation.
- Added a pinned NVIDIA/AMD/Intel × controlled/Deadline Run/Megacity Metro
  public matrix.
  Current AMD engine evidence is provisional; missing vendor hardware and ETW
  privilege remain visible instead of being inferred or fabricated.
- Rebuilt the showcase around an eight-state startup seed plus a real deferred
  `combat` phase activated at a mid-game content request with a 1.50 s deadline.
- Added request/reveal/complete markers, request-scoped HUD statistics, continuous
  motion, two-state-per-presentation reveal, and two synchronized visual chapters.
- Extended acceptance to 1080 frames, required `CONTENT_COMPLETE`, anchored marker
  intervals to pixel-detected reveal, and added explicit Player/ffmpeg priorities.
- Added deferred-phase evidence and scorecards; the accepted D3D12 run recorded
  all-at-once at 15.902 ms / one miss versus scheduled at 7.867 ms / zero misses.

## 0.2.0 — 2026-09-02

- Added deadline, observed cost, expected-use probability, and hot-set tiers to plans.
- Added batch-boundary phase reprioritization and a throughput baseline strategy.
- Added warmup frame distributions, batch evidence, and exact receipt paths.
- Added `Find-PsoWarmupPolicy.ps1` for worker/batch Pareto search.
- Added a truthful three-Player visual comparison with workload-reset statistics.
- Replaced the portfolio hero with synchronized capture of three actual D3D12 Players.
- Added visible-client recording, blank-frame validation, first-tile pixel alignment, and MP4/GIF composition to the one-command showcase runner.
- Paced the 384-state workload independently of render FPS so real cold presentation freezes remain visible without simulated stalls.
- Added a pre-seeded plan-scoped feedback trace that remains truthful across progressive batches and multiple phases.
- Added strict warmup/hash/miss validation plus separately reported presentation-budget and severe-stall parity.

## 0.1.0 — 2026-09-01

- Added player-side graphics-state tracing and manifests.
- Added deterministic environment-partitioned merge profiles.
- Added verified plan installation and build target/API gate.
- Added adaptive phased progressive warmup and cache-miss receipts.
- Added raw-sample cold/prewarmed benchmark controller.
- Added the 384-state PSO Hitch Showcase and evidence renderer.
