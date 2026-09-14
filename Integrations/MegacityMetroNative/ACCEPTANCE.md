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
Events record both Stopwatch seconds and direct Unity realtime. The ECS population
window uses the event and snapshot values from that same engine clock; frame IDs
and CPU intervals remain explicit. A bootstrap clock offset is not used to infer
exact cross-clock alignment.

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

For the common final Player, run independent non-diagnostic pilots into new
`megacity-policy-pilot-POLICY-SUFFIX` directories. Use `-PlanBaselineForDisabled`
for the disabled arm so its observation cost matches the other arms while it
submits zero warmup work. Keep the same original entry and observation duration.
The following gates refuse missing content, stale internally consistent receipts,
incomplete native work or an unretired owner:

```powershell
python Tools/pso_megacity_capture.py --stage $pilotStage --output $newValidation --require-warmup
python Tools/pso_megacity_comparison.py freeze $attempt --player-stage $finalStageName `
  --pilot-suffix $pilotSuffix --output $newComparison
Tools/Invoke-PsoMegacityComparison.ps1 -Protocol "$newComparison/protocol.json"
```

Freezing reads and revalidates all four actual pilots, the installed plan and
collection bytes, actual captured/receipt environments, and the complete common
Player/source indices. The finite runner checks frozen execution tools and
identities, holds the shared resource gate separately for each native/CPU stage,
and stops on failure without replacing samples. A new `summarize` output can
record partial failure evidence; missing windows remain null with available
sample counts, and failed attempts remain distinct from unexecuted runs.
Graphics states and native warmup permutations remain different units.
Throughput receipts use `native-async-throughput`; scheduled/observed receipts in
this selected cell use `deadline-gated-native-async-bulk`. Neither is evidence of
fixed-progressive support. These commands do not themselves establish that any
Megacity policy has passed its native gate.

The adapter passed real pinned Editor compilation/configuration after repairing
a missing `Unity.Mathematics.Extensions` reference. The first complete training
Player (`839c626320694c07a0f9957ea5a18fc3`, source `b2cd6dd`) built with zero errors
and 23 warnings. Its first process retained 28,594 real Menu render submissions,
then exited 79 at the declared readiness timeout. The original camera's owning
scene is `DontDestroyOnLoad`; the observer incorrectly required Menu/Main camera
ownership. This failed Menu-only trace is excluded from Main training.

The repaired observer records `activeScene` separately from the retained camera
scene path/name, and associates both Menu and Main with the actual original
HybridCameraManager screen camera and application readiness. Eight targeted
data-integrity checks passed, including rejection of unrelated cameras and
ownership-only route inference. A newly built Player must still prove Main,
all six payloads, simulation, visible content and normal exit. Neither this repair
nor the completed build establishes content or performance acceptance.

The repaired training-03 source reached MSVC linking, then an external Unity
6000.5.2f1 workload triggered the owned-process stop gate. Its incomplete output,
logs and retained Library/Bee inputs remain intact. The next build requires fresh
workload clearance and a new output directory; it must use the current declared
package/documentation inputs and obtain its own identity. See the
[dated execution record](../../Docs/EXTERNAL_MEGACITY_ACCEPTANCE_2026-09-14.md).
