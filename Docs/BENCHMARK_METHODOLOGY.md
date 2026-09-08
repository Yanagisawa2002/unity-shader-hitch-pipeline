# Benchmark methodology

## Acceptance workload

The sample creates 384 preallocated quads with selected combinations across nine local shader features and hundreds of distinct blend, depth, and cull configurations. Eight startup seed tiles keep a continuously moving scene visible. The remaining 376 renderers belong to a real deferred `combat` trace phase and stay disabled until content reveal, so object/material setup is not mistaken for first-use GPU pipeline work.

The Player emits `CONTENT_REQUEST` 0.65 s after the measured gameplay window begins and gives both warmup policies a 1.50 s deadline. The throughput control submits the whole deferred phase at once; the deadline policy admits measured batches. At the deadline, content begins revealing at two new states per presentation with a 240 FPS target (nominally 480 states/s). Cold hitches naturally reduce presentation frequency; no catch-up loop or synthetic stall is added.

There are no artificial sleeps, busy loops on the CPU, or fabricated frame times. The intentionally complex shader affects real GPU-program/PSO creation only.

## Controlled variables

- Unity 6000.5.2f1.
- WindowsPlayer / Direct3D12 / Ultra quality.
- AMD Radeon AI PRO R9700, driver 32.0.31041.1004.
- 1280×720 window, VSync off, target 240 FPS.
- 1080 retained samples per run, covering request, deadline, reveal, and completion.
- 11 s / 60 FPS external capture of the same 1280×720 Player client area for all modes.
- 8.33 ms hitch threshold (missed 120 FPS budget).
- 16.67 ms severe-stall threshold, with zero severe-regression allowance.
- Same application version, scene, workload, resolution, device, API, and quality.
- Baseline trace is the cold measurement and training source.
- The same 13-entry startup phase gates gameplay; both deferred policies activate the same 388-entry `combat` phase exactly at `CONTENT_REQUEST`.
- The same desktop-region recorder is active during all measured runs.
- All three Players use Windows `High` priority and every ffmpeg process uses `BelowNormal`; these values are retained in alignment metadata.
- The live recorder is restricted to two ultrafast encoding threads; slow high-quality composition runs after the Player exits.
- Every raw capture must contain ordered `CONTENT_REQUEST`, `CONTENT_REVEAL`, and `CONTENT_COMPLETE` markers and pass luma validation.

The final player has a different build GUID because it embeds the generated plan. The comparator deliberately requires every relevant environment field to match while recording, but not equating, the two build GUIDs.

## Result

| Metric | Cold | Unity all-at-once | Deadline scheduled | Scheduled improvement |
|---|---:|---:|---:|---:|
| Mean | 4.743 ms | 4.220 ms | 4.173 ms | 12.0% |
| P95 | 8.372 ms | 4.169 ms | 4.169 ms | 50.2% |
| P99 | 10.030 ms | 4.268 ms | 4.363 ms | 56.5% |
| Maximum | 32.519 ms | 23.552 ms | 7.867 ms | 75.8% |
| Hitches ≥ 8.33 ms | 57 | 5 | 0 | 57 eliminated |
| Severe stalls ≥ 16.67 ms | 1 | 2 | 0 | 1 eliminated |

The full table reports every retained Player frame, including isolated recorder/OS events outside the deferred phase. The direct `combat` receipt is narrower and causal: Unity all-at-once submitted 388 states in one 40.175 ms phase and produced one 15.902 ms presentation miss; the deadline scheduler used 34 admitted batches over 152.346 ms, produced a 7.867 ms maximum, zero misses, zero budget violations, and met the same deadline. Finishing later but inside the content deadline is the intended exchange for preserving presentation.

