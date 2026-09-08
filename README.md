# Shader Hitch Pipeline

**Schedule Unity shader and pipeline warmup around the frames that need it.**

First-use shader compilation and graphics-pipeline creation can interrupt a
scene reveal. I built a Unity 6 package that records the states a Player uses,
plans warmup by phase, and schedules batches by deadline and measured cost.

## Results

![Cold first use, Unity all-at-once, and scheduled warmup in actual Players](Docs/Media/actual-comparison.gif)

- **Workload P95: 35.706 → 4.210 ms (88.2% lower)** for scheduled warmup versus
  cold first use in the same 384-object reveal workload.
- **Warmup-window P95: 127.0 → 12.9 ms** versus Unity all-at-once, trading total
  warmup time from 643.5 to 774.6 ms. The scheduled window includes a 178.9 ms outlier.

Recorded on AMD Radeon AI PRO R9700, D3D12 and Unity 6000.5.2f1. These two
metrics cover different intervals: the scene reveal and warmup itself.
[Watch the Player comparison](Docs/Media/actual-comparison.mp4) ·
[Watch the warmup comparison](Docs/Media/actual-warmup-comparison.mp4)

## Visual walkthrough

[![Engineering overview and evidence](Docs/portfolio/overview.svg)](Docs/portfolio/overview.png)

The two panels separate scene-reveal frame times from the warmup interval; the flow above is schematic. [Sources and reproduction](Docs/portfolio/README.md).

## Engineering challenges

1. **Capture the states that actually matter.** A useful plan must include both
   shader variants and render states, and remain tied to the correct build and API.
2. **Fit work around deadlines.** Batch size and worker count affect completion
   time and frame pressure; later phases also need reprioritization and feedback.

## My contribution

I implemented the trace-to-plan-to-runtime workflow: state collection and merging,
phase scheduling, worker/batch tuning, build validation and missed-state feedback.
I also built the controlled three-Player benchmark and synchronized capture tools.
Unity and the driver provide shader compilation and graphics-pipeline creation.

## Evidence and reproduction

