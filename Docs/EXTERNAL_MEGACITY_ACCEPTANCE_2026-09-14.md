# Official Megacity Metro: acceptance in progress

The new adapted original-application Player has completed a real Windows x64
D3D12/IL2CPP build. Its first run exposed a camera-observation error before Main
entry. The repair passed targeted checks and real Editor compilation, but its
new Player link was interrupted by an external Unity workload. **Main, the six
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

## Capacity, provenance and remaining execution

Training-03 started with measured 682.327202 GiB available and ended with
677.729752 GiB; the follow-up measured 675.998638 GiB while external work was
active. Its 80 GiB additional-peak budget was a conservative estimate, with a
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
