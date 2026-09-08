# Original Megacity Metro scene adapter — Unmeasured

This optional UPM package observes the official application's Entities SubScene
loading lifecycle. It preserves the original workload: Menu then Main, original
SubScenes, materials, simulation, camera, render distances and input path. No
procedural district, shader allowlist, generated materials or simulation freeze
is installed. No runtime experiment was executed in this repair.

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

`PsoNativeHostBuild.BuildWindowsPlayer` is an explicit build helper that declares
the actual Menu/Main build options to the existing identity capture. It requires
`-pso-native-build`, `-pso-native-build-output`, the pinned Editor, and an explicitly
configured D3D12 Windows cell. It rejects changed scene order and existing output
directories. It does not alter target settings or auto-run the Player. Training
uses the existing `-pso-training-build` plan-gate exception; final builds validate
the traced identity. This helper was compiled, not invoked, during this repair.

The observer is disabled unless `-pso-native-host` is present. It observes
`SceneReference`, `RequestSceneLoaded` and
[SceneSystem.IsSceneLoaded](https://docs.unity3d.com/Packages/com.unity.entities@1.3/api/Unity.Scenes.SceneSystem.IsSceneLoaded.html).
Each actual async request owns `scene-<scene GUID>`. The pinned Level phase is
`scene-ed1a49ee1f7b28b499c8cece71ee2353`. Multiple scenes/client worlds share phase
demand. Dependencies-ready activates once; removing an unfinished load cancels
it; unloading loaded content releases the last owner. Late callbacks cannot
revive a cancelled generation. Missing plans/phases remain visible failures.

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
contract to install each `scene-<GUID>` collection, with deferred activation.
Do not restart a global trace on every overlapping async event. The orchestrator's
plan-seeded feedback records true observed-minus-baseline misses; do not count
cancelled phases as completed or unknown coverage as zero misses.

[workload-contract.json](workload-contract.json) declares four policy arms with
shared player/shader/plan/route/cache identities. Missing evidence is null; this
file is preparation, not a self-attested benchmark result. `fixed-progressive`
cannot silently use the pinned Editor's bulk backend. Testing true progressive
warmup on a newer Editor requires a separately locked cell for **all arms**.
The existing `scheduled` default is not promoted to `observed-budget`.

The old [controlled Megacity reveal](../MegacityMetro/README.md) remains historical
evidence with 48 generated material instances and restricted rendering conditions.
Its timing or zero-miss results cannot be copied to this original-scene cell.

Before any runtime execution, obtain new explicit authorization and complete the
missing dependencies, route/cache protocol, trace, plan and current-build identity.
Reference-only compilation cannot verify asset import, Entities code generation,
Player correctness, driver behavior or performance.
