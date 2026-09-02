#!/usr/bin/env python3
"""Validate cold, Unity-throughput, and scheduled PSO benchmark receipts."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any


ENVIRONMENT_KEYS = (
    "unityVersion",
    "productName",
    "applicationVersion",
    "runtimePlatform",
    "graphicsDeviceType",
    "graphicsDeviceName",
    "graphicsDeviceVendor",
    "graphicsDeviceVersion",
    "qualityLevelName",
)


def load_receipt(path: Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schemaVersion") not in (1, 2):
        raise ValueError(f"Unsupported receipt schema in {path}")
    if not data.get("completed"):
        raise ValueError(f"Benchmark did not complete: {path}: {data.get('error', '')}")
    samples = data.get("frameTimeSamplesMilliseconds") or []
    if not samples:
        raise ValueError(f"Benchmark has no raw frame samples: {path}")
    return data


def load_warmup_receipt(path: Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schemaVersion") != 2:
        raise ValueError(f"Unsupported warmup receipt schema in {path}")
    if not data.get("completed"):
        raise ValueError(f"Warmup did not complete: {path}: {data.get('error', '')}")
    return data


def summarize_warmup(
    receipt: dict[str, Any] | None,
    benchmark: dict[str, Any],
) -> dict[str, Any]:
    if receipt is None:
        return {"available": False, "valid": True}

    phases = receipt.get("phases") or []
    completed_states = sum(int(phase.get("completedGraphicsStates", 0)) for phase in phases)
    total_states = sum(int(phase.get("totalGraphicsStates", 0)) for phase in phases)
    phases_complete = bool(phases) and all(
        phase.get("completed")
        and int(phase.get("completedGraphicsStates", 0))
        == int(phase.get("totalGraphicsStates", 0))
        for phase in phases
    )
    feedback = receipt.get("cacheMissTrace") or {}
    feedback_requested = bool(feedback.get("requested"))
    feedback_armed = bool(feedback.get("armed"))
    feedback_ready = not feedback_requested or feedback_armed
    feedback_error = str(feedback.get("error") or "")
    cache_misses = int(feedback.get("cacheMissGraphicsStates", 0))
    plan_matches = receipt.get("planSha256") == benchmark.get("planSha256")
    valid = (
        phases_complete
        and plan_matches
        and feedback_ready
        and not feedback_error
        and cache_misses == 0
    )
    return {
        "available": True,
        "valid": valid,
        "strategy": receipt.get("strategy", ""),
        "workerCount": int(receipt.get("asyncPsoJobCount", -1)),
        "elapsedMilliseconds": float(receipt.get("elapsedMilliseconds", 0.0)),
        "completedGraphicsStates": completed_states,
        "totalGraphicsStates": total_states,
        "phasesComplete": phases_complete,
        "planMatchesBenchmark": plan_matches,
        "feedbackTraceRequested": feedback_requested,
        "feedbackTraceArmed": feedback_armed,
        "feedbackTraceScope": feedback.get("scope", ""),
        "feedbackBaselineGraphicsStates": int(
            feedback.get("baselineGraphicsStates", 0)
        ),
        "feedbackObservedGraphicsStates": int(
            feedback.get("observedGraphicsStates", 0)
        ),
        "cacheMissGraphicsStates": cache_misses,
        "feedbackCollectionContainsBaseline": bool(
            feedback.get("collectionContainsBaseline")
        ),
        "feedbackError": feedback_error,
    }


def improvement(baseline: float, optimized: float) -> float:
    return 0.0 if baseline == 0.0 else 100.0 * (baseline - optimized) / baseline


def build_report(
    baseline: dict[str, Any],
    optimized: dict[str, Any],
    plan: dict[str, Any] | None,
    naive: dict[str, Any] | None = None,
    naive_warmup: dict[str, Any] | None = None,
    optimized_warmup: dict[str, Any] | None = None,
) -> dict[str, Any]:
    baseline_environment = baseline["environment"]
    optimized_environment = optimized["environment"]
    compared_environments = [optimized_environment]
    if naive is not None:
        compared_environments.append(naive["environment"])
    mismatches = [
        key
        for key in ENVIRONMENT_KEYS
        if any(
            baseline_environment.get(key) != environment.get(key)
            for environment in compared_environments
        )
    ]

    names = {
        "meanMilliseconds": "mean",
        "p95Milliseconds": "p95",
        "p99Milliseconds": "p99",
        "maximumMilliseconds": "maximum",
    }
    metrics: dict[str, Any] = {}
    for source_name, report_name in names.items():
        cold = float(baseline["frameTimes"][source_name])
        warm = float(optimized["frameTimes"][source_name])
        metrics[report_name] = {
            "baselineMilliseconds": cold,
            "naiveMilliseconds": (
                None if naive is None else float(naive["frameTimes"][source_name])
            ),
            "optimizedMilliseconds": warm,
            "improvementPercent": improvement(cold, warm),
        }

    cold_hitches = int(baseline["frameTimes"]["hitchFrameCount"])
    naive_hitches = (
        None if naive is None else int(naive["frameTimes"]["hitchFrameCount"])
    )
    warm_hitches = int(optimized["frameTimes"]["hitchFrameCount"])
    hitch_threshold = float(baseline["frameTimes"]["hitchThresholdMilliseconds"])
    severe_hitch_threshold = max(16.67, hitch_threshold * 2.0)
    jitter_allowance = int(
        len(optimized["frameTimeSamplesMilliseconds"]) * 0.005
    )

    def count_at_or_above(receipt: dict[str, Any], threshold: float) -> int:
        return sum(
            float(value) >= threshold
            for value in receipt["frameTimeSamplesMilliseconds"]
        )

    cold_severe_hitches = count_at_or_above(baseline, severe_hitch_threshold)
    warm_severe_hitches = count_at_or_above(optimized, severe_hitch_threshold)
    naive_severe_hitches = (
        None if naive is None else count_at_or_above(naive, severe_hitch_threshold)
    )
    no_regression = (
        metrics["p95"]["optimizedMilliseconds"]
        <= metrics["p95"]["baselineMilliseconds"] * 1.05
        and metrics["maximum"]["optimizedMilliseconds"]
        <= metrics["maximum"]["baselineMilliseconds"] * 1.05
        and warm_hitches <= cold_hitches
    )
    naive_parity = True
    naive_parity_exact = True
    if naive is not None:
        naive_parity_exact = (
            metrics["p95"]["optimizedMilliseconds"]
            <= metrics["p95"]["naiveMilliseconds"] * 1.05
            and metrics["maximum"]["optimizedMilliseconds"]
            <= metrics["maximum"]["naiveMilliseconds"] * 1.10
            and warm_hitches <= int(naive_hitches)
        )
        naive_parity = (
            metrics["p95"]["optimizedMilliseconds"]
            <= metrics["p95"]["naiveMilliseconds"] * 1.05
            and metrics["maximum"]["optimizedMilliseconds"]
            <= max(
                metrics["maximum"]["naiveMilliseconds"] * 1.10,
                severe_hitch_threshold,
            )
            and warm_hitches <= int(naive_hitches) + jitter_allowance
            and warm_severe_hitches <= int(naive_severe_hitches)
        )
    material_improvement = (
        metrics["p95"]["improvementPercent"] >= 5.0
        or metrics["maximum"]["improvementPercent"] >= 5.0
        or warm_hitches < cold_hitches
    )

    phases = [] if plan is None else plan.get("phases", [])
    state_count = sum(int(phase.get("graphicsStateCount", 0)) for phase in phases)
    variant_count = sum(int(phase.get("variantCount", 0)) for phase in phases)
    warmup_evidence = {
        "naive": (
            {"available": False, "valid": True}
            if naive is None
            else summarize_warmup(naive_warmup, naive)
        ),
        "optimized": summarize_warmup(optimized_warmup, optimized),
    }
    warmup_evidence_valid = all(
        item["valid"] for item in warmup_evidence.values()
    )
    return {
        "schemaVersion": 2,
        "verdict": (
            "PASS"
            if not mismatches
            and no_regression
            and naive_parity
            and material_improvement
            and warmup_evidence_valid
            else "FAIL"
        ),
        "naiveParity": naive_parity,
        "naiveParityExact": naive_parity_exact,
        "parityPolicy": {
            "presentationHitchThresholdMilliseconds": hitch_threshold,
            "severeHitchThresholdMilliseconds": severe_hitch_threshold,
            "presentationJitterAllowanceFrames": jitter_allowance,
            "presentationJitterAllowancePercent": 0.5,
            "rationale": (
                "Report every missed presentation budget; permit at most 0.5% "
                "isolated non-severe capture/OS jitter while requiring zero "
                "regression in severe stalls."
            ),
        },
        "warmupEvidenceValid": warmup_evidence_valid,
        "environmentCompatible": not mismatches,
        "environmentMismatches": mismatches,
        "environment": baseline_environment,
        "baselineBuildGuid": baseline_environment.get("buildGuid", ""),
        "naiveBuildGuid": "" if naive is None else naive["environment"].get("buildGuid", ""),
        "optimizedBuildGuid": optimized_environment.get("buildGuid", ""),
        "sampleFrames": {
            "baseline": len(baseline["frameTimeSamplesMilliseconds"]),
            "naive": 0 if naive is None else len(naive["frameTimeSamplesMilliseconds"]),
            "optimized": len(optimized["frameTimeSamplesMilliseconds"]),
        },
        "hitchThresholdMilliseconds": baseline["frameTimes"]["hitchThresholdMilliseconds"],
        "hitches": {
            "baseline": cold_hitches,
            "naive": naive_hitches,
            "optimized": warm_hitches,
            "eliminated": cold_hitches - warm_hitches,
        },
        "severeHitches": {
            "thresholdMilliseconds": severe_hitch_threshold,
            "baseline": cold_severe_hitches,
            "naive": naive_severe_hitches,
            "optimized": warm_severe_hitches,
            "eliminated": cold_severe_hitches - warm_severe_hitches,
        },
        "metrics": metrics,
        "plan": {
            "profileId": "" if plan is None else plan.get("profileId", ""),
            "planSha256": "" if plan is None else plan.get("planSha256", ""),
            "variantCount": variant_count,
            "graphicsStateCount": state_count,
        },
        "warmupEvidence": warmup_evidence,
    }


def write_markdown(path: Path, report: dict[str, Any]) -> None:
    metrics = report["metrics"]
    hitches = report["hitches"]
    severe_hitches = report["severeHitches"]
    plan = report["plan"]
    rows = []
    for name in ("mean", "p95", "p99", "maximum"):
        item = metrics[name]
        naive_value = item.get("naiveMilliseconds")
        naive_text = "n/a" if naive_value is None else f"{naive_value:.3f} ms"
        rows.append(
            f"| {name} | {item['baselineMilliseconds']:.3f} ms | "
            f"{naive_text} | {item['optimizedMilliseconds']:.3f} ms | "
            f"{item['improvementPercent']:+.1f}% |"
        )
    naive_hitches = "n/a" if hitches.get("naive") is None else str(hitches["naive"])
    warmup = report["warmupEvidence"]["optimized"]
    warmup_text = (
        "not supplied"
        if not warmup["available"]
        else (
            f"{warmup['completedGraphicsStates']}/{warmup['totalGraphicsStates']} states, "
            f"plan-scoped feedback trace miss count {warmup['cacheMissGraphicsStates']}"
        )
    )
    text = f"""# Shader Hitch Pipeline A/B/C

