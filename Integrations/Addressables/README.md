# Optional Addressables streaming

This opt-in adapter is separate from the base package. The base `UnityProject` and
engine-neutral .NET core do not depend on Addressables. Install the local package
`Packages/com.yanagisawa.shader-hitch-pipeline.addressables` alongside the base
package; its pinned Addressables version is 2.10.3. The fixture uses Unity 6000.5.2f1.

## Lifecycle and identity

Create one `PsoAddressablesLoader` on the Unity main thread. Call
`Load(addressablePrefabKey, contentId, contentRevision, collections, validateLoaded)`
and pump `Tick`. Addressables first loads the prefab and its material/shader
dependencies. Only after successful completion does the required attestation
callback run; only after it succeeds does the adapter open collection files.
Each phase maps to an immutable `PsoStreamingCollectionAsset(path, sha256, expectedStateCount)`
from the attested plan. The adapter verifies bytes before native load and exact
resolved state count afterwards, rejecting partial or empty shader resolution.
The callback must verify loaded catalog/bundle revision and digest, current build
and shader identities, collection compatibility and artifact hashes. Return the
attested compatible namespace; throw on unknown/stale/incompatible identities.
A caller-provided string or collection file hash alone does not attest a build.
When used with the vNext compatibility package, require
`PsoCompatibility.Evaluate(contract, current).collectionCompatible` before returning
`PsoCompatibility.CollectionKey(current)`; populate current content identity from
the verified loaded catalog mapping. Cost compatibility is a separate decision.

The handle's `Owner` becomes available after registration. Request phases with
`loader.Coordinator.RequestPhase(owner, phase)`; dispose a request to cancel only
its demand. Multiple owners and phase requests share the union of required states.
`Tick(maximumStates)` submits one bounded state subset at a time. It bounds admitted
state count, **not** opaque driver duration. The existing static orchestrator and
online cost fitting remain unchanged; this opt-in streaming pump has no automatic
deadline policy. A host can use its scheduling policy to choose when/count to tick.

`load.Unload()` is idempotent. It removes unsubmitted work and retires the owner.
Submitted work cannot be cancelled: its collections and **all participating owner
asset leases** remain retained until `Complete` fences the job. `Drain` blocks on
submitted work without submitting pending work. An operation failure with a proven
fence is reported by `IPsoStreamingBatchResult.Failure`; a throwing/unknown fence
keeps resources retained and propagates the error for retry/diagnosis. Backends
must never throw after submitting work without returning its completion fence.

A newer revision of the same content ID retires older owners and pending loads.
Late completion of an older Addressables operation cannot re-register it. Same
revision owners coexist; stale or foreign owner requests reject. Addressables
failures, callback rejection and cancelled loads release their handles exactly
once when the actual operation finishes. Call `BeginShutdown`, continue `Tick`
until `PendingLoads` is false, then `DrainAndDispose`. Do not discard a loader with
pending operations. No `WaitForCompletion` is used for remote loads. The caller
must arrange this shutdown pump before destroying its scene/service.

The engine-neutral `PsoRetainedAsset` provides explicit reference-counted leases
for non-Addressables integrations. The caller releases its original reference
after registrations. Release callbacks must run on the creating thread and should
not throw. The APIs support overlapping requests on that thread, not parallel
calls from worker threads.

## Shader state deduplication

`PsoUnityStreamingBackend` enumerates loaded collections and asks Unity's native
`AddGraphicsStateForVariant` to compare complete variants and graphics states,
including nested layouts/attachments. It does not equate shader names, file
hashes or partial managed-state fields. Shared shaders must be explicit shared
bundle dependencies, as in this fixture; duplicated shader assets may resolve to
different Unity Shader objects and conservatively remain distinct. Assets load
before collections and stay loaded through warmup, respecting Unity shader load
and deduplication order.

Canonical state IDs are session-local and valid only while a retained import
references them. Namespace and content revision also participate in the core key;
unknown identities reject, and different namespaces/revisions never share a
completion claim. Completed states are evicted when their last owner/job leaves.
Reload therefore warms again instead of assuming an opaque driver cache survives.
Native interning is deliberately exact and currently O(N²) in the worst case,
performed at content registration. Its cost is **unmeasured** for large catalogs;
this implementation must not be promoted as a measured large-content speedup.

## Real fixture and smoke

The independent `UnityProject` uses the existing `-pso-training-build` path to
build a trace-capable fixture before its dynamic content collections exist; it
does not pretend to pass the static installed-plan production build gate.
The Player builds two actual Addressables prefab/material
revisions (blue/red) plus an explicitly shared shader bundle. Its bootstrap scene
holds no direct reference to those materials or that shader. The build creates
real local bundles/catalog and a Windows D3D12 Player. A trace process loads and
renders each revision and writes a separate nonempty graphics-state collection.
A second process loads the built bundles and tests overlapping owners, native
dedup/subset submission, unload while submitted, fence release, reload, changed
content, cancellation, attestation failure and a missing Addressables key.

The fixture's bounded identity policy binds exact Player build GUID, Unity/device,
the full shipped bundle/catalog byte tree, loaded prefab/material revision and
each collection hash. Each trace receipt includes the captured environment and
content ID/revision for the integrated compatibility bridge. This exact-build
fixture policy does not assert arbitrary production catalog compatibility.

Run from PowerShell (all Unity/GPU operations remain within the provided lock):

```powershell
& ./Tools/Invoke-PsoAddressablesSmoke.ps1 `
  -SerializationScript '<control>/Invoke-SerializedValidation.ps1' `
  -EvidenceRoot '<repo>/Evidence/Local/addressables-smoke'
```

`-SkipBuild` reruns trace/smoke against an existing fixture Player. Receipts,
raw logs, collection files and artifact/binary SHA256 provenance go to the evidence
directory. Expected missing-key Addressables errors appear in the smoke log;
success requires both process exit code zero and `passed: true`, never merely the
absence of log errors. This is correctness evidence, not a formal timing matrix,
cache-cold proof, ETW capture or a five-process statistical acceptance result.

Engine-neutral regressions (no Unity/Addressables required):

```powershell
dotnet run --project DotNet/ShaderHitchPipeline.Streaming.Smoke -c Release
dotnet run --project DotNet/ShaderHitchPipeline.Core.Smoke -c Release
```

Native API reference: [Unity GraphicsStateCollection](https://docs.unity3d.com/6000.5/Documentation/ScriptReference/Rendering.GraphicsStateCollection.html).
