# Plan and cost compatibility v1

The outer document schema stays at v3. Newly captured trace environments contain
`identity`; inbox merging emits `plan.compatibility.version = 1` for those traces.
The collection and cost decisions are separate and include machine-readable reasons.
No cross-device compatible-match relaxation is enabled.

| Change or missing evidence | Collection action | Cost action |
| --- | --- | --- |
| Shader dependencies, content revision, build inputs, target/API, quality, Unity patch version | Reject before loading; retrace for current inputs or render cold | Discard |
| GPU device/vendor/name changed or unavailable | Reject; cross-device collection reuse is unvalidated | Discard |
| Same device, changed/unknown driver version | Keep identity-compatible abstract collection, subject to Unity LoadFromFile/metadata checks | Reset seed and cold-probe; fit online |
| OS, CPU, threading, launch context changed/unknown | Keep collection | Reset seed and cold-probe; fit online |
| No measured cost cache/model version changed | Keep collection when identity matches | Reset seed and cold-probe; fit online |
| Build GUID changes but captured inputs are identical | Keep collection | Other cost fields must still match |
| Missing/corrupt embedded build identity or unknown contract version | Reject strict plan; retrace/fix build capture | Discard |

Collection rejection raises an actionable validation error before warmup submission.
The caller may explicitly disable warmup and render cold or capture a new trace;
the package does not silently relabel an old collection as current. A driver change
does not reuse native compiled PSOs: the collection contains abstract graphics states
that Unity submits to the current driver. This does not promise shader coverage on a
different device. Unity's own collection metadata/load validation remains mandatory.

## Actual capture and its limits

Build adapters must call `PsoBuildIdentityCapture.DeclareBuildInputs(options)`
immediately before `BuildPipeline.BuildPlayer(options)` with the **same** value.
The one-shot declaration captures the ordered actual scene list, extra scripting
defines, subtarget and optional AssetBundle manifest bytes; prebuild verifies target
and effective options against BuildReport. Hashing all imported assets alone would
not distinguish builds that strip different subsets through different scene lists.
Undeclared builds keep the legacy build path, but embed an unavailable identity;
new captures cannot downgrade to legacy and strict final builds reject. Offline
`Capture(...)` defaults to EditorBuildSettings only for inspection/tests and is not
used as evidence of an undeclared build's actual scenes.

`PsoBuildIdentityCapture` runs before **every** Player build, including training and
cold controls. It hashes sorted imported asset paths, GUIDs and
`AssetDatabase.GetAssetDependencyHash` results across Assets and Packages. Shader,
compute, include and shader graph dependencies receive a separate digest. Build input
identity also includes ProjectSettings, package manifest/lock, target, exact Unity
version and actual BuildReport build options (including development/stripping-affecting flags). This conservatively invalidates on unrelated asset
changes. It is build-input attestation by the build pipeline, not a signature or a
claim that two binary files are identical.

The generated `Assets/Resources/PsoBuildIdentity.json`, configured installed-plan
subtree (including installer staging/backups), scheduler-only
`ProjectSettings/ShaderHitchPipeline.json`, and EditorUserSettings are excluded to
avoid plan/identity self-reference. No shader/source/content subtree is excluded.
Build receipts and external runtime outputs are outside imported Assets. The final
build gate recomputes actual inputs before accepting a strict traced plan. Merely
recomputing `planSha256` cannot satisfy this gate. Identical trace/final build input
digests are permitted even though `Application.buildGUID` differs; that GUID is
retained as provenance in the captured environment.

At runtime the embedded resource is read with Resources.Load, its version/Unity and
identity checksum are validated, and `Application`/`SystemInfo` capture current API,
quality, numeric device/vendor IDs, driver/API version string, CPU, OS and threading.
On D3D12, `graphicsDeviceVersion` can contain only the API feature level. It is
not accepted as driver attestation. The Windows provider matches the numeric GPU
vendor/device to one display-driver registry entry (DriverVersion/Date/INF) and hashes
actual loaded DLL bytes under DriverStore/FileRepository in this process. The combined
`driverIdentity` has a versioned source; missing/ambiguous registry matches, no loaded
DriverStore modules or access errors explicitly invalidate cost reuse. This does not
claim kernel-mode trace/counter evidence. Capture hashes binaries during environment
capture, outside scheduler batch timing; its startup overhead needs integrated measurement.
The cost launch-context SHA-256 includes the **entire exact command line** to avoid
unproven normalization of worker/scheduling overrides. Different executable/output
paths or unrelated arguments consequently invalidate the cost cache too. If cache
reuse is desired, keep the same `-pso-cost-cache <path>` argument on both the initial
calibration launch (missing cache is safe) and later launches. This deliberately
favors false invalidation over unjustified reuse. No raw command line is persisted. Explicit adapters can call
`CaptureForCostScope(adapterId, declaredScope)`, which retains actual captured system
identity and omits only the values of `-pso-output`, `-pso-warmup-receipt`, and `-logFile`.
The core `PsoCostExecutionScope.FromArguments` rejects malformed evidence options and
retains all unknown/workload/route/worker arguments. Dropping a held-out route label
alone is not evidence of compatible costs; cross-route prediction is a separately
validated model generalization decision.

