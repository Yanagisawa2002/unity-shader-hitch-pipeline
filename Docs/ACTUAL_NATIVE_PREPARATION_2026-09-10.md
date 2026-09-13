# Megacity native preparation — 2026-09-10

Actual comparison execution is authorized. This continuation completed source
preparation but did not start Unity import, a Player build, tracing or timing:
the measured free space was about **47.77 GiB**, below the current conservative
**100 GiB** starting budget. This is a capacity decision, not an approval gate.
No default policy or performance/coverage claim changes.

Integration base: `9150ab6a1606e0010829b4abace57a270899ebf7`.
Official upstream: `https://github.com/Unity-Technologies/megacity-metro.git`,
commit `07652ee74a1f322c2c3e607020f07be720175680`, tree
`7b0bf700ebc01face91223fd7185a02f44ec376e`, Unity `6000.1.0f1`.
This remains an **external application scene**, not a standardized PSO benchmark.

## Completed, measured preparation

The new independent checkout is `work/actual-20260910/megacity-native`.
Git objects and 432 unique LFS objects came only from the old local object stores
in `C:/Users/EdwinLiu/Downloads/MegacityMetro-pso-validation`. The modified old
working tree, Library and scenes were not copied or changed. The new clone has
no alternates or hardlinks, its origin is the official URL, and its LFS cache is
local to the new `.git`. Downloaded bytes: **0**.

All **3,820 tracked files** passed the existing strict working-byte verifier.
All **434 LFS paths / 432 distinct payloads** passed size and SHA-256 validation.
The tracked native working files total **516,833,318 bytes** (about 0.481 GiB);
source plus independent Git/LFS storage added about **0.93 GiB**, within the
5 GiB preparation budget. The small checkout size does not predict import size.

Manual hydration initially left LFS index stat entries reporting modifications.
The initial strict failure and unsuccessful index-refresh log are retained.
After hashing every actual LFS payload, only the new checkout's 434 LFS index
entries were reconciled through Git's clean filter. The cached diff stayed empty,
Git status became clean, and full strict verification passed. Verification was
not weakened; the earlier [objects-only receipt](Verification/native-host-20260908.json)
has not been promoted or overwritten.

The additive manifest adds exactly the two local PSO UPM packages and changes
no original dependency version. It has **not been applied** to the pristine host.
`resume-outcome.json` identifies the final overlay directory and local integration
commit. Each overlay receipt binds its package files by SHA-256.

Installed-file inspection found Editor `6000.1.0f1_9ea152932a88`, Windows
Standalone support, Windows Player build program/source, IL2CPP executable and
Windows IL2CPP Bee build logic. VS C++ tools are under `Visual Studio/18/Community`;
Windows SDK headers are `10.0.26100.0`. Presence is not proof of successful
IL2CPP linking or this Editor's compatibility with that toolchain. No silent
Mono fallback is permitted. Upstream is IL2CPP and NetCode Client target `0`
(Client); both remain unchanged.

The pinned dependency lock has **91 entries**. Each has matching local package
metadata in the old PackageCache or installed Editor built-ins. Full package
payload integrity and the new UPM resolution remain **unverified**; no old
PackageCache was copied. Preserve and compare the newly resolved lock after
import, and reject original package-version substitutions.

The modified native Editor helper compiled against the installed 6000.1 managed
references in one project, with cached dependencies, `--no-restore`, no build
servers and `BuildProjectReferences=false`: **0 warnings, 0 errors**. This did
not start Unity or validate Entities baking, original assets or the Player.
The existing 14 external-host functional regressions passed, including corrupt
or unhydrated LFS rejection and changes hidden from Git status.
The stage wrapper also passed parsing, rejection-before-action at the measured
capacity, and exception/mutex-release checks. Actual logs and receipts are in
`work/actual-20260910/`; the summary is [native-host-20260910.json](Verification/native-host-20260910.json).

## Original startup and trace path

These paths were read from the fully hydrated pinned source:

- `Assets/Scripts/Gameplay/Client/UI/MonoBehaviours/MainMenu/MainMenu.cs`:
  **Single Player** sets `GameMode.SinglePlayer`, calls `SceneController.LoadGame`
  and shows the original loading screen. The callback is private; no new
  reflection call, scene bypass or injected input route was added.
- `Assets/Scripts/Gameplay/Mix/UI/MonoBehaviours/MainMenu/SceneController.cs`:
  loads `Main` asynchronously from the original `Menu` entry scene.
- `Assets/Scripts/Gameplay/Mix/Player/SpawnSinglePlayer.cs`: uses the original
  prefab and first spawn point, with the original simulation and input systems.
