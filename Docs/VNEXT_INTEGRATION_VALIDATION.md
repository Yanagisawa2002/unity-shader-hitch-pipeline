# vNext integrated validation contract

This bounded validation applies to the four vNext workstreams: build/content
compatibility and cost invalidation, loaded streaming content, trace-derived
startup hotsets, and Windows system evidence. Historical published receipts
retain their original meaning and source identity.

## Frozen platform and execution controls

- Source baseline: `7679223b9e61aa01575c35b4e3b4a3f672c7da4b`, development
  branch `codex/v0.3-hard-budget`.
- Target: Windows D3D12, AMD Radeon AI PRO R9700, Ryzen 9 9950X,
  Unity 6000.5.2f1. Results do not establish cross-device reuse or performance.
- All Unity/build/GPU/formal timing work uses the shared
  `Local\CodexR9700VNextUnityGpu` mutex via the supplied validation runner.
  One acquisition encloses child process lifetimes; no nested lock acquisition.
- Process-cold means a fresh Player process. The driver cache is uncontrolled.
  No global cache deletion, user-process termination, or GUI control. Ordinary
  builds remain non-elevated. The user's later permission-repair instruction
  allows a single reviewed five-run Windows capture through standard RunAs/UAC
  after the parent-managed capture probe succeeds; no persistent elevated
  service, account changes or policy changes are permitted.
- Preserve failures, unsupported metrics, raw logs and hashes. Engine frame
  timestamps are proxies for first presentation; only OS evidence can establish
  actual presentation. Missing data cannot become zero.

## Required cells

1. Run the integrated Python tests, engine-neutral smoke and Unity EditMode
   tests. Include legacy plan hashing, changed/unknown/corrupt identity,
   cost invalidation and normal trace-to-final-build compatibility.
2. Build and execute the optional real Addressables fixture. Exercise loaded
   shader dependencies before collection registration, overlapping owners and
   phase requests, pending cancellation, submitted-job resource retention,
   unload/reload, revision change, stale handles and failure paths. The base
   project must remain usable without the Addressables package.
3. Freeze training routes separately from two physically different held-out
   render sequences. Compare required-only, trace-budgeted optional hotset and
   a natural all-at-once control on each held-out route, with three fresh
   processes per cell (18 short validation processes). Declare the route IDs,
   seeds, required collections, budgets and rotated arm order in the executable
   fixture declaration before measurement. Do not select on held-out results.
   Report startup latency, later miss/hitch evidence, extra warmed states,
   warmup work and memory availability together; three repetitions support a
   bounded local observation, not a universal performance guarantee.
4. Run five independent scheduled Player processes from one frozen integrated
   binary and plan. Retain PID/time-associated PresentMon CSV for every run and
   at least one WPR GPU ETL for system acceptance. If trace privileges are
   denied, preserve exact denied-capture logs and still execute the independent
   engine runs. Mark the system gate unmet.

Concrete fixture declarations and source/binary/workload hashes are retained
with final run evidence. Any amendment before a run must be explicit. Additional
full historical, cross-version or cross-device benchmark matrices are outside
this bounded acceptance.

## Correctness regression command

Run from an ordinary shell without an already-held validation mutex:

```powershell
./Tools/Invoke-PsoIntegratedRegression.ps1 `
  -ValidationLockRunner C:/path/to/Invoke-SerializedValidation.ps1 `
  -Output C:/Evidence/pso-integrated-regression-new-directory
```

The runner refuses to overwrite evidence and writes `regression.json` with
source identity, Unity binary identity, checks and artifact hashes. It does not
replace the real Player or system evidence cells above.

## Merge and completion

Merge clean worker commits in compatibility, streaming, hotset, system order.
Resolve shared orchestrator seams without dropping validation, selection or
marker hooks. After integrated validation, fast-forward the original checkout
only if its branch, baseline HEAD and clean worktree still match the frozen
source. Never push. Mark the project complete only when every required gate is
satisfied; permission-denied system evidence leaves `needs_attention` even if
all implementation and available regressions pass.
