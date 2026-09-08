# Compile and CPU validation

The default entry is `Tools/Invoke-PsoValidation.ps1`. It uses a named Python
test allowlist and engine-neutral C# functional programs. It does not discover
arbitrary tests, invoke Editor/Player, create graphics state, acquire native
performance counters, run a benchmark or render performance figures.

```powershell
python -m pip install -r Tools/requirements-validation.txt
pwsh Tools/Invoke-PsoValidation.ps1
```

The Python checks use synthetic receipt/CSV data to test parsers and rejection
rules. The C# checks use deterministic inputs and mock fences; numeric costs are
test inputs. Test-runner/build elapsed time is incidental tool output and is not
used as a performance result. The same allowlist runs in PR CI on Windows/Linux.
The scheduler executable also emits a synthetic lifecycle receipt from its real
serialization code. The entry validates that receipt against the scheduling
Schema, so the C# producer and Python/schema consumer are checked together.

## Optional Unity API compilation

Use a short repository path (for example `C:/src/pso`) and the exact installed
Editor DLLs. The script reads Windows version metadata without starting Unity
and rejects an incompatible namespace define. It also checks path headroom.

```powershell
pwsh Tools/Invoke-PsoValidation.ps1 `
  -UnityManagedPath 'C:/Program Files/Unity/Hub/Editor/6000.5.2f1/Editor/Data/Managed' `
  -ExpectedUnityVersion 6000.5.2f1 `
  -UnityApiDefines UNITY_6000_5_OR_NEWER
```

For the pinned native external host, use **6000.1.0f1**, omit `UnityApiDefines`,
and pass `-EntitiesAssembliesPath <host>/Library/ScriptAssemblies` only if the
pinned Entities 1.3.14 / Collections dependency assemblies are already available.
This reads those DLLs as compiler references; it does not import or mutate that
host. The supplied compilation project links actual package/runtime/editor and
native adapter sources. It does not compile a fake Unity implementation.

The compile-only library is never executed. This catches C# and public API
compatibility errors; it is not an asmdef import, IL post-processing, Entities
source-generation, shader compilation or Player build test. These unperformed
checks remain separate from the successful API reference build.

## Runtime entrypoints

Historical runners remain ordinary, explicitly named commands such as
`Invoke-PsoShowcase.ps1`, `Find-PsoWarmupPolicy.ps1` and
`Invoke-PsoMegacityMetro.ps1`. They can start a Player, warmup, profiling or
performance work and are excluded from the safe entry/CI. The old broad
`Invoke-PsoIntegratedRegression.ps1` also includes Editor tests outside the CPU
allowlist. None of those commands was invoked in this repair.

External source verification and manifest preparation use
`Tools/pso_external_host.py`; it has only `verify` and `prepare`, and no run mode.
Its `preparationOnly: true` result describes this tool's capability, not a product
execution restriction. It never labels a modified or unhydrated checkout ready.
No chat authorization token or source edit is required to use the separate
runtime entrypoints. Actual identity, source, budget and resource safety checks
remain enforced.

## Evidence boundary

All September 8 source/policy changes are **Unmeasured**. Historical reports
retain their own snapshot or measured-source identity. The f352210 overview
figure now hashes a preserved historical methodology copy, so merging a later
methodology does not silently change its provenance. No new figures were drawn.

See [the integration record](REPAIR_20260908.md) for the final source commits,
executed functional checks, compiler coverage and unperformed runtime work.
