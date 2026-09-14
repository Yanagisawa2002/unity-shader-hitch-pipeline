# Official Megacity Metro: acceptance in progress

The new adapted original-application Player has completed a real Windows x64
D3D12/IL2CPP build. Its first run exposed a camera-observation error before Main
entry. The repair passed targeted checks and real Editor compilation, but its
new Player build was interrupted twice by external work. A subsequent clearance
attempt was rejected while another Unity workload was active. **Main, the six
SubScenes, usable Main training, installed plan and policy comparisons are not
accepted.** Boat Attack and URP results do not substitute for this acceptance.

## Source and entry

The official source remains Unity-Technologies/megacity-metro commit
`07652ee74a1f322c2c3e607020f07be720175680`, tree
`7b0bf700ebc01face91223fd7185a02f44ec376e`, under its original license. The retained
host uses Unity 6000.1.0f1 (`9ea152932a88`), Windows x64 Development IL2CPP Player,
D3D12, NetCode Client, original High quality, Menu/Main and all six original
SubScenes. No new checkout, replacement city, camera trajectory, material
allowlist or simulation freeze was introduced.

The declared [application adapter](../Integrations/MegacityMetroNative/ACCEPTANCE.md)
shares the original Single Player action with the existing UI through a public
application API. It waits for initialized, actually rendered Menu, then uses the
original GameMode assignment, asynchronous Main loader and LoadingScreen. A
shared tutorial dismissal happens only after the original loading system hides
its screen. Native QuitSystem owns the requested normal exit. This uses no input
simulation, UI callback reflection or runtime `-batchmode` matchmaking.

Source repairs retain one owned tutorial subscription, dispose the original
temporary spawn command buffer, and apply the same declared Core 17.1 RenderGraph
cleanup backport as Boat Attack/URP. The historical one-off 36-allocation warning
in training-10 is not attributed to these changes without new native proof.

## Actual attempts and failure handling

| Attempt | Actual result |
| --- | --- |
| Configure 01 | Failed on missing `Unity.Mathematics.Extensions` assembly reference; preserved and repaired. |
| Configure 02 | Real pinned Editor compilation and D3D12/Client configuration passed. |
| Training 01 | Own build stopped to add direct engine-clock event timestamps; incomplete output retained. |
| Training 02 | Normal full BuildPipeline/Editor exit; zero errors, 23 warnings; 11,501.364942 seconds. |
| Native training 01 | 28,594 original Menu-camera submissions, then readiness timeout/exit 79; no Main entry. |
| Camera repair | Both Menu and Main gates now separate active route from original persistent-camera ownership; eight targeted checks passed. |
| Training 03 | Repaired source compiled and reached MSVC link. At 03:14:41 UTC an external Unity 6000.5.2f1 workload appeared. The monitor stopped only its owned tree; no completed Player or build receipt. |
| Training 04 | After the earlier peer task completed, fresh clearance accepted four resident build services with zero CPU growth. Two new unowned managed CLI processes appeared at 03:37:03 UTC; the monitor stopped its owned build. This is another incomplete output. |
| Further clearance | At 03:45:04 UTC, preflight found a separate Unity 6000.5.2f1 Editor and its workers/shader compilers. The action never started; no fifth native attempt was launched. |

Training-02 build GUID is `839c626320694c07a0f9957ea5a18fc3`, actual build-command
commit `b2cd6ddbf2fb9f455509d5b719322de323f393cf`. Its complete packaged inventory
contains 1,431 files / 3,989,983,150 bytes; index SHA-256 is
`e71e2203fee9a223aa9967bad2e0309660fbec136760e71f56cc90e1ae0450d4`.
The adapter/package input index is
`c0699ba95451613cda9d825ef20391c2b22c1a28bbbaafad93b4174bcc8628cc`.
Later source/documentation commits do not relabel that binary.

All failed-run render rows identify the original `MainCamera`, Game type,
1920×1080 and no target texture, with camera scene `DontDestroyOnLoad`.
The old observer required camera ownership to equal Menu/Main, preventing its
entry action. Schema 2 preserves camera scene path/name, adds `activeScene` and
menu initialization, and checks the actual HybridCameraManager screen camera for
both route gates. The Menu-only trace is excluded from Main training.

Training-03 used repair commit `977d81fcf081f63376567023d22e92845993d60c`.
Its serial guard detected a foreign Editor with a distinct creation identity;
subsequent inspection identified the external `FourSceneMain20260914` project.
No foreign process was stopped or changed. A follow-up at 03:16:33 UTC confirmed
the owned Editor/link were absent and N: and the mutex were released. That
snapshot does not establish a future exclusive execution window.

Training-04 used `0c307208ba0bd0fb6cf22bb53cbc049e05d222cf` and included the updated
package README in its newly captured asset identity. Its non-resident-classified
`dotnet` clients and parents had exited before follow-up, so their exact commands
are not inferred from process names. Owned processes were absent and N: released
at 03:39:41 UTC. The later rejected preflight proves new external Unity work was
still present; completion of the earlier peer task did not reserve the machine
against subsequent work. Independent local build/test reviews also need the
shared resource boundary before the next long native build.

The full explicit CPU CI entry passed 78 Python checks, 42 scheduler cases,
Core/streaming/lifecycle checks, the simulated-sink public API example, the actual
current-process Windows module API smoke and training-build scope checks. Tools
PowerShell syntax passed. The two subsequent workload-name additions also passed
syntax checks and Ubuntu/Windows PR CI at `0c307208`; neither CPU CI nor these
process checks substitutes for the missing native Main run.

## Capacity, provenance and remaining execution

Training-03 started with measured 682.327202 GiB available and ended with
677.729752 GiB; the follow-up measured 675.998638 GiB while external work was
active. Training-04 measured 668.427994 GiB before and 664.908138 GiB after.
The 80 GiB additional-peak budget was a conservative estimate, with a
20 GiB reserve and 25 GiB early-stop threshold. Net space growth is not the
temporary peak. No cache or old evidence was deleted.

Raw attempts and receipts remain under ignored `work/actual-20260914-external-01/`.
[Machine-readable evidence](Verification/megacity-native-20260914.json) binds the
completed build, failed process and repair to their retained hashes. Large
third-party assets, Library/Bee, Players and raw machine logs remain outside Git.

After the external workload ends, take fresh clearance and use the retained host
and Library/Bee in a new attempt/output directory. A normal complete build must
precede the new original-route run. Do not manually link one DLL or launch either
incomplete output as a Player. The [reproduction commands and native gates](../Integrations/MegacityMetroNative/ACCEPTANCE.md)
require sustained same-world payloads for Blimps, Common, Level,
MegacityMetroLevelBounds, Player_Subscene and Traffic; positive native population,
advancing simulation, stable moving traffic/blimps, visible original-camera
submissions/screenshots, and normal exit.

Only a usable original Main capture may feed merge/install/validation and a
common final Player. Independent native-work/ownership pilots must pass before
freezing supported serial comparisons. One process-wide trace is not six isolated
coverages. Native-async-bulk does not provide fixed-progressive; retained caches
are not driver-cold. No Megacity performance or coverage improvement is claimed.
