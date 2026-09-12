# Megacity continuation after upload clearance — 2026-09-12

The original BuildPipeline completed `training-08` with Editor exit 0. Its complete
Windows x64 / D3D12 / IL2CPP Player started the original application and exited
normally. The startup trace covers only the Menu interval. It exposed a real
driver-identity failure; Main, all six SubScenes, representative training, plan
installation and formal comparisons remain unverified.

Integration start is `2b006e0e9240a0812b23380678746f596f87c3f4`. The external
application remains official Megacity Metro
`07652ee74a1f322c2c3e607020f07be720175680`, tree
`7b0bf700ebc01face91223fd7185a02f44ec376e`, on Unity
`6000.1.0f1_9ea152932a88`. Editor SHA-256 remains
`d2336629da111800a35b592b8c8f595dda02c658a8e3e88c0e7d002e1a0a7f8b`.
The reused host is `work/actual-20260910/megacity-native`, with the existing
additive UPM overlay and generated build metadata. It is not a pristine checkout.
No checkout, installation, full reimport, cache cleanup or upstream workload
substitution was performed in this continuation.

## Completed build and startup

All new raw evidence is under `work/actual-20260912-after-upload/`; earlier
outcomes remain immutable. A fresh four-sample, 45-second clearance checked
other processes and resident activity before `training-08`. The original 1,138
link inputs were present, with original lengths and unmodified timestamps;
15 bare SDK library names were resolved through the retained RSP's LIBPATH.
The normal build subsequently regenerated 1,120 input paths. Presence of the
old cache is not a claim of byte-identical object reuse.

`training-08` ran from 2026-09-12 15:26:23 UTC to 18:28:46 UTC. The real Editor
log records `Build Finished, Result: Success.` and a successful batchmode
shutdown. The accepted Player contains 1,421 files / 3,969,415,108 bytes, with
x64 PE headers and IL2CPP metadata version 31. Its complete file index SHA-256
is `084d333f309c8fa0618f8c728000878057f6df3b7313fd9d4dbf3a02bb129be5`.
This is build evidence, not a PSO performance measurement.

Post-build audit retained all 3,735 original Asset paths, an empty tracked
Assets diff, the original 70 manifest / 91 lock entries, and the 93-entry
resolved lock. The prior complete working-byte/LFS audit is explicitly reused,
not described as a new full LFS rehash. Unity rewrote two additional settings
files from CRLF to LF; exact normalized bytes match. The first strict byte
audit failure and both raw versions are preserved. Thirteen previously audited
generated SceneDependencyCache files remain unchanged.

`native-startup-01` used that complete Player without warmup, automated input
or runtime batchmode. It verified the main D3D12 renderer on AMD Radeon AI PRO
R9700, saved 18 variants / 18 graphics states, and accepted the original
application's normal window-close path with exit 0. No process tree was killed.
The trace SHA-256 is
`305583026a79b1c82aa225674c88574a1565a9476f80fc044e9dd5e8055febd2`.
It is a process-wide Menu startup capture, not a Main route or coverage result.
The original services notice, missing-script warning on ServerEditorWarning
and shutdown allocation telemetry remain in the unedited Player log.

## Repairs requiring the next Player

The native session reported `ArgumentNullException` from driver module capture.
The pinned IL2CPP source implements `Process.GetModules_icall` as an unsupported
internal call. The Windows provider now enumerates its own real loaded modules
with `K32EnumProcessModules` and `GetModuleFileNameW`; existing registry matching,
DriverStore filtering, DLL hashing and compatibility failure behavior remain.
A .NET 8 CPU check links this production provider and makes two actual Windows
captures. It passed locally and is explicitly included in the functional CI
allowlist. It does not substitute for IL2CPP marshalling or GPU-driver validation.

The native observer now emits read-only JSON events for actual SceneManager
callbacks, world identity, request generations and observed SceneSystem readiness.
It adds no input, scene load, reveal fence or simulation change. Readiness can
follow rendering and does not establish first-draw coverage. Windows Unity and
Entities reference compilation passed after fixing a Time-name collision; the
failed first compile is retained.

The owned-process monitor now checks parent/child creation times as well as PIDs.
The actual build history exposed an unrelated older process whose stale parent
PID was reused by a compiler. Four pure fixtures and replay of that real record
passed; no unrelated process was terminated. `scheduled` is now explicit in the
native workload contract alongside cold, all-at-once and observed-budget.
Fixed-progressive remains a separate, unsupported default-bulk requirement.

Before the repaired build, C: has approximately 116.57 GiB free. The new build
uses a conservative estimate of 72 GiB additional peak plus a 20 GiB reserve,
with the unchanged 25 GiB early-stop threshold. This estimate allows regenerated
native code/link output and a separate Player while retaining the imported
Library (measured 15,852,928,148 bytes), old Player and all evidence. It is not a
measured native peak. The earlier 32 GiB estimate applied to a same-source resume.

The next attempt must use a fresh output and complete the normal build flow,
then validate the runtime repair before any representative training. The original
Menu has no equivalent single-player CLI. After a corrected complete Player is
ready, an operator must use the original services notice and Single Player menu;
no input simulation, reflection route or automatic matchmaking substitutes for
that action. No formal comparison or default-policy promotion has occurred.
