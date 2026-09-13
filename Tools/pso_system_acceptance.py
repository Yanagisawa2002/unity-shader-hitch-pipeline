#!/usr/bin/env python3
"""Fail-closed, local raw-artifact cross-checks for the five-process Windows gate."""
import argparse
import json
import re
from pathlib import Path

from pso_windows_evidence import build_summary, parse_utc, sha256


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def verify_record(record, label, errors):
    path = Path(record.get("path") or "__missing__")
    if not record.get("exists") or not path.is_file():
        errors.append(f"{label}: missing file")
        return None
    if path.stat().st_size != record.get("bytes") or sha256(path) != record.get("sha256"):
        errors.append(f"{label}: size/hash mismatch")
        return None
    return path


def validate_manifest(path):
    m = load(path)
    errors = []
    if m.get("schemaVersion") != 2 or not m.get("completed") or m.get("probeOnly"):
        errors.append("capture not completed with schema v2")
    artifacts = m.get("artifacts", {})
    resolved = {key: verify_record(artifacts.get(key, {}), key, errors) for key in
                ("presentMonCsv", "presentMonSummary", "warmupReceipt", "benchmarkReceipt", "markers", "buildManifest", "warmupPlan", "playerLog")}
    for i, record in enumerate(m.get("logs", [])):
        verify_record(record, f"log {i}", errors)
    for key in ("presentMon", "wpr", "analyzer", "wrapper"):
        verify_record(m.get("tools", {}).get(key, {}), key, errors)
    target = m.get("target", {})
    env = m.get("environment", {})
    if not env.get("operatingSystem") or not env.get("processor") or not env.get("gpuDevices"):
        errors.append("exact host OS/CPU/GPU environment missing")
    for gpu in env.get("gpuDevices", []):
        fields = {k.lower(): v for k, v in gpu.items()}
        if not all(fields.get(k) for k in ("name", "driverversion", "pnpdeviceid")):
            errors.append("GPU name/driver/PCI identity missing")
    if m.get("tools", {}).get("presentMonExitCode") != 0 or m.get("tools", {}).get("analyzerExitCode") != 0:
        errors.append("capture/analyzer did not exit successfully")
    files = target.get("buildFiles", [])
    if not files:
        errors.append("complete Player build inventory missing")
    for record in files:
        verify_record(record, "Player build", errors)
    if not any(r.get("path") == target.get("player") and r.get("sha256") == target.get("playerSha256") for r in files):
        errors.append("Player executable is not bound to build inventory")
    start, end = parse_utc(target.get("startedUtc")), parse_utc(target.get("endedUtc"))
    if not target.get("processId") or target.get("exitCode") != 0 or not start or not end or start >= end:
        errors.append("successful independent Player process interval missing")
    if m.get("cache") != {"process": "process-cold", "driver": "unknown-preserved", "globalCachesModified": False}:
        errors.append("process-cold / preserved unknown driver cache declaration missing")
    if resolved["markers"]:
        markers = [json.loads(line) for line in resolved["markers"].read_text(encoding="utf-8-sig").splitlines() if line.strip()]
        names = {marker.get("name") for marker in markers}
        if not {"benchmark-start", "benchmark-end"}.issubset(names):
            errors.append("benchmark boundary markers missing")
        if not start or not end or any(not (stamp := parse_utc(marker.get("utc"))) or not start <= stamp <= end for marker in markers):
            errors.append("markers outside owned Player interval")
    for key in ("warmupReceipt", "benchmarkReceipt"):
        if resolved[key]:
            receipt = load(resolved[key])
            begin, finish = parse_utc(receipt.get("startedUtc")), parse_utc(receipt.get("endedUtc"))
            if not receipt.get("completed") or not begin or not finish or not start or not end or not (start <= begin <= finish <= end):
                errors.append(f"{key}: incomplete or outside owned process interval")
            env = receipt.get("environment", {})
            if not env.get("unityVersion") or "direct3d12" not in str(env.get("graphicsDeviceType", "")).lower():
                errors.append(f"{key}: exact Unity/D3D12 environment missing")
    build = load(resolved["buildManifest"]) if resolved["buildManifest"] else {}
    if not re.fullmatch(r"[0-9a-f]{40}", build.get("sourceRevision", "")) or build.get("playerSha256") != target.get("playerSha256") or build.get("sourceDirty") is not False:
        errors.append("clean source revision to Player build attestation missing/mismatched")
    engine_errors = []
    if not build.get("workloadId") or build.get("expectedBenchmarkFrames", 0) < 1 or not build.get("expectedPhaseStates"):
        engine_errors.append("predeclared workload sample count / phase state counts missing")
    if resolved["benchmarkReceipt"]:
        benchmark = load(resolved["benchmarkReceipt"])
        if not benchmark.get("completed") or benchmark.get("mode") != "scheduled" or benchmark.get("frameTimes", {}).get("sampleCount") != build.get("expectedBenchmarkFrames"):
            engine_errors.append("benchmark did not complete the declared scheduled frame count")
    else: engine_errors.append("benchmark receipt unavailable")
    if resolved["warmupReceipt"]:
        warmup = load(resolved["warmupReceipt"])
        observed_states = {p.get("phase"): p.get("completedGraphicsStates") for p in warmup.get("phases", []) if p.get("completed") and p.get("completedGraphicsStates") == p.get("totalGraphicsStates")}
        if not warmup.get("completed") or warmup.get("strategy") != "scheduled" or observed_states != build.get("expectedPhaseStates"):
            engine_errors.append("warmup did not complete the declared scheduled phase/state workload")
        if not resolved["warmupPlan"] or sha256(resolved["warmupPlan"]) != warmup.get("planSha256"):
            engine_errors.append("actual warmup plan hash missing or mismatched")
    else: engine_errors.append("warmup receipt unavailable")
    errors.extend(engine_errors)
    # Recompute from raw CSV, never accept manually edited aggregate metrics.
    if all(resolved[k] for k in ("presentMonCsv", "presentMonSummary", "warmupReceipt", "markers")):
        summary = load(resolved["presentMonSummary"])
        try:
            replay = build_summary(resolved["presentMonCsv"], resolved["warmupReceipt"], summary.get("targetFrameMilliseconds"),
                                   summary.get("rowSelection", {}).get("localUtcOffsetMinutes", 0), target.get("processId"), resolved["markers"])
            for key in ("source", "metrics", "rowSelection", "budgetVerdict", "phaseCorrelation"):
                if replay.get(key) != summary.get(key): errors.append(f"PresentMon recomputation differs: {key}")
            if replay["rowSelection"]["correlationStatus"] != "matched": errors.append("no correlated warmup frames")
            if not replay["metrics"]["presentedFrameMilliseconds"]["sampleCount"]: errors.append("no valid presented cadence")
            phases = replay.get("phaseCorrelation", {})
            expected = {p["phase"] for p in load(resolved["warmupReceipt"]).get("phases", [])}
            observed = {p["phase"] for p in phases.get("windows", [])}
            if not expected or expected != observed or phases.get("unfinishedPhases"):
                errors.append("phase markers do not match completed warmup phases")
        except (ValueError, KeyError, TypeError) as error:
            errors.append(f"PresentMon/marker correlation: {error}")
    etw_errors = []
    etl = verify_record(artifacts.get("etl", {}), "GPU ETL", etw_errors)
    commands = m.get("tools", {}).get("wprCommands", [])
    started_gpu = any(c.get("arguments") == ["-start", "GPU", "-filemode"] and c.get("exitCode") == 0 for c in commands)
    stopped_gpu = any(c.get("arguments", [None])[0] == "-stop" and c.get("exitCode") == 0 for c in commands)
    has_etw = bool(etl and etl.stat().st_size > 0 and started_gpu and stopped_gpu)
    return {"manifest": str(Path(path).resolve()), "manifestSha256": sha256(Path(path)), "valid": not errors,
            "errors": errors, "hasEtw": has_etw, "etwErrors": etw_errors,
            "processKey": [target.get("processId"), target.get("startedUtc")], "sessionId": m.get("sessionId"),
            "startedUtc": target.get("startedUtc"), "endedUtc": target.get("endedUtc"),
            "buildIdentity": [build.get("sourceRevision"), target.get("playerSha256"), build.get("workloadId"), artifacts.get("buildManifest", {}).get("sha256"), artifacts.get("warmupPlan", {}).get("sha256"), sorted((r.get("path", ""), r.get("sha256", "")) for r in files)],
            "engineWorkloadErrors": engine_errors,
            "environmentIdentity": {k: m.get("environment", {}).get(k) for k in ("operatingSystem", "processor", "gpuDevices")}}


