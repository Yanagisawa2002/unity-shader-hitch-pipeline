# Shader Hitch Pipeline

Unity 6 runtime/editor tooling for tracing real shader variant + graphics state combinations, deterministically merging profiles, scheduling modern PSO warmup by deadline/cost/hot-set value, gating builds, and producing auditable A/B/C evidence.

Use **Tools > Shader Hitch Pipeline > Control Center** for inbox processing and plan installation. Import the **PSO Hitch Showcase** sample for an end-to-end D3D12 demonstration.

Core capabilities:

- phase-aware `GraphicsStateCollection` trace sessions;
- environment isolation and SHA-256 provenance;
- deduplicated profiles and merge receipts;
- deadline-, cost-, probability-, and hot-set-aware `WarmUpProgressively` orchestration;
- a Unity all-at-once control strategy and worker/batch Pareto search;
- plan-scoped feedback tracing that does not mistake earlier progressive batches for misses;
- build target/API validation;
- cold/all-at-once/scheduled raw-frame benchmarks;
- a sample runner that records and synchronizes three actual Players.

Supported modern graphics APIs are D3D12, Metal, and Vulkan. Unity's API is experimental; capture a distinct profile per platform, API, quality, and materially different shader build.

Copyright © 2026 Edwin Liu. All rights reserved.
