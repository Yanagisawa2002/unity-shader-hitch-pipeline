# unity-shader-hitch-pipeline TODO

Updated: 2026-09-28. Repository-specific handoff for testing on another device. All tasks below are pending on that device; this documentation change runs no benchmarks or application tests.

Source baseline: `55a9f168684ffc0dc538204f991a5c293fb8c8c4` on main. Start with the [pinned README](https://github.com/Yanagisawa2002/unity-shader-hitch-pipeline/blob/55a9f168684ffc0dc538204f991a5c293fb8c8c4/README.md) for prerequisites and the scope of historical evidence.

## Prepare the checkout

- [ ] Clone into a fresh directory and select the recorded source commit. If deliberately using a newer revision, record the new SHA and review the intervening changes.

```powershell
gh repo clone Yanagisawa2002/unity-shader-hitch-pipeline
Set-Location unity-shader-hitch-pipeline
git switch --detach 55a9f168684ffc0dc538204f991a5c293fb8c8c4
git rev-parse HEAD
```

## Functional checks first

- [ ] Use a short checkout, Python 3.12, PowerShell 7 and the required .NET 8+ SDK/runtime. Run:

```powershell
python -m pip install -r Tools/requirements-validation.txt
pwsh Tools/Invoke-PsoValidation.ps1
```

- [ ] Record deterministic CPU results separately from native warmup. Optional reference-only compilation against installed Editor DLLs does not execute Unity.
- [ ] Import `Packages/com.yanagisawa.shader-hitch-pipeline` into a configured Unity host and record exact Editor/graphics API/driver versions.
- [ ] Exercise trace -> plan -> build -> runtime identity invalidation and retained material/shader ownership during submitted work.
- [ ] Test streaming cancellation, reactivation, stale revisions and completion reporting. Cancellation and content readiness do not imply first-draw shader coverage.
- [ ] If selecting official external scenes later, follow each adapter's pinned preparation/acceptance guide and obtain its assets separately.
- [ ] Keep Megacity Main/six-SubScene acceptance incomplete until actually observed. Preserve the documented URP outlier and unproven overall scheduling/coverage benefits.
- [ ] Run policy comparisons only as a separately selected experiment with matched route/build/cache conditions; keep scheduled, observed-budget and fixed-progressive policies explicit.

## Record the new-device outcome

- [ ] Record source and binary identity, machine/OS, toolchain, hardware/driver, command/exit code, input/configuration hashes and the actual PASS / FAIL / BLOCKED / NOT RUN result.
- [ ] Keep generated binaries, captures and large assets outside source control. Link the retained evidence from the affected validation document/PR.
- [ ] Preserve unavailable metrics and negative outcomes. Separate static/functional success, runtime correctness and performance conclusions.

Create a focused `codex/` branch before implementing a fix. Existing unexecuted acceptance items remain open until the selected workflow is actually run.
