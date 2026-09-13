# Official URP 3D Sample: native multi-scene evidence

The pinned official template has completed actual Windows D3D12/IL2CPP import,
packaging, full four-scene content validation, process-wide training, native
collection merge, plan installation, strict final-build verification and four
independent policy pilots. A finite 16-process formal sequence is frozen and in
progress; this checkpoint does not claim that sequence is complete or that any
policy improves performance. The default remains unchanged.

## Source and adapted cell

The official registry archive is `com.unity.template.urp-sample@17.1.5`,
924,710,979 bytes, SHA-256
`c2a2bcbd8bac1340be683b33ca53e1fddc78c2ae80fa3a0b5406bd616df6628f`,
registry SHA-1 `cbf518aa0f1d9f0d4eb93d94c33a6cb4bf4fe43b`.
[The source lock](../Integrations/Urp3DSample/source-lock.json) binds its exact
official URL, license and original selected files. All 3,532 regular archive
members / 1,012,172,703 expanded bytes were verified, including 3,501 project
files / 985,684,796 bytes. Original project index SHA-256 is
`fb0b29f0211db85b64bf03a61768e141e5306da20651dcf0cb0c536560b173cc`.
The Unity Companion License remains with the external host; large third-party
assets and private raw evidence are excluded from this repository.

The archive declares Unity 6000.0 and contains no ProjectVersion.txt. Selecting
installed **6000.1.0f1 (9ea152932a88)** is explicit and was checked by real builds,
not inferred from a registry version. Editor SHA-256 is
`d2336629da111800a35b592b8c8f595dda02c658a8e3e88c0e7d002e1a0a7f8b`.
The cell is Windows x64 Development IL2CPP Player, D3D12, 1920×1080 windowed,
original **PC High**, AMD Radeon AI PRO R9700, driver 32.0.31041.1004. Runtime
reports Ryzen 9 9950X 16-Core Processor and `processorCount=8`; its cause is not
established. No worker/affinity, driver, power or priority adjustment was applied.

Original BenchmarkScene is enabled as the build entry. Its resolved original
prefab stages are **Terminal → Garden → Oasis → Cockpit**, with five-second
warmup followed by each complete original Timeline, approximately 68.8, 50.117,
35 and 85 seconds. Full-timeline mode has variable frame counts; the serialized
500 setting is not a 500-frame completion contract. The original benchmark
starts automatically. The observer exits normally only after the original full
CSV and all four Finished states; it does not invoke UI callbacks or send input.

Both original desktop quality configurations and the template's original
embedded universal-config 17.0.1 remain. The original Forward+ light limit 32
is an upstream configuration, not a new optimization. There are no substituted
meshes, materials, whitelists, timeline/camera routes, or simulation freezes.

## Declared source/settings differences

Original requested dependencies number 45; the adapted manifest has 46. The only
manifest changes are the local package and official Splines 2.8.0→2.8.1 patch for
the demonstrated IL2CPP finalizer issue. The generated lock has 62 entries versus
the archive's 63: two unreferenced stale template/authoring entries disappear,
the local package is added, Core 17.1 becomes the declared embedded cleanup
backport, and its light-transport dependency depth changes. All other requested
versions remain. This does not vendor the separately researched unlicensed
Unity-Benchmark-Tool.

The [same narrow RenderGraph cleanup backport used for Boat Attack](EXTERNAL_BOAT_ATTACK_2026-09-14.md)
retains the original Core 17.1 package and pool behavior. Registry cache bytes are
not edited. The full original/adapted package inventory and exact diff remain.

The post-build audit verifies all 3,501 original project paths with no missing
files. Eight originals differ: manifest/lock, EditorBuildSettings, ProjectSettings,
VFXManager, UniversalRenderPipelineGlobalSettings, PC_High and PC_Low. The first
five-file import audit initially failed this later eight-file comparison, and its
failure is retained. The additional differences were then reviewed against the
actual official producer code: URP 17.1 serialized defaults/renames and
ShaderBuildPreprocessor's prefilter fields, plus VFXManagerEditor's official
PrefixSum shader reference. This is an adapted engine cell, not pristine settings.
Original scenes, materials, timelines and benchmark code remain byte-identical.
Full project diff SHA-256 is
`8af3484b27ae991049253eb5af0d4b2424957e2549990e9a9ebe90dac345a54a`;
lock-audit SHA-256 is
`b47cf73a95fd2ba2c3c30998b17351a9a6ebfde44e0b25154af4adefdcf883a0`.