[Benchmark methodology](Docs/BENCHMARK_METHODOLOGY.md) ·
[Architecture](Docs/ARCHITECTURE.md) · [Integration guide](Docs/INTEGRATION.md) ·
[Quick start](#quick-start). The full measurements and scheduling tradeoffs are
retained in the expandable evaluation section below.

## What it adds beyond Unity

Unity and the graphics driver remain responsible for shader compilation and PSO creation. This package deliberately does not recreate either. It adds the missing production workflow around Unity's `GraphicsStateCollection`:

- player-side, phase-aware trace sessions with environment manifests;
- strict platform/API/quality isolation;
- SHA-256 verification for every input collection and generated plan;
- deterministic deduplication and merge receipts;
- deadline-, observed-cost-, probability-, and hot-set-aware phase selection;
- batch-boundary reprioritization with adaptive progressive warmup;
- hardware-local async-worker/batch Pareto search with an applied recommendation;
- plan-scoped, pre-seeded feedback tracing that reports true post-plan misses without misclassifying progressive warmup batches;
- build-time target/API validation and build receipts;
- identical-workload cold/all-at-once/scheduled benchmarks with raw samples;
- synchronized three-Player MP4/GIF capture with marker-bounded pixel alignment;
- JSON, Markdown, PNG, and raw-sample animated GIF evidence.

The Unity API is experimental, so all direct API usage is isolated behind a small runtime/editor boundary. If Unity changes it, the application-facing trace, plan, benchmark, and receipt contracts remain stable.

## Repository layout

```text
Packages/com.yanagisawa.shader-hitch-pipeline/  Reusable UPM package
  Runtime/                                      Trace, plan validation, warmup, benchmark
  Editor/                                       Merge, install, build gate, control center
  Samples~/PSO Hitch Showcase/                  Measurable 384-state demo
  Tests/Editor/                                 Deterministic unit tests
UnityProject/                                   Minimal development and acceptance project
Tools/                                          One-command showcase and evidence generator
Docs/                                           Architecture, CLI, methodology, media
```

Generated player traces, plans, reports, and builds stay outside package source and are ignored by Git. This keeps project data and the reusable tool legally and technically separate.

## Quick start

Install the tagged package directly from GitHub with Unity Package Manager's
**Add package from git URL** action:

```text
https://github.com/Yanagisawa2002/unity-shader-hitch-pipeline.git?path=/Packages/com.yanagisawa.shader-hitch-pipeline#v0.2.0
```

Then:

1. Import the package's **PSO Hitch Showcase** sample, or add the runtime package to your own player.
2. Build a development player for D3D12, Metal, or Vulkan.
3. Run a representative trace:

```powershell
YourGame.exe `
  -pso-disable-warmup `
  -pso-trace `
  -pso-trace-phase startup `
  -pso-session qa-2026-09-01 `
  -pso-output C:\PsoArtifacts
```

4. Copy or point the Editor at `C:\PsoArtifacts\Inbox`, then use **Tools > Shader Hitch Pipeline > Control Center** to process and install the plan.
5. Rebuild. The build gate rejects a plan for the wrong runtime platform or graphics API.
6. The final player automatically loads `StreamingAssets/ShaderHitchPipeline/plan.json` and prewarms phases marked for startup.

For an end-to-end portfolio run from this repository:

```powershell
python -m pip install -r Tools/requirements.txt
ffmpeg -version
pwsh Tools/Invoke-PsoShowcase.ps1
```

The Windows showcase runner builds a cache-isolated training Player, records the cold trace and visible client area, installs the resulting plan, builds the matching final Player, searches worker/batch candidates, records the all-at-once and scheduled Players, and emits both visual and numerical evidence. Pixel synchronization cannot search before each Player's workload marker, and every capture is checked for blank/obscured output. The GPU Player is intentionally visible during capture; a hidden or obscured Windows swapchain may stop presenting or produce invalid video.

## Runtime phases

Startup phases are automatic. Later phases can be activated shortly before the content is needed:

```csharp
PsoWarmupOrchestrator.Instance.ActivatePhase("combat");
```

Representative traces can be split the same way:

```csharp
PsoTraceController trace = PsoTraceController.EnsureInstance();
trace.Configure("nightly-win-d3d12", @"C:\PsoArtifacts", false);
trace.BeginPhase("combat", "city", "rain");
// Exercise the representative workload.
trace.EndPhase();
```

Each trace phase becomes a schedulable unit. Tier 0 is the critical hot set; higher tiers are progressively colder. A finite deadline is measured from phase activation. The scheduler first protects work whose estimated remaining cost threatens its deadline, then considers hot-set tier, expected-use probability per remaining millisecond, stable priority, and phase name. Batch costs are updated from completed jobs during the run.

<details>
<summary>Evaluation details, tradeoffs and supported scope</summary>

## Measured result

The included showcase was measured on Unity 6000.5.2f1, D3D12, and an AMD Radeon AI PRO R9700. Each run contains 240 raw frame samples and the same 384-object reveal workload.

| Metric | Cold first use | Unity all-at-once | Deadline scheduled | Scheduled vs cold |
|---|---:|---:|---:|---:|
| Mean | 8.861 ms | 4.169 ms | 4.201 ms | 52.6% lower |
| P95 | 35.706 ms | 4.169 ms | 4.210 ms | 88.2% lower |
| P99 | 40.674 ms | 4.203 ms | 4.279 ms | 89.5% lower |
| Maximum | 46.387 ms | 4.436 ms | 9.799 ms | 78.9% lower |
| Frames ≥ 8.33 ms | 48 | 0 | 1 | 47 eliminated |
| Severe stalls ≥ 16.67 ms | 48 | 0 | 0 | 48 eliminated |

The generated plan covered 388 shader variants and 389 graphics states. A two-repeat 24-candidate worker/batch search selected two async workers and an initial batch of 64 on this machine (577.8 ms median warmup, 30.6 ms median worst warmup frame). In the externally recorded acceptance run, all-at-once completed in 643.5 ms as one batch; scheduled completed in 774.6 ms across 20 batches. Scheduled reduced sustained warmup pressure—P95 fell from 127.0 ms to 12.9 ms—but retained one 178.9 ms capture-window outlier, so the full distribution is reported rather than reduced to a favorable maximum. The measured workload had one isolated 9.80 ms scheduled frame, but zero ≥16.67 ms severe stalls, zero `Shader.CreateGPUProgram` time, and a plan-scoped feedback trace of 389 expected / 389 observed states (zero misses). Results are hardware, driver, project, and cache-state dependent; use the included search and runner for each target profile.

## When to use scheduled warmup

Use this package when warmup must share a frame budget with loading UI or other
work, or when later content has a known activation deadline. If a blocking load
screen can absorb all warmup, Unity's all-at-once path remains a strong baseline.

| Decision | Evidence from the retained showcase |
| --- | --- |
| Smooth the warmup period | Scheduled warmup reduced capture-window P95 from 127.0 ms to 12.9 ms, but still had a 178.9 ms outlier. It does not guarantee hitch-free warmup. |
| Minimize total warmup time | All-at-once completed in 643.5 ms; scheduled took 774.6 ms across 20 batches. Scheduling traded completion time for lower sustained pressure. |
| Minimize frame times after warmup | All-at-once had lower workload P95 (4.169 vs 4.210 ms) and maximum (4.436 vs 9.799 ms), with zero vs one frame at or above 8.33 ms. |
| Prepare later phases | Trace, phase planning, deadline scheduling and feedback are the package's integration features; their value depends on the application's content schedule. |

Warmup-window statistics and post-warmup workload statistics describe different
intervals. The large gains versus cold first use demonstrate the value of
prewarming; they do not establish that progressive scheduling beats Unity's
all-at-once warmup on every metric. See the [measurement protocol](Docs/BENCHMARK_METHODOLOGY.md).

## Support and constraints

- Unity 6.0 or newer.
- `GraphicsStateCollection` targets modern APIs: D3D12, Metal, and Vulkan. D3D11 requires a different shader warmup strategy and is intentionally not mislabeled as PSO coverage.
- Capture once per runtime platform, graphics API, quality level, and materially different shader build.
- A plan must ship with the build whose shader set it was generated for.
- Profiler marker availability varies by Unity build/platform; frame-time samples and warmup completion receipts are always retained.

See [Architecture](Docs/ARCHITECTURE.md), [CLI reference](Docs/CLI.md), [integration guide](Docs/INTEGRATION.md), and [benchmark methodology](Docs/BENCHMARK_METHODOLOGY.md).

</details>

## Ownership

Copyright © 2026 Edwin Liu. All rights reserved. See [LICENSE.md](LICENSE.md) and [PROVENANCE.md](PROVENANCE.md). This repository is an independent clean-room implementation and contains no employer-project source or assets.

## Benchmark reproduction permission

The [limited benchmark reproduction permission](LICENSE.md#limited-benchmark-reproduction-permission)
allows anyone to run the benchmarks and required project components, make local
changes needed for reproduction, and publish measurement results. Other plugin
rights remain reserved; this is not an MIT or general open-source license.
