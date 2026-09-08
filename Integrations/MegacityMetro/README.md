# Megacity Metro external validation adapter

**Historical controlled PSO-isolation experiment.** This adapter adds 48 generated
material instances and changes scene conditions. It is not the original upstream
workload or a standard benchmark. The new [native-scene adapter](../MegacityMetroNative/README.md)
preserves upstream content and is Unmeasured. Results below belong only to this
retained controlled cell. Runtime scripts now reject execution unless a future
run is explicitly authorized and `-AllowPerformanceExecution` is supplied.

This integration validates the Shader Hitch Pipeline in Unity's public,
large-scale Megacity Metro project without copying or modifying its assets in
this repository.

## Pinned source

- Repository: `https://github.com/Unity-Technologies/megacity-metro.git`
- Commit: `07652ee74a1f322c2c3e607020f07be720175680`
- License: Unity Companion License

The pin is stored in `pin.json`. Moving the pin is an explicit benchmark change,
not an automatic dependency update.

The pinned project uses Unity 6000.1.0f1. The package's compatibility seam uses
the pre-6000.5 experimental graphics-state namespace/signatures on that editor
and the promoted 6000.5+ API in the main benchmark project; the scene does not
need to be upgraded merely to compile the adapter. Unity 6000.1 uses a
deadline-gated native asynchronous bulk job for deferred phases because its
experimental progressive job has impractical repeated-job latency. Unity
6000.5+ retains true progressive batches. Receipts label the backend mode and
separately record predicted and observed background completion time.
For the pinned 6000.1 cell, policy search therefore varies native worker
parallelism while holding the non-operative progressive batch dimension at one.

## Deterministic integration procedure

1. Clone Megacity Metro and checkout the exact pinned commit.
2. Add `com.yanagisawa.shader-hitch-pipeline` to its package manifest.
3. Run `Tools/Install-PsoMegacityMetroAdapter.ps1 -TargetRepository <path>`
   from this repository. The installer refuses a different revision unless an
   explicit override is supplied.
4. Run the complete external cell from this repository:

   ```powershell
   .\Tools\Invoke-PsoMegacityMetro.ps1 -ProjectPath <megacity-checkout>
   ```

   The automation opens `Main` and `Main/Level`, selects a deterministic
   camera-forward corridor (100-800 m deep, 350 m half-width), inventories its
   6,947 authoring renderers without changing their layers, selects single-player
   mode, disables matchmaking and the
   tutorial overlay, and builds `Main` directly. The measured Windows Player is
   D3D12, Mono, and the project's Medium quality profile on the pinned editor
   because that exact local editor install has the Mono Standalone module. The
   formal PSO-isolation cell pins 1280x720 native output, URP render scale 1.0,
   a 150 m street-canyon far plane, VSync off, and a 120 Hz producer; every
   comparison arm uses the same configuration.
5. Keep the generated binding receipt, benchmark receipts, scenario sidecars,
   trace, plan, scheduler receipts, capture metadata, and acceptance receipt
   together under `PsoArtifacts/MegacityMetroRuns/<timestamp>`.

The menu binder remains available for an intentionally different route, but a
manual selection is a new benchmark cell and must not be mixed with the pinned
automated result.

Megacity Metro renders its city through ECS and Entities Graphics. The adapter
uses that real 6,947-renderer corridor as the third-party scene and visual
context, then adds 48 controlled material instances so the A/B/C experiment
has a reproducible cold first-use boundary independent of whatever states the
stock sample happened to create during asynchronous SubScene startup. The
actual number of recorded graphics states is trace-derived, not claimed to be
identical to the material-instance count.

All 48 meshes and materials are created before measurement on a dedicated
camera-hidden layer (layer 28). The real city remains continuously visible and
is not part of the controlled reveal. While Megacity initializes its player camera, their one
common parent follows that live camera. The parent freezes in world space 0.25
seconds before `CONTENT_REVEAL`, outside the measured first-draw frame. At reveal the
measured path changes only the camera culling mask; it does not instantiate
objects, stream assets, reposition hundreds of transforms, add/remove ECS
components, perform per-frame relay transform writes, or inject a sleep. Each
formal build receives new shader bytecode
through the generated cache-buster, without deleting the user's or driver's
cache. The deferred trace begins at `CONTENT_REQUEST` and remains active through
`CONTENT_COMPLETE`, two seconds after reveal, so Unity 6000.1 can publish both
each variant and its potentially delayed graphics-state payload. At merge time,
the reusable per-phase
Shader allowlist removes background city variants and records raw, excluded,
and retained counts in the merge receipt. The Megacity cell intentionally
excludes its unrelated startup trace from the installed plan. This keeps the
warmup collection scoped to the controlled first-use boundary while the real
city remains continuously rendered. The same `PsoDeadlineScenario`
contract used by Deadline Run controls phase tracing, request timing,
activation, and deadline markers. When the Player receives
`-pso-scenario-report <path>`, the adapter also retains the full 12-second
timed-frame sidecar, reveal-window maximum, first post-reveal frame, device
identity, and `RUN_COMPLETE` state. The selected renderer bounds provide the
world-space continuity reticle target. Its HUD and timed-sample buffers use the
same allocation-controlled evidence path as Deadline Run.

