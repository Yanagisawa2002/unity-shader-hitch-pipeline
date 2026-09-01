#!/usr/bin/env python3
"""Validate two PSO benchmark receipts and render a measured A/B report."""

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
    if data.get("schemaVersion") != 1:
        raise ValueError(f"Unsupported receipt schema in {path}")
    if not data.get("completed"):
        raise ValueError(f"Benchmark did not complete: {path}: {data.get('error', '')}")
    samples = data.get("frameTimeSamplesMilliseconds") or []
    if not samples:
        raise ValueError(f"Benchmark has no raw frame samples: {path}")
    return data


def improvement(baseline: float, optimized: float) -> float:
    return 0.0 if baseline == 0.0 else 100.0 * (baseline - optimized) / baseline


def build_report(
    baseline: dict[str, Any],
    optimized: dict[str, Any],
    plan: dict[str, Any] | None,
) -> dict[str, Any]:
    baseline_environment = baseline["environment"]
    optimized_environment = optimized["environment"]
    mismatches = [
        key
        for key in ENVIRONMENT_KEYS
        if baseline_environment.get(key) != optimized_environment.get(key)
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
            "optimizedMilliseconds": warm,
            "improvementPercent": improvement(cold, warm),
        }

    cold_hitches = int(baseline["frameTimes"]["hitchFrameCount"])
    warm_hitches = int(optimized["frameTimes"]["hitchFrameCount"])
    no_regression = (
        metrics["p95"]["optimizedMilliseconds"]
        <= metrics["p95"]["baselineMilliseconds"] * 1.05
        and metrics["maximum"]["optimizedMilliseconds"]
        <= metrics["maximum"]["baselineMilliseconds"] * 1.05
        and warm_hitches <= cold_hitches
    )
    material_improvement = (
        metrics["p95"]["improvementPercent"] >= 5.0
        or metrics["maximum"]["improvementPercent"] >= 5.0
        or warm_hitches < cold_hitches
    )

    phases = [] if plan is None else plan.get("phases", [])
    state_count = sum(int(phase.get("graphicsStateCount", 0)) for phase in phases)
    variant_count = sum(int(phase.get("variantCount", 0)) for phase in phases)
    return {
        "schemaVersion": 1,
        "verdict": "PASS" if not mismatches and no_regression and material_improvement else "FAIL",
        "environmentCompatible": not mismatches,
        "environmentMismatches": mismatches,
        "environment": baseline_environment,
        "baselineBuildGuid": baseline_environment.get("buildGuid", ""),
        "optimizedBuildGuid": optimized_environment.get("buildGuid", ""),
        "sampleFrames": {
            "baseline": len(baseline["frameTimeSamplesMilliseconds"]),
            "optimized": len(optimized["frameTimeSamplesMilliseconds"]),
        },
        "hitchThresholdMilliseconds": baseline["frameTimes"]["hitchThresholdMilliseconds"],
        "hitches": {
            "baseline": cold_hitches,
            "optimized": warm_hitches,
            "eliminated": cold_hitches - warm_hitches,
        },
        "metrics": metrics,
        "plan": {
            "profileId": "" if plan is None else plan.get("profileId", ""),
            "planSha256": "" if plan is None else plan.get("planSha256", ""),
            "variantCount": variant_count,
            "graphicsStateCount": state_count,
        },
    }


def write_markdown(path: Path, report: dict[str, Any]) -> None:
    metrics = report["metrics"]
    hitches = report["hitches"]
    plan = report["plan"]
    rows = []
    for name in ("mean", "p95", "p99", "maximum"):
        item = metrics[name]
        rows.append(
            f"| {name} | {item['baselineMilliseconds']:.3f} ms | "
            f"{item['optimizedMilliseconds']:.3f} ms | "
            f"{item['improvementPercent']:+.1f}% |"
        )
    text = f"""# Shader Hitch Pipeline A/B

Verdict: **{report['verdict']}**

| Metric | Cold baseline | Prewarmed | Improvement |
|---|---:|---:|---:|
{chr(10).join(rows)}
| hitches ≥ {report['hitchThresholdMilliseconds']:.2f} ms | {hitches['baseline']} | {hitches['optimized']} | {hitches['eliminated']} eliminated |

- Profile: `{plan['profileId']}`
- Plan content hash: `{plan['planSha256']}`
- Coverage: {plan['variantCount']} variants / {plan['graphicsStateCount']} graphics states
- Device: {report['environment']['graphicsDeviceName']} / {report['environment']['graphicsDeviceType']}
- Unity: {report['environment']['unityVersion']}
- Raw samples: {report['sampleFrames']['baseline']} cold + {report['sampleFrames']['optimized']} prewarmed frames

The cold and prewarmed players are separate builds because the final build must embed the traced plan. Environment compatibility is checked independently of build GUID.
"""
    path.write_text(text, encoding="utf-8")


def render_gif(
    path: Path,
    png_path: Path,
    baseline: dict[str, Any],
    optimized: dict[str, Any],
    report: dict[str, Any],
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
    sample_count = min(len(cold), len(warm))
    threshold = float(report["hitchThresholdMilliseconds"])
    graph_max = max(12.0, math.ceil(max(max(cold), max(warm)) / 5.0) * 5.0)
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
        draw.text((48, 40), "Shader Hitch Pipeline · Measured D3D12 A/B", font=title_font, fill="#eaf5ff")
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
                value = f"{report['hitches']['baseline']} → {report['hitches']['optimized']}"
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
        draw.line(warm_points, fill="#36d9ff", width=3, joint="curve")
        cursor_x = point(visible - 1, 0)[0]
        draw.line((cursor_x, top, cursor_x, bottom), fill="#d8f4ff", width=1)
        draw.text((left, bottom + 11), "COLD / FIRST USE", font=label_font, fill="#ff665e")
        draw.text((left + 190, bottom + 11), "PREWARMED", font=label_font, fill="#36d9ff")
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
    parser.add_argument("--optimized", required=True, type=Path)
    parser.add_argument("--plan", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--no-gif", action="store_true")
    args = parser.parse_args()

    baseline = load_receipt(args.baseline)
    optimized = load_receipt(args.optimized)
    plan = None if args.plan is None else json.loads(args.plan.read_text(encoding="utf-8"))
    report = build_report(baseline, optimized, plan)
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
        )
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0 if report["verdict"] == "PASS" else 2


if __name__ == "__main__":
    raise SystemExit(main())
