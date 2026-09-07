#!/usr/bin/env python3
"""Summarize PresentMon frame CSV and correlate it with a PSO warmup receipt."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Iterable


METRIC_ALIASES = {
    "presentedFrameMilliseconds": (
        "FrameTime",
        "MsBetweenAppStart",
        "MsBetweenPresents",
        "MsBetweenSimulationStart",
    ),
    "displayedFrameMilliseconds": ("DisplayedTime", "MsBetweenDisplayChange"),
    "cpuBusyMilliseconds": ("CPUBusy", "MsCPUBusy"),
    "cpuWaitMilliseconds": ("CPUWait", "MsCPUWait"),
    "gpuLatencyMilliseconds": ("GPULatency", "MsGPULatency", "MsUntilRenderStart"),
    "gpuTimeMilliseconds": ("GPUTime", "MsGPUTime", "MsGPUDuration"),
    "gpuBusyMilliseconds": ("GPUBusy", "MsGPUBusy", "MsGPUActive"),
    "gpuWaitMilliseconds": ("GPUWait", "MsGPUWait"),
    "displayLatencyMilliseconds": ("DisplayLatency", "MsUntilDisplayed"),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def normalize(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.lower())


def select_column(fieldnames: Iterable[str], aliases: Iterable[str]) -> str | None:
    normalized = {normalize(name): name for name in fieldnames}
    for alias in aliases:
        selected = normalized.get(normalize(alias))
        if selected is not None:
            return selected
    return None


def parse_number(value: Any) -> float | None:
    if value is None:
        return None
    text = str(value).strip()
    if not text or text.upper() in {"NA", "N/A", "NAN", "INF", "-INF"}:
        return None
    try:
        result = float(text)
    except ValueError:
        return None
    return result if math.isfinite(result) else None


def percentile(values: list[float], quantile: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    position = (len(ordered) - 1) * quantile
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return ordered[lower]
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower)


def metric_summary(values: list[float], target_ms: float) -> dict[str, Any]:
    if not values:
        return {
            "sampleCount": 0,
            "meanMilliseconds": 0.0,
            "p50Milliseconds": 0.0,
            "p95Milliseconds": 0.0,
            "p99Milliseconds": 0.0,
            "maximumMilliseconds": 0.0,
            "overBudgetCount": 0,
            "overBudgetPercent": 0.0,
        }
    over = sum(value > target_ms for value in values)
    return {
        "sampleCount": len(values),
        "meanMilliseconds": sum(values) / len(values),
        "p50Milliseconds": percentile(values, 0.50),
        "p95Milliseconds": percentile(values, 0.95),
        "p99Milliseconds": percentile(values, 0.99),
        "maximumMilliseconds": max(values),
        "overBudgetCount": over,
        "overBudgetPercent": over * 100.0 / len(values),
    }


def parse_utc(value: str | None) -> datetime | None:
    if not value:
        return None
    text = value.strip().replace("Z", "+00:00")
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def parse_presentmon_time(value: str, offset_minutes: int) -> datetime | None:
    text = value.strip()
    if not text:
        return None
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError:
        return None
    local_zone = timezone(timedelta(minutes=offset_minutes))
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=local_zone)
    return parsed.astimezone(timezone.utc)


def dominant_swap_chain(rows: list[dict[str, str]], column: str | None) -> tuple[str, list[dict[str, str]]]:
    if column is None or not rows:
        return "", rows
    counts: dict[str, int] = {}
    for row in rows:
        key = row.get(column, "")
        counts[key] = counts.get(key, 0) + 1
    selected = max(counts, key=counts.get)
    return selected, [row for row in rows if row.get(column, "") == selected]


def load_warmup_window(path: Path | None) -> tuple[dict[str, Any] | None, datetime | None, datetime | None, float | None]:
    if path is None:
        return None, None, None, None
    receipt = json.loads(path.read_text(encoding="utf-8-sig"))
    targets = [
        float(phase.get("hardFrameBudgetMilliseconds", 0.0))
        for phase in receipt.get("phases", [])
        if float(phase.get("hardFrameBudgetMilliseconds", 0.0)) > 0.0
    ]
    return (
        receipt,
        parse_utc(receipt.get("startedUtc")),
        parse_utc(receipt.get("endedUtc")),
        min(targets) if targets else None,
    )


def build_summary(
    csv_path: Path,
    warmup_path: Path | None,
    target_ms: float | None,
    local_offset_minutes: int,
) -> dict[str, Any]:
    with csv_path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        if not reader.fieldnames:
            raise ValueError("PresentMon CSV has no header.")
        fieldnames = list(reader.fieldnames)
        rows = list(reader)
    total_csv_rows = len(rows)

    warmup, warmup_start, warmup_end, receipt_target = load_warmup_window(warmup_path)
    effective_target = target_ms or receipt_target or 16.67
    columns = {
        key: select_column(fieldnames, aliases)
        for key, aliases in METRIC_ALIASES.items()
    }
    swap_column = select_column(fieldnames, ("SwapChainAddress",))
    selected_swap, rows = dominant_swap_chain(rows, swap_column)
    time_column = select_column(fieldnames, ("CPUStartDateTime", "TimeInDateTime"))

    warmup_rows: list[dict[str, str]] = []
    if time_column and warmup_start and warmup_end:
        for row in rows:
            row_time = parse_presentmon_time(
                row.get(time_column, ""), local_offset_minutes
            )
            if row_time is not None and warmup_start <= row_time <= warmup_end:
                warmup_rows.append(row)

    analysis_rows = warmup_rows or rows
    metrics: dict[str, Any] = {}
    for key, column in columns.items():
        values = [] if column is None else [
            value
            for row in analysis_rows
            if (value := parse_number(row.get(column))) is not None and value >= 0.0
        ]
        metrics[key] = metric_summary(values, effective_target)
        metrics[key]["sourceColumn"] = column or ""

    frame_column = columns["presentedFrameMilliseconds"]
    tails: list[dict[str, Any]] = []
    if frame_column:
        ranked = sorted(
            analysis_rows,
            key=lambda row: parse_number(row.get(frame_column)) or -1.0,
            reverse=True,
        )[:5]
        for row in ranked:
            tail = {
                "cpuStart": row.get(time_column, "") if time_column else "",
                "presentedFrameMilliseconds": parse_number(row.get(frame_column)),
            }
            for key in (
                "displayedFrameMilliseconds",
                "cpuBusyMilliseconds",
                "cpuWaitMilliseconds",
                "gpuLatencyMilliseconds",
                "gpuTimeMilliseconds",
                "gpuBusyMilliseconds",
                "gpuWaitMilliseconds",
                "displayLatencyMilliseconds",
            ):
                column = columns[key]
                tail[key] = None if column is None else parse_number(row.get(column))
            tails.append(tail)

    phase_receipts = [] if warmup is None else warmup.get("phases", [])
    engine_budget_met = bool(phase_receipts) and all(
        phase.get("hardFrameBudgetMet") is True for phase in phase_receipts
    )
    frame_metric = metrics["presentedFrameMilliseconds"]
    result = {
        "schemaVersion": 1,
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "source": {
            "presentMonCsv": str(csv_path.resolve()),
            "presentMonCsvSha256": sha256(csv_path),
            "warmupReceipt": "" if warmup_path is None else str(warmup_path.resolve()),
            "warmupReceiptSha256": "" if warmup_path is None else sha256(warmup_path),
        },
        "targetFrameMilliseconds": effective_target,
        "rowSelection": {
            "totalCsvRows": total_csv_rows,
            "dominantSwapChain": selected_swap,
            "warmupWindowApplied": bool(warmup_rows),
            "analyzedRows": len(analysis_rows),
            "timeColumn": time_column or "",
            "localUtcOffsetMinutes": local_offset_minutes,
        },
        "metrics": metrics,
        "tailFrames": tails,
        "budgetVerdict": {
            "engineWarmupReceiptMet": engine_budget_met,
            "presentMonPresentedFramesMet": (
                frame_metric["sampleCount"] > 0
                and frame_metric["overBudgetCount"] == 0
            ),
            "scope": "dominant-swap-chain/warmup-window"
            if warmup_rows
            else "dominant-swap-chain/full-capture",
        },
        "notes": [
            "FrameTime/MsBetweenAppStart measures CPU-present cadence.",
            "DisplayedTime/MsBetweenDisplayChange measures actual display cadence when available.",
            "A PresentMon row is evidence of correlation, not proof that a single subsystem caused the tail.",
        ],
    }
    return result


def write_markdown(path: Path, summary: dict[str, Any]) -> None:
    metrics = summary["metrics"]
    rows = []
    for label, key in (
        ("Presented cadence", "presentedFrameMilliseconds"),
        ("Displayed cadence", "displayedFrameMilliseconds"),
        ("CPU busy", "cpuBusyMilliseconds"),
        ("GPU busy", "gpuBusyMilliseconds"),
        ("Display latency", "displayLatencyMilliseconds"),
    ):
        item = metrics[key]
        rows.append(
            f"| {label} | {item['sourceColumn'] or 'unavailable'} | "
            f"{item['sampleCount']} | {item['p99Milliseconds']:.3f} | "
            f"{item['maximumMilliseconds']:.3f} | {item['overBudgetCount']} |"
        )
    verdict = summary["budgetVerdict"]
    text = f"""# PresentMon / ETW frame evidence

Target: **{summary['targetFrameMilliseconds']:.3f} ms**<br>
Scope: `{verdict['scope']}`<br>
Engine receipt met: **{verdict['engineWarmupReceiptMet']}**<br>
Present cadence met: **{verdict['presentMonPresentedFramesMet']}**

| Metric | CSV column | Samples | p99 ms | max ms | over budget |
|---|---|---:|---:|---:|---:|
{chr(10).join(rows)}

The analyzer selects the dominant swap chain and, when timestamp correlation is
possible, clips rows to the warmup receipt's UTC interval. Raw CSV and receipt
SHA-256 hashes are retained in the JSON summary.
"""
    path.write_text(text, encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--presentmon-csv", required=True, type=Path)
    parser.add_argument("--warmup-receipt", type=Path)
    parser.add_argument("--target-ms", type=float)
    parser.add_argument("--local-utc-offset-minutes", type=int, default=0)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    summary = build_summary(
        args.presentmon_csv,
        args.warmup_receipt,
        args.target_ms,
        args.local_utc_offset_minutes,
    )
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "presentmon-summary.json").write_text(
        json.dumps(summary, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    write_markdown(args.output / "presentmon-summary.md", summary)
    print(json.dumps(summary, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
