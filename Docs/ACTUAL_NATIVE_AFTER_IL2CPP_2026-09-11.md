# Megacity continuation after Windows IL2CPP installation — 2026-09-11

**Windows IL2CPP support is verified. The real training build reached native
linking, then an external Unity job interrupted it. No complete runnable Player
or native runtime/comparison result was produced.**

The user-installed Windows IL2CPP module is now accepted by the real pinned
Editor. `windows-build-diagnostic-03` exited 0 with the actual reason line:

```text
active=StandaloneWindows64; selectedStandalone=StandaloneWindows64; subtarget=Player; backend=IL2CPP; targetSupported=True; reason=<none>
```

The previous [execution record](ACTUAL_NATIVE_EXECUTION_2026-09-11.md) remains a
historical account of the missing-module failure. Its raw outcome and evidence
were not modified. Module installation happened in the parent/user workflow;
this continuation neither repeated installation nor changed execution policy.

## Identities and retained host

- Integration start: `783376a79dbd13b9892836fac962a1e6ccfe755e`.
- Official application: `Unity-Technologies/megacity-metro`, commit
  `07652ee74a1f322c2c3e607020f07be720175680`, tree
  `7b0bf700ebc01face91223fd7185a02f44ec376e`.
- Editor: `6000.1.0f1_9ea152932a88`, unchanged SHA-256
  `d2336629da111800a35b592b8c8f595dda02c658a8e3e88c0e7d002e1a0a7f8b`.
- Windows x64 / D3D12 / IL2CPP / Player subtarget / NetCode Client.
- Reused host: `work/actual-20260910/megacity-native`. The existing applied
  `overlay-final-e5c75ad67d9d` remains an additive overlay, not a pristine host.
  No second checkout or full reimport was created.

After the interrupted build, read-only working-byte verification passed
for all 3,820 original tracked files, 3,735 original Assets, 434 LFS paths / 432
unique payloads, and 149 integration package files. The original 70 manifest
entries and complete 91 lock entries were preserved; the lock still has 93
entries after adding the two local packages. The previous 698-file immutable
evidence manifest, previous outcome and original overlay receipt were rehashed.

The actual host also contains generated build metadata. Thirteen ignored
`Assets/SceneDependencyCache*` files already existed after the previous
`training-01`: the pinned Entities generator, binary field layout and all six
original SubScene GUIDs identify them. They are neither replacement scenes nor
coverage evidence. Interrupted `training-06` additionally left six untracked
Resources files: the PSO build identity and two Unity performance-framework
metadata documents, each with its `.meta`. Their exact bytes and generator
sources were retained. The latter documents contain no measurement results.
These additions must be accounted for separately from unchanged original Assets.

`training-06` also rewrote `GraphicsSettings.asset` line endings. Full normalized
bytes match the retained pre-build file. The only semantic tracked host changes
remain the two local package additions and explicit Windows D3D12 selection.

## Actual interruptions and tooling repairs

Several preflights rejected active SUMMIT/Forest work. During two real Editor
attempts and the first substantive training build, new external Unity 6000.5.2
or 6000.5.3 workloads appeared despite the held shared mutex. The process monitor
stopped only its own Editor tree and retained each failure. No unrelated process
was terminated. A later stable window allowed `training-07` to proceed into
IL2CPP conversion and actual VS 18 / MSVC 14.51 linking of `GameAssembly.dll`.
The `/LTCG` linker command and process identity/progress observations are retained
as build diagnosis, not PSO timing evidence. On 2026-09-11 at 08:01:35 UTC,
Unity 6000.5.2 PID 25056 started SUMMIT's
`MainPr1890Pr1875Preview20260911` EditMode tests. The owned 6000.1 Editor, PID
21692, was stopped at 08:01:49 UTC after approximately 82 minutes of this build.
The declared two-hour limit and capacity stop did not cause that interruption.

| Attempt | Actual outcome |
|---|---|
| `windows-build-diagnostic-02` | Positive backend reason observed; interrupted by newly started external Unity processes, so not accepted as a completed stage. |
| `windows-build-diagnostic-03` | Accepted Editor exit 0 and `reason=<none>`. |
| `training-03` | Player script compilation started; external 6000.5.2 work interrupted the owned process. |
| `training-04`, `training-05` | Preflight rejected active external work; no Megacity Editor started. |
| `training-06` | Original shader/VFX compilation progressed; external 6000.5.3 work interrupted the owned process. |
| `training-07` | IL2CPP conversion, object compilation and actual MSVC linking reached; external 6000.5.2 work interrupted the owned tree. |

The retained output has **1,420 files / 2,416,073,120 bytes**, including a Player
launcher and data, but lacks the completed `GameAssembly.dll`. It must not be
launched or counted as a complete Player. About 29 MB of available host/Bee logs,
trace events, response files and partial link output were additionally copied
into immutable attempt evidence. The Editor also logged a D3D12 upload-buffer
sizing diagnostic before it proceeded to the Bee build; native GPU correctness
remains unverified. No new C++ compilation error was reported before interruption.