def validate_group(paths):
    runs = []
    for path in paths:
        try: runs.append(validate_manifest(path))
        except (ValueError, KeyError, OSError, TypeError) as error:
            runs.append({"manifest": str(path), "valid": False, "errors": [str(error)], "hasEtw": False})
    errors = []
    if len(runs) != 5: errors.append("exactly five declared process-cold runs required")
    if not all(r["valid"] for r in runs): errors.append("one or more run cross-checks failed")
    if not any(r["hasEtw"] for r in runs): errors.append("at least one retained WPR GPU ETL required")
    keys = [json.dumps(r.get("processKey")) for r in runs]
    sessions = [r.get("sessionId") for r in runs]
    if len(set(keys)) != len(runs) or None in sessions or len(set(sessions)) != len(runs): errors.append("duplicate or missing process/session identities")
    for identity in ("buildIdentity", "environmentIdentity"):
        if len({json.dumps(r.get(identity), sort_keys=True) for r in runs}) != 1: errors.append(f"incomparable {identity}")
    ordered = sorted((r for r in runs if r.get("startedUtc") and r.get("endedUtc")), key=lambda r: r["startedUtc"])
    if any(parse_utc(a["endedUtc"]) > parse_utc(b["startedUtc"]) for a, b in zip(ordered, ordered[1:])): errors.append("process runs overlap")
    return {"schemaVersion": 1, "completed": not errors, "status": "reproducible" if not errors else "blocked", "errors": errors, "runs": runs,
            "scope": "five process-cold runs; driver cache unknown and preserved; WPR GPU capture is not causal attribution"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--runs", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    result = validate_group(sorted(args.runs.glob("run-*/windows/windows-evidence-manifest.json")))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("completed", "status", "errors")}))
    return 0 if result["completed"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
