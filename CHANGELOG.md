# Changelog

## 0.2.0 - 2026-09-02

- Added deadline-, observed-cost-, and hot-set-aware batch scheduling.
- Added hardware-local worker-count and batch-size Pareto search.
- Added measured warmup-frame receipts with clean measurement boundaries.
- Added a synchronized cold / Unity all-at-once / scheduled Player triptych.
- Added a live presentation scanline, clock, and workload-reset frame metrics.
- Added plan-scoped, pre-seeded feedback tracing so progressive batches cannot be misreported as cache misses.
- Added warmup/hash/miss evidence gates and explicit presentation-versus-severe hitch policy.

## 0.1.0 - 2026-09-01

- Added runtime graphics-state tracing with phase/session manifests.
- Added deterministic collection merging and merge receipts.
- Added progressive, frame-budget-aware warmup and cache-miss capture.
- Added build validation, benchmark receipts, A/B scripts, and report generation.
- Added an independent 384-state shader showcase and measured GIF workflow.
