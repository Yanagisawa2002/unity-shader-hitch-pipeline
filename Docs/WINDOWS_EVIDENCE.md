# Windows presentation and GPU evidence

Unity receipts establish engine-side scheduling and raw `unscaledDeltaTime`
samples. They do not establish when DWM displayed a frame or which GPU queue was
busy. The Windows evidence layer retains both planes instead of substituting one
for the other.

## Tools and privilege

The wrapper was exercised against PresentMon 2.5.1 portable x64, SHA-256
`9bec3083069f58f911e6a512f4806db51a27bd096103087bc1d05ef54c80a191`.
PresentMon's console documentation defines one CSV row per presented frame and
the v2 `FrameTime`, `CPUBusy`, `GPUTime`, `GPUBusy`, `DisplayLatency`, and
`DisplayedTime` columns:

- https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md
- https://github.com/GameTechDev/PresentMon/releases

PresentMon and WPR require permission to start ETW sessions. Use an elevated
shell or add the test account to **Performance Log Users**, then sign out/in.
The current non-elevated validation deliberately produced PresentMon exit code 6
and a failed manifest. It did not produce placeholder CSV data.

## Capture

```powershell
pwsh Tools/Invoke-PsoWindowsEvidence.ps1 `
  -Player C:\Build\Game.exe `
  -PresentMon C:\Tools\PresentMon-2.5.1-x64.exe `
  -PlayerArguments @(
    "-pso-warmup-strategy", "scheduled",
    "-pso-warmup-receipt", "C:\Evidence\scheduled.warmup.json",
    "-pso-benchmark", "-pso-benchmark-mode", "scheduled",
    "-pso-benchmark-report", "C:\Evidence\scheduled.benchmark.json"
  ) `
  -WarmupReceipt C:\Evidence\scheduled.warmup.json `
  -Output C:\Evidence\Windows `
  -CaptureEtw
```

The wrapper starts PresentMon before the owned Player, targets the executable
name, optionally starts WPR's built-in `GPU` file-mode profile, and refuses to
touch an existing WPR session. On completion it hashes the executable, tools,
CSV, ETL, receipts, and logs and records exact arguments and hardware identity.

`pso_windows_evidence.py` selects the dominant swap chain. When
`CPUStartDateTime` and receipt UTC bounds are available, it clips analysis to the
warmup interval using the recorded local UTC offset. It reports p50/p95/p99/max
for CPU-present cadence and displayed cadence separately, plus CPU/GPU busy and
latency signals for the worst five rows. Correlation is not mislabeled as causal
proof; the raw CSV and GPU ETL remain the audit source.

Microsoft documents WPR's CLI and profile handling here:
https://learn.microsoft.com/windows-hardware/test/wpt/wpr-command-line-options

## Matrix acceptance

`Matrix/targets.json` defines six independent cells: NVIDIA, AMD, and Intel over
the controlled showcase and pinned Unity Megacity Metro. A cell becomes
`reproducible` only with five process-cold runs, PresentMon for all five, one GPU
ETL, exact revisions, and declared cache state. Use `--strict` on
`pso_matrix.py aggregate` in CI to fail while any cell is incomplete.
