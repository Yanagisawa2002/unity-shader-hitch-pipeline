# Pinned official Boat Attack integration

[Native results and limitations](../../Docs/EXTERNAL_BOAT_ATTACK_2026-09-14.md)
cover two complete four-arm sequences, including a retained negative regression
and its verified repair. [source-lock.json](source-lock.json) binds the original
official source. The optional adapter is copied into the external host; it is
not bundled as generated workload content in the UPM runtime package.

Use Unity 6000.1.0f1 with installed Windows IL2CPP support, the Windows compiler/SDK,
Git LFS, Python 3.12 and PowerShell 7. Hosts/evidence must be new directories under
this repository's ignored `work/`. All heavy stages use the shared machine mutex,
capacity gate and ownership monitor. N: must be free for the owned short mapping.
Normal visible Players render the workload; no Computer Use or input simulation
is required. Do not add runtime `-batchmode`.

## Source and first build

Acquire exactly `6d51b73619199c6dc8266045ea5c494355acf6b5` from
`https://github.com/Unity-Technologies/BoatAttack.git` into a separate checkout,
then retrieve its complete LFS payloads. Do not use a moving release head.
The original payload is approximately 0.92 GiB; that is not an import/build peak
estimate. Preserve the Unity Companion License and all original source objects.

Example paths below assume the fixed checkout already exists at
`work/external/boatattack`. Stage/output names must not exist before each call.

```powershell
$hostPath = 'work/external/boatattack'
$evidence = 'work/external-evidence'
python Tools/prepare_boatattack.py $hostPath "$evidence/source"
python Tools/repair_boatattack.py $hostPath "$evidence/source-repairs"
pwsh Tools/Invoke-PsoExternalEditor.ps1 -HostPath $hostPath -Output "$evidence/import" -Method PsoBoatAttackBuild.Configure -EstimatedAdditionalPeakGiB 80
python Tools/repair_urp_rendergraph.py $hostPath "$evidence/rendergraph-repair"
pwsh Tools/Invoke-PsoExternalEditor.ps1 -HostPath $hostPath -Output "$evidence/addressables" -Method PsoBoatAttackBuild.BuildAddressables -EstimatedAdditionalPeakGiB 20
pwsh Tools/Invoke-PsoExternalEditor.ps1 -HostPath $hostPath -Output "$evidence/training-build" -Method PsoBoatAttackBuild.BuildPlayer -Training -PlayerPath "$evidence/players/training/BoatAttack.exe" -EstimatedAdditionalPeakGiB 40
```

The preparation verifies actual Git/LFS working bytes before applying the local
package, host adapter and [explicit project configuration](ShaderHitchPipeline.json).
The repair retains complete originals/diffs. Editor Configure checks the upstream
loader/prefab/runs and sets D3D12/IL2CPP; Addressables must complete before Player.
No replacement meshes, shader whitelist or synthetic scenery are created.

## Training and final validation

```powershell
& Tools/Invoke-PsoExternalPlayer.ps1 -Player "$evidence/players/training/BoatAttack.exe" -Output "$evidence/training-run" -Session boat-training -Trace -TracePhase boat-loading -Screenshots -ExtraArguments @('-pso-external-train-phases')
python Tools/pso_external_capture.py "$evidence/training-run" --output "$evidence/training-run/validation.json" --require-no-leaks
```

Use the owned `N:/` mapping path in Editor-only arguments (the wrapper creates
and releases it). Invoke `PsoBoatAttackBuild.ProcessInbox` with `-pso-inbox` pointing
at the completed training run's `capture/Inbox` and `-pso-profile-output` pointing
at a new profile directory. This resolves actual project shaders/materials for
the offline native merge. Then invoke:

1. `Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.InstallPlan` with
   `-pso-install-plan .../boatattack-6000.1-d3d12-high/plan.json`.
2. `Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.ValidateInstalledPlan`.
3. `PsoBoatAttackBuild.BuildPlayer` into a new final output, **without** `-Training`.

All three use `Invoke-PsoExternalEditor.ps1`. Its strict final-build gate must
pass; a changed runtime/input requires new identified training and plan validation.
Every attempt retains actual arguments, settings, lock, source snapshots, build
identity, process/capacity evidence and failures.

Run one independent final pilot for each policy (`disabled`, `all-at-once`,
`scheduled`, `observed-budget`), with `-PlanBaselineForDisabled`, then require
`pso_external_capture.py --require-warmup --require-no-leaks`. Disabled keeps common
baseline/shader-retention overhead while submitting zero warmup; all planned
phases must have no automatic startup activation. Do not use the upstream warmup
FPS alone as first-hitch evidence.

`pso_boat_comparison.py freeze` verifies the four pilots and hashes the common
Player before creating a fresh 16-process protocol. `Invoke-PsoBoatComparison.ps1
-Protocol .../protocol.json` executes it once in balanced order and refuses to
overwrite/resample. The helper uses the documented `players/<player-stage>` and
`boat-policy-pilot-<arm>-<suffix>` attempt layout. `summarize ... --output NEW.json`
can independently revalidate retained evidence. It is not a performance scheduler
or part of CPU CI.

The estimate cap during the original warmup traversal is 1000 ms per admission,
not a guaranteed bound. This native bulk backend does not support the selected
fixed-progressive comparison. Fresh processes retain existing caches; no driver
cache purge is part of these commands. Inspect full totals, maxima, failures and
native entry-growth limitations before interpreting any result.
