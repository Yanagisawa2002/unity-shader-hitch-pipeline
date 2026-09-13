# Engine-neutral core

`ShaderHitchPipeline.Core` links the same no-engine C# sources shipped in the
Unity package. It contains versioned trace/plan/receipt contracts, backend
interfaces, structural plan validation, statistics, hard-budget admission
control, and deadline/cost/hot-set scheduling. It references neither
`UnityEngine` nor `UnityEditor`.

Build and run the dependency-free portability smoke test:

```powershell
dotnet build DotNet/ShaderHitchPipeline.Core/ShaderHitchPipeline.Core.csproj
dotnet run --project DotNet/ShaderHitchPipeline.Core.Smoke/ShaderHitchPipeline.Core.Smoke.csproj
```

An engine adapter implements `IPsoTraceBackend` and `IPsoWarmupBackend`, while
retaining its native opaque state artifact. The Unity adapter wraps
`GraphicsStateCollection`; a native D3D12, Vulkan, Unreal, or other adapter can
reuse the contracts and scheduler without linking Unity assemblies.
