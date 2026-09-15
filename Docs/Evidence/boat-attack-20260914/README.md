# Boat Attack driver-attestation repair

![All 32 native process samples](boat-attack-cpu-intervals.png)

From the repository root, reproduce the public figure in a new directory with
`python Tools/plot_boat_latency_review.py --points Docs/Evidence/boat-attack-20260914/source-points.json --output work/boat-figure-reproduction`.
The [generator](../../../Tools/plot_boat_latency_review.py) uses the pinned
[plotting dependencies](requirements-plot.txt); it does not run a Player.

This figure reports all 32 processes from two completed, sequential revisions of the pinned official Boat Attack benchmark: four processes per policy per revision. Each revision uses one common Player and balanced policy order. The first panel is the original first 500-frame flythrough warmup's P99 CPU Update interval; the second is each process's maximum across all captured Update intervals. Initial startup before the first Update is outside the maximum's scope and remains in the full elapsed-time receipts.

The pre-repair warmup policies introduced roughly 0.6-second stalls. Direct diagnostic timing identified repeated synchronous driver-module hashing, and the implementation now reuses process-local byte attestation after fresh metadata/device/module checks. Initial process hashing remains. The post-repair sequence contains no intervals above 200 ms. First-pass P99 did not improve across revisions, and scheduled's total elapsed-time median still exceeds disabled: this is evidence for removing a diagnosed completion stall, not a general scheduling speedup.

Application, OS and driver caches were retained. These are CPU observation intervals, not GPU completion or presentation measurements. The cohorts ran sequentially, so ordinary before/after frame-time differences cannot be assigned wholly to the source change. No runs or outliers were removed; four independent processes per arm are descriptive repetitions without a population significance claim.

`source-points.json` contains every plotted value, each original validation receipt's SHA-256, and the two frozen protocol/summary hashes. Local creation rechecked those receipts and the matching workload/environment. The chart can be regenerated from these public points with `plot_boat_latency_review.py --points source-points.json --output NEW_DIRECTORY`. This public-points mode reproduces the plot; it does not substitute for checking private raw captures against their published hashes.

PNG and SVG were rendered with Matplotlib 3.11.2 and visually checked for unclipped labels, visible individual points and honest axes. A second rendering using only public points produced the identical PNG SHA-256: `e0191dc09d2ba59c4ade242670597209e6f6706e0a6a07f442a8763be1107b03`.

The separate [attestation diagnostic timings](attestation-diagnostic-timings.json) preserve all recorded operations from three native diagnostic processes. Repeated driver checks dropped from about 563 ms to 0.27–0.42 ms; first-process byte hashing remains about 574 ms. The file is byte-identical to the diagnostic summary attested by `diagnosticTimingsSha256` in the [verification receipt](../../Verification/boatattack-native-20260914.json). These operation timings establish the specific overhead repair and are not extra formal processes or replacements for the 32 comparison points.
