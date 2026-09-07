# Shader Hitch Pipeline

Unity 6 runtime/editor tooling for tracing real shader variant + graphics state
combinations, deterministically merging profiles, hard-gating required startup
hot sets, scheduling deferred PSO work by budget/deadline/cost/hot-set value,
gating builds, and producing auditable A/B/C evidence.

Use **Tools > Shader Hitch Pipeline > Control Center** for inbox processing and plan installation. Import the **PSO Hitch Showcase** sample for an end-to-end D3D12 demonstration.

Import **Deadline Run** for the portfolio-facing 12-second tunnel-to-combat
benchmark. It uses the same runtime pipeline and receipt contracts while making
missed presentation continuity visible through the camera, turbines, rain,
target tracking, and a real trail. Its 80 ms cold / zero 16.67 ms scheduled gate
is evaluated by `Tools/validate_deadline_run.py`. Accepted D3D12 run
`20260903-031014` measured 1,039.290 ms cold versus 5.093 ms scheduled with zero
scheduled misses; a source scene or staged video without a passing receipt still
does not count as evidence.

Core capabilities:

- phase-aware `GraphicsStateCollection` trace sessions;
- environment isolation and SHA-256 provenance;
- deduplicated profiles and merge receipts;
- required startup hot sets completed before first presentation, with their full latency reported;
- cold/steady probes and strict deadline/cost/hot-set admission for deferred phases;
- a Unity all-at-once control strategy and worker/dynamic-ceiling Pareto search;
- plan-scoped feedback tracing that does not mistake earlier progressive batches for misses;
- build target/API validation;
- cold/all-at-once/scheduled raw-frame benchmarks;
- a sample runner that records and synchronizes three actual Players.
- a Unity-free Core assembly, `netstandard2.0` portability build, and versioned schemas;
- PresentMon/WPR evidence helpers and an explicit cross-vendor public matrix.

Supported modern graphics APIs are D3D12, Metal, and Vulkan. Unity 6000.0–6000.4
uses the package's experimental-namespace/signature compatibility seam; Unity
6000.5+ uses the promoted rendering namespace and native collection append.
Unity's API remains experimental; capture a distinct profile per platform, API,
quality, and materially different shader build.

Copyright © 2026 Edwin Liu. All rights reserved.
