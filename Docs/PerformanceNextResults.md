# Independent PSO follow-up: inconclusive performance

Keep required-only as the conservative recommendation. This round completed
the fixed offline audit, ten capture processes, and eighteen new held-out cells.
It did not establish a reliable hotset benefit or a five-pair equal-work estimate
of WPR overhead. Defaults are unchanged; nothing was merged into the original
checkout or pushed.

| Evidence | Measured source | Actual Player build GUID |
|---|---|---|
| Capture pairs | d896b394cf8536c15031d072e967130c98dcb2ab | 6af5c4870eca4233a20e234d7dd06af5 |
| Held-out v2 | 87cb80adf7d21408d8d6048cfa32791abb73d373 | 9361fed76c6d468d9d56bb14a94ae0f3 |

The starting baseline remains b04ea55846e1bb681bcff8448da302236434ecb7.
Later documentation/offline-reader commits are not substituted for either
measured source revision. Binary inventories bind the complete Player directories;
the common Unity bootstrap EXE hash alone is insufficient.

Historical replay retains all 39,400 owned-PID rows. Four largest CPU-cadence
tails occur after content completion; one occurs before workload start. Native
QPC alignment, full tracerpt decoding and the finalized ETL header agree on
coverage. Reported event and buffer loss are zero. CPU busy/wait metrics locate
symptoms but do not resolve native stacks or a causal critical path.

Both new capture arms wait 20 seconds with PresentMon active before Player.
All ten processes complete 7,200 benchmark frames. Pair-1 PM completes only
2/388 combat states; the other nine complete 12 startup plus 388 combat states.
All five WPR ETLs cover the full Player interval and report zero event/buffer
loss. The failed pair remains visible and is ineligible for equal-work inference.
No performance-triggered retry occurred. For complete pairs 2–5, WPR-minus-PM
CPU p99 increases by 0.021570/0.126426/0.059279/0.071110 ms, median 0.0651945 ms.
Display cadence is unstable and must be assessed separately; its maximum ranges
from 934.1508 to 3252.675 ms. No whole-application hard-budget success is claimed.

New held-out routes are [0,3,1,2,3] and [0,2,3,1,2], with three independent
processes per arm/route. Fresh discovery finds six states per collection.
Required-only/hotset/all-at-once warm 6/12/24 startup entries, then submit
18/12/0 deferred states. Every cell has 480 actual and covered uses, zero native
feedback misses, and 119 retained inter-frame samples. The frozen startup budget
is 1.60185 ms and the hotset is u0+u1. Required-only has three >16.67-ms intervals
in one process; hotset has one in another; all-at-once has none in this small
sample. These sparse outcomes do not establish repeatable hotset benefit.

The first held-out discovery failed because its whitelist rejected the declared
PSO worker argument. The v2 source fix accepts the frozen value four and binds it
to the cost context. V1 failure logs remain; v2 rebuilds and retrains in a fresh
directory before executing the unchanged matrix. V2's 18 cells pass independent
raw audits. OS first-Present, displayed cadence and opaque driver PSO memory
remain unavailable in this fixture; engine first-render and allocation deltas
are explicitly proxies. No new first-screen claim is made.

## Reusable offline tools

`python Tools/analyze_pso_capture_windows.py RUN_ROOT --output FRESH.json`
audits all owned CSV rows against integer native QPC boundaries, validates raw
hashes, preserves all swap chains and missing metrics, and reports full and
per-stage statistics without receipt clipping. Tests are in Tools/tests.

On Win64, `python Tools/pso_etl_header.py FILE.etl [MORE.etl]` opens finalized
local ETLs with OpenTraceW, reports EventsLost/BuffersLost, QPC frequency and
UTC bounds, then closes every handle. It never starts a recorder. Header reads
do not replace provider/event decoding. The historical file was also decoded
using `tracerpt ... -summary ... -report ... -o NUL -of CSV -y`.

The external performance-next/pso delivery includes English reports, complete
per-cell JSON, retained raw paths/hashes, small-evidence ZIP, five large ETL links,
reproduction scripts and the code-fault record. Historical artifacts are retained
at their original paths and are never relabelled as new measurements.
