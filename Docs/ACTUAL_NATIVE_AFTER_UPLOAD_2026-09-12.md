# Megacity continuation after upload clearance — 2026-09-12

`training-10` completed the normal BuildPipeline with Editor exit 0. Its complete
Windows x64 / D3D12 / IL2CPP Player passed actual original-Menu startup, Windows
driver identity verification and normal exit. Main, all six SubScenes,
representative training, plan installation and formal comparisons remain
unverified. One startup reported a Persistent-allocation warning; its origin
remains unresolved. No leak-free, performance or coverage improvement is claimed.

The [machine-readable record](Verification/native-host-after-upload-20260912.json)
binds raw evidence paths, hashes, commands, failures, budgets and limitations.
All new raw evidence is in `work/actual-20260912-after-upload/`; previous receipts,
failed attempts, Player outputs and traces remain intact.

## Identities and retained source

Integration start: `2b006e0e9240a0812b23380678746f596f87c3f4`.
The repaired Player uses source commit
`2e1e28b3a1ccd7854de5136fe1596ff5a436eae0`; later result-document changes do not
describe another Player build.

The official external application remains Megacity Metro commit
`07652ee74a1f322c2c3e607020f07be720175680`, tree
`7b0bf700ebc01face91223fd7185a02f44ec376e`, with Unity
`6000.1.0f1_9ea152932a88`. Editor SHA-256 remains
`d2336629da111800a35b592b8c8f595dda02c658a8e3e88c0e7d002e1a0a7f8b`.
The actual Player toolchain was VS 18 Community MSVC 14.51.36231 and Windows SDK
10.0.26100.0. The compiler version printed when the prebuilt Editor starts does
not identify this Player compiler.

The existing host `work/actual-20260910/megacity-native` retained its applied
additive UPM overlay and imported Library/Bee. The original overlay receipt at
`work/actual-20260910/overlay-final-e5c75ad67d9d` is unchanged. The host is not a
pristine checkout. No new checkout, installation, full reimport, cache cleanup,
manual DLL substitution or upstream scene/material/simulation change occurred.

The fresh post-training-10 audit compared all 3,820 original working files,
including 3,735 byte-identical original Assets, and rehashed 434 LFS paths / 432
unique payloads. It verified 151 current package files: the earlier 149 plus
the new Windows module provider and its metadata. Original 70 manifest and 91
lock entries remain unchanged; the resolved lock has the two local packages,
93 entries total. Settings match the stage-start bytes. The complete host diff,
before/after settings, generated metadata and 13 unchanged SceneDependencyCache
files are accounted for separately.

## Actual attempts

| Attempt | Actual result |
| --- | --- |
| training-08 | Normal build success and exit 0; 1,421 files / 3,969,415,108 bytes. |
| native-startup-01 | Original D3D12 Menu and normal exit passed; driver identity failed on the unsupported IL2CPP module-enumeration call. |
| training-09 | Interrupted when new external dotnet client trees appeared. Only the owned tree was stopped; the retained partial output lacks GameAssembly.dll and failed acceptance. |
| training-10 | Normal build success and exit 0, 2026-09-12 20:16:09–23:15:04 UTC; 1,422 files / 3,980,298,529 bytes. |
| native-startup-02 | Repaired IL2CPP driver identity, original Menu events, trace save and normal exit passed; one shutdown warning reported 36 Persistent allocations. |
| native-startup-leak-diagnostic-01 | Same Player with the log-recommended stack-diagnostic argument; normal exit, no recurrence of that warning and no allocation stack captured. |
| native-startup-03 | Same Player and original startup arguments; normal exit, no recurrence of that warning. |

The training-10 complete Player index SHA-256 is
`897950da79f85c0d2e75caba07ef856c3b06ed1ad4d4012c50b641c3ac73b6fe`.
Its GameAssembly SHA-256 is
`85cad41f47f51944c07b3777c96f329bb63d60406bfbc240ed34196b16e708a6`.
The native binaries have x64 PE headers and IL2CPP metadata version 31.
All 1,422 Player files were rehashed after the runs and remained unchanged.
These build intervals and process CPU observations diagnose compilation; they
are not PSO workload performance results.

The original 1,138 retained link inputs were checked using the RSP's LIBPATH,
including its 15 bare SDK library names. The normal build may regenerate cached
inputs; retained input presence does not attest byte-identical object reuse.
training-09's interruption snapshot did not retain external command lines or
repository identity. No origin is attributed to those external clients. A new
source/input audit and observed serial window preceded training-10.

The first training-08 settings audit rejected two CRLF-to-LF changes. Its failure
and both raw versions remain; exact normalized bytes matched. Another retained
auxiliary checkpoint had an incorrectly parsed elapsed-time field, corrected by
an additive receipt using its ISO timestamps. Neither issue was hidden by
rewriting original evidence or relaxing the native build/Player gates.

## Runtime repair and validation scope

