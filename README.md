# Shader Hitch Pipeline

An independent Unity 6 UPM package for eliminating first-use shader and graphics-pipeline hitches on modern graphics APIs. It records the **actual shader variant + render-state combinations** seen by a player, produces a reproducible warmup profile, progressively creates the corresponding GPU representations, and proves the result with a controlled A/B run.

This is a performance pipeline, not a crash detector, culling system, asset simplifier, or streaming layer.

![Actual D3D12 Player: cold first use versus prewarmed](Docs/Media/actual-comparison.gif)

This is a synchronized capture of two **actual Unity Players**, not a chart animation or a reconstructed replay. The left Player creates previously unseen shader/graphics-state combinations during rendering; the right Player executes the same reveal after the captured states have been prewarmed. Red columns are real presented frames that exceeded 8.33 ms. The higher-quality [MP4](Docs/Media/actual-comparison.mp4), [poster frame](Docs/Media/actual-comparison.png), and secondary [raw-sample chart](Docs/Media/comparison.gif) are retained with the repository.

## Measured result

The included showcase was measured on Unity 6000.5.2f1, D3D12, and an AMD Radeon AI PRO R9700. Both runs contain 240 raw frame samples and the same 384-object reveal workload.

| Metric | Cold first use | Prewarmed | Change |
|---|---:|---:|---:|
| Mean | 8.535 ms | 4.167 ms | 51.2% lower |
| P95 | 34.300 ms | 4.169 ms | 87.8% lower |
| P99 | 37.467 ms | 4.169 ms | 88.9% lower |
| Maximum | 48.943 ms | 4.174 ms | 91.5% lower |
| Frames ≥ 8.33 ms | 48 | 0 | 48 eliminated |

The generated plan covered 388 shader variants and 389 graphics states. The final warmup receipt confirmed 389/389 completion in 243.9 ms. The external recorder was active for both measurements. Results are hardware, driver, project, and cache-state dependent; use the included runner to produce evidence for each target profile.

## What it adds beyond Unity

Unity and the graphics driver remain responsible for shader compilation and PSO creation. This package deliberately does not recreate either. It adds the missing production workflow around Unity's `GraphicsStateCollection`:

- player-side, phase-aware trace sessions with environment manifests;
- strict platform/API/quality isolation;
- SHA-256 verification for every input collection and generated plan;
- deterministic deduplication and merge receipts;
- frame-budgeted progressive warmup with an adaptive batch size;
- cache-miss feedback files for the next training cycle;
- build-time target/API validation and build receipts;
- identical-workload cold/prewarmed benchmarks with raw samples;
- synchronized actual-Player MP4/GIF capture with blank-frame validation;
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

1. Add `Packages/com.yanagisawa.shader-hitch-pipeline` as a local or Git UPM dependency.
2. Import the **PSO Hitch Showcase** sample, or add the runtime package to your own player.
3. Build a development player for D3D12, Metal, or Vulkan.
4. Run a representative trace:

```powershell
YourGame.exe `
  -pso-disable-warmup `
  -pso-trace `
  -pso-trace-phase startup `
  -pso-session qa-2026-09-01 `
  -pso-output C:\PsoArtifacts
```

5. Copy or point the Editor at `C:\PsoArtifacts\Inbox`, then use **Tools > Shader Hitch Pipeline > Control Center** to process and install the plan.
6. Rebuild. The build gate rejects a plan for the wrong runtime platform or graphics API.
7. The final player automatically loads `StreamingAssets/ShaderHitchPipeline/plan.json` and prewarms phases marked for startup.

For an end-to-end portfolio run from this repository:

```powershell
python -m pip install -r Tools/requirements.txt
ffmpeg -version
pwsh Tools/Invoke-PsoShowcase.ps1
```

The Windows showcase runner builds the training Player, records the cold trace and visible client area, installs the resulting plan, builds the final Player, records the prewarmed run, aligns both videos from the first visible tile, validates that captured pixels are non-blank, and emits both visual and numerical evidence. The GPU Player is intentionally visible during capture; a hidden or obscured Windows swapchain may stop presenting or produce invalid video.

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

## Support and constraints

- Unity 6.0 or newer.
- `GraphicsStateCollection` targets modern APIs: D3D12, Metal, and Vulkan. D3D11 requires a different shader warmup strategy and is intentionally not mislabeled as PSO coverage.
- Capture once per runtime platform, graphics API, quality level, and materially different shader build.
- A plan must ship with the build whose shader set it was generated for.
- Profiler marker availability varies by Unity build/platform; frame-time samples and warmup completion receipts are always retained.

See [Architecture](Docs/ARCHITECTURE.md), [CLI reference](Docs/CLI.md), [integration guide](Docs/INTEGRATION.md), and [benchmark methodology](Docs/BENCHMARK_METHODOLOGY.md).

## Ownership

Copyright © 2026 Edwin Liu. All rights reserved. See [LICENSE.md](LICENSE.md) and [PROVENANCE.md](PROVENANCE.md). This repository is an independent clean-room implementation and contains no employer-project source or assets.
