# Portfolio figure

The two panels separate scene-reveal frame times from the warmup interval; the flow above is schematic.

## Reproduce

From the repository root:

```bash
python -m pip install -r Docs/portfolio/requirements.txt
python Docs/portfolio/render.py
```

The renderer verifies source SHA-256 hashes (CRLF normalized to LF) before plotting the reviewed values in `figure.json`. If a source changes, review and refresh the snapshot before regenerating. It writes SVG and PNG with matching content.

## Sources

- [Docs/BENCHMARK_METHODOLOGY.md](../../Docs/BENCHMARK_METHODOLOGY.md)

The flow/memory/timing illustrations are schematics. Only explicitly labeled measurements represent recorded experiments. Confidence intervals are copied from source reports, not recomputed.
