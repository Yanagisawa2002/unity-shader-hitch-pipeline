# Shader Hitch Pipeline A/B/C

Verdict: **PASS**

| Metric | Cold baseline | Unity all-at-once | Deadline scheduled | Scheduled vs cold |
|---|---:|---:|---:|---:|
| mean | 8.861 ms | 4.169 ms | 4.201 ms | +52.6% |
| p95 | 35.706 ms | 4.169 ms | 4.210 ms | +88.2% |
| p99 | 40.674 ms | 4.203 ms | 4.279 ms | +89.5% |
| maximum | 46.387 ms | 4.436 ms | 9.799 ms | +78.9% |
| hitches ≥ 8.33 ms | 48 | 0 | 1 | 47 eliminated |
| severe stalls ≥ 16.67 ms | 48 | 0 | 0 | 48 eliminated |

- Profile: `showcase-d3d12-20260902-025624`
- Plan content hash: `3a7cbd3c2a695cd507c0dab566bff46d7c178131db81addc478859569c06d345`
- Coverage: 388 variants / 389 graphics states
- Device: AMD Radeon AI PRO R9700 / Direct3D12
- Unity: 6000.5.2f1
- Raw samples: 240 cold + 240 all-at-once + 240 scheduled frames
- Scheduled warmup evidence: 389/389 states, plan-scoped feedback trace miss count 0
- Parity policy: report every ≥ 8.33 ms frame; allow at most 1 isolated non-severe frame(s), while severe-stall parity has zero allowance
- Exact zero-jitter parity: False

The cold and prewarmed players are separate builds because the final build must embed the traced plan. Environment compatibility is checked independently of build GUID.