The plan contains 400 phase-local variants / 401 graphics-state entries. Cross-phase overlap yields 389 unique observed states; plan-scoped feedback reported 389 expected / 389 observed and zero misses. Investigation of the earlier startup-only tail reproduced a 165.36 ms 64-state backend call and a 178.86 ms frame. An irreducible one-state cold probe still took 119.05 ms and yielded a 131.59 ms frame, falsifying the assumption that admission control can hard-bound an opaque driver call after entry.

In accepted run `20260902-064931`, the 13-entry startup phase stayed behind the preinteractive boundary. The real deferred phase then recorded zero scheduled misses in both its receipt and the full 1080-frame window. Five earlier process-cold AMD startup-gate runs produced median gameplay p99 4.229 ms and median gate latency 29.892 ms; those remain separate startup evidence rather than being relabeled as deferred evidence.

The machine-readable result is [comparison.json](Media/comparison.json). The two-chapter workload is available as [GIF](Media/actual-comparison.gif) and [MP4](Media/actual-comparison.mp4), with separate [first-use](Media/actual-first-use-comparison.mp4) and [deferred-request](Media/actual-warmup-comparison.mp4) videos, a [phase scorecard](Media/actual-scorecard.png), and the secondary [raw-sample animation](Media/comparison.gif).

The parity gate reports every ≥8.33 ms frame. It permits at most 0.5% isolated non-severe recorder/OS jitter while requiring zero severe-stall regression, matching P95 within 5%, validating hashes and hard-budget receipts, and requiring zero feedback-trace misses. The accepted scheduled run had no ≥8.33 ms frames, no severe stalls, no deferred budget violations, and `naiveParityExact=true`.

## Actual-picture capture and synchronization

The runner does not use Unity screenshots because an every-frame GPU readback would contaminate the workload being measured. It records the visible Windows Player client region externally with ffmpeg. Title-based GDI capture is not used: D3D12 flip-model swapchains can appear black through that route. The runner resolves the owned Player's Win32 client rectangle, keeps that window unobscured during its 11-second capture, and rejects video whose luma range indicates a blank or obscured frame.

Runtime markers retain logical timing, but the marker-file poller and encoder startup clocks are not precise enough for visual A/B/C alignment. For each video, the compositor searches only around marker-derived `CONTENT_REVEAL`, examines the first tile band, and requires four persistent frames above a median pre-roll reference. That pixel frame becomes the absolute video anchor; `CONTENT_REQUEST`, `DEFERRED_READY`, and `CONTENT_COMPLETE` are recovered from the Player's monotonic realtime intervals. The first-use chapter retains 0.35 s of lead-in. The deferred chapter begins on the first presented 60 Hz frame after `CONTENT_REQUEST`, so no pre-request recorder noise is attributed to either policy. Alignment data is retained in [visual-alignment.json](Media/visual-alignment.json).

The scanline and clock are ordinary Player GUI drawing driven by `realtimeSinceStartup`; a real presentation freeze therefore holds and then jumps them. Visual frame history, current/worst frame, missed-presentation milliseconds, and hitch count are atomically reset at `CONTENT_REQUEST`; the benchmark receipt independently retains the complete 1080-frame window. Warmup receipts distinguish first-present gate latency, deferred scheduler work, and interactive frame samples. A zero deferred miss count beside nonzero phase elapsed time means work completed inside slack, not that compilation was free.

For cache-miss feedback, the runtime pre-seeds an independent plan-scoped trace collection with every planned phase before calling `BeginTrace`. Miss count is `observed states - baseline states`. This is intentional: Unity's `traceCacheMisses` flag is scoped to one `WarmUp` call and would otherwise classify states from earlier progressive batches as misses. A saved feedback collection therefore includes the baseline and is marked as such; the merge stage deduplicates it.

## Interpretation limits

Driver disk caches can make repeated cold runs look warm. For release gating, use a controlled CI image or a documented cache-reset procedure owned by the hardware lab. Never delete broad user/driver caches from this tool automatically.