The measured camera path is a closed 12-second circuit rather than unbounded
forward travel. Before `CAPTURE_READY`, the real city completes two whole
circuits with its normal groups active. At the next circuit seam the adapter
freezes Initialization and Simulation while leaving Presentation active, so
startup streaming is allowed to converge but cannot bleed into measurement.
The marker then starts the external recorder; only after a new continuous
three-second window stays below 14.0 ms does the adapter arm T+0 at the next
fixed 4.0-second circuit phase. Active pre-freeze Megacity Simulation is not
required to fit the final frame ceiling because that would make the freeze gate
circular. The authoring camera pose is bound into the benchmark scene, then restored at one
mode-independent startup seam, so warmup duration cannot move T+0 to another
city block. All A/B/C receipts
retain the origin, rotation, circuit phase, and capture lead, and acceptance
rejects a position delta above 5 cm or a rotation mismatch.

The generic benchmark sampler is scenario-gated: it opens at T+0 and closes at
`RUN_COMPLETE`, so `-pso-benchmark-frames` is capacity/backward-compatibility
input rather than a substitute for 12 seconds. The adapter also resolves quality
once, avoids repeated frame-pacing setters, polls Unity's native trace state only
in training runs, and records GC collection counters. HUD text and numeric values
are prebuilt before measurement; runtime presentation selects retained
`GUIContent` references, including a separately latched cold-hitch value. After
the capture-active stability gate, the adapter prepares the managed heap and
waits one complete frame before opening either sampler. A run with any timed
managed collection or less than 95% native `FrameTiming` coverage fails
acceptance even if its headline miss count is zero.

The controlled district is a two-second transient first-use effect. At
`CONTENT_COMPLETE` one camera-mask change removes its benchmark layer; no
per-renderer disable loop runs. The gated set includes the first presented frame
that crosses 12.00 seconds, then closes immediately; this conservative boundary
rule prevents a straddling hitch from being hidden and rejects any later frame.

## Accepted local result

Formal run `20260903-054644` on Unity 6000.1.0f1 and an AMD Radeon AI PRO R9700
passed the complete cold/all-at-once/scheduled workflow. Cold reveal reached
227.658 ms. The all-at-once and deadline-scheduled retained maxima were 8.569 ms
and 8.581 ms respectively; the conservative scheduled scenario-wide maximum was
8.738 ms. Both warm arms recorded zero frames at or above 16.67 ms, and all three
12-second windows recorded zero managed collections.

Hardware-local search selected one worker. The scheduled arm completed all 48
trace-derived states in 16.793 ms through Unity 6000.1's deadline-gated native
asynchronous bulk backend, with valid admission, zero budget violations, zero
deadline misses, and zero plan-feedback cache misses. The all-at-once control
also stayed below the presentation threshold on this hardware; it is reported
as parity rather than manipulated to look worse.

An unrestricted-far diagnostic at the same 120 Hz target measured about 26.08 ms
P95 before controlled PSO work and therefore could not satisfy the 14 ms
background-headroom precondition. The accepted 150 m view is explicitly a
street-canyon PSO-isolation cell, not a claim about whole-city throughput.

## Evidence boundary

Megacity is a third-party validation cell, not the deterministic microbenchmark
or an unrestricted scene-throughput benchmark. Pin the scene, route, quality,
API, resolution, driver, and generated shader key. Retain
cold/all-at-once/scheduled receipts and reject a run when streaming, networking,
tutorial/system UI, or server startup overlaps the measured window. The
automated acceptance gate additionally requires a baseline reveal-window frame
of at least 80 ms and zero scheduled frames at or above 16.67 ms.

No Megacity content is redistributed here. The adapter code and benchmark
contract remain independently owned and reusable.
