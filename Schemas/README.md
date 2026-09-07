# Versioned interchange schemas

Schema version 3 separates portable scheduling and evidence contracts from the
opaque artifact produced by an engine adapter. `adapterId` identifies the
producer/consumer of that artifact; the core never assumes a Unity file format.

- `trace-session.schema.json`: one representative trace and its environment.
- `warmup-plan.schema.json`: verified phase, deadline, cost, hot-set, and hard-budget policy.
- `warmup-receipt.schema.json`: exact admissions, predictions, batches, violations, and completion evidence.

The receipt separates `preinteractiveBootstrapMilliseconds` from interactive
frame samples. `hardBudgetGuaranteeScope` is mandatory: startup-gated scheduled
work, strict deferred admission, and throughput/no-guarantee are never conflated.

`deadline-run-acceptance.schema.json` is the portfolio-facing gate layered on
top of the general receipts. It requires a naturally visible cold stall and zero
scheduled presentations at or above the configured 60 FPS threshold while also
retaining the scheduled phase's deadline and hard-budget outcome. Acceptance
schema 2 covers both `deadline-run` and `megacity-metro`; the latter also carries
time-gated camera/capture, native frame-timing, ECS-world, and managed-GC
invariants.

The Unity adapter id is `unity.graphics-state-collection`. Third-party adapters
should use a stable reverse-DNS or product-qualified id and retain the same
state-count, hash, deadline, and receipt semantics.

Validate generated documents:

```powershell
python Tools/validate_pso_documents.py `
  --trace path/to/session.json `
  --plan path/to/plan.json `
  --warmup path/to/warmup.json
```

Plan schema v3 also supports the additive version-1 `compatibility` contract. Legacy plans remain valid but unattested. `cost-cache.schema.json` describes optional measured cost seeds; schema validation alone does not establish integrity or current-build compatibility. See [compatibility](../Docs/COMPATIBILITY.md).