- `Assets/Scripts/Gameplay/Mix/Misc/LoadingScreenSystem.cs` counts requested and
  loaded sections. `Gameplay/Client/UI/Systems/StartingGameSystem.cs` hides the
  screen only when sections, player and camera are ready. It does not wait for
  the PSO observer or guarantee pre-reveal warmup.
- `Gameplay/Mix/Multiplayer/Netcode/CommandLineConfig.cs`: no single-player
  autostart flag; `Application.isBatchMode` means automatic matchmaking.
  `MatchMakingConnector.Start` displays a services notice because the pinned
  `cloudProjectId` is empty. Dismiss it and choose Single Player. Runtime success
  of that exact flow has not yet been observed. Multiplayer, even local sessions,
  requires UGS setup as described in upstream `Documentation/multiplayer-setup.md`.

Main retains six auto-loaded SubScenes: Player_Subscene, Common,
MegacityMetroLevelBounds, Traffic, Level and Blimps. Their original GUIDs and
auto-load values are recorded in `native-scene-inventory.json`. No traffic limit,
material whitelist, camera override, replacement city or simulation freeze was
introduced. The Level phase remains `scene-ed1a49ee1f7b28b499c8cece71ee2353`.

`PsoTraceController` starts a process-wide trace **before Menu** when `-pso-trace`
is present, labels it with `-pso-trace-phase`, and saves on normal application
quit. That phase label does not isolate a SubScene's graphics. Preserve Menu,
background and overlapping states; record their scope, dependencies and original
input route. Six labels alone do not certify six dependency-complete captures.
The observer activates at `SceneSystem.IsSceneLoaded`, potentially after draws;
the observed loading screen is not a new warmup fence or an asset-retention lease.

## Commands for the next capacity-qualified stage

Run from this repository in PowerShell. These commands are prepared, **not run**.
The wrapper rejects occupied mutexes/workloads and checks host, output and temp
volumes. It never cleans caches or kills a process. Each action must wait for
every child; inspect logs and capacity between stages. The default estimate is
80 GiB additional peak plus 20 GiB reserve. Do not lower it using checkout size.
If future measured cache reuse supports a different budget, record that basis
before passing `-EstimatedAdditionalPeakGiB`.

```powershell
$repo = (Get-Location).Path
$actual = Join-Path $repo 'work/actual-20260910'
$outcome = Get-Content -Raw (Join-Path $actual 'resume-outcome.json') | ConvertFrom-Json
$nativeHost = $outcome.preparedHostPath
$overlay = $outcome.preparedOverlayPath
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.1.0f1/Editor/Unity.exe'
$helper = 'Yanagisawa.ShaderHitchPipeline.NativeScenes.Editor.PsoNativeHostBuild'

# Apply only to the new host, after one last strict check. This stage also imports.
$stage = Join-Path $actual 'configure-01'
& ./Tools/Invoke-PsoNativeStage.ps1 -Output $stage -BudgetPath $nativeHost -Action {
    python Tools/pso_external_host.py verify --checkout $nativeHost
    if ($LASTEXITCODE -ne 0) { throw 'Pristine source verification failed.' }
    Copy-Item -LiteralPath (Join-Path $nativeHost 'Packages/manifest.json') -Destination (Join-Path $stage 'original-manifest.json')
    Copy-Item -LiteralPath (Join-Path $nativeHost 'Packages/packages-lock.json') -Destination (Join-Path $stage 'original-packages-lock.json')
    Copy-Item -LiteralPath (Join-Path $overlay 'manifest.json') -Destination (Join-Path $nativeHost 'Packages/manifest.json')
    $unityArguments = @('-batchmode', '-quit', '-buildTarget', 'Win64', '-force-d3d12',
        '-projectPath', ('"' + $nativeHost + '"'), '-executeMethod', ($helper + '.ConfigureWindowsD3D12'),
        '-logFile', ('"' + (Join-Path $stage 'editor.log') + '"'))
    $child = Start-Process -FilePath $unity -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
    $child.WaitForExit()
    if ($child.ExitCode -ne 0) { throw "Configure/import failed: $($child.ExitCode)" }
    Copy-Item -LiteralPath (Join-Path $nativeHost 'Packages/packages-lock.json') -Destination (Join-Path $stage 'resolved-packages-lock.json')
    git -C $nativeHost diff --binary --output="$stage/host-settings.diff"
}

# Review resolved dependencies, settings and import log before this separate stage.
# Preserve Menu/Main, all native assets, IL2CPP and NetCode Client settings.
$stage = Join-Path $actual 'training-build-01'
$trainingPlayer = Join-Path $actual 'players/training-01/Megacity.exe'
& ./Tools/Invoke-PsoNativeStage.ps1 -Output $stage -BudgetPath $nativeHost -Action {
    $unityArguments = @('-batchmode', '-quit', '-buildTarget', 'Win64', '-force-d3d12',
        '-projectPath', ('"' + $nativeHost + '"'), '-executeMethod', ($helper + '.BuildWindowsPlayer'),
        '-pso-training-build', '-pso-native-build-output', ('"' + $trainingPlayer + '"'),
        '-logFile', ('"' + (Join-Path $stage 'editor.log') + '"'))
    $child = Start-Process -FilePath $unity -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
    $child.WaitForExit()
    if ($child.ExitCode -ne 0) { throw "Training build failed: $($child.ExitCode)" }
}

# First capture/correctness session: no benchmark sampler, timer or headless Player.
# The original UI must be visible for the operator's Menu -> Single Player route.
$stage = Join-Path $actual 'native-trace-level-01'
& ./Tools/Invoke-PsoNativeStage.ps1 -Output $stage -BudgetPath $nativeHost -Action {
    $playerArguments = @('-force-d3d12', '-pso-native-host', '-pso-disable-warmup', '-pso-trace',
        '-pso-trace-phase', 'scene-ed1a49ee1f7b28b499c8cece71ee2353',
        '-pso-session', 'native-level-01', '-pso-output', ('"' + (Join-Path $stage 'capture') + '"'),
        '-logFile', ('"' + (Join-Path $stage 'player.log') + '"'))
    $child = Start-Process -FilePath $trainingPlayer -ArgumentList $playerArguments -WindowStyle Normal -PassThru
    $child.WaitForExit() # original menu notice, Single Player, native route, normal quit
    if ($child.ExitCode -ne 0) { throw "Native capture failed: $($child.ExitCode)" }
}
```

