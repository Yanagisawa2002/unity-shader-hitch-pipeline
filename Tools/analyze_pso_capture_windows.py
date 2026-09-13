"""Audit every owned-PID PresentMon row against native QPC stage boundaries.

No receipt-window trimming, UTC offset fitting, tail removal or causal attribution.
Adjacent marker intervals partition rows exactly once. A row's CPUStartQPC is
its stage attribution; an interval crossing a boundary is explicitly counted.
"""
import argparse
import bisect
import csv
import json
from collections import Counter
from datetime import timedelta
from pathlib import Path

from pso_windows_evidence import (METRIC_ALIASES, load_markers, metric_summary,
                                 parse_number, parse_qpc, parse_utc, select_column, sha256)


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def audit(manifest_path, target=16.67):
    manifest = read(manifest_path)
    artifacts = manifest["artifacts"]
    provenance = []
    for name in ("presentMonCsv", "markers", "warmupReceipt", "benchmarkReceipt"):
        item = artifacts[name]
        actual = sha256(Path(item["path"]))
        if actual != item["sha256"]:
            raise ValueError("Raw evidence hash mismatch: " + name)
        provenance.append(dict(role=name, path=item["path"], sha256=actual))
    pid = manifest["target"]["processId"]
    markers = load_markers(Path(artifacts["markers"]["path"]), pid, True)
    ticks = [parse_qpc(m["qpc"]) for m in markers]
    frequency = parse_qpc(markers[0]["qpcFrequency"])
    anchor = parse_utc(markers[0]["utc"])
    def utc(qpc):
        return anchor + timedelta(seconds=(qpc-ticks[0])/frequency)
    def label(m):
        return m["name"] + (":"+m["phase"] if m.get("phase") else "")
    with Path(artifacts["presentMonCsv"]["path"]).open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        fields = reader.fieldnames
        all_rows = list(reader)
    pid_col = select_column(fields, ("ProcessID",))
    qpc_col = select_column(fields, ("CPUStartQPC", "TimeInQPC"))
    swap_col = select_column(fields, ("SwapChainAddress",))
    if not pid_col or not qpc_col:
        raise ValueError("Explicit PID and native QPC columns required")
    owned = [r for r in all_rows if parse_number(r[pid_col]) == pid]
    if not owned:
        raise ValueError("No owned-PID rows")
    cols = {k: select_column(fields, v) for k,v in METRIC_ALIASES.items()}
    rows = [(parse_qpc(r[qpc_col]),r) for r in owned]
    rows.sort(key=lambda x:x[0])
    stage_rows = [[] for _ in range(len(markers)+1)]
    boundary_crossings = 0
    for qpc,row in rows:
        index = bisect.bisect_right(ticks,qpc)
        stage_rows[index].append((qpc,row))
        duration = parse_number(row.get(cols["presentedFrameMilliseconds"]))
        if index < len(ticks) and duration is not None and qpc+duration*frequency/1000 > ticks[index]:
            boundary_crossings += 1
    def stats(items):
        metrics = {}
        for name,col in cols.items():
            values = [parse_number(row.get(col)) for _,row in items]
            valid = [v for v in values if v is not None]
            metrics[name] = metric_summary(valid, target)
            metrics[name]["missingCount"] = len(values)-len(valid)
            metrics[name]["sourceColumn"] = col
        return dict(rowCount=len(items), metrics=metrics)
    stages=[]
    for i,items in enumerate(stage_rows):
        stages.append(dict(index=i, start=(label(markers[i-1]) if i else "before-first-marker"),
                           end=(label(markers[i]) if i<len(markers) else "after-last-marker"),
                           startQpc=ticks[i-1] if i else None,
                           endQpc=ticks[i] if i<len(markers) else None, **stats(items)))
    warmup = read(artifacts["warmupReceipt"]["path"])
    receipt_start, receipt_end = parse_utc(warmup["startedUtc"]), parse_utc(warmup["endedUtc"])
    process_start=parse_utc(manifest["target"]["startedUtc"])
    process_end=parse_utc(manifest["target"]["endedUtc"])
    cpu_col=cols["presentedFrameMilliseconds"]
    tail=[]
    for qpc,row in sorted(rows,key=lambda x:parse_number(x[1].get(cpu_col)) or -1,reverse=True)[:10]:
        stage=bisect.bisect_right(ticks,qpc)
        tail.append(dict(qpc=qpc,cpuStartUtc=utc(qpc).isoformat(),stageIndex=stage,
                         stage=stages[stage]["start"]+" -> "+stages[stage]["end"],
                         metrics={k:parse_number(row.get(v)) for k,v in cols.items()}))
    chains=Counter(r.get(swap_col,"") for _,r in rows)
    return dict(schemaVersion=1,manifest=str(manifest_path),manifestSha256=sha256(manifest_path),
                processId=pid, sourceEvidence=provenance, targetMilliseconds=target,
                scope="ALL owned-PID CSV rows; no warmup/benchmark receipt window clipping",
                clock=dict(source=markers[0]["clockSource"],anchor=markers[0],markers=markers),
                completeness=dict(totalCsvRows=len(all_rows),foreignPidRows=len(all_rows)-len(rows),
                    ownedRows=len(rows),partitionRows=sum(s["rowCount"] for s in stages),
                    swapChains=dict(chains), firstCpuStartUtc=utc(rows[0][0]).isoformat(),
                    lastCpuStartUtc=utc(rows[-1][0]).isoformat(),
                    rowsOutsideReceipt=sum(not receipt_start<=utc(q)<=receipt_end for q,_ in rows),
                    rowsOutsideProcessBounds=sum(not process_start<=utc(q)<=process_end for q,_ in rows),
                    launchToFirstCpuStartMilliseconds=(utc(rows[0][0])-process_start).total_seconds()*1000,
                    lastCpuStartToExitMilliseconds=(process_end-utc(rows[-1][0])).total_seconds()*1000,
                    cpuIntervalsCrossingMarkerBoundary=boundary_crossings),
                full=stats(rows), stages=stages, largestCpuCadenceRows=tail,
                osFirstPresent=dict(available=False,reason="CPUStartQPC is CPU frame start, not exact Present API entry; no reliable first-Present anchor in this CSV. DisplayedTime is cadence, not first-screen latency."),
                limitations=["Boundary attribution uses CPU frame start; crossing frames are not proof of work within either phase.",
                    "Marker labels locate instrumented activity; gaps do not identify CPU stacks, scheduler delay, I/O or PSO causality.",
                    "Launch-before-first-frame and shutdown-after-last-frame intervals have no cadence samples.",
                    "ETW loss must be audited separately; CSV count cannot prove all presents were captured."])


if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("root",type=Path)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    if args.output.exists():
        raise SystemExit("Use a fresh output file")
    paths=sorted(args.root.rglob("windows-evidence-manifest.json"))
    result=dict(schemaVersion=1,runs=[audit(p) for p in paths])
    args.output.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps([dict(pid=r["processId"],rows=r["full"]["rowCount"],completeness=r["completeness"],maxTail=r["largestCpuCadenceRows"][0]) for r in result["runs"]],indent=2))
