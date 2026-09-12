# Public PSO reproduction matrix

Generated: `2026-09-02T05:20:15.899907Z`<br>
Matrix complete: **False**

| Vendor | Scene | Status | Runs | PresentMon | ETL | benchmark p99 ms | interactive warmup max ms | startup gate ms | Remaining evidence |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
| NVIDIA | showcase-controlled | pending-hardware | 0/5 | 0 | 0 | 0.000 | 0.000 | 0.000 | no completed public run |
| NVIDIA | unity-megacity-metro | pending-hardware | 0/5 | 0 | 0 | 0.000 | 0.000 | 0.000 | no completed public run |
| AMD | showcase-controlled | provisional | 5/5 | 0 | 0 | 4.229 | 0.000 | 29.892 | needs 0 more runs, 5 PresentMon captures, and 1 ETL |
| AMD | unity-megacity-metro | pending-run | 0/5 | 0 | 0 | 0.000 | 0.000 | 0.000 | no completed public run |
| Intel | showcase-controlled | pending-hardware | 0/5 | 0 | 0 | 0.000 | 0.000 | 0.000 | no completed public run |
| Intel | unity-megacity-metro | pending-hardware | 0/5 | 0 | 0 | 0.000 | 0.000 | 0.000 | no completed public run |

`reproducible` means at least 5 completed process-cold runs, PresentMon
for every run, and at least one WPR GPU ETL in the cell. Zeroes in pending rows
mean no measurement, not zero cost. A measured zero interactive-warmup maximum
with a nonzero startup gate means the required hot set completed before frame
presentation; it does not mean compilation was free.
