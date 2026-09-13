# Installation and first build — 2026-09-13

The package now declares Unity 6000.1 as its minimum family. The compatibility backend added before this repair supports the experimental GraphicsStateCollection API on earlier Unity 6 versions; 6000.5 uses the promoted API. The pinned 6000.1 native integration has a recorded IL2CPP training Player/Menu startup, while this repair compiles against installed 6000.3.13f1 and 6000.5.2f1 reference assemblies. This is not an all-version compatibility promise. Earlier bulk mode does not provide an equivalent fixed-progressive benchmark.

1. Install the package and import PSO Hitch Showcase or Deadline Run through Package Manager Samples.
2. For an empty project, select `Tools > Shader Hitch Pipeline > Build PSO Showcase Training Player (First Run)` (or the Deadline Run equivalent). This explicitly scopes the training-plan exception to the synchronous build and selects a Development Player.
3. Run the training Player using the matching scenario tool, collect traces, build the plan and install it using Control Center/the documented scenario runner.
4. Use the original `Build PSO Showcase` / `Build Deadline Run` entry for the planned comparison. The installed-plan and identity gates apply. Neither menu changes `validateInstalledPlanBeforeBuild` persistently. A failed training build disposes the exception scope, so the next normal build still requires its plan.

The existing terminal runners keep their explicit `-pso-training-build` flag. `PsoTrainingBuildScope` is also covered by the CPU validation allowlist for nested scopes, double disposal and failed-build unwinding.

Run `Tools/Invoke-PsoValidation.ps1` after installing `Tools/requirements-validation.txt`. Optional UnityManagedPath/API defines compile against the corresponding Editor reference assemblies; the check never starts Editor/Player or measures GPU performance.

Historical cold reveal P95 35.706 to 4.210 ms belongs to the 384-object R9700/D3D12 showcase. All-at-once post-warmup P95 was 4.169 ms; scheduled total warmup took about 20% longer. The native Megacity Player reached Menu, while Main/SubScenes, representative training, plan installation and formal native comparisons remain unverified. Code fixes and reference compilation do not widen those claims.
