# Megacity native execution — 2026-09-11

**Real import and D3D12 configuration passed. The training Player build failed
because the installed 6000.1 Editor lacks Windows IL2CPP Player support.** No
Player, native correctness result, trace, installed native plan or formal timing
result was produced. This is the official external application scene, not a
standardized PSO benchmark. No default policy, coverage or performance claim changes.

Execution was authorized after the parent completed the specified cache cleanup.
The first large-stage gate measured **112.311 GiB free**, exceeding the initial
**80 GiB additional-peak estimate + 20 GiB reserve**. Capacity did not stop this
run. The earlier [preparation record](ACTUAL_NATIVE_PREPARATION_2026-09-10.md) and
its receipts remain historical and unchanged.

## Identities and host audit

- Integration start: `b367aa0f98facb044f8f2df4f5a1cc7ca526dd36`.
- Original overlay code: `e5c75ad67d9de791ea5058b4e2a610f91b14273e`.
  The only intervening tracked change was `.github/workflows/functional.yml`,
  adding this branch's CPU CI trigger. Neither commit is a native measurement.
- Official application: `Unity-Technologies/megacity-metro`, commit
  `07652ee74a1f322c2c3e607020f07be720175680`, tree
  `7b0bf700ebc01face91223fd7185a02f44ec376e`.
- Actual Editor: `6000.1.0f1_9ea152932a88`; Windows x64 / D3D12-only / IL2CPP /
  Player subtarget / NetCode Client. No Mono, Editor-version or runtime-route substitution.

The existing independent `work/actual-20260910/megacity-native` checkout was
reused. Before applying the overlay, strict verification reread all **3,820
tracked files**, including **434 LFS paths / 432 unique payloads**, and verified
Git pin/tree, clean state, all **149 package files**, and the unapplied manifest.
The reused overlay is `work/actual-20260910/overlay-final-e5c75ad67d9d`;
its preparation receipt SHA-256 is
`1a112fe6c6b1fac4e657394b056113573dd68264802985e927436c8c41c776de`.

The host is now an **overlaid application, not a pristine checkout**. Each Editor
attempt retains its command, Editor hash, package-file hashes, complete settings
snapshots, resolved lock, host diff, process receipt and capacity samples under
`work/actual-20260911/`. Package fixes changed two of the 149 files; their final
hashes are recorded separately from the original overlay receipt.

Fresh UPM resolution registered **93 packages**. The original **70 manifest
entries** and **91 lock entries**, including their complete values, remained
unchanged; only the two local PSO packages were added. No old PackageCache was
copied. Actual upstream working-file hashes remained unchanged outside:

- `Packages/manifest.json` and `Packages/packages-lock.json`: additive local packages.
- `ProjectSettings/ProjectSettings.asset`: explicit Windows D3D12-only API entry.
- `NetCodeClientAndServerSettings.asset` and `ShaderGraphSettings.asset`: line endings only.

There are no added or changed upstream Assets. Menu/Main order, all six original
auto-loaded SubScenes, original materials, simulation, traffic, resources and
camera paths are preserved. The original loading screen has not become a warmup fence.

## Actual attempts and repairs

| Attempt | Observed result and follow-up |
|---|---|
| `configure-01` | UPM resolved; C# compilation failed with CS0012/CS8377. Added the missing `Unity.Mathematics` reference to the native observer asmdef. |
| `configure-02` | C# compilation and configuration completed, Editor exit 0; VFX imports still failed on long paths. The stage also retained a false workload rejection caused by an exited Unity process handle. Neither issue was counted as an accepted import. |
| `path-alias-preflight` | Rejected while build-server processes were present. No unrelated process was terminated. |
| `path-alias-preflight-02` | Recognized idle compiler servers were observed with zero CPU growth and recorded separately. A temporary `N:` alias mapped to the same repository bytes. |
| `configure-03` | Accepted real import/configure, exit 0. All 34 original VFX assets reimported without the earlier VFX/path/C# errors; asset, UPM and settings audit passed. |
| `training-01` | Actual training build entered Entities content processing, then Scriptable Build Pipeline rejected the configuration. No Player or IL2CPP C++ compilation/link result. |
| `windows-build-diagnostic-01` | Actual Editor diagnosis: Windows target supported, Player selected, IL2CPP selected, but that scripting backend is not installed. |
| `training-02` | Updated build helper compiled and rejected the same missing backend before expensive content baking. Expected failure validates the diagnostic, not a successful build. |

