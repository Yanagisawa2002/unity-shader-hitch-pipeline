# Shader Hitch Pipeline A/B

Verdict: **PASS**

| Metric | Cold baseline | Prewarmed | Improvement |
|---|---:|---:|---:|
| mean | 8.535 ms | 4.167 ms | +51.2% |
| p95 | 34.300 ms | 4.169 ms | +87.8% |
| p99 | 37.467 ms | 4.169 ms | +88.9% |
| maximum | 48.943 ms | 4.174 ms | +91.5% |
| hitches ≥ 8.33 ms | 48 | 0 | 48 eliminated |

- Profile: `showcase-d3d12-20260901-142133`
- Plan content hash: `d870032ac5f85c03acc590401b838d23b2705ebbd1fb2295a313e0ea5c348fa3`
- Coverage: 388 variants / 389 graphics states
- Device: AMD Radeon AI PRO R9700 / Direct3D12
- Unity: 6000.5.2f1
- Raw samples: 240 cold + 240 prewarmed frames

The cold and prewarmed players are separate builds because the final build must embed the traced plan. Environment compatibility is checked independently of build GUID.