Verdict: **{report['verdict']}**

| Metric | Cold baseline | Unity all-at-once | Deadline scheduled | Scheduled vs cold |
|---|---:|---:|---:|---:|
{chr(10).join(rows)}
| hitches ≥ {report['hitchThresholdMilliseconds']:.2f} ms | {hitches['baseline']} | {naive_hitches} | {hitches['optimized']} | {hitches['eliminated']} eliminated |
| severe stalls ≥ {severe_hitches['thresholdMilliseconds']:.2f} ms | {severe_hitches['baseline']} | {severe_hitches['naive']} | {severe_hitches['optimized']} | {severe_hitches['eliminated']} eliminated |

- Profile: `{plan['profileId']}`
- Plan content hash: `{plan['planSha256']}`
- Coverage: {plan['variantCount']} variants / {plan['graphicsStateCount']} graphics states
- Device: {report['environment']['graphicsDeviceName']} / {report['environment']['graphicsDeviceType']}
- Unity: {report['environment']['unityVersion']}
- Raw samples: {report['sampleFrames']['baseline']} cold + {report['sampleFrames']['naive']} all-at-once + {report['sampleFrames']['optimized']} scheduled frames
- Scheduled warmup evidence: {warmup_text}
- Parity policy: report every ≥ {report['hitchThresholdMilliseconds']:.2f} ms frame; allow at most {report['parityPolicy']['presentationJitterAllowanceFrames']} isolated non-severe frame(s), while severe-stall parity has zero allowance
- Exact zero-jitter parity: {report['naiveParityExact']}

