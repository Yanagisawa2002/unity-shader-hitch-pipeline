# Public reproduction matrix

The matrix deliberately separates the deterministic 389-state microbenchmark,
the 12-second/320-state Deadline Run cinematic gate, and Unity's public Megacity
Metro large scene. A cell is **reproducible** only after five process-cold runs,
PresentMon CSV for every run, and at least one WPR GPU ETL. Missing NVIDIA or
Intel hardware remains visible as `pending-hardware`; it is never inferred from
another vendor.

`Matrix/evidence` contains the five current AMD engine-level receipts. They are
portable publication copies: only local absolute path fields are normalized,
and each document retains the original SHA-256 plus the changed-field list.
Frame samples, metrics, environment, and plan content hashes are unchanged.

Megacity Metro is pinned to commit
`07652ee74a1f322c2c3e607020f07be720175680` and uses the Unity Companion
License. Follow its upstream Git LFS setup and build the single-player
`Assets/Scenes/Main.unity` route with a fixed quality preset and camera script.

Record a completed scheduled run:

```powershell
python Tools/pso_matrix.py record `
  --definition Matrix/targets.json `
  --hardware amd-d3d12 `
  --scene showcase-controlled `
  --cache-state process-cold `
  --benchmark path/to/scheduled.benchmark.json `
  --warmup path/to/scheduled.warmup.json `
  --presentmon path/to/presentmon-summary.json `
  --windows-manifest path/to/windows-evidence-manifest.json `
  --output Matrix/runs/amd-showcase-r1.matrix-run.json
```

For the external large-scene cell, use the exact source revision in
`Integrations/MegacityMetro/pin.json`. The adapter is intentionally installed
into a separate Megacity checkout; no Unity sample assets belong in this
repository. Keep that cell pending until the pinned guided route, reveal set,
PresentMon CSV, and WPR GPU ETL are all retained.

Regenerate the public status table:

```powershell
python Tools/pso_matrix.py aggregate `
  --definition Matrix/targets.json `
  --runs Matrix/runs `
  --output Matrix/results
```

Run ETW capture from an elevated shell or an account in **Performance Log
Users**. The capture wrapper refuses to disturb an existing WPR session.

For vNext, use `Tools/Invoke-PsoSystemMatrix.ps1` with the frozen five-run
declaration and build attestation described in `Docs/WINDOWS_EVIDENCE.md`.
It continues the five engine workloads when capture privileges are denied, but
the required OS gate remains blocked. Schema-v2 acceptance rehashes raw artifacts,
recomputes PresentMon metrics, verifies PID/phase/UTC correlation and actual
completed workload counts. Historical receipts remain provisional and are not
silently upgraded to system proof.