Use fresh stage/build/session names on retries; retain the first failure. The
first stage intentionally makes the new host an overlaid application, so its
earlier pristine-source receipt is not a post-import clean-checkout claim.
Retain the complete resulting diff, untracked generated identities and new UPM
lock; fail unexpected upstream content/settings changes before building.

Subsequent native phase captures and dependency readiness must be validated
before merging. Use the existing `PsoBatch.ProcessInbox`, `InstallPlan` and
`ValidateInstalledPlan` [batch API](CLI.md#editor-batch-methods) with a new native
inbox/profile, never the old controlled-reveal plan. Keep no excluded phases or
shader filters; verify every needed `scene-<GUID>` plan phase has
`prewarmAtStartup: false`. Then use the same build helper/options and a fresh
final output, omitting only `-pso-training-build`. A training build is not the
final comparison executable. Validate native content, loading, cancellation,
resource fences and exit separately before freezing formal timing.

## Comparison readiness and capacity

All arms must use one final Player, Editor version, shader/content/build identity,
plan/collection identity, graphics/quality/resolution, device/driver, dependency
events, route and declared cache protocol. None is frozen or timed yet. Fresh
processes with untouched caches are not proof of a driver-cold state. No cache
deletion is implied by the cold arm's name.

| Arm | Current pinned 6000.1 capability and missing evidence |
|---|---|
| cold | `-pso-disable-warmup`; new native Player and route unvalidated; no plan-seeded cold feedback |
| all-at-once | `-pso-warmup-strategy throughput`; native bulk API available in source, trace/plan/native execution still missing |
| scheduled | Existing default; native bulk admission on this cell, not state-count progressive scheduling |
| observed-budget | Opt-in strategy, same bulk granularity here; current native trace/plan, dependency readiness and measured feedback missing |
| fixed-progressive | Rejected by the default native-async-bulk backend; **not executable in the selected default cell** |

The existing explicit `-pso-deadline-backend-mode progressive` can select the
experimental 6000.1 progressive code path; that is not the selected default cell
and has not been validated here. It needs independent correctness/capability
validation and a common declared backend policy for all arms before a four-arm
protocol can be frozen. A newer Editor would require a separately pinned cell
for every arm. Bulk results cannot stand in for fixed-progressive.

The prior host's roughly 42 GiB total / 34 GiB rebuildable Library cache is a
prior reported observation, not a new measurement. With no imported native
cache here, budget **45–65 GiB** new import/build growth and **10–15 GiB** temporary
growth: **55–80 GiB estimated additional peak**. Adding the mandatory **20 GiB**
reserve gives **75–100 GiB** needed at start. At 47.77 GiB free, this means roughly
**27.23–52.23 GiB more free space**, with **52.23 GiB more recommended**. These
ranges are estimates, not a measured native peak. Recheck free space immediately
before any later stage; no old cache or evidence was removed in this continuation.

`work/actual-20260910/resume-outcome.json` is the machine-readable handoff with
the final commit, final measured free space, exact prepared paths, completed and
unexecuted work, blockers and resource-release state. Root outputs and the parent
queue are not modified. No new comparison or performance result was produced.