The cold and prewarmed players are separate builds because the final build must embed the traced plan. Environment compatibility is checked independently of build GUID.
"""
    path.write_text(text, encoding="utf-8")


def render_gif(
    path: Path,
    png_path: Path,
    baseline: dict[str, Any],
    optimized: dict[str, Any],
    report: dict[str, Any],
    naive: dict[str, Any] | None = None,
) -> None:
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError as exception:
        raise RuntimeError(
            "GIF rendering requires Pillow. Run: python -m pip install -r Tools/requirements.txt"
        ) from exception

    width, height = 1120, 630
    cold = [float(value) for value in baseline["frameTimeSamplesMilliseconds"]]
    warm = [float(value) for value in optimized["frameTimeSamplesMilliseconds"]]
    naive_samples = (
        []
        if naive is None
        else [float(value) for value in naive["frameTimeSamplesMilliseconds"]]
    )
    sample_count = min(
        len(cold),
        len(warm),
        len(naive_samples) if naive_samples else len(cold),
    )
    threshold = float(report["hitchThresholdMilliseconds"])
    all_samples = cold + warm + naive_samples
    graph_max = max(12.0, math.ceil(max(all_samples) / 5.0) * 5.0)
    graph_rect = (58, 238, 1062, 535)

    font_candidates = (
        Path("C:/Windows/Fonts/segoeui.ttf"),
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
    )
    font_path = next((candidate for candidate in font_candidates if candidate.exists()), None)

    def font(size: int, bold: bool = False):
        if font_path is None:
            return ImageFont.load_default()
        selected = font_path
        if bold and "Windows" in str(font_path):
            bold_path = Path("C:/Windows/Fonts/segoeuib.ttf")
            if bold_path.exists():
                selected = bold_path
        return ImageFont.truetype(str(selected), size)

    title_font = font(30, True)
    subtitle_font = font(16)
    number_font = font(27, True)
    label_font = font(14)
    small_font = font(12)

    def frame(visible: int) -> Any:
        image = Image.new("RGB", (width, height), "#07101f")
        draw = ImageDraw.Draw(image)
        draw.rounded_rectangle((22, 18, width - 22, height - 18), 24, fill="#0b1729", outline="#1b3855", width=2)
        draw.text((48, 40), "Shader Hitch Pipeline · Measured D3D12 A/B/C", font=title_font, fill="#eaf5ff")
        device = report["environment"]["graphicsDeviceName"]
        draw.text((50, 82), f"{device} · Unity {report['environment']['unityVersion']} · {sample_count} frames per run", font=subtitle_font, fill="#7fa9c7")

        cards = (
            ("P95", "p95", 48),
            ("MAX", "maximum", 314),
            ("HITCHES", None, 580),
            ("PSO STATES", None, 846),
        )
        for label, metric, x in cards:
            draw.rounded_rectangle((x, 116, x + 226, 208), 14, fill="#10243b", outline="#214865")
            draw.text((x + 16, 130), label, font=label_font, fill="#79a8c9")
            if metric:
                cold_value = report["metrics"][metric]["baselineMilliseconds"]
                warm_value = report["metrics"][metric]["optimizedMilliseconds"]
                value = f"{cold_value:.1f} → {warm_value:.1f} ms"
                detail = f"{report['metrics'][metric]['improvementPercent']:.1f}% lower"
            elif label == "HITCHES":
                middle = report["hitches"].get("naive")
                value = (
                    f"{report['hitches']['baseline']} → {report['hitches']['optimized']}"
                    if middle is None
                    else f"{report['hitches']['baseline']} → {middle} → {report['hitches']['optimized']}"
                )
                detail = f"≥ {threshold:.2f} ms"
            else:
                value = str(report["plan"]["graphicsStateCount"])
                detail = f"{report['plan']['variantCount']} variants"
            draw.text((x + 16, 153), value, font=number_font, fill="#eef9ff")
            draw.text((x + 16, 188), detail, font=small_font, fill="#57d6ae")

        left, top, right, bottom = graph_rect
        draw.rounded_rectangle((left - 14, top - 12, right + 14, bottom + 34), 14, fill="#081421", outline="#193850")
        for tick in range(0, 5):
            value = graph_max * tick / 4.0
            y = bottom - (bottom - top) * tick / 4.0
            draw.line((left, y, right, y), fill="#193047", width=1)
            draw.text((12, y - 7), f"{value:.0f}", font=small_font, fill="#62849e")
        threshold_y = bottom - (bottom - top) * min(1.0, threshold / graph_max)
        draw.line((left, threshold_y, right, threshold_y), fill="#a84949", width=2)

        def point(index: int, value: float) -> tuple[float, float]:
            x = left + (right - left) * index / max(1, sample_count - 1)
            y = bottom - (bottom - top) * min(graph_max, value) / graph_max
            return x, y

        visible = max(2, min(sample_count, visible))
        cold_points = [point(i, cold[i]) for i in range(visible)]
        warm_points = [point(i, warm[i]) for i in range(visible)]
        draw.line(cold_points, fill="#ff665e", width=3, joint="curve")
        if naive_samples:
            naive_points = [point(i, naive_samples[i]) for i in range(visible)]
            draw.line(naive_points, fill="#ffb83d", width=3, joint="curve")
        draw.line(warm_points, fill="#36d9ff", width=3, joint="curve")
        cursor_x = point(visible - 1, 0)[0]
        draw.line((cursor_x, top, cursor_x, bottom), fill="#d8f4ff", width=1)
        draw.text((left, bottom + 11), "COLD / FIRST USE", font=label_font, fill="#ff665e")
        draw.text((left + 190, bottom + 11), "UNITY ALL-AT-ONCE", font=label_font, fill="#ffb83d")
        draw.text((left + 390, bottom + 11), "DEADLINE SCHEDULED", font=label_font, fill="#36d9ff")
        draw.text((right - 280, bottom + 11), f"Verdict: {report['verdict']}", font=label_font, fill="#57d6ae")
        draw.text((50, 592), "Measured frame-time samples; no artificial sleeps or simulated stalls.", font=small_font, fill="#6f91aa")
        return image

    frames = []
    animation_frames = 72
    for index in range(animation_frames):
        visible = 2 + int((sample_count - 2) * (index + 1) / animation_frames)
        frames.append(frame(visible))
    final_frame = frame(sample_count)
    final_frame.save(png_path)
    frames.extend([final_frame.copy() for _ in range(12)])
    frames[0].save(
        path,
        save_all=True,
        append_images=frames[1:],
        duration=80,
        loop=0,
        optimize=True,
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--naive", type=Path)
    parser.add_argument("--optimized", required=True, type=Path)
    parser.add_argument("--naive-warmup", type=Path)
    parser.add_argument("--optimized-warmup", type=Path)
    parser.add_argument("--plan", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--no-gif", action="store_true")
    args = parser.parse_args()

    baseline = load_receipt(args.baseline)
    optimized = load_receipt(args.optimized)
    naive = None if args.naive is None else load_receipt(args.naive)
    naive_warmup = (
        None if args.naive_warmup is None else load_warmup_receipt(args.naive_warmup)
    )
    optimized_warmup = (
        None
        if args.optimized_warmup is None
        else load_warmup_receipt(args.optimized_warmup)
    )
    plan = None if args.plan is None else json.loads(args.plan.read_text(encoding="utf-8"))
    report = build_report(
        baseline,
        optimized,
        plan,
        naive,
        naive_warmup,
        optimized_warmup,
    )
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "comparison.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    write_markdown(args.output / "comparison.md", report)
    if not args.no_gif:
        render_gif(
            args.output / "comparison.gif",
            args.output / "comparison.png",
            baseline,
            optimized,
            report,
            naive,
        )
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0 if report["verdict"] == "PASS" else 2


if __name__ == "__main__":
    raise SystemExit(main())