The embedded base-player identity does not attest externally replaced Addressables
catalogs/bundles. Streaming adapters must bind `contentId`, `contentRevision` and the
actual content digest from the loaded content source, preserving captured build and
shader identity; unavailable revision provenance must reject. `CollectionKey(env)`
is a validated namespace for trace/hotset/dedup grouping, **not** proof that an asset
really has that revision. Out-of-band build transformations, custom shader stripping
based on external services and files outside imported Assets/Packages/settings need
an explicit captured input source before strict compatibility can be claimed.

## Cost-cache lifecycle

Existing online fitting is retained. A collection trace supplies no cost measurements;
new inbox plans leave `costEnvironment` null. Presence-aware `PsoDocumentJson` read/write preserves missing/null identity semantics despite JsonUtility creating inline empty objects for absent fields. Explicit empty contracts and duplicate JSON properties remain invalid. Such plans start with the documented
0.25 ms/state bootstrap prior, minimum initial/bootstrap batch sizes, and still use
the existing measured online model and safety margins. These numbers are priors,
never measured guarantees or a hard bound on opaque driver calls.

`PsoWarmupOrchestrator.SaveCostCache(path)` exports estimates only after successful
warmup and only from phases whose varied steady-state progressive batches have
actually fitted a slope (`HasMeasuredCostSlope`). A cold batch or repeated equal-size
batches do not establish a slope and cannot export the unchanged initial prior. Native bulk
warmup is excluded from this model. `-pso-cost-cache <path>` optionally imports a
version-1 cache before scheduler creation. `PsoCostCacheStorage.TryApply` checks the
cache checksum, exact plan and per-phase collection hashes, model version, nonempty
observations, finite positive estimates, and current collection/cost environment.
It validates every entry before applying any seed. Missing, corrupt, stale or
unmeasured caches leave reset priors intact. Cold probing/online fitting still occur
even after a valid import; fitted intercepts are not imported as observed facts.
No cache is silently promoted to a default and no GPU driver cache is deleted.

## Legacy and migration

Schema-v3 plans without `compatibility` retain the old platform/API/quality and
collection/hash validation path, including the old optional quality rule. Their
`planSha256` is computed using a frozen DTO with the **original field order** so
previously retained evidence remains verifiable. Use `PsoFileUtility.ReadJson` / `PsoDocumentJson.Parse`, not direct `JsonUtility.FromJson`, for plan/trace compatibility semantics. The runtime/build log explicitly
labels this `legacy-platform-api-quality-validation`; it provides no current-build
attestation. Legacy configured cost priors retain their previous behavior, but cannot
import a strict version-1 measured cost cache.

Legacy traces merge only with other legacy traces; identity-bound traces group by
the validated collection namespace and cannot mix different build/content/Unity/device
identities. New invalid identities fail rather than downgrading to legacy. Migration
requires a fresh build with capture, a fresh trace, inbox processing and final build
validation. Do not upgrade an old trace by stamping today's build digest onto it.
Configuration v1/v2-to-v3 migration is unchanged. Unsupported outer document versions
remain rejected. Legacy mode is a compatibility contract, not a security boundary:
these local JSON artifacts are not authenticated against a malicious author.

## Validation and outstanding integration gate

Run `dotnet run --project DotNet/ShaderHitchPipeline.Core.Smoke -c Release` and
`python -m unittest discover -s Tools/tests`. Unity EditMode tests include old hash
roundtrip, identity checksum corruption, actual content/shader edits and generated
identity exclusion. Use the shared serialized-validation runner for Unity tests.

The integrator must exercise the actual training Player -> trace -> installed plan
-> final Player chain after all workers merge, then verify current-identity success,
changed shader/content/quality rejection and a same-device changed-cost-context
bootstrap. The real Addressables fixture supplies loaded-content revision evidence.
The integrated five-run system matrix supplies OS/Player capture evidence. Local
unit tests or Editor dependency hashes do not substitute for these Player gates;
this branch alone claims no measured startup advantage or cross-device coverage.

Unity API references: [dependency hashes](https://docs.unity3d.com/ScriptReference/AssetDatabase.GetAssetDependencyHash.html),
[driver/API identity](https://docs.unity3d.com/ScriptReference/SystemInfo-graphicsDeviceVersion.html),
[build GUID](https://docs.unity3d.com/ScriptReference/Application-buildGUID.html).
