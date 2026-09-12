# Configure a policy and own a content request

This example uses the actual portable Core project. It displays policy settings
and exercises accepted activation, dependency-ready deferral, cancellation, a late
callback and a replacement content revision through an explicitly simulated sink.
It prints the three configurations; it does not run or compare scheduling policies.
The fixed count of eight is an illustrative argument, not a tuned recommendation
or a change to the package defaults.

With a .NET 8 or later SDK, from the repository root:

```powershell
dotnet run --disable-build-servers -p:UseSharedCompilation=false --project DotNet/ShaderHitchPipeline.PolicyExample -c Release
```

The target is `net8.0`, matching the SDK installed by functional CI. Major runtime
roll-forward also lets this portable example run on machines with only .NET 10.
The existing `Tools/Invoke-PsoValidation.ps1` allowlist compiles and runs it in the
Windows/Linux CI jobs alongside the existing CPU checks. There are no command-line
workload options or extra package dependencies.

The scenario first activates a request and explicitly marks a simulated batch
pending. Unloading the request leaves the simulated resource owner retained. A
second ready request defers behind that owner, is cancelled, and ignores a late
callback without calling the sink again. A fresh request remains deferred until
`SignalSimulatedFence` is called, then succeeds on retry. Finishing its batch does
not mark warmup complete: the example separately calls
`ReportSimulatedWarmupComplete`, observes completion through `loads.Refresh()`,
and unloads the now-retirable owner. Assertions fail the executable if any of
those contracts break; success ends with `POLICY_EXAMPLE_OK`.

`Generation` in this output is the content request's generation, not the runtime
scheduler's activation number. The simulated owner handles one phase (`city`)
and models lease/fence decisions with booleans. The request lifecycle itself is
the actual Core implementation, not a copied scheduler. It cannot retain an
application's shader/material assets or drain native work for the host.

No Unity, native collection, real clock or GPU work executes. Simulated
completion is not first-draw coverage, and these inputs/results are not a
benchmark. A real host maps the sink to its orchestrator and keeps shader,
material and backend resources alive through the native completion fence.
The existing [native scene sink](../../Integrations/MegacityMetroNative/Package/Runtime/PsoNativeSceneObserver.cs)
shows the orchestrator mapping; explicit asset leases are demonstrated by the
[Addressables integration](../../Integrations/Addressables/README.md).

[Choose and integrate a policy](../../Docs/POLICY_ADOPTION.md).
