# Lightweight preparation evidence

These are retained local validation logs, not Unity scene captures or performance results.
The latest checks after adding actual Unity job worker identity are
`python-worker-identity-tests.log` (93 passing checks) and
`shared-diagnostics-worker-identity-compile.log` (zero errors, seven existing warnings).
The earlier successful logs below remain available.
`python-functional-tests-final.log` contains 93 passing offline checks.
`shared-diagnostics-compile.log` records compilation against the installed Unity
6000.5.9f1 references with its API define: zero errors and seven existing legacy DTO warnings.
The official URP host adapter, full Linux IL2CPP build, native graphics, screenshot
contents, job behavior and complete-task benefit have not been validated.

The verification receipt at `Docs/Verification/whole-task-preparation-20260915.json`
binds the changed source inputs to these checks and keeps `nativeExecutionReady=false`.
The prior missing-jsonschema failures remain in the local preparation directory;
this successful run used a separate task-owned Python environment with the included dependency lock.
