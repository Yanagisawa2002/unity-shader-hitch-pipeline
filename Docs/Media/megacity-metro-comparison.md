# Shader Hitch Pipeline A/B/C

Verdict: **PASS**

Verdict scope: supporting sample integrity; final performance verdict requires the 12-second scenario acceptance receipt.

| Metric | Cold baseline | Unity all-at-once | Deadline scheduled | Scheduled vs cold |
|---|---:|---:|---:|---:|
| mean | 8.485 ms | 8.334 ms | 8.334 ms | +1.8% |
| p95 | 8.335 ms | 8.335 ms | 8.335 ms | +0.0% |
| p99 | 8.338 ms | 8.336 ms | 8.336 ms | +0.0% |
| maximum | 227.658 ms | 8.569 ms | 8.581 ms | +96.2% |
| hitches ≥ 16.67 ms | 1 | 0 | 0 | 1 eliminated |
| severe stalls ≥ 33.34 ms | 1 | 0 | 0 | 1 eliminated |

- Profile: `megacity-metro-d3d12-20260903-054644`
- Plan content hash: `feeaf7e3e6164bb1d53800fa4640f132be1c30660c87e4be089e74d540fee4a5`
- Coverage: 48 variants / 48 graphics states
- Device: AMD Radeon AI PRO R9700 / Direct3D12
- Unity: 6000.1.0f1
- Raw samples: 1415 cold + 1440 all-at-once + 1440 scheduled frames
- Scheduled warmup evidence: 48/48 states, plan-scoped feedback trace miss count 0, hard budget met True (0 violations; outcome met); preinteractive bootstrap 0.000 ms
- Parity policy: report every ≥ 16.67 ms frame; allow at most 7 isolated non-severe frame(s), while severe-stall parity has zero allowance
- Exact zero-jitter parity: True

The cold and prewarmed players are separate builds because the final build must embed the traced plan. Environment compatibility is checked independently of build GUID.
