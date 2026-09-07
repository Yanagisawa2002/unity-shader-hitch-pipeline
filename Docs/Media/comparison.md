# Shader Hitch Pipeline A/B/C

Verdict: **PASS**

| Metric | Cold baseline | Unity all-at-once | Deadline scheduled | Scheduled vs cold |
|---|---:|---:|---:|---:|
| mean | 4.743 ms | 4.220 ms | 4.173 ms | +12.0% |
| p95 | 8.372 ms | 4.169 ms | 4.169 ms | +50.2% |
| p99 | 10.030 ms | 4.268 ms | 4.363 ms | +56.5% |
| maximum | 32.519 ms | 23.552 ms | 7.867 ms | +75.8% |
| hitches ≥ 8.33 ms | 57 | 5 | 0 | 57 eliminated |
| severe stalls ≥ 16.67 ms | 1 | 2 | 0 | 1 eliminated |

- Profile: `showcase-d3d12-20260902-064931`
- Plan content hash: `7e79d3014eef5e9d8701007ae3216082a1acf9bd602fdf74d8290f78c6c722da`
- Coverage: 400 variants / 401 graphics states
- Device: AMD Radeon AI PRO R9700 / Direct3D12
- Unity: 6000.5.2f1
- Raw samples: 1080 cold + 1080 all-at-once + 1080 scheduled frames
- Scheduled warmup evidence: 401/401 states, plan-scoped feedback trace miss count 0, hard budget met True (0 violations; outcome met); preinteractive bootstrap 1.537 ms
- Parity policy: report every ≥ 8.33 ms frame; allow at most 5 isolated non-severe frame(s), while severe-stall parity has zero allowance
- Exact zero-jitter parity: True

The cold and prewarmed players are separate builds because the final build must embed the traced plan. Environment compatibility is checked independently of build GUID.