The showcase additionally generates one compile-time shader cache-buster per acceptance invocation. The training and final builds share it; later invocations receive different shader bytecode. Its only rendered effect is a sub-pixel color delta. This protects the sample from reusing a prior showcase run's PSO while preserving an identical workload inside the current A/B/C experiment. It is a demo-evidence mechanism, not a replacement for clean hardware images in a consuming game.

Desktop-region capture requires the Player to remain visible and unobscured. It is a portfolio/acceptance evidence path for Windows; headless performance gates should use the JSON benchmark receipts and a controlled hardware image.

PresentMon and WPR provide a second, OS-level evidence plane, but starting their
ETW sessions requires administrator or Performance Log Users rights. The current
Codex host returned PresentMon exit code 6 (`access denied`), preserved in a
failed manifest. Therefore the five-run AMD row is provisional and no
PresentMon/ETW claim is made yet. NVIDIA and Intel hardware rows and the pinned
Megacity Metro five-run OS-evidence cell remain pending rather than being
extrapolated. The single accepted Megacity engine/visual run below is a separate
evidence tier and does not promote that matrix cell.

The built-in Profiler markers named by Unity are recorded when exposed, but availability differs by player configuration. Percentiles are computed from raw `Time.unscaledDeltaTime` samples, and the full arrays are retained in each benchmark receipt.

## Deadline Run visual contract (accepted)

Deadline Run uses a fixed 12-second camera path. `CONTENT_REQUEST` occurs at
T+2.0 seconds and `CONTENT_REVEAL` at T+4.5 seconds, giving all-at-once and
scheduled policies the same 2.5-second window. The future combat airspace and
its 320 material/render-state combinations are allocated before measurement and
excluded only through the camera culling mask. Revealing the layer avoids
runtime instantiation, streaming, and a mass renderer-enable loop in the
deadline frame.

All phase collections are loaded and validated before `CAPTURE_READY`; phase
execution objects, policies, statistics buffers, and scheduler scratch arrays
are also constructed up front. The request only timestamps and enqueues an
already resident execution. Marker lines remain in memory after
`CAPTURE_READY`, while phase receipt materialization, benchmark phase logs, and
JSON writes are deferred until measurement completes. Request/reveal frames
therefore contain neither artifact-file reads nor evidence-file writes or bulk
receipt-array allocation.

The portfolio acceptance is additional to the general A/B parity gate:

- baseline reveal-window maximum must be at least 80.0 ms;
- scheduled must have zero time-gated samples at or above 16.67 ms;
- the `deadline-run-reveal` phase must complete every state, meet its deadline,
  report zero budget violations, and pass scheduler admission;
- every generic benchmark receipt must be opened and closed by the same
  12-second scenario gate; its reported duration must be 12.0 seconds and its
  actual sample count must agree with the retained array. The first presented
  frame crossing 12.00 seconds is included, after which the gate closes; sample
  counts may differ because a real stall necessarily presents fewer frames.

The reveal-window maximum always includes the first post-reveal presented
frame, even when a very large cold compile pushes that frame beyond the nominal
two-second event window. A longer stall therefore cannot evade the cold gate.

`Tools/validate_deadline_run.py` writes the versioned acceptance receipt. The
12-second two-up MP4/GIF and three-way technical MP4 are composed only after
that receipt passes. The compositor burns the receipt's cold reveal maximum,
scheduled worst frame, threshold, and scheduled miss count into a footer on the
actual Player footage.

Accepted run `20260903-031014` used Unity 6000.5.2f1, D3D12, and an AMD Radeon
AI PRO R9700. It measured 1,039.290 ms cold versus 5.093 ms scheduled, with zero
scheduled frames at or above 16.67 ms. The scheduled phase completed all 351
trace-derived graphics states in 19 progressive batches over 83.109 ms, met the
2.5-second deadline, and reported zero admission violations and cache misses.

## Megacity Metro third-party contract (accepted)