`Invoke-PsoNativeStage.ps1` now includes Forest Player names in the existing
workload gate. It also fixes a reproduced PowerShell scoping bug: a local
`$LASTEXITCODE = 0` shadowed the automatic global exit code of native commands.
An actual child exited 7 while the action saw 0. The guard now initializes and
checks the global code and records it in the stage receipt. Separate native
exit-7 and exit-0 checks passed after the repair. The exact process snapshot that
decides post-action acceptance is retained even if a process exits before the
final release snapshot.

`Wait-PsoOwnedProcess.ps1` now detects newly started external native workloads
and noncompiler managed clients during an owned stage. It records their process
identities and closes only the process object owned by this stage, using its
existing tree-termination fallback. Real interrupted attempts exercised this
path; the independent successful Editor diagnosis exercised normal completion.
The 20 GiB reserve and 25 GiB early-stop threshold remain unchanged.

The 16 existing CPU external-host/execution-policy regressions passed under the
shared mutex, and all 15 Tools PowerShell scripts parsed. Native exit-code 7/0
regressions also passed. A later ten-minute wait never obtained a short serial
window for an additional final suite. An unexecuted optional normal-close
extension was therefore kept only as an ignored local proposal; it is not part
of the shipped monitor. The committed monitor is byte-identical to the version
actually exercised by `training-07`. Final file preservation, hashing and source
review were read-only operations, not additional functional/native executions.
No CPU allowlist was broadened and no remote CI was executed.

## Remaining native gates

This is an official external application scene, not a standardized PSO benchmark.
Menu → Single Player → asynchronous Main, all six automatic SubScenes, original
materials, simulation, traffic and camera remain intact. Source inspection found
no equivalent Single Player CLI option. Runtime `-batchmode` requests automatic
matchmaking; no input simulation or reflective menu invocation is permitted.

No native process, including a Menu-only startup probe, was launched. The
prepared local capture recipe requires a completed Player and rejects this
partial output. Its bounded normal-close proposal remains unexecuted.
Process-wide tracing can begin before Menu, but a Menu-only capture cannot pass
the native Main/content gate. Phase labels do not isolate SubScenes, and
`SceneSystem.IsSceneLoaded` can follow rendering. The original loading screen
does not establish a warmup/resource fence. Native correctness, route/dependency
evidence, trace coverage, plan merge/install/validation and a common final Player
must pass before a formal comparison protocol is frozen.

No formal cold, all-at-once, scheduled or observed-budget comparison has run.
A new process with unchanged caches is not driver-cold. The pinned 6000.1
`native-async-bulk` backend does not implement fixed-progressive; no experimental
backend or Editor substitution was selected. Defaults, coverage and performance
claims are unchanged.

## Capacity and handoff

The post-installation preflight measured 99.757 GiB free. The remaining-build
budget stayed at an estimated 72 GiB growth plus the required 20 GiB reserve;
the already consumed installation allowance was not added again. Before
`training-07`, the retained Library contained 7,096,694,580 measured file bytes
and available space was about 95.22 GiB. After interruption the Library contains
12,726,649,772 bytes and Temp 1,106,321,842 bytes. The link input file inventory
contains about 2.43 GB, and the partial Player about 2.42 GB. These are measured
file sizes, not a measured full native peak.

For a same-source retry that reuses the completed import, shader compilation,
IL2CPP conversion and object files, the remaining **32 GiB additional-peak
estimate + 20 GiB reserve = 52 GiB recommended free** covers the unfinished link,
PDB/scratch and another Player copy with margin. It does not apply to a fresh
import or changed runtime sources/settings; recheck the retained inputs and
budget before resuming. Keep the 25 GiB early-stop threshold. The final outcome
contains the latest measured free space; volume changes also include other
machine activity and are not attributed entirely to this task.

New evidence is under `work/actual-20260911-after-il2cpp/`. Its
`resume-outcome.json` distinguishes completed gates from unexecuted ones,
identifies the final local commit, and records the final process/mutex/alias check.
The committed [verification summary](Verification/native-host-after-il2cpp-20260911.json)
binds the retained evidence by hash.
The owned processes and `N:` mapping were released and the shared mutex was
available/released. Other Unity processes, including a later interactive
SUMMIT Editor, were left untouched. A parent-coordinated uninterrupted window
across all launchers is the immediate resume requirement. Reuse this host and
compiled cache with a fresh training output directory; no module reinstall,
checkout recreation or additional cache deletion is requested.
No large assets, caches, Player or ignored raw evidence are committed. Parent
outputs, global queue and cloud synchronization remain under parent control.
