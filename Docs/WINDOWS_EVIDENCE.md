# Windows presentation and GPU evidence

Unity receipts prove engine-side execution and raw `unscaledDeltaTime` samples.
They do not prove display cadence or GPU queue activity. A reproducible cell
requires five independent process-cold runs with actual PresentMon CSV for each,
at least one WPR GPU ETL, and verified identities/artifact hashes.

## Actual privilege limitation

The 2026-09-07 R9700 probe retained PresentMon 2.5.1 x64, SHA-256
`9bec3083069f58f911e6a512f4806db51a27bd096103087bc1d05ef54c80a191`.
WPR GPU start returned exit `-984068079`, error `0xc5585011`,
"Failed to enable the policy to profile system performance."
PresentMon returned exit 6, "failed to start trace session: access denied."
That non-elevated token could not produce the required system evidence. No elevation,
group changes, user/global cache deletion, or existing-session cancellation was
performed. Failed captures remain failures, including when engine runs succeed.

Official tool contracts:

- [PresentMon 2.5.1 console options and CSV](https://github.com/GameTechDev/PresentMon/blob/v2.5.1/README-ConsoleApplication.md)
- [PresentMon releases](https://github.com/GameTechDev/PresentMon/releases)
- [WPR command line](https://learn.microsoft.com/windows-hardware/test/wpt/wpr-command-line-options)

## Predeclare and run five processes

Build the integrated Player and traced plan first, retaining its Unity build log.
Use a dedicated build directory with the Player's DLL/data files. Do not substitute
an old executable for the integrated source revision. After the successful build,
write a build attestation JSON (the source/build association is an operator
attestation, not a property inferred from the executable hash):

```json
{
  "sourceRevision": "<full clean 40-character git commit>",
  "sourceDirty": false,
  "playerSha256": "<actual executable SHA-256>",
  "workloadId": "showcase-controlled-389-scheduled-vnext",
  "expectedBenchmarkFrames": 180,
  "expectedPhaseStates": {"startup": 389}
}
```

Use the actual selected benchmark count in both attestation and arguments. The
180-frame example follows the retained public showcase receipts; a larger fixed
count is allowed when predeclared. Freeze the Player/plan/settings across all five.
Save the following declaration with real absolute input paths:

```json
{
  "schemaVersion": 1,
  "repetitions": 5,
  "cacheState": "process-cold",
  "workloadId": "showcase-controlled-389-scheduled-vnext",
  "player": "C:/Build/ShaderHitchShowcase.exe",
  "presentMon": "C:/Tools/PresentMon-2.5.1-x64.exe",
  "buildManifest": "C:/Evidence/build.json",
  "captureSeconds": 120,
  "playerTimeoutSeconds": 110,
  "playerArguments": [
    "-force-d3d12", "-screen-fullscreen", "0",
    "-screen-width", "1280", "-screen-height", "720",
    "-pso-benchmark", "-pso-benchmark-mode", "scheduled",
    "-pso-benchmark-frames", "180", "-pso-benchmark-discard-frames", "0",
    "-pso-benchmark-delay-seconds", "3",
    "-pso-warmup-strategy", "scheduled"
  ]
}
```

Call from PowerShell 7; the runner holds the supplied shared mutex wrapper until
all owned children finish, including capture cleanup. Output must be fresh.

```powershell
& ./Tools/Invoke-PsoSystemMatrix.ps1 -Declaration C:/Evidence/declaration.json `
  -Output C:/Evidence/system-five `
  -SerializedRunner C:/Control/Invoke-SerializedValidation.ps1
python Tools/pso_system_acceptance.py --runs C:/Evidence/system-five `
  --output C:/Evidence/system-five/system-acceptance.json
```

The runner appends unique `-pso-output`, `-pso-warmup-receipt`,
`-pso-benchmark-report`, `-pso-system-markers`, and `-logFile` paths. Custom paths in
other arguments may use `{runDirectory}`. Each repetition starts a new process;
driver caches remain **unknown-preserved**, never described as driver-cold.
There is no shader cache buster, undocumented cache deletion, or cache reset.
Capture-unavailable continuation retains all five engine attempts and per-run
errors. Final exit remains unsuccessful until all required OS evidence passes.

## Capture and correlation contracts

`Invoke-PsoWindowsEvidence.ps1` schema v2 starts WPR's built-in GPU file-mode
profile on run 1 and independently starts a unique PresentMon session before the
owned Player. The measured Player has a visible, non-minimized window so DXGI can
present; capture helpers remain hidden. Hiding a Unity Player can produce no
presentation CSV and unfinished background warmup. It never stops an unrelated recording. Unknown WPR status fails
closed. `-ProbeOnly -CaptureEtw` tests rights without launching the Player;
`-CaptureUnavailableContinueEngine` allows engine evidence despite denied capture.
All helper stdout/stderr, command exits, actual PID, process UTC interval,
hardware/driver/OS identity, process snapshot, Player build inventory, plan,
receipts, tools and analysis hashes are retained. Capture timeout must exceed
Player timeout. The wrapper refuses nonempty outputs and stale receipt paths.

`PsoSystemMarkers` is opt-in. On Windows it calls native `QueryPerformanceCounter`
and `QueryPerformanceFrequency`, and records `clockSource` explicitly. Unity's
managed Stopwatch may have a process-relative epoch and cannot supply absolute
ETW clock anchors. Live PresentMon capture uses `--qpc_time`, avoiding wall-clock
formatting and timezone ambiguity. It buffers phase start/end, benchmark start/end and
scenario markers with PID, session ID, frame number, realtime, UTC and QPC anchors;
disk writes occur at benchmark completion/shutdown. These are **sidecar clock
anchors, not native ETW provider events**. QPC continuity must agree with UTC within
10 ms. Correlation reports phase windows separately, including zero-present startup
windows; it does not invent a presented frame during preinteractive work.

The CSV parser filters the owned PID before selecting the dominant swap chain.
Receipt UTC bounds and PresentMon local offset select the warmup interval.
An empty or unavailable interval never falls back to a full-capture budget pass.
Unavailable metrics are null. CPU-present cadence and displayed cadence remain
separate; raw tails retain available CPU/GPU busy and display-latency signals.
Clock correlation is not causal attribution. GPU queue interpretation still
requires reading the retained ETL in a compatible trace analyzer.

Acceptance rehashes raw files and recomputes summaries, checks receipts lie within
the owned process interval, verifies completed scheduled sample/phase/state counts,
and requires marker phases to match warmup phases. Five copied receipts, changed
builds/environments, overlapping processes, stale files and forged summaries fail.
WPR evidence requires a nonempty hashed ETL plus successful GPU start/stop commands;
this is capture provenance, not automatic verification of individual GPU events
or zero ETW event loss. Those interpretation limits remain explicit.

`pso_matrix.py` keeps historical engine receipts provisional. Aggregation reopens
raw manifests before promotion and never upgrades historical claims by copying
run JSON. The full public definition includes other vendor/scene cells; the vNext
bounded experiment targets only the R9700 controlled showcase.