The pinned Megacity Metro cell keeps the stock ECS/Entities Graphics city in
the picture while isolating a 48-state deferred first-use district. It lets the
real SubScene converge while traversing two complete 12-second closed camera
circuits with the stock Initialization, Simulation, and Presentation groups
active. At the following circuit seam the adapter freezes Initialization and
Simulation, leaves Presentation active, and emits `CAPTURE_READY`. Only after
the external recorder is running must a new continuous three-second window stay
below 14.0 ms. The build-time binder serializes the authoring camera pose and
every arm restores it at the same mode-independent startup seam. T+0 is armed
at the next fixed 4.0-second circuit phase, so cold, all-at-once, and scheduled
runs use the same origin, path, content, and camera phase regardless of warmup
duration. Requiring the 14 ms condition before freezing the CPU-heavy startup
groups would be circular, so no such pre-freeze performance claim is made.

The adapter resolves the Medium quality index once, avoids repeated graphics
configuration setters, skips native trace polling outside training, and
preallocates timed buffers. HUD labels and numeric values are materialized before
the gate and selected by retained `GUIContent` references during measurement;
the cold hitch banner separately latches the triggering frame without formatting
a new string. A final managed-heap preparation is followed by one complete frame
before either sampler opens. The sidecar records mean/P95/maximum Player frame
time, native CPU total/main/present-wait/render/GPU values, camera origin and
phase, frozen/active ECS worlds, output configuration, and managed-GC counters.
Acceptance requires at least 95% native `FrameTiming` coverage and rejects any
measured-window GC, camera-origin drift, diagnostic override, incomplete
deadline warmup, cache miss, or receipt that substitutes a fixed frame count
for the 12-second interval.

The formal PSO-isolation cell is pinned to Unity 6000.1.0f1, Medium quality,
D3D12, 1280x720 native output, URP render scale 1.0, a 150 m street-canyon far
plane, VSync off, and a 120 Hz producer. The editor automation inventories the
complete 6,947-renderer camera-forward corridor before building, while the
150 m presentation view leaves enough real scene headroom to test PSO work
against the 16.67 ms contract. A diagnostic unrestricted-far/120 Hz run measured
about 26.08 ms P95 before deferred work, so it is explicitly excluded rather
than mislabeled as a scheduler failure. This cell does not claim unrestricted
whole-city throughput.

Accepted run `20260903-054644` produced:

| Metric | Cold | Unity all-at-once | Deadline scheduled |
|---|---:|---:|---:|
| Retained samples | 1,415 | 1,440 | 1,440 |
| Mean | 8.485 ms | 8.334 ms | 8.334 ms |
| P99 | 8.338 ms | 8.336 ms | 8.336 ms |
| Maximum | 227.658 ms | 8.569 ms | 8.581 ms |
| Frames ≥ 16.67 ms | 1 | 0 | 0 |
| Managed collections during 12 s | 0 | 0 | 0 |

The scenario-wide scheduled maximum, including the conservative boundary frame,
was 8.738 ms. Hardware-local policy search selected one worker while its
Unity-6000.1 compatibility batch dimension remained pinned to one and
non-operative; the backend accepted one deadline-gated native asynchronous bulk
dispatch.
All 48 states completed in 16.793 ms with zero misses, zero budget violations,
valid strict admission, zero feedback cache misses, and 2.48 seconds of deadline
slack. The all-at-once arm also stayed under budget on this hardware, so the
result claims measured parity plus a scheduled hard-budget contract, not a
fabricated throughput-control regression.

The publishable result is retained as
[megacity-metro-comparison.json](Media/megacity-metro-comparison.json), with an
[actual-picture GIF](Media/megacity-metro-actual-comparison.gif),
[MP4](Media/megacity-metro-actual-comparison.mp4), and
[poster](Media/megacity-metro-actual-comparison.png). The
[publication manifest](Media/megacity-metro-publication.json) records both
source and repository hashes. The original run directory
retains the larger raw captures, scenario sidecars, policy trials, plan, hashes,
and versioned acceptance receipt.