The actual pinned IL2CPP source leaves `Process.GetModules_icall` unsupported.
The provider now uses real `K32EnumProcessModules`, `GetCurrentProcess` and
`GetModuleFileNameW` calls. Registry matching, DriverStore filtering, DLL hashing
and compatibility failure behavior remain. The .NET 8 CPU smoke project links
this production provider and is explicitly included in functional CI.

Actual native-startup-02 validated AMD Radeon AI PRO R9700, D3D12 and driver
32.0.31041.1004. Four loaded DriverStore DLL hashes and the display registry were
independently reread; the canonical identity reproduced as
`e97d5c545ccdc38bcfd39c4a0f53f6116f6a3fe55832479256d79e16bbc6985c`.
Driver verification also passed in both declared follow-up runs. The secondary
D3D11 device in the log is for Media Foundation video decoding.

Each repaired run saved 18 variants / 18 graphics states from the process-wide
Menu interval, with no warmup. The accepted startup-02 collection SHA-256 is
`3780f0a2cb559ad020968c9888490885e5c91e886e80d9cad5d20917b039187f`.
Its build GUID is `09787dbb57014c449c02c170bea659a0`. These captures have no Main
route or native dependency-readiness evidence and cannot seed an accepted native
plan. Startup-01 recorded Low quality; the repaired runs recorded High. These
diagnostic runs are not matched performance arms, and no cause is assigned to
that quality difference. Future formal runs must freeze and verify their settings.

Read-only observer events now record actual process/SceneManager callbacks,
worlds, requests, generations and observed SceneSystem readiness. Process and
SceneManager rows have default request/world/warmup fields; use the event kind
and the populated observer/request rows. A readiness event may follow rendering.
No label isolates a SubScene or proves first-draw coverage, and the observer does
not hold the original loading screen or own upstream resource fences.

The owned-process helper now checks parent/child creation times as well as PIDs,
fixing an observed stale-parent PID reuse. No unrelated process was terminated.
Local checks passed: 60 Python tests, Core smoke, 127 streaming checks, 39 virtual
clock scheduler tests, the simulated-sink PolicyExample, two actual Windows module
captures, four ownership fixtures and replay of the real PID-reuse record.
The native observer compiled against the pinned Windows Unity/Entities assemblies
after qualifying `UnityEngine.Time`; the failed initial compile remains.
No remote CI was executed by this worker.

The original ServerEditorWarning missing-script reference and unlinked-services
notice remain in the native logs. Shutdown allocation telemetry remains too.
The new 36-allocation warning did not recur in the two declared follow-ups;
that is not a fix or a leak-free guarantee. Its origin is still unresolved.

## Capacity, release and next original-menu action

The repaired-source training-09 estimate was 72 GiB additional plus a 20 GiB
reserve. The same-source training-10 resume used 32 GiB additional plus the same
reserve, based on retained compiled inputs and the imported Library. It started
with 114.006 GiB and ended with 110.104 GiB free. The retained Library then measured
15,910,856,828 bytes; Unity's normal build cleanup left Temp at zero bytes.
The trace pilots each allowed 2 GiB of new output. These are explicit estimates;
sampled volume changes are not an exact peak or solely task-attributed growth.
The 25 GiB early-stop threshold remained unchanged throughout.

At the release receipt, C: had 109.930 GiB free. No identity-matched owned native
or build processes remained; all acquired stage mutexes were released. The shared
mutex was obtainable and the owned N: mapping was removed. No extra cache or old
evidence was deleted. A future same-source cached build provisionally retains the
32 + 20 GiB estimate; reassess it after the accepted trace/plan changes are known.

The remaining external action is to enter Single Player in the original UI.
The pinned application has no equivalent single-player CLI. From this repository,
with the operator ready, the existing bounded pilot can be invoked in a fresh
stage directory:

```powershell
pwsh -NoProfile -File work/actual-20260912-after-upload/run-native-pilot.ps1 `
  -StageName native-content-01 -PlayerStageName training-10 `
  -OperatorSinglePlayer -CloseAfterSeconds 600 -TraceAutoStopSeconds 0
```

This command is prepared, not executed. Dismiss the original services notice if
shown and click **Single Player**. The flag only records the intended operator
action; it does not click, invoke a callback, change the camera or load Main.
The script holds the shared lock, verifies the Player identity, creates N: only
if free and bound to this repository, and releases any mapping it creates.
After 600 seconds it requests a normal window close, then waits 10 seconds. A
failure stops only the owned tree and is retained as failed. Normal close was
verified in Menu; exit from Main remains unverified. Do not add runtime batchmode:
upstream uses it for automatic matchmaking.

That pilot still needs content, all six original SubScenes, readiness, native
route and Main-exit review. Its process label is not automatically representative
phase training. Only after those gates may trace/plan merge, installation and
validation, the common final Player, and a frozen protocol precede finite cold,
all-at-once, scheduled and observed-budget comparisons. The default 6000.1
native-async-bulk cell cannot execute fixed-progressive; the experimental backend
has not been selected or validated here. No default was promoted and no formal
comparison or performance queue was started.