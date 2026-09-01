# Integration guide

## Package installation

Add the package directory through Package Manager, or reference a Git tag when this repository is hosted under your personal account. The package contains runtime and editor assemblies; generated profiles remain in the consuming project's ignored artifact directories.

## Capture design

Choose phases at content boundaries: `startup`, `city`, `combat`, `weather-rain`, or similar. A phase should be early enough to prewarm ahead of use and small enough to fit its loading-screen budget.

Capture representative material keywords, vertex layouts, render targets, MSAA modes, depth/stencil formats, and render states. Modern PSO identity is broader than a shader keyword list, which is why a legacy `ShaderVariantCollection` alone is not sufficient on D3D12/Metal/Vulkan.

Run each coverage scenario more than once. The merge is idempotent: duplicate inputs are accepted but add zero states, and the receipt makes that visible.

## CI stages

1. Build a development training player with `-pso-training-build`.
2. Execute deterministic representative scenarios with `-pso-trace`.
3. Process the inbox and archive the merge receipt.
4. Review state/variant growth. Unexpected large growth often signals keyword or render-state explosion.
5. Install the selected profile and build the candidate player normally.
6. Run cold/prewarmed benchmarks on the same hardware pool and driver image.
7. Store raw benchmark receipts, warmup receipts, plan hash, build receipt, and comparison output.

## Application hooks

No code is required for startup warmup when a valid plan is embedded. For later phases:

```csharp
if (PsoWarmupOrchestrator.Instance != null)
    PsoWarmupOrchestrator.Instance.ActivatePhase("combat");
```

For manual trace orchestration:

```csharp
PsoTraceController controller = PsoTraceController.EnsureInstance();
controller.Configure(sessionId, outputDirectory, shouldSendToEditor: false);
controller.BeginPhase("combat", "nightly", "high-quality");
// Run the representative path.
PsoSessionManifest manifest = controller.EndPhase();
```

## Profile refresh policy

Recapture when shader source, shader stripping rules, render-pipeline version, render-target topology, graphics API, runtime platform, or quality configuration changes materially. Treat a plan as build input with provenance, not a permanent asset.

Feed cache-miss collections into a later representative run only after review. A cache-miss trace is evidence that runtime content escaped the current plan; blindly adding every incidental editor/debug state can bloat startup work.
