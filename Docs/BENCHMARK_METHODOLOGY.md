# Benchmark methodology

## Acceptance workload

The sample creates 384 preallocated quads with 256 local shader-keyword combinations and hundreds of distinct blend, depth, and cull configurations. Renderers stay disabled until the measurement window, so object/material setup is not mistaken for first-use GPU pipeline work. Eight previously unseen combinations become visible per frame.

There are no artificial sleeps, busy loops on the CPU, or fabricated frame times. The intentionally complex shader affects real GPU-program/PSO creation only.

## Controlled variables

- Unity 6000.5.2f1.
- WindowsPlayer / Direct3D12 / Ultra quality.
- AMD Radeon AI PRO R9700, driver 32.0.31041.1004.
- 1280×720 window, VSync off, target 240 FPS.
- 240 retained samples per run.
- 8.33 ms hitch threshold (missed 120 FPS budget).
- Same application version, scene, workload, resolution, device, API, and quality.
- Baseline trace is the cold measurement and training source.
- Optimized sampling begins only after 389/389 warmup completion plus the same one-second stabilization delay used by the cold run.

The final player has a different build GUID because it embeds the generated plan. The comparator deliberately requires every relevant environment field to match while recording, but not equating, the two build GUIDs.

## Result

| Metric | Cold | Prewarmed | Improvement |
|---|---:|---:|---:|
| Mean | 6.466 ms | 4.167 ms | 35.6% |
| P95 | 26.738 ms | 4.169 ms | 84.4% |
| P99 | 34.941 ms | 4.172 ms | 88.1% |
| Maximum | 39.643 ms | 4.271 ms | 89.2% |
| Hitches ≥ 8.33 ms | 24 | 0 | 24 eliminated |

Trace coverage was 260 variants / 389 graphics states. The final build completed all 389 warmup states in 228.6 ms on the acceptance device. The full machine-readable result is [comparison.json](Media/comparison.json), and the raw measured visualization is [comparison.gif](Media/comparison.gif).

## Interpretation limits

Driver disk caches can make repeated cold runs look warm. For release gating, use a controlled CI image or a documented cache-reset procedure owned by the hardware lab. Never delete broad user/driver caches from this tool automatically.

The built-in Profiler markers named by Unity are recorded when exposed, but availability differs by player configuration. Percentiles are computed from raw `Time.unscaledDeltaTime` samples, and the full arrays are retained in each benchmark receipt.