The inaccessible URP VFX template existed at a **264-character physical path**.
A temporary `subst N:` alias shortened it to **192 characters**, with identical
SHA-256. Reimporting all original `.vfx` files through that alias repaired the
observed failures. No second checkout, template replacement, global long-path
setting change or package-version change was used.

`PsoNativeHostBuild.DiagnoseWindowsBuild` reads the pinned Editor's build-support
reason. The build helper uses that same diagnostic to report a known backend
failure early; it does not bypass Unity's gate. Reflection is confined to this
Editor diagnostic and does not invoke a runtime menu callback.

`Invoke-PsoNativeStage.ps1` still holds `Local\CodexR9700VNextUnityGpu` and rejects
active build/workload processes. It records recognized resident Roslyn/MSBuild
servers separately only after observing zero CPU growth over three seconds. It
does not stop those services. Failure receipts now also retain post-stage process
and capacity snapshots.

`Wait-PsoOwnedProcess.ps1` samples capacity and observed descendants, logs progress,
and uses a **25 GiB early-stop threshold** ahead of the 20 GiB reserve. On a
declared timeout or monitor failure it closes only the process returned by this
stage's `Start-Process`, then terminates that owned tree if it cannot exit. It
disposes the completed process handle so an exited Unity process is not mistaken
for another active workload. Normal-exit, timeout and owned child-tree cleanup
checks passed using hidden PowerShell sleep processes only. No real stage needed
a capacity termination.

## Exact external component gap

The runtime Editor diagnostic states:

> Currently selected scripting backend (IL2CPP) is not installed.

Only Mono Player variations exist in the installed WindowsStandaloneSupport
directory. The generic `il2cpp.exe`, Windows build program and Bee source being
present did not establish Windows IL2CPP Player support. VS 18 Community, MSVC
14.51 and SDK 10.0.26100 are present, but their compatibility with this Player has
**not reached compilation or linking**. This evidence does not justify a VS reinstall.

