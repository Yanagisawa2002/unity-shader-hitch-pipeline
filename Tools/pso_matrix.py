#!/usr/bin/env python3
"""Record and aggregate the public cross-vendor PSO evidence matrix."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import statistics
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from pso_system_acceptance import validate_manifest, validate_group


def load(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def source_record(path: Path | None) -> dict[str, Any]:
    if path is None:
        return {"path": "", "sha256": "", "bytes": 0}
    resolved = path.resolve()
    try:
        display_path = resolved.relative_to(Path.cwd().resolve()).as_posix()
    except ValueError:
        display_path = resolved.name
    return {
        "path": display_path,
        "sha256": sha256(resolved),
        "bytes": resolved.stat().st_size,
    }


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def find_by_id(items: list[dict[str, Any]], identifier: str, label: str) -> dict[str, Any]:
    for item in items:
        if item.get("id") == identifier:
            return item
    raise ValueError(f"Unknown {label} id: {identifier}")


def warmup_metrics(receipt: dict[str, Any]) -> dict[str, Any]:
    phases = receipt.get("phases", [])
    max_frames = [
        float(phase.get("maximumObservedFrameMilliseconds", 0.0))
        for phase in phases
    ]
    cold_starts = [
        float(phase.get("coldStartBatchMilliseconds", 0.0)) for phase in phases
    ]
    return {
        "completed": receipt.get("completed") is True,
        "strategy": receipt.get("strategy", ""),
        "elapsedMilliseconds": float(receipt.get("elapsedMilliseconds", 0.0)),
        "maximumObservedFrameMilliseconds": max(max_frames, default=0.0),
        "interactiveWarmupFrameSampleCount": sum(
            int(phase.get("warmupFrameTimes", {}).get("sampleCount", 0))
            for phase in phases
        ),
        "coldStartBatchMilliseconds": max(cold_starts, default=0.0),
        "preinteractiveBootstrapMilliseconds": sum(
            float(phase.get("preinteractiveBootstrapMilliseconds", 0.0))
            for phase in phases
        ),
        "budgetViolationCount": sum(
            int(phase.get("budgetViolationCount", 0)) for phase in phases
        ),
        "hardFrameBudgetMet": bool(phases)
        and all(phase.get("hardFrameBudgetMet") is True for phase in phases),
        "hardFrameBudgetScope": sorted(
            {
                phase.get("hardFrameBudgetGuaranteeScope", "unspecified")
                for phase in phases
            }
        ),
    }


def benchmark_metrics(receipt: dict[str, Any]) -> dict[str, Any]:
    frames = receipt.get("frameTimes", {})
    return {
        "completed": receipt.get("completed") is True,
        "mode": receipt.get("mode", ""),
        "sampleCount": int(frames.get("sampleCount", 0)),
        "p99Milliseconds": float(frames.get("p99Milliseconds", 0.0)),
        "maximumMilliseconds": float(frames.get("maximumMilliseconds", 0.0)),
        "hitchFrameCount": int(frames.get("hitchFrameCount", 0)),
    }


def presentmon_metrics(summary: dict[str, Any] | None) -> dict[str, Any]:
    if summary is None:
        return {
            "available": False,
            "p99PresentedMilliseconds": 0.0,
            "maximumPresentedMilliseconds": 0.0,
            "presentBudgetMet": False,
        }
    presented = summary.get("metrics", {}).get("presentedFrameMilliseconds", {})
    return {
        "available": int(presented.get("sampleCount", 0)) > 0,
        "p99PresentedMilliseconds": presented.get("p99Milliseconds"),
        "maximumPresentedMilliseconds": presented.get("maximumMilliseconds"),
        "presentBudgetMet": summary.get("budgetVerdict", {}).get(
            "presentMonPresentedFramesMet"
        )
        is True,
        "scope": summary.get("budgetVerdict", {}).get("scope", ""),
    }


def command_record(args: argparse.Namespace) -> int:
    definition = load(args.definition)
    hardware = find_by_id(definition["hardwareTargets"], args.hardware, "hardware")
    scene = find_by_id(definition["scenes"], args.scene, "scene")
    benchmark = load(args.benchmark)
    warmup = load(args.warmup)
    presentmon = None if args.presentmon is None else load(args.presentmon)
    windows = None if args.windows_manifest is None else load(args.windows_manifest)

    environment = warmup.get("environment") or benchmark.get("environment") or {}
    gpu_vendor = str(environment.get("graphicsDeviceVendor", ""))
    expected = hardware["vendor"].lower()
    accepted_names = {expected}
    if expected == "amd":
        accepted_names.add("ati")
    if not any(name in gpu_vendor.lower() for name in accepted_names):
        pnp_ids = [] if windows is None else [
            str(gpu.get("pnpDeviceId", ""))
            for gpu in windows.get("environment", {}).get("gpuDevices", [])
        ]
        if not any(hardware["pciVendorId"].lower() in value.lower() for value in pnp_ids):
            raise ValueError(
                f"Run GPU vendor '{gpu_vendor}' does not match target {hardware['id']}."
            )

    has_etl = False
    windows_complete = False
    verification = None
    if windows is not None:
        verification = validate_manifest(args.windows_manifest)
        windows_complete = verification["valid"]
        has_etl = verification["hasEtw"]
        for key, supplied in (("benchmarkReceipt", args.benchmark), ("warmupReceipt", args.warmup), ("presentMonSummary", args.presentmon)):
            if supplied is None or windows.get("artifacts", {}).get(key, {}).get("sha256") != sha256(supplied):
                raise ValueError(f"Supplied {key} does not match Windows capture.")
        if args.pipeline_revision != verification["buildIdentity"][0]:
            raise ValueError("Pipeline revision differs from captured build attestation.")

    result = {
        "schemaVersion": 1,
        "runId": args.run_id or datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S"),
        "recordedUtc": utc_now(),
        "matrixId": definition["matrixId"],
        "hardwareTarget": hardware["id"],
        "sceneTarget": scene["id"],
        "cacheState": args.cache_state,
        "pipelineRevision": args.pipeline_revision,
        "sceneRevision": scene["revision"],
        "environment": environment,
        "hardwareEnvironment": {} if windows is None else windows.get("environment", {}),
        "benchmark": benchmark_metrics(benchmark),
        "warmup": warmup_metrics(warmup),
        "presentMon": presentmon_metrics(presentmon),
        "evidence": {
            "systemVerification": verification,
            "windowsManifestComplete": windows_complete,
            "hasPresentMon": presentmon is not None,
            "hasEtw": has_etl,
            "sources": {
                "benchmark": source_record(args.benchmark),
                "warmup": source_record(args.warmup),
                "presentMon": source_record(args.presentmon),
                "windowsManifest": source_record(args.windows_manifest),
            },
        },
    }
    result["completed"] = (
        result["benchmark"]["completed"]
        and result["warmup"]["completed"]
        and (args.windows_manifest is None or windows_complete)
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(args.output.resolve())
    return 0


def median(runs: list[dict[str, Any]], path: tuple[str, str]) -> float:
    values = [float(run[path[0]][path[1]]) for run in runs]
    return statistics.median(values) if values else 0.0


def command_aggregate(args: argparse.Namespace) -> int:
    definition = load(args.definition)
    run_files = sorted(args.runs.rglob("*.matrix-run.json")) if args.runs.exists() else []
    runs = [load(path) for path in run_files]
    cells = []
    incomplete = False
    minimum = int(definition["minimumRepetitionsPerCell"])

    for hardware in definition["hardwareTargets"]:
        for scene in definition["scenes"]:
            matching = [
                run
                for run in runs
                if run.get("hardwareTarget") == hardware["id"]
                and run.get("sceneTarget") == scene["id"]
                and run.get("completed") is True
            ]
            historical_count = sum(not r.get("evidence", {}).get("systemVerification") for r in matching)
            formal = [r for r in matching if r.get("evidence", {}).get("systemVerification")]
            if formal:
                matching = formal
            present_count = sum(
                run.get("presentMon", {}).get("available") is True for run in matching
            )
            etw_count = sum(
                run.get("evidence", {}).get("hasEtw") is True for run in matching
            )
            reproducible = (
                len(matching) >= minimum
                and present_count == len(matching)
                and etw_count >= 1
            )
            # Old/publication receipts stay historical. Re-open the five raw manifests
            # on every aggregation so copied run JSON or subsequently tampered CSV
            # cannot upgrade a cell to reproducible.
            manifests = [Path(r.get("evidence", {}).get("systemVerification", {}).get("manifest", "__missing__"))
                         for r in matching if r.get("evidence", {}).get("systemVerification")]
            system_gate = validate_group(manifests)
            reproducible = reproducible and system_gate["completed"]
            if reproducible:
                status = "reproducible"
                reason = "requirements-met"
            elif matching:
                status = "provisional"
                reason = (
                    f"needs {max(0, minimum - len(matching))} more runs, "
                    f"{max(0, len(matching) - present_count)} PresentMon captures, "
                    f"and {max(0, 1 - etw_count)} ETL; " + "; ".join(system_gate["errors"])
                )
            else:
                status = "pending-hardware" if hardware["vendor"] != "AMD" else "pending-run"
                reason = "no completed public run"
            incomplete = incomplete or not reproducible
            cells.append(
                {
                    "hardwareTarget": hardware["id"],
                    "vendor": hardware["vendor"],
                    "sceneTarget": scene["id"],
                    "sceneKind": scene["kind"],
                    "status": status,
                    "reason": reason,
                    "runCount": len(matching),
                    "historicalEngineRunCount": historical_count,
                    "presentMonCount": present_count,
                    "etwCount": etw_count,
                    "medianBenchmarkP99Milliseconds": median(
                        matching, ("benchmark", "p99Milliseconds")
                    ),
                    "medianWarmupMaximumMilliseconds": median(
                        matching, ("warmup", "maximumObservedFrameMilliseconds")
                    ),
                    "medianPreinteractiveGateMilliseconds": median(
                        matching, ("warmup", "preinteractiveBootstrapMilliseconds")
                    ),
                    "allEngineBudgetsMet": bool(matching)
                    and all(run["warmup"]["hardFrameBudgetMet"] for run in matching),
                    "allPresentBudgetsMet": bool(matching)
                    and all(run["presentMon"]["presentBudgetMet"] for run in matching),
                    "runIds": [run["runId"] for run in matching],
                }
            )

    result = {
        "schemaVersion": 1,
        "matrixId": definition["matrixId"],
        "generatedUtc": utc_now(),
        "minimumRepetitionsPerCell": minimum,
        "sourceRunCount": len(runs),
        "complete": not incomplete,
        "cells": cells,
    }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "matrix.json").write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )

    fields = [
        "vendor",
        "hardwareTarget",
        "sceneTarget",
        "sceneKind",
        "status",
        "runCount",
        "presentMonCount",
        "etwCount",
        "medianBenchmarkP99Milliseconds",
        "medianWarmupMaximumMilliseconds",
        "medianPreinteractiveGateMilliseconds",
        "allEngineBudgetsMet",
        "allPresentBudgetsMet",
        "reason",
    ]
    with (args.output / "matrix.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(cells)

    markdown_rows = []
    for cell in cells:
        markdown_rows.append(
            f"| {cell['vendor']} | {cell['sceneTarget']} | {cell['status']} | "
            f"{cell['runCount']}/{minimum} | {cell['presentMonCount']} | "
            f"{cell['etwCount']} | {cell['medianBenchmarkP99Milliseconds']:.3f} | "
            f"{cell['medianWarmupMaximumMilliseconds']:.3f} | "
            f"{cell['medianPreinteractiveGateMilliseconds']:.3f} | {cell['reason']} |"
        )
    markdown = f"""# Public PSO reproduction matrix

