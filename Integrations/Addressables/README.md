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
callback run; only after it succeeds does the adapter load the native collection
assets through Addressables. Each phase maps to an immutable
`PsoStreamingCollectionAsset(originPath, sha256, expectedStateCount, addressableKey)`
from the attested plan. The adapter verifies the retained original capture bytes
and the loaded collection's exact resolved state count. The attestation must bind
the original artifact to the loaded bundle/catalog mapping; hashing a loose file
does not attest an arbitrary loaded asset.
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
asset leases** (both prefab and native GSC Addressables handles) remain retained
until `Complete` fences the job. `Drain` blocks on
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
calls from worker threads. Gameplay instances created from the prefab must be
destroyed before releasing their content owner, or have separately retained leases.

## Shader state deduplication

`PsoUnityStreamingBackend` enumerates loaded collections and asks Unity's native
`AddGraphicsStateForVariant` to compare complete variants and graphics states,
including nested layouts/attachments. It does not equate shader names, file
hashes or partial managed-state fields. Shared shaders must be explicit shared
bundle dependencies, as in this fixture; duplicated shader assets may resolve to
different Unity Shader objects and conservatively remain distinct. Assets load
before collections and stay loaded through warmup, respecting Unity shader load
and deduplication order. The `.graphicsstate` asset must itself be imported and
included in an Addressables group. A raw `LoadFromFile` path did **not** resolve
the bundle shader GUID in the tested separate Player process, even after a frame
delay; both failures are retained. The optional adapter therefore requires the
native collection address. The base Unity backend retains its raw-file import
path for other integrations whose shaders are resolvable in that context.
This follows [Unity's Addressables/GSC resolution notes](https://issuetracker.unity.com/issues/21757/graphics-state-collection-warm-up-does-not-work-when-using-with-addressables-shaders).

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
build a Development Player fixture before its dynamic content collections exist; it
does not pretend to pass the static installed-plan production build gate.
The fixture builds two actual Addressables prefab/material
revisions (blue/red) plus an explicitly shared shader bundle. Its bootstrap scene
holds no direct reference to those materials or that shader. The build creates
real local bundles/catalog and a Windows D3D12 Player. A trace process loads and
explicitly renders each revision to a 64x64 render texture, checks a GPU pixel
readback against the revision color, and writes a separate nonempty graphics-state
collection. It does not rely on a hidden window presenting. On the tested Unity
6000.5.2f1 build, the non-Development Player passed GPU pixel readback but recorded
zero graphics states; that failed attempt is retained. The actual tracing/runtime
smoke therefore uses Development Player, matching the repository's tracing samples.
The initial captures are imported into a Collections Addressables group. The
already explicit SharedShaders assignment prevents the GSC and prefab bundles
from embedding separate copies of the custom shader. A final Player is built,
then frozen. Its independent trace loads each bundled native GSC and constructs
a native union with the live trace: variant and graphics-state counts must match
on both sides and remain unchanged by the union. Original source hashes and this
exact native validation are retained separately; a new build is not declared
compatible just because a filename or count was relabeled.
A further process loads the built bundles and tests overlapping owners, native
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

`-SeedTraceRoot` reuses an existing initial capture as the source to import; the
final frozen Player still performs its own full native validation. `-SkipBuild`
reruns final trace/smoke against the existing Player. `-SmokeOnly` resumes only
smoke against an existing frozen Player and its already validated trace. Receipts,
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
