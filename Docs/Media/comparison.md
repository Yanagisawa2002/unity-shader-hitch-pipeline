# Shader Hitch Pipeline A/B

Verdict: **PASS**

| Metric | Cold baseline | Prewarmed | Improvement |
|---|---:|---:|---:|
| mean | 6.466 ms | 4.167 ms | +35.6% |
| p95 | 26.738 ms | 4.169 ms | +84.4% |
| p99 | 34.941 ms | 4.172 ms | +88.1% |
| maximum | 39.643 ms | 4.271 ms | +89.2% |
| hitches ≥ 8.33 ms | 24 | 0 | 24 eliminated |

- Profile: `cycle2-d3d12`
- Plan content hash: `96f6525f79f4860e6c27fcc50342901a1519b2198dc22e156c954ce994af2ca5`
- Coverage: 260 variants / 389 graphics states
- Device: AMD Radeon AI PRO R9700 / Direct3D12
- Unity: 6000.5.2f1
- Raw samples: 240 cold + 240 prewarmed frames

The cold and prewarmed players are separate builds because the final build must embed the traced plan. Environment compatibility is checked independently of build GUID.