## Actual content, training and native work

The first complete Player built successfully in 1958.10 seconds. Its first native
run finished all four scenes and exited 0, but failed the observer's Cockpit
association gate. Cockpit uses an Animation track rather than a direct camera
Brain track. The observer was repaired to record the original Timeline-bound
Animator ancestor of the actually live `Root/Cockpit/ActionCamera`, using public
APIs. No original route/binding was changed. New IL2CPP training build02 completed
in 112.42 seconds, build GUID `1fc74887cf4147d7941d5165d26d5b55`.

The new native capture exited 0 after 264.344998 seconds, with all four full native
CSV routes, original director stop/completion, positive camera movement and
actual rendered screenshots for every scene. Cockpit shows its original cockpit,
space and asteroid content; the original translucent benchmark UI remains.
There were no observer errors/exceptions or post-exit Persistent-allocation
warnings. Screenshot/training overhead is diagnostic, not formal timing.

One subsequent validator failure identified two actual cameras rendering on the
original Cockpit loading frame before the upstream coroutine disabled its scene
camera. Validation now associates the persistent benchmark camera from its
Running pass and retains the additional original submission separately. All CPU
intervals remain. Time-based sampled endpoint gaps also remain, while actual
director stop plus full native completion establishes route completion. A real
long frame is not discarded merely because it skipped an endpoint sample.

Native merge accepted all five sessions without shader filters or exclusions:
loading 22, Terminal 83, Garden 168, Oasis 84, Cockpit 123 recorded states. These are
phase counts with overlap, not 480 unique useful PSOs. Installation and strict
final build passed. The accepted training/final input hash is identical:
`4772e83a205379db57f86820834bcc6fa1fc618ee72b9dbcb0213cae286112a2`.

The common final Player build GUID is`6039c8ddc983453a84f2987c54c35bef`,
681 files / 2,450,900,245 bytes. Player index SHA-256 is
`aacb1da0e3f50296824aa22ec6c3ba722ccf135b27a29c3e4b2cdd4ede5c6995`;
adapter/package source index SHA-256 is
`34026f2d3ab852c213904f3df6d11a9b2a41a45c41627bc7df60f9b951417199`.
The build retained its actual HEAD plus dirty source snapshot. A subsequent
source checkpoint must not be described as the earlier binary's build command.

Four independent final pilots passed native content/work/ownership and normal
exit: disabled 264.605121 s, all-at-once 264.845100 s, scheduled 264.715544 s and
observed-budget 264.735467 s. All warmup policies completed all five real native
phase batches and released owners; disabled had zero activations. Pilot times
are validation observations, not substitutes for the separate formal sequence.

## Frozen measurement and limitations

Protocol SHA-256:
`4c949c78b65375755d99a910261401eec9e03787ec0c837110620e5624407507`.
Plan file SHA-256:
`cf37d35e650bdd9cfd18ed264ae5081d5b88c625757a7428edb49ace152e578e`.
Sixteen predeclared processes use Williams order
`A B D C / B C A D / C D B A / D A C B`, where A=disabled, B=all-at-once,
C=scheduled, D=observed-budget. Each arm uses the same final Player and seeded
baseline. Formal captures disable screenshots and training phase switching.

The selected backend is **native-async-bulk**, not fixed-progressive. Scheduling
admits ready phase work during the original five-second warmup, with a 1000 ms
estimated per-admission cap. It adds no scene fence or wait and guarantees no
hard latency bound. New processes keep application/OS/driver caches; this is
not driver-cold. Training and pilots precede the separately frozen sequence.

Metrics retain startup/first-Update gap, full CPU Update intervals, original
first loading/warmup, full route time, warmed native CSV, P95/P99, strict hitch
counts above 16.67/33.33/50/200/500 ms, maxima, memory and failures. CPU Update and
render callbacks are not GPU completion or presentation. Native FrameTimingManager
CSV columns are not promoted to independently verified GPU timings.

Seeded native entry growth in pilots is397→781. The legacy field's 384 growth is
not 384 demonstrated compilation misses or a validated useful-coverage percentage.
Process-wide trace intervals and scene readiness do not prove first draw or
isolate every resource's first use. These limits remain even with complete
native work/route receipts. No default or performance/coverage claim is promoted.

[Reproduction commands and adapter](../Integrations/Urp3DSample/README.md).
Raw attempts are under ignored `work/actual-20260914-external-01/`, including all
failed diagnostics and source-audit checks; none are overwritten by later success.