The exact component is **Windows Build Support (IL2CPP) for 6000.1.0f1**, available
on the [official release page](https://unity.com/releases/editor/whats-new/6000.1.0f1).
The saved [official release API metadata](https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.1.0f1&limit=1) declares **187,532,288 download bytes**
and **715,260,928 installed bytes**; the installer HEAD response reports
**187,532,368 bytes**. These are publisher/server values, not a local installation
measurement. The published MD5 is `6eeace05af659d927c223ae4cafccf2b`; the payload
was not downloaded or verified. The metadata response itself has a retained SHA-256.

Restoring this component changes the Editor installation under Program Files.
Per this run's instruction to record system/toolchain installation gaps, no
installer was executed. A **2 GiB estimated staging/install allowance** covers
the declared download/install sizes with margin; the actual installation peak
is unknown. Record and verify any retrieved payload before using it. Recheck
Editor support after adding the exact component, then attempt a fresh training
build; do not substitute Mono or patch Scriptable Build Pipeline eligibility.

## Resume after restoring that component

The retained machine-specific driver is
`work/actual-20260911/run-editor-stage.ps1`. It preserves per-attempt commands,
settings, package hashes and process/capacity receipts. It invokes the tracked
stage guard and process monitor. Do not reapply the original manifest or rerun
the pristine-checkout verifier against the intentionally modified host.

In PowerShell 7, with a new short alias to the same workspace, the next independent commands are:

```powershell
# Run from the integration worktree after the exact module is restored.
# N: was released at handoff. Do not replace an existing drive mapping.
$repo = (Get-Location).Path
if (Test-Path -LiteralPath 'N:/') { throw 'Choose an unused short drive alias.' }
subst N: $repo
if ($LASTEXITCODE -ne 0) { throw 'Could not create the short path alias.' }
try {
    & ./work/actual-20260911/run-editor-stage.ps1 `
        -StageName windows-build-diagnostic-02 -Mode Diagnose -AdditionalPeakGiB 2 `
        -UnityHostPath 'N:/work/actual-20260910/megacity-native'
    # Exit 0 from Diagnose alone does not assert IL2CPP support. The separate
    # build stage independently checks that backend before content baking.
    & ./work/actual-20260911/run-editor-stage.ps1 `
        -StageName training-03 -Mode Training -AdditionalPeakGiB 72 `
        -UnityHostPath 'N:/work/actual-20260910/megacity-native' `
        -UnityPlayerPath 'N:/work/actual-20260911/players/training-03/Megacity.exe'
} finally {
    # Remove only the alias created above, after all owned processes exit.
    subst N: /d
}
```

Choose new attempt names if those directories already exist. This code is a
resume recipe, not a completed build. A new import is not required simply to
create another complete checkout. Reuse the actual imported cache and review
the current full upstream diff before building.

The first actual build budget was reduced to **72 GiB estimated remaining growth**
after roughly 8 GiB of measured volume-space consumption and successful UPM/import.
The host Library now contains about **5.39 GiB of measured file bytes**. Volume
free-space changes can include other machine activity; they are not an exact
accounting of task allocations or a measured full native peak. With a separate
2 GiB module-recovery allowance, the conservative resume recommendation is
**74 GiB additional + 20 GiB reserve = 94 GiB available before recovery/build**.
The alias-release check measured **104.155 GiB free**; the later host audit
measured **104.079 GiB**. No further cache cleanup is requested. The handoff has
the final fresh reading. Recheck actual volumes and processes at each stage.

## Unexecuted native gates and verification

There is no built executable to launch yet. After a training build succeeds,
the original services notice and **Menu → Single Player → asynchronous Main**
route still needs independent native verification. Source inspection found no
equivalent noninteractive single-player flag. Runtime `-batchmode` requests
automatic matchmaking and is unsuitable. No automated input, reflection-based
menu invocation or replacement startup path was added. Any operator action can
be scoped against the produced Player after the build succeeds.

Correctness/loading/normal exit, process-wide training trace, dependency readiness,
plan merge/install/validation, final comparison Player and timing protocol remain
**not executed**. A phase label does not isolate a SubScene; scene readiness can
follow rendering, and the existing loading screen is not a warmup/resource fence.
Neither six labels nor the old controlled-reveal trace establishes native coverage.

No cold, all-at-once, scheduled or observed-budget comparison ran. A common final
Player, scene/build/plan/collection identities, route/dependency events, device,
graphics settings, cache protocol, rounds and stop rules must be frozen only after
the native gates pass. A fresh process with unchanged caches is not driver-cold.
The selected 6000.1 `native-async-bulk` backend does not support fixed-progressive.
Its experimental progressive alternative remains unvalidated and unselected.

The 14 external-host regressions and two execution-policy regressions passed;
all 15 Tools PowerShell scripts parsed. These checks do not measure native
performance. Failure logs, including upstream warnings and intermediate failed
imports, are retained rather than replaced by the successful retry.

The committed [verification summary](Verification/native-host-20260911.json)
binds raw stage evidence by SHA-256. The complete ignored handoff is
`work/actual-20260911/resume-outcome.json`, including the final integration commit,
fresh capacity/resource checks and remaining work. No large third-party assets,
cache, Player or raw result is committed. Parent outputs, queue and cloud PR
remain under the parent's control.
