# Benchmark methodology

## Acceptance workload

The sample creates 384 preallocated quads with 384 selected combinations across nine local shader features and hundreds of distinct blend, depth, and cull configurations. Renderers stay disabled until the measurement window, so object/material setup is not mistaken for first-use GPU pipeline work. Eight previously unseen combinations become visible per 60 Hz reveal interval. A cold hitch delays the next batch instead of allowing catch-up, which makes the real presentation freeze visible without adding synthetic delay.

There are no artificial sleeps, busy loops on the CPU, or fabricated frame times. The intentionally complex shader affects real GPU-program/PSO creation only.

## Controlled variables

- Unity 6000.5.2f1.
- WindowsPlayer / Direct3D12 / Ultra quality.
- AMD Radeon AI PRO R9700, driver 32.0.31041.1004.
- 1280×720 window, VSync off, target 240 FPS.
- 240 retained samples per run.
- 60 FPS external capture of the same 1280×720 Player client area for both modes.
- 8.33 ms hitch threshold (missed 120 FPS budget).
- Same application version, scene, workload, resolution, device, API, and quality.
- Baseline trace is the cold measurement and training source.
- Optimized sampling begins only after 389/389 warmup completion plus the same one-second stabilization delay used by the cold run.
- The same desktop-region recorder is active during both measured runs.

The final player has a different build GUID because it embeds the generated plan. The comparator deliberately requires every relevant environment field to match while recording, but not equating, the two build GUIDs.

## Result

| Metric | Cold | Prewarmed | Improvement |
|---|---:|---:|---:|
| Mean | 8.535 ms | 4.167 ms | 51.2% |
| P95 | 34.300 ms | 4.169 ms | 87.8% |
| P99 | 37.467 ms | 4.169 ms | 88.9% |
| Maximum | 48.943 ms | 4.174 ms | 91.5% |
| Hitches ≥ 8.33 ms | 48 | 0 | 48 eliminated |

Trace coverage was 388 variants / 389 graphics states. The final build completed all 389 warmup states in 243.9 ms on the acceptance device. The full machine-readable result is [comparison.json](Media/comparison.json), the actual Player comparison is available as [GIF](Media/actual-comparison.gif) and [MP4](Media/actual-comparison.mp4), and the secondary raw-sample visualization is [comparison.gif](Media/comparison.gif).

## Actual-picture capture and synchronization

The runner does not use Unity screenshots because an every-frame GPU readback would contaminate the workload being measured. It records the visible Windows Player client region externally with ffmpeg. Title-based GDI capture is not used: D3D12 flip-model swapchains can appear black through that route. The runner resolves the owned Player's Win32 client rectangle, keeps that window unobscured during its six-second capture, and rejects video whose luma range indicates a blank or obscured frame.

Runtime markers retain logical timing, but process and encoder startup clocks are not precise enough for visual A/B alignment. The compositor therefore examines the actual video pixels in the first tile band, finds the first persistent transition from the empty scene, and aligns both inputs on that visible event. A two-frame seek guard avoids timestamp-rounding bias. The acceptance alignment data is retained in [visual-alignment.json](Media/visual-alignment.json).

## Interpretation limits

Driver disk caches can make repeated cold runs look warm. For release gating, use a controlled CI image or a documented cache-reset procedure owned by the hardware lab. Never delete broad user/driver caches from this tool automatically.

Desktop-region capture requires the Player to remain visible and unobscured. It is a portfolio/acceptance evidence path for Windows; headless performance gates should use the JSON benchmark receipts and a controlled hardware image.

The built-in Profiler markers named by Unity are recorded when exposed, but availability differs by player configuration. Percentiles are computed from raw `Time.unscaledDeltaTime` samples, and the full arrays are retained in each benchmark receipt.
