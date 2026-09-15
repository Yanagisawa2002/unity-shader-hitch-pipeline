# Official URP 3D Sample: four original scenes

This additive integration uses the fixed official **17.1.5** template archive,
with its original Terminal, Garden, Oasis and Cockpit content and complete native
timelines. [source-lock.json](source-lock.json) identifies the archive, original
license and selected source files. It is external official-scene evidence, not
a standardized general PSO score. No Unity-Benchmark-Tool code is vendored.

The archive declares Unity 6000.0 and includes no ProjectVersion.txt. Selection
of installed **6000.1.0f1 (9ea152932a88)** is a declared adaptation, subsequently
validated by real import and Windows D3D12/IL2CPP builds. Do not substitute a
moving template version, another Editor, Mono or Editor-play measurements.

## Original route and capture

The original `Assets/SharedAssets/Benchmark/BenchmarkScene.unity` resolves its
prefab overrides to **TerminalScene → GardenScene → OasisScene → CockpitScene**.
The adapter enables this existing scene as the packaged entry and retains all
four original scene build entries. It does not instantiate the bare prefab with
its stale scene names, generate content, replace cameras, shorten timelines, or
simulate input. The official native benchmark starts automatically.

Each original stage loads asynchronously, waits **five seconds** while its
timeline is already advancing, resets that timeline, and records the full route.
The 500-frame serialized setting does **not** truncate full-timeline mode.
Durations observed from the original assets are approximately 68.8, 50.117, 35
and 85 seconds. Original PC High remains the default; both original desktop
quality configurations and the embedded universal-config package remain.

The observer starts before splash, retains the complete CPU Update stream,
native stage/load events, real camera/render submissions, original CSV and
memory observations. It observes actual director stops separately from sampled
endpoint poses: a real long frame can step over an endpoint. The first three
routes bind the rendering camera's CinemachineBrain directly; Cockpit's original
animation track binds an ancestor of its actually live virtual camera. Both
associations are recorded through public APIs, without modifying bindings.
Original scene cameras may also render on the first loading frame before the
upstream coroutine disables them. Those submissions and their CPU cost remain.

The original benchmark publishes one aggregate CSV and leaves a Close UI. Only
after all four original stages report Finished and that publication arrives
does the opt-in observer call normal `Application.Quit(0)`. It never invokes a
menu/benchmark callback or performs a route change. The native UI remains visible.
Optional code-captured screenshots are for correctness and are absent in formal
timing. CPU intervals and endCameraRendering are not presentation/GPU completion;
the original CSV's FrameTimingManager columns are retained without claiming they
were independently validated as GPU timings.

## Reproduction

Use PowerShell 7, Python 3.12, installed Windows IL2CPP/compiler/SDK support and
the exact archive URL/hash in the source lock. No new Editor installation is
required. All host/evidence/output paths below must be new under ignored `work/`;
the Editor wrapper owns and releases an otherwise-free N: short mapping. Heavy
stages check the shared mutex, external workloads and at least 20 GiB reserve.
Peak estimates are capacity budgets, not measured native maxima.

```powershell
$hostPath = 'work/external/urp-sample'
$evidence = 'work/urp-evidence'
python Tools/prepare_urp_sample.py work/downloads/com.unity.template.urp-sample-17.1.5.tgz $hostPath "$evidence/source"
pwsh Tools/Invoke-PsoExternalEditor.ps1 -HostPath $hostPath -Output "$evidence/import" -Method PsoUrpSampleBuild.Configure -EstimatedAdditionalPeakGiB 80
python Tools/repair_urp_rendergraph.py $hostPath "$evidence/rendergraph-repair"
pwsh Tools/Invoke-PsoExternalEditor.ps1 -HostPath $hostPath -Output "$evidence/training-build" -Method PsoUrpSampleBuild.BuildPlayer -Training -PlayerPath "$evidence/players/training/UrpExternal.exe" -EstimatedAdditionalPeakGiB 40
& Tools/Invoke-PsoExternalPlayer.ps1 -Player "$evidence/players/training/UrpExternal.exe" -Output "$evidence/training-run" -Session urp-training -Trace -TracePhase urp-loading -Screenshots -MaximumSeconds 1200 -ExtraArguments @('-pso-external-train-phases')
python Tools/pso_urp_capture.py "$evidence/training-run" --output "$evidence/training-run/validation.json"
```

The preparation retains every archive member and the complete original project
file index before adding the adapter/local package. It declares the official
Splines 2.8.0→2.8.1 IL2CPP finalizer patch. After import, the shared RenderGraph
repair embeds the narrowly patched original Core RP17.1 package, retaining
original bytes/diff/license and leaving registry caches untouched. Review Unity's
generated lock and settings migration. Actual builds also serialize URP17.1
defaults/renamed fields and official shader-prefilter outputs in PC_High/PC_Low,
and add the official VFX PrefixSum reference in VFXManager. These are declared
engine-generated differences, not a claim of pristine settings. Original scenes,
timelines, materials and source benchmark code must remain byte-identical.

Invoke the following with `Tools/Invoke-PsoExternalEditor.ps1`, using its owned
`N:/` path for project-relative CLI arguments:

1. `PsoUrpSampleBuild.ProcessInbox`, with `-pso-inbox` pointing at the completed
   training run's `capture/Inbox` and `-pso-profile-output` at a new profile folder.
2. `Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.InstallPlan`, with
   `-pso-install-plan` pointing at that folder's
   `urp-sample-17.1.5-6000.1-d3d12-pchigh/plan.json`.
3. `Yanagisawa.ShaderHitchPipeline.Editor.PsoBatch.ValidateInstalledPlan`.
4. `PsoUrpSampleBuild.BuildPlayer` into a new final output, without `-Training`.

Require the strict final-build identity gate. A changed runtime/adapter input
needs a newly identified training/plan chain. Run independent final pilots for
`disabled`, `all-at-once`, `scheduled` and `observed-budget`, each with
`-PlanBaselineForDisabled`, no screenshots/training trace, and
`pso_urp_capture.py --require-warmup`. Every planned phase must actually complete
native work and release ownership; disabled must submit zero activations.

`Tools/pso_urp_comparison.py freeze` binds accepted pilots, source snapshots,
common Player/plan/collections/environment, original route, existing-cache
condition, thresholds and a finite 16-process Williams order. The helper expects
`players/<player-stage>` and `urp-policy-pilot-<arm>-<suffix>` under the attempt.
`Tools/Invoke-PsoUrpComparison.ps1 -Protocol .../protocol.json` executes it once,
stopping on failure without replacing samples. The summary recomputes metrics
from raw captures and verifies actual artifacts. No recurring queue is installed.

The selected backend is native-async-bulk, not fixed-progressive. Admission during
the original warmup uses a 1000 ms estimate cap without an extra wait/fence or
hard latency guarantee. New processes retain application/OS/driver caches;
no cache purge or driver-cold claim is part of this protocol. Native entry growth
does not by itself establish compilation misses or useful first-draw coverage.
