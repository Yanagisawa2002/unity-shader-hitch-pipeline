# Shader Hitch Pipeline

Unity 6 runtime/editor tooling for tracing real shader variant + graphics state combinations, deterministically merging profiles, progressively prewarming modern PSOs, gating builds, and producing auditable A/B evidence.

Use **Tools > Shader Hitch Pipeline > Control Center** for inbox processing and plan installation. Import the **PSO Hitch Showcase** sample for an end-to-end D3D12 demonstration.

Core capabilities:

- phase-aware `GraphicsStateCollection` trace sessions;
- environment isolation and SHA-256 provenance;
- deduplicated profiles and merge receipts;
- adaptive `WarmUpProgressively` orchestration;
- build target/API validation;
- cold/prewarmed raw-frame benchmarks.

Supported modern graphics APIs are D3D12, Metal, and Vulkan. Unity's API is experimental; capture a distinct profile per platform, API, quality, and materially different shader build.

Copyright © 2026 Edwin Liu. All rights reserved.
