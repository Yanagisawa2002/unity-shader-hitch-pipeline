# September 10 positioning and adoption example

This change adopts the confirmed product focus: scheduling warmup around loading
trades loading pressure against total completion, tail latency, deadline outcomes
and useful coverage. Unity and the driver perform native compilation and pipeline
creation. Prewarming versus cold first use does not isolate the scheduler's value.

The starting commit is `5fa075640fb8f5fd7411bfee46aee1b7fdb6df80`. This delivery
incorporates the parent's documentation/example drafts, reviews their API usage,
and fixes the teaching and CI gaps. It changes no Core/Runtime scheduling algorithm,
default policy, native adapter, historical measurement data or performance queue.
All new performance claims remain **Unmeasured**.

## Files and behavior

| Files | Result |
|---|---|
| [Root README](../README.md), [package README](../Packages/com.yanagisawa.shader-hitch-pipeline/README.md), [portfolio entry](portfolio/README.md) | Lead with the loading/completion/tail/coverage tradeoff, point to adoption, and retain the historical evidence boundary. |
| [POLICY_ADOPTION.md](POLICY_ADOPTION.md) | Documents explicit policy selection and real orchestrator outcomes: accepted demand, already resident work, pending retirement, unavailable phases, cancellation and separate eviction. Full-plan trace arming and external resource ownership are explicit. |
| [SCHEDULING_OPTIONS.md](SCHEDULING_OPTIONS.md) | Distinguishes request generation from scheduler activation identity, batch completion from collection warmup, and request terminal state from external asset release. |
| [PolicyExample project](../DotNet/ShaderHitchPipeline.PolicyExample/ShaderHitchPipeline.PolicyExample.csproj), [Program.cs](../DotNet/ShaderHitchPipeline.PolicyExample/Program.cs), [example guide](../DotNet/ShaderHitchPipeline.PolicyExample/README.md) | References the actual portable Core. Displays scheduled/fixed/observed options and uses an explicitly simulated single-phase sink to illustrate lifecycle and owner/fence handling. Targets .NET 8 with C# 12 and supports major runtime roll-forward. |
| [Invoke-PsoValidation.ps1](../Tools/Invoke-PsoValidation.ps1), [functional workflow](../.github/workflows/functional.yml), [validation guide](NON_PERFORMANCE_VALIDATION.md) | Add the example to the existing named CPU allowlist, which CI already invokes. Keep the .NET 8 SDK setup and every existing check; add no Unity, Player, performance or test-discovery path. |

The initial draft toggled acceptance/completion directly. The reviewed example
first accepts real lifecycle demand, explicitly marks simulated work pending,
and unloads that request while its simulated resource owner remains retained.
A new dependency-ready request defers, is cancelled, and rejects its late callback
without re-entering the sink or cancelling the previous owner's work. A fresh
generation also waits; it succeeds only after an explicit simulated fence signal
and a retry. Its batch fence and reported complete warmup are separate events.
`Refresh` observes completion, and the final unload releases the retirable owner.
Assertions make each distinction executable through the real Core lifecycle.

The booleans in the teaching sink represent external ownership events. They are
not Unity assets, actual job fences or a replacement scheduler. The example only
prints policy configurations; it does not exercise or compare policy execution.
Every lifecycle output identifies its simulated scope and sets
`nativeWarmupExecuted` and `realFirstDrawCoverageEstablished` to false.

## Checks actually executed

The host had approximately 3.24 GiB free before checking. Local SDK 10.0.302 and
runtime 10.0.10 were already installed, with .NET 8 reference packages cached.
The new example's restore used an empty local-only package source and disabled
NuGet audit for that command. It resolved from existing caches, without downloading
dependencies. Subsequent commands used `--no-restore`; no full rebuild, pack or
cache deletion was performed.

| Local check | Result |
|---|---|
| PolicyExample single-project incremental build, Release/net8.0 | Passed, zero warnings and zero errors. The actual Core project is its project reference. |
| PolicyExample run with no build/restore | Passed; three configuration records, six simulated lifecycle records and `POLICY_EXAMPLE_OK`. The generation/retention/retry/completion assertions passed. |
| Existing Core.Smoke | Passed: core smoke plus compatibility 71, hotset 36, integrated core 15, lifecycle 19 and production-file 15 assertions. |
| Existing Scheduler.Tests | All 39 mock-backend/virtual-clock cases passed, including pending unload, cancellation/reactivation, retained fences and distinct reload identity. |
| Static verification | Validation entry PowerShell syntax, CI/example SDK linkage, retention of all three existing CPU projects, 51 documentation links, historical-source preservation and `git diff --check` passed. |

Local logs are retained under `work/positioning-20260910-policy/`. Incidental build
and test-runner elapsed-time output is not performance evidence. This run used
runtime roll-forward to .NET 10; it is not a local execution on runtime 8. The
functional CI targets .NET 8 on Windows/Linux, but no remote CI was executed.
The broad CPU allowlist, Unity reference compiler chain and other unrelated
projects were not rerun for this positioning change.

## Evidence and remaining limits

The historical report still owns warmup-window P95 **127.0 → 12.9 ms**, total
warmup **643.5 → 774.6 ms**, the **178.9 ms** outlier, and post-warmup workload P95
**4.169 versus 4.210 ms**. These numbers are not attributed to the new example or
current source, and the outlier is not claimed eliminated. Existing figures,
source locks, raw evidence and failure logs remain retained.

Cold, all-at-once, fixed progressive and a candidate policy require the same
engine/dependency version, player/shader build, content, resolved collections,
route, activation events and cache protocol. The pinned Unity 6000.1 bulk adapter
cannot stand in for a fixed-progressive arm; changing engine capability requires
a separate compatible cell shared by all arms. No default candidate is promoted.

Native loading, shader compilation, actual warmup/fences, first-draw coverage,
trace collection, full-frame behavior and performance were not executed or
revalidated here. A lifecycle terminal state does not release external leases by
itself, and complete warmup of a loaded collection does not prove coverage of
unseen application states. No normal API has a chat authorization gate. No task,
subagent, remote publication or performance queue was started.