Generated: `{result['generatedUtc']}`<br>
Matrix complete: **{result['complete']}**

| Vendor | Scene | Status | Runs | PresentMon | ETL | benchmark p99 ms | interactive warmup max ms | startup gate ms | Remaining evidence |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
{chr(10).join(markdown_rows)}

`reproducible` means at least {minimum} completed process-cold runs, PresentMon
for every run, and at least one WPR GPU ETL in the cell. Zeroes in pending rows
mean no measurement, not zero cost. A measured zero interactive-warmup maximum
with a nonzero startup gate means the required hot set completed before frame
presentation; it does not mean compilation was free.
"""
    (args.output / "matrix.md").write_text(markdown, encoding="utf-8")
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 2 if args.strict and incomplete else 0


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    record = subparsers.add_parser("record")
    record.add_argument("--definition", required=True, type=Path)
    record.add_argument("--hardware", required=True)
    record.add_argument("--scene", required=True)
    record.add_argument(
        "--cache-state",
        required=True,
        choices=("process-cold", "driver-warm", "clean-image"),
    )
    record.add_argument("--benchmark", required=True, type=Path)
    record.add_argument("--warmup", required=True, type=Path)
    record.add_argument("--presentmon", type=Path)
    record.add_argument("--windows-manifest", type=Path)
    record.add_argument("--pipeline-revision", default="working-tree")
    record.add_argument("--run-id")
    record.add_argument("--output", required=True, type=Path)
    record.set_defaults(handler=command_record)

    aggregate = subparsers.add_parser("aggregate")
    aggregate.add_argument("--definition", required=True, type=Path)
    aggregate.add_argument("--runs", required=True, type=Path)
    aggregate.add_argument("--output", required=True, type=Path)
    aggregate.add_argument("--strict", action="store_true")
    aggregate.set_defaults(handler=command_aggregate)

    args = parser.parse_args()
    return args.handler(args)


if __name__ == "__main__":
    raise SystemExit(main())
