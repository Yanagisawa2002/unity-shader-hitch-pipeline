# Opt-in original Single Player acceptance

This host adaptation exposes the pinned application's original Single Player
action as a shared public API. The original UI handler and the explicitly opted
in adapter use the same GameMode assignment, asynchronous Main loader and
LoadingScreen transition. The adapter waits for initialized Menu and an actual
Menu camera submission. It does not invoke UI callbacks, reflect into the menu,
send input, use runtime `-batchmode`, generate a route or replace scene content.
The resulting Player has a new identity; historical training-10 remains separate.

`Tools/repair_megacity_entry.py` checks the exact original three source preimages
before any change, then preserves original/adapted files and a complete diff.
Besides the shared application action, it owns one tutorial input subscription
per display, disposes it on dismissal/disable/destroy, and disposes the original
temporary spawn EntityCommandBuffer after Playback. The opt-in adapter dismisses
tutorial instructions through the shared ordinary action only after the original
game system independently hides LoadingScreen. It never hides that loading screen.

The acceptance observer records actual six-SubScene requests, every resolved
section's load status, payload and render entity counts, the original SinglePlayer
entity, simulation time, original HybridCameraManager camera submissions and
actual rendered screenshots. Stable world/entity/version samples track up to eight
traffic vehicles and four blimps; these are observation limits, not reduced
populations. All original content and simulation remain active. The validator
requires persistent same-world payloads and advancing simulation across the
declared observation window, positive native movement, sustained original-camera
render submissions and a normal exit through the original public QuitSystem.

This is bounded application acceptance with the original stationary player camera
and evolving city content. It is not a whole-city trajectory, six isolated traces,
or a standardized PSO benchmark. Training uses one uninterrupted process-wide
`megacity-process` collection. Its readiness mapping requires all six actual
payloads; the existing real lifecycle bridge retains encountered shaders and
submits only after that observation. Scene readiness may follow first draw. The
original loading screen supplies an observed admission window, never an added
fence or wait. Unsupported/incomplete policy work must fail its execution gate.

Use the retained hydrated official host and new stage/output directories. Run
preparation/repairs under `Tools/Invoke-PsoNativeStage.ps1`, preserving existing
evidence and the shared mutex. `prepare_megacity_acceptance.py` verifies the original
70 requested dependencies, original 91 lock entries and 21 targeted original
route/settings/LFS files before copying the additive adapter. Apply the declared
Core 17.1 RenderGraph cleanup backport with `repair_urp_rendergraph.py`; it creates
an embedded host copy and leaves registry caches untouched.

After source preparation:

```powershell
Tools/Invoke-PsoExternalEditor.ps1 -HostPath $hostPath -Output $newConfigureStage `
  -Method PsoMegacityAcceptanceBuild.Configure -EstimatedAdditionalPeakGiB 10
Tools/Invoke-PsoExternalEditor.ps1 -HostPath $hostPath -Output $newBuildStage `
  -Method PsoMegacityAcceptanceBuild.BuildPlayer -Training -PlayerPath $newPlayer `
  -EstimatedAdditionalPeakGiB 80 -MaximumSeconds 21600
Tools/Invoke-PsoExternalPlayer.ps1 -Player $newPlayer -Output $newRun -Session $newSession `
  -Policy disabled -Trace -TracePhase megacity-process -Screenshots -MaximumSeconds 900 `
  -ExtraArguments @('-pso-megacity-single-player','-pso-megacity-observe-seconds','60')
python Tools/pso_megacity_capture.py --stage $newRun --output $newValidation
```

The 80 GiB build allowance is a conservative estimate for changed native inputs
with retained Library/Bee, not a measured current peak. All stages reserve 20 GiB
and stop owned work at 25 GiB. The default observation is 60 seconds after actual
readiness; the 480-second timeout applies only before readiness. A longer requested
observation needs an outer timeout covering readiness + observation + normal exit.
Training/correctness screenshots and diagnostics are excluded from formal timing.
Validate real content and a compatible installed plan before freezing a common
final Player and any supported serial comparison. No driver-cache purge or default
policy promotion is part of this workflow.

The adapter passed a real pinned Editor compilation/configuration on 2026-09-14
after the first compile exposed a missing `Unity.Mathematics.Extensions` reference
for SceneSectionData. That failure is retained. At this source checkpoint, the new
Player and Main acceptance are still pending; source/Editor success is not native
content or performance evidence.
