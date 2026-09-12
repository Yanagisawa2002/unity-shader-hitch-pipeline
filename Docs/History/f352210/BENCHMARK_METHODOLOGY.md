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
- 60 FPS external capture of the same 1280×720 Player client area for all modes.
- 8.33 ms hitch threshold (missed 120 FPS budget).
- 16.67 ms severe-stall threshold, with zero severe-regression allowance.
- Same application version, scene, workload, resolution, device, API, and quality.
- Baseline trace is the cold measurement and training source.
- All-at-once and scheduled sampling begin only after 389/389 completion plus the same one-second stabilization delay used by the cold run.
- The same desktop-region recorder is active during all measured runs.
- All-at-once and scheduled use the same worker count selected by the policy search.

The final player has a different build GUID because it embeds the generated plan. The comparator deliberately requires every relevant environment field to match while recording, but not equating, the two build GUIDs.

## Result

| Metric | Cold | Unity all-at-once | Deadline scheduled | Scheduled improvement |
|---|---:|---:|---:|---:|
| Mean | 8.861 ms | 4.169 ms | 4.201 ms | 52.6% |
| P95 | 35.706 ms | 4.169 ms | 4.210 ms | 88.2% |
| P99 | 40.674 ms | 4.203 ms | 4.279 ms | 89.5% |
| Maximum | 46.387 ms | 4.436 ms | 9.799 ms | 78.9% |
| Hitches ≥ 8.33 ms | 48 | 0 | 1 | 47 eliminated |
| Severe stalls ≥ 16.67 ms | 48 | 0 | 0 | 48 eliminated |

Trace coverage was 388 variants / 389 graphics states. The 24-trial policy search selected two workers / batch 64, with a 577.8 ms median warmup and 30.6 ms median worst warmup frame under the hidden search harness. With the external recorder and foreground-window control active, all-at-once completed in 643.5 ms and scheduled in 774.6 ms. Scheduled warmup-window P95 was 12.9 ms versus 127.0 ms for all-at-once; one early scheduled outlier made its P99 145.7 ms and maximum 178.9 ms, so no tail advantage is claimed for this run. The plan-scoped feedback trace remained 389 expected / 389 observed states with zero misses in both prewarmed modes. These warmup numbers include real presentation/capture interference and are not substituted for runtime workload statistics. The full machine-readable result is [comparison.json](Media/comparison.json), the workload triptych is available as [GIF](Media/actual-comparison.gif) and [MP4](Media/actual-comparison.mp4), the warmup period as [MP4](Media/actual-warmup-comparison.mp4) and [GIF](Media/actual-warmup-comparison.gif), and the secondary raw-sample visualization as [comparison.gif](Media/comparison.gif).

The parity gate reports every ≥8.33 ms frame. To avoid turning one recorder/OS scheduling event into a false PSO regression, it permits at most 0.5% isolated non-severe frames (one of 240 here), while requiring zero regression at ≥16.67 ms, matching P95 within 5%, validating warmup completion and plan hashes, and requiring zero feedback-trace misses. Exact zero-jitter parity is recorded separately and is false for this run; the isolated scheduled frame is sample 6 at 9.799 ms, with 4.17 ms neighbors and zero `Shader.CreateGPUProgram` time.

## Actual-picture capture and synchronization

The runner does not use Unity screenshots because an every-frame GPU readback would contaminate the workload being measured. It records the visible Windows Player client region externally with ffmpeg. Title-based GDI capture is not used: D3D12 flip-model swapchains can appear black through that route. The runner resolves the owned Player's Win32 client rectangle, keeps that window unobscured during its six-second capture, and rejects video whose luma range indicates a blank or obscured frame.

Runtime markers retain logical timing, but process and encoder startup clocks are not precise enough for visual A/B/C alignment. For each video, the compositor first computes `WORKLOAD_START` in video time. Pixel search is forbidden before that point. It then examines the first tile band, requires four persistent frames above a median pre-roll reference, and aligns all three inputs on that visible event. A two-frame seek guard avoids timestamp-rounding bias. The acceptance alignment data is retained in [visual-alignment.json](Media/visual-alignment.json).

The scanline and clock are ordinary Player GUI drawing driven by `realtimeSinceStartup`; a real presentation freeze therefore holds and then jumps them. Workload frame history, current/worst frame, missed-presentation milliseconds, and hitch count are atomically reset at `WORKLOAD_START`. Warmup receipts independently begin their frame window only after the first Unity warmup job is scheduled.

For cache-miss feedback, the runtime pre-seeds an independent plan-scoped trace collection with every planned phase before calling `BeginTrace`. Miss count is `observed states - baseline states`. This is intentional: Unity's `traceCacheMisses` flag is scoped to one `WarmUp` call and would otherwise classify states from earlier progressive batches as misses. A saved feedback collection therefore includes the baseline and is marked as such; the merge stage deduplicates it.

## Interpretation limits

Driver disk caches can make repeated cold runs look warm. For release gating, use a controlled CI image or a documented cache-reset procedure owned by the hardware lab. Never delete broad user/driver caches from this tool automatically.

The showcase additionally generates one compile-time shader cache-buster per acceptance invocation. The training and final builds share it; later invocations receive different shader bytecode. Its only rendered effect is a sub-pixel color delta. This protects the sample from reusing a prior showcase run's PSO while preserving an identical workload inside the current A/B/C experiment. It is a demo-evidence mechanism, not a replacement for clean hardware images in a consuming game.

Desktop-region capture requires the Player to remain visible and unobscured. It is a portfolio/acceptance evidence path for Windows; headless performance gates should use the JSON benchmark receipts and a controlled hardware image.

The built-in Profiler markers named by Unity are recorded when exposed, but availability differs by player configuration. Percentiles are computed from raw `Time.unscaledDeltaTime` samples, and the full arrays are retained in each benchmark receipt.
