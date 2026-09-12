# Portfolio figure

This figure is retained historical evidence from report snapshot `f352210`; it was not regenerated during the 2026-09-08 repair. The two panels separate scene-reveal frame times from the warmup interval; the flow above is schematic.

Read the [policy adoption guide](../POLICY_ADOPTION.md) for the scheduler's own
tradeoff: warmup-window pressure versus total completion and tail latency.
Cold-start improvement alone does not isolate that contribution, and the
historical 178.9 ms outlier remains unresolved by measured current-source evidence.

## Reproduce

From the repository root:

```bash
python -m pip install -r Docs/portfolio/requirements.txt
python Docs/portfolio/render.py
```

The renderer verifies source SHA-256 hashes (CRLF normalized to LF) before plotting the reviewed values in `figure.json`. If a source changes, review and refresh the snapshot before regenerating. It writes SVG and PNG with matching content.

## Sources

- [Docs/BENCHMARK_METHODOLOGY.md](../../Docs/History/f352210/BENCHMARK_METHODOLOGY.md)

The flow/memory/timing illustrations are schematics. Only explicitly labeled measurements represent recorded experiments. Confidence intervals are copied from source reports, not recomputed.
