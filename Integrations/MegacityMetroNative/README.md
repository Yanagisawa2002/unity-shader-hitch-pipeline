# Original Megacity Metro scene adapter — Unmeasured

This optional UPM package observes the official application's Entities SubScene
loading lifecycle. It preserves the original workload: Menu then Main, original
SubScenes, materials, simulation, camera, render distances and input path. No
procedural district, shader allowlist, generated materials or simulation freeze
is installed. No native runtime experiment has been executed. The
[2026-09-10 preparation record](../../Docs/ACTUAL_NATIVE_PREPARATION_2026-09-10.md)
verifies a complete independent Git/LFS checkout. The
[2026-09-11 execution record](../../Docs/ACTUAL_NATIVE_EXECUTION_2026-09-11.md)
records the successful real import/D3D12 configuration and historical missing-module
failure. The [post-installation continuation](../../Docs/ACTUAL_NATIVE_AFTER_IL2CPP_2026-09-11.md)
verifies actual IL2CPP backend support and reaches native C++ linking. Repeated
external Unity work interrupted the training builds; there is no complete runnable
Player, native trace or comparison result. Earlier source-only and objects-only
receipts remain historical.

## Source and classification

The selected [upstream application](https://github.com/Unity-Technologies/megacity-metro/tree/07652ee74a1f322c2c3e607020f07be720175680)
is pinned to commit `07652ee74a1f322c2c3e607020f07be720175680`, tree
`7b0bf700ebc01face91223fd7185a02f44ec376e`, Unity **6000.1.0f1**.
[source-lock.json](source-lock.json) hashes its license notice, manifest,
package lock, build settings, quality/graphics settings and Menu/Main/Level
scenes and metadata. The complete Git tree binds the remaining source/assets;
strict verification also reads actual tracked working bytes and LFS payloads.

Upstream's [LICENCE.md](https://github.com/Unity-Technologies/megacity-metro/blob/07652ee74a1f322c2c3e607020f07be720175680/LICENCE.md)
identifies the Unity Companion License. Assets remain in a separately obtained
upstream checkout. The adapter adds no permission to redistribute them.

The reviewed [EntityComponentSystemSamples catalog](https://github.com/Unity-Technologies/EntityComponentSystemSamples/tree/6786a741ee1f118ed14cecfa02beae8e926937b0)
is recorded separately in [reviewed-sources.json](reviewed-sources.json); it has
not been adapted or executed here. Unity's official
[GraphicsStateCollection example](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Experimental.Rendering.GraphicsStateCollection.html)
is an API example. These reviewed sources do not establish a common cross-engine
PSO benchmark. This integration is labelled **external-application-scene**.

## Prepare without execution

Use a short external checkout path such as `C:/src/metro`. Obtain the pinned
Git/LFS content separately; the preparation tool performs no clone, download,
Editor startup, import, build, warmup, tracing, timing or cache operation.

```powershell
python Tools/pso_external_host.py verify --checkout C:/src/metro
python Tools/pso_external_host.py prepare --checkout C:/src/metro --output work/native-preparation
```

`verify` rejects a changed HEAD, dirty/untracked content, source digest mismatch,
worktree bytes hidden by Git index flags, and missing or corrupt LFS payloads.
`prepare` writes only a **new directory in this repository**: an additive UPM
manifest overlay, a workload contract and a preparation receipt. It never edits
the external checkout. Existing preparation directories are retained.

To reuse only available upstream Git objects from an old modified experiment:

```powershell
python Tools/pso_external_host.py prepare --checkout C:/path/to/old-checkout --git-objects-only --output work/native-objects
```

That receipt sets `checkoutVerified: false`, `lfsPayloadsVerified: false`,
`compileStatus: NotCompiled`, `runtimeStatus: NotRun` and
`measurementStatus: Unmeasured`. Source verification does not imply the old
scene is a valid native-workload checkout. Missing partial-clone blobs fail
locally; they do not trigger background downloads.

## Application wiring

Review and apply the manifest overlay only to a separate pristine host. It adds
the base package and `Package/`; registry package versions remain unchanged.
Re-resolve and retain the resulting UPM lock alongside the original pinned lock.
No package or Editor upgrade is silently accepted.

`PsoNativeHostBuild.ConfigureWindowsD3D12` explicitly sets the Windows API list
to D3D12 and disables automatic API selection. Invoking this Editor method starts
an import: it is a later capacity-gated stage, not part of source-only preparation.
It preserves the pinned IL2CPP backend and Menu/Main scene order.
The optional `-pso-native-reimport-vfx` flag synchronously reimports every original
VFX asset after a host path-access repair. It does not change or select asset content.
On this machine, a temporary short drive alias resolved actual long-path import
failures without copying the checkout or changing Windows path policy.

`PsoNativeHostBuild.BuildWindowsPlayer` is an explicit build helper that declares
the actual Menu/Main build options to the existing identity capture. It requires
`-pso-native-build-output`, the pinned Editor, and an explicitly
configured D3D12 Windows cell. It rejects changed scene order, a substituted Mono
backend and existing output directories, and explicitly selects the Player
subtarget. It does not alter target settings or auto-run the Player. Training
uses the existing `-pso-training-build` plan-gate exception; final builds validate
the traced identity. `DiagnoseWindowsBuild` reads the pinned Editor's active
target, subtarget and backend-support reason. The build helper reports that reason
before expensive content baking when available; it never bypasses Unity's build
gate. Target support and a generic `il2cpp.exe` alone do not prove that the Windows
IL2CPP Player module is installed. Actual invocations and failures are retained in
the execution record.

The observer is disabled unless `-pso-native-host` is present. It observes
`SceneReference`, `RequestSceneLoaded` and
[SceneSystem.IsSceneLoaded](https://docs.unity3d.com/Packages/com.unity.entities@1.3/api/Unity.Scenes.SceneSystem.IsSceneLoaded.html).
Each actual async request owns `scene-<scene GUID>`. The pinned Level phase is
`scene-ed1a49ee1f7b28b499c8cece71ee2353`. Multiple scenes/client worlds share phase
demand. Dependencies-ready activates once; removing an unfinished load cancels
it; unloading loaded content releases the last owner. Late callbacks cannot
revive a cancelled generation. A reload waits for an older unload fence to retire,
then retries activation with a new generation. Missing plans/phases remain visible
failures. A dependency-ready transition also attempts to arm full-plan feedback;
an unresolved baseline stays explicitly unavailable.

`Core/PsoContentPhaseLifecycle` supplies the same request/ready/cancel/unload
hooks for hosts with their own asset loaders. The backend must retain shader and
material dependencies through its cancellation fence. This observer does not
take ownership of upstream scene assets or delay their unload. The existing
Addressables adapter supplies explicit asset retention for owned streamed content.

Unity may publish scene readiness after some content has rendered. This observer
therefore supplies lifecycle integration, **not a guarantee of first-draw
coverage**. A host needing pre-reveal warmup must load and retain dependencies
before activation, or use an existing loading screen with that contract. The
native workload is not modified to manufacture such a window.

## Trace, plan and later comparison contract

Future tracing must use actual native scene routes, one declared capture phase
per process/session. Unity tracing is process-wide: overlapping scene/background
states remain included and named in the trace receipt. No material allowlist
removes native content. Use the existing inbox merge and strict compatibility
contract to install each `scene-<GUID>` collection, with `prewarmAtStartup: false`
for dependencies that become available during loading and deferred activation.
Do not restart a global trace on every overlapping async event. The orchestrator's
plan-seeded feedback records true observed-minus-baseline misses; do not count
cancelled phases as completed or unknown coverage as zero misses.

[workload-contract.json](workload-contract.json) declares four policy arms with
shared player/shader/plan/route/cache identities. Missing evidence is null; this
file is preparation, not a self-attested benchmark result. `fixed-progressive`
cannot silently use the pinned Editor's bulk backend. Testing true progressive
warmup on a newer Editor requires a separately locked cell for **all arms**.
The existing `-pso-deadline-backend-mode progressive` is an explicit experimental
opt-in on 6000.1 too; it has not been validated for this native scene or selected
for this cell. The default bulk cell cannot execute the fixed-progressive arm.
The existing `scheduled` default is not promoted to `observed-budget`.

The old [controlled Megacity reveal](../MegacityMetro/README.md) remains historical
evidence with 48 generated material instances and restricted rendering conditions.
Its timing or zero-miss results cannot be copied to this original-scene cell.

Runtime use requires the dependencies, route/cache protocol, trace, plan and
current-build identity. The following are independent build and runtime examples;
they do not establish a successful Player build or native run. Apply the prepared UPM overlay and
declare an explicit D3D12-only Windows target before building. Use a new output
directory each time.

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.1.0f1/Editor/Unity.exe' `
  -batchmode -quit -projectPath C:/src/metro `
  -executeMethod Yanagisawa.ShaderHitchPipeline.NativeScenes.Editor.PsoNativeHostBuild.BuildWindowsPlayer `
  -pso-training-build -pso-native-build-output C:/builds/metro-training/Megacity.exe

& C:/builds/metro-training/Megacity.exe -force-d3d12 `
  -pso-native-host -pso-disable-warmup -pso-trace `
  -pso-trace-phase scene-ed1a49ee1f7b28b499c8cece71ee2353 `
  -pso-output C:/captures/metro-level

# After inbox merge/plan installation, build again with the same helper,
# omit -pso-training-build, and choose a fresh final output directory.
& C:/builds/metro-final/Megacity.exe -force-d3d12 `
  -pso-native-host -pso-warmup-strategy observed-budget `
  -pso-output C:/captures/metro-observed
```

The Player commands open the original **Menu**, not an automatic flythrough.
The pinned project has no cloud project ID: dismiss the original services notice,
then select **Single Player**. That original callback sets the game mode and
loads **Main** asynchronously. Its loading screen hides only after the original
sections, player and camera are ready; the adapter does not hold that screen for
warmup. There is no single-player autostart CLI in this source. Do not add
`-batchmode` to the Player: upstream uses it to request automatic matchmaking.
Multiplayer sessions, including local multiplayer, require separate UGS setup.

The user/content route and normal application exit delimit that training capture;
the example adds no generated route or automatic timeout. Include background and
overlapping SubScene states and repeat for every declared phase. Cold disables
the warmup orchestrator, so it produces no plan-seeded feedback receipt. Missing
cold feedback remains unavailable; a separate declared native capture is needed
to inspect its observed state set. Do not infer zero misses from its absent receipt.

Reference-only compilation cannot verify asset import, Entities code generation,
Player correctness, driver behavior or performance.

For this machine, use the execution record's `Invoke-PsoNativeStage.ps1` wrapper for
each import/build/Player stage. It checks the shared mutex, running workloads,
host/output/temp-volume capacity and the 20 GiB reserve before invoking a
synchronous action. Its default 80 GiB additional-peak estimate requires 100 GiB
free at stage start. It does not authorize unattended queues, clean caches or
establish an actual native peak. Recognized idle compiler servers are recorded
separately only after observing zero CPU growth; active clients still block the
stage. `Wait-PsoOwnedProcess.ps1` monitors the stage's own process and capacity,
records observed descendants, and closes that owned process on timeout or below
a 25 GiB early-stop threshold. It never searches for an unrelated process to kill.
Later-stage estimates must account for the cache already generated, as recorded
in the execution evidence.
