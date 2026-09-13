#!/usr/bin/env python3
"""Validate the portfolio-specific 12-second reveal visual contract.

This gate is deliberately stricter and more legible than the general benchmark
parity gate: cold must contain a naturally visible stall and scheduled must not
miss a 60 FPS presentation deadline. It consumes retained receipts only; it
never launches Unity or modifies driver caches.
"""

from __future__ import annotations

import argparse
import json
import math
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object in {path}")
    return value


def frame_samples(receipt: dict[str, Any]) -> list[float]:
    samples = receipt.get("frameTimeSamplesMilliseconds", [])
    if not isinstance(samples, list):
        raise ValueError("frameTimeSamplesMilliseconds must be an array")
    return [float(value) for value in samples]


def scenario_samples(receipt: dict[str, Any]) -> list[tuple[float, float]]:
    samples = receipt.get("frameSamples", [])
    if not isinstance(samples, list):
        raise ValueError("frameSamples must be an array")
    result: list[tuple[float, float]] = []
    for index, sample in enumerate(samples):
        if not isinstance(sample, dict):
            raise ValueError(f"frameSamples[{index}] must be an object")
        result.append(
            (
                float(sample.get("elapsedSeconds", -1.0)),
                float(sample.get("milliseconds", 0.0)),
            )
        )
    return result


def maximum_frame(receipt: dict[str, Any], samples: list[float]) -> float:
    statistics = receipt.get("frameTimes", {})
    if isinstance(statistics, dict) and "maximumMilliseconds" in statistics:
        return float(statistics["maximumMilliseconds"])
    return max(samples, default=0.0)


def find_phase(receipt: dict[str, Any], phase_name: str) -> dict[str, Any] | None:
    phases = receipt.get("phases", [])
    if not isinstance(phases, list):
        return None
    for phase in phases:
        if isinstance(phase, dict) and str(phase.get("phase", "")).casefold() == phase_name.casefold():
            return phase
    return None


def validate_scenario_coverage(
    name: str,
    samples: list[tuple[float, float]],
    failures: list[str],
) -> None:
    if not samples:
        return
    previous_elapsed = -1.0
    for index, (elapsed, milliseconds) in enumerate(samples):
        if not math.isfinite(elapsed) or not math.isfinite(milliseconds):
            failures.append(f"{name} scenario sample {index} is not finite")
            return
        if elapsed < previous_elapsed:
            failures.append(f"{name} scenario sample timestamps are not monotonic")
            return
        if elapsed < 0.0 or milliseconds < 0.0:
            failures.append(f"{name} scenario sample {index} contains a negative value")
            return
        previous_elapsed = elapsed
    if samples[0][0] > 0.25:
        failures.append(
            f"{name} scenario starts at {samples[0][0]:.3f} s instead of workload start"
        )
    if samples[-1][0] < 12.0:
        failures.append(
            f"{name} scenario ends at {samples[-1][0]:.3f} s before the 12-second boundary"
        )
    if samples[-1][0] > 12.25:
        failures.append(
            f"{name} scenario retains samples beyond the 12.25-second evidence limit"
        )


def validate_native_frame_timing(
    name: str,
    receipt: dict[str, Any],
    failures: list[str],
) -> tuple[int, int]:
    raw_samples = receipt.get("frameSamples", [])
    if not isinstance(raw_samples, list):
        return 0, 0
    exact_samples = [
        sample
        for sample in raw_samples
        if isinstance(sample, dict)
        and 0.0 <= float(sample.get("elapsedSeconds", -1.0)) <= 12.25
    ]
    available = [
        sample for sample in exact_samples if bool(sample.get("nativeTimingAvailable"))
    ]
    reported_count = int(receipt.get("frameTimingSampleCount", -1))
    if reported_count != len(available):
        failures.append(
            f"{name} native frame-timing count does not match its raw samples"
        )
    minimum_count = math.ceil(len(exact_samples) * 0.95)
    if len(available) < minimum_count:
        failures.append(
            f"{name} native frame-timing coverage is {len(available)}/"
            f"{len(exact_samples)}; expected at least 95%"
        )

    fields = {
        "cpuTotalMilliseconds": "maximumCpuTotalMilliseconds",
        "cpuMainThreadMilliseconds": "maximumCpuMainThreadMilliseconds",
        "cpuMainThreadPresentWaitMilliseconds": (
            "maximumCpuMainThreadPresentWaitMilliseconds"
        ),
        "cpuRenderThreadMilliseconds": "maximumCpuRenderThreadMilliseconds",
        "gpuMilliseconds": "maximumGpuMilliseconds",
    }
    for sample_field, receipt_field in fields.items():
        values: list[float] = []
        for index, sample in enumerate(available):
            value = float(sample.get(sample_field, -1.0))
            if not math.isfinite(value) or value < 0.0:
                failures.append(
                    f"{name} native timing sample {index} has invalid {sample_field}"
                )
                values = []
                break
            values.append(value)
        if not values:
            continue
        reported = float(receipt.get(receipt_field, -1.0))
        calculated = max(values)
        if not math.isfinite(reported) or abs(reported - calculated) > 0.01:
            failures.append(
                f"{name} {receipt_field} does not match its raw native timings"
            )
    return len(available), len(exact_samples)


def validate(args: argparse.Namespace) -> tuple[dict[str, Any], list[str]]:
    baseline = load_json(args.baseline)
    naive = load_json(args.naive)
    scheduled = load_json(args.scheduled)
    scheduled_warmup = load_json(args.scheduled_warmup)
    baseline_scenario = load_json(args.baseline_scenario)
    naive_scenario = load_json(args.naive_scenario)
    scheduled_scenario = load_json(args.scheduled_scenario)

    failures: list[str] = []
    for name, receipt in (
        ("baseline", baseline),
        ("naive", naive),
        ("scheduled", scheduled),
        ("scheduled warmup", scheduled_warmup),
    ):
        if not bool(receipt.get("completed")):
            failures.append(f"{name} receipt is incomplete: {receipt.get('error', '')}")

    baseline_samples = frame_samples(baseline)
    naive_samples = frame_samples(naive)
    scheduled_samples = frame_samples(scheduled)
    for name, samples in (
        ("baseline", baseline_samples),
        ("naive", naive_samples),
        ("scheduled", scheduled_samples),
    ):
        if any(not math.isfinite(value) or value < 0.0 for value in samples):
            failures.append(f"{name} benchmark contains an invalid frame sample")
    baseline_scenario_samples = scenario_samples(baseline_scenario)
    naive_scenario_samples = scenario_samples(naive_scenario)
    scheduled_scenario_samples = scenario_samples(scheduled_scenario)
    for name, timed_samples in (
        ("baseline", baseline_scenario_samples),
        ("naive", naive_scenario_samples),
        ("scheduled", scheduled_scenario_samples),
    ):
        validate_scenario_coverage(name, timed_samples, failures)
    if not baseline_samples or not scheduled_samples:
        failures.append("baseline and scheduled receipts must retain raw frame samples")
    if not baseline_scenario_samples or not scheduled_scenario_samples:
        failures.append("baseline and scheduled scenario receipts must retain timed samples")
    for name, benchmark, timed_samples, raw_samples in (
        ("baseline", baseline, baseline_scenario_samples, baseline_samples),
        ("naive", naive, naive_scenario_samples, naive_samples),
        ("scheduled", scheduled, scheduled_scenario_samples, scheduled_samples),
    ):
        if not bool(benchmark.get("scenarioMeasurementGated")):
            failures.append(
                f"{name} benchmark was not synchronized to the scenario gate"
            )
        actual_samples = int(benchmark.get("actualSampleFrames", -1))
        if actual_samples != len(raw_samples):
            failures.append(
                f"{name} benchmark actual-sample count does not match its array"
            )
        measured_seconds = float(benchmark.get("measurementDurationSeconds", -1.0))
        if not math.isfinite(measured_seconds) or abs(measured_seconds - 12.0) > 0.25:
            failures.append(
                f"{name} benchmark measured {measured_seconds:.3f} s instead of 12 s"
            )
        # Controller execution order can include either the opening or closing
        # boundary frame. It may differ from the scenario sidecar by one frame,
        # but never by a frame-count-sized surrogate for elapsed time.
        if timed_samples and abs(len(timed_samples) - len(raw_samples)) > 2:
            failures.append(
                f"{name} benchmark/scenario gated sample counts diverge "
                f"({len(raw_samples)} vs {len(timed_samples)})"
            )

    baseline_maximum = maximum_frame(baseline, baseline_samples)
    naive_maximum = maximum_frame(naive, naive_samples)
    scheduled_maximum = maximum_frame(scheduled, scheduled_samples)
    reveal_start = float(baseline_scenario.get("contentRevealSeconds", 0.0))
    reveal_end = float(baseline_scenario.get("revealWindowEndSeconds", 0.0))
    baseline_reveal_window_maximum = max(
        (
            milliseconds
            for elapsed, milliseconds in baseline_scenario_samples
            if reveal_start <= elapsed <= reveal_end
        ),
        default=0.0,
    )
    baseline_first_post_reveal = float(
        baseline_scenario.get("firstPostRevealFrameMilliseconds", 0.0)
    )
    baseline_reveal_maximum = max(
        baseline_reveal_window_maximum,
        baseline_first_post_reveal,
    )
    scheduled_scenario_maximum = max(
        (
            milliseconds
            for elapsed, milliseconds in scheduled_scenario_samples
            if elapsed <= 12.25
        ),
        default=0.0,
    )
    scheduled_scenario_missed_frames = sum(
        milliseconds >= args.scheduled_threshold_ms
        for elapsed, milliseconds in scheduled_scenario_samples
        if elapsed <= 12.25
    )
    baseline_visible_stalls = sum(
        milliseconds >= args.baseline_min_ms
        for elapsed, milliseconds in baseline_scenario_samples
        if reveal_start <= elapsed <= reveal_end
    )
    if baseline_visible_stalls == 0 and baseline_first_post_reveal >= args.baseline_min_ms:
        baseline_visible_stalls = 1
    scheduled_missed_frames = sum(
        value >= args.scheduled_threshold_ms for value in scheduled_samples
    )
    naive_missed_frames = sum(
        value >= args.scheduled_threshold_ms for value in naive_samples
    )

    for name, receipt, samples, reported_maximum in (
        ("baseline", baseline, baseline_samples, baseline_maximum),
        ("naive", naive, naive_samples, naive_maximum),
        ("scheduled", scheduled, scheduled_samples, scheduled_maximum),
    ):
        calculated_maximum = max(samples, default=0.0)
        if not math.isfinite(reported_maximum) or abs(
            reported_maximum - calculated_maximum
        ) > 0.01:
            failures.append(
                f"{name} benchmark maximum does not match its raw samples"
            )

    expected_scenario_modes = {
        "baseline": "baseline",
        "naive": "throughput",
        "scheduled": "scheduled",
    }
    native_timing_coverage: dict[str, tuple[int, int]] = {}
    for name, scenario_receipt, timed_samples in (
        ("baseline", baseline_scenario, baseline_scenario_samples),
        ("naive", naive_scenario, naive_scenario_samples),
        ("scheduled", scheduled_scenario, scheduled_scenario_samples),
    ):
        if not bool(scenario_receipt.get("completed")):
            failures.append(f"{name} {args.scenario} scenario receipt is incomplete")
        if (
            str(scenario_receipt.get("scenario", "")).casefold()
            != args.scenario.casefold()
        ):
            failures.append(f"{name} scenario receipt identifies the wrong scenario")
        if str(scenario_receipt.get("mode", "")).casefold() != expected_scenario_modes[name]:
            failures.append(f"{name} scenario receipt has the wrong execution mode")
        if str(scenario_receipt.get("phase", "")).casefold() != args.phase.casefold():
            failures.append(f"{name} scenario receipt has the wrong phase")
        if args.scenario.casefold() == "megacity-metro":
            if int(scenario_receipt.get("schemaVersion", 0)) < 6:
                failures.append(
                    f"{name} Megacity scenario receipt predates circuit/GC evidence"
                )
            if not bool(scenario_receipt.get("deterministicCameraPass")):
                failures.append(
                    f"{name} Megacity scenario did not declare a deterministic camera pass"
                )
            simulation_time_scale = float(
                scenario_receipt.get("simulationTimeScale", -1.0)
            )
            if not math.isfinite(simulation_time_scale) or abs(
                simulation_time_scale
            ) > 0.000001:
                failures.append(
                    f"{name} Megacity scenario simulation time scale is "
                    f"{simulation_time_scale:.6f}; expected 0.0"
                )
            if not str(scenario_receipt.get("cameraMotion", "")).strip():
                failures.append(
                    f"{name} Megacity scenario did not identify its camera motion"
                )
            if not bool(scenario_receipt.get("cameraMotionPreconditioned")):
                failures.append(
                    f"{name} Megacity camera motion did not begin before measurement"
                )
            camera_circuit_seconds = float(
                scenario_receipt.get("cameraCircuitSeconds", -1.0)
            )
            camera_phase = float(
                scenario_receipt.get("cameraCircuitPhaseAtArmSeconds", -1.0)
            )
            capture_lead = float(
                scenario_receipt.get("captureConditioningLeadSeconds", -1.0)
            )
            if (
                not math.isfinite(camera_circuit_seconds)
                or abs(camera_circuit_seconds - 12.0) > 0.001
                or not bool(
                    scenario_receipt.get(
                        "cameraCircuitCompletedBeforeMeasurement"
                    )
                )
                or not math.isfinite(camera_phase)
                or abs(camera_phase - 4.0) > 0.1
                or not bool(scenario_receipt.get("captureConditioned"))
                or not math.isfinite(capture_lead)
                or capture_lead + 0.001 < 3.0
            ):
                failures.append(
                    f"{name} Megacity fixed-phase camera/capture conditioning "
                    "evidence is inconsistent"
                )
            camera_origin_values = [
                float(scenario_receipt.get(field, math.nan))
                for field in (
                    "cameraOriginWorldX",
                    "cameraOriginWorldY",
                    "cameraOriginWorldZ",
                    "cameraOriginRotationX",
                    "cameraOriginRotationY",
                    "cameraOriginRotationZ",
                    "cameraOriginRotationW",
                )
            ]
            if any(not math.isfinite(value) for value in camera_origin_values):
                failures.append(
                    f"{name} Megacity camera origin is not fully recorded"
                )
            else:
                rotation_norm = math.sqrt(
                    sum(value * value for value in camera_origin_values[3:])
                )
                if abs(rotation_norm - 1.0) > 0.001:
                    failures.append(
                        f"{name} Megacity camera-origin rotation is not normalized"
                    )
            if not bool(scenario_receipt.get("startupQuiescenceRequired")):
                failures.append(
                    f"{name} Megacity scenario did not require startup quiescence"
                )
            required_quiescence = float(
                scenario_receipt.get("startupQuiescenceRequiredSeconds", -1.0)
            )
            quiescence_ceiling = float(
                scenario_receipt.get(
                    "startupQuiescenceFrameCeilingMilliseconds",
                    -1.0,
                )
            )
            observed_quiescence = float(
                scenario_receipt.get("startupQuiescenceObservedSeconds", -1.0)
            )
            controlled_preflight = float(
                scenario_receipt.get("controlledPreflightSeconds", -1.0)
            )
            if (
                not math.isfinite(required_quiescence)
                or abs(required_quiescence - 3.0) > 0.001
                or not math.isfinite(quiescence_ceiling)
                or abs(quiescence_ceiling - 14.0) > 0.001
                or not math.isfinite(observed_quiescence)
                or observed_quiescence + 0.001 < required_quiescence
                or not math.isfinite(controlled_preflight)
                or controlled_preflight + 0.001 < required_quiescence
                or int(scenario_receipt.get("startupQuiescenceResetCount", -1)) < 0
            ):
                failures.append(
                    f"{name} Megacity startup-quiescence evidence is inconsistent"
                )
            if int(scenario_receipt.get("targetFrameRate", 0)) != 120:
                failures.append(
                    f"{name} Megacity scenario did not use the pinned 120 FPS producer"
                )
            if (
                bool(scenario_receipt.get("diagnosticHudDisabled"))
                or bool(scenario_receipt.get("diagnosticStaticCamera"))
                or abs(float(scenario_receipt.get("diagnosticFarClipPlane", 0.0)))
                > 0.001
                or int(scenario_receipt.get("diagnosticTargetFrameRate", 0)) != 0
            ):
                failures.append(
                    f"{name} Megacity formal run used a diagnostic workload shortcut"
                )
            output_width = int(scenario_receipt.get("outputWidth", 0))
            output_height = int(scenario_receipt.get("outputHeight", 0))
            internal_render_scale = float(
                scenario_receipt.get("internalRenderScale", math.nan)
            )
            camera_far_clip = float(
                scenario_receipt.get("cameraFarClipPlane", math.nan)
            )
            if (
                output_width != 1280
                or output_height != 720
                or not bool(scenario_receipt.get("internalRenderScaleApplied"))
                or not math.isfinite(internal_render_scale)
                or abs(internal_render_scale - 1.0) > 0.001
                or not math.isfinite(camera_far_clip)
                or abs(camera_far_clip - 150.0) > 0.01
            ):
                failures.append(
                    f"{name} Megacity output/render-scale contract is inconsistent"
                )
            if int(scenario_receipt.get("vSyncCount", -1)) != 0:
                failures.append(f"{name} Megacity scenario did not disable vSync")
            if not bool(scenario_receipt.get("frameTimingStatsEnabled")):
                failures.append(
                    f"{name} Megacity scenario did not enable native frame timings"
                )
            gc_start = int(
                scenario_receipt.get("managedGcCollectionCountAtStart", -1)
            )
            gc_end = int(
                scenario_receipt.get("managedGcCollectionCountAtEnd", -1)
            )
            gc_during = int(
                scenario_receipt.get("managedGcCollectionsDuringRun", -1)
            )
            if (
                gc_start < 0
                or gc_end < gc_start
                or gc_during != gc_end - gc_start
                or gc_during != 0
            ):
                failures.append(
                    f"{name} Megacity measured window contains managed GC "
                    f"collections ({gc_start} -> {gc_end})"
                )
            if not bool(scenario_receipt.get("simulationSystemsFrozen")):
                failures.append(
                    f"{name} Megacity scenario did not freeze ECS simulation systems"
                )
            frozen_worlds = scenario_receipt.get("frozenSimulationWorlds", [])
            if not isinstance(frozen_worlds, list):
                failures.append(
                    f"{name} Megacity frozen simulation worlds must be an array"
                )
                frozen_worlds = []
            frozen_world_count = int(
                scenario_receipt.get("frozenSimulationWorldCount", -1)
            )
            if (
                frozen_world_count <= 0
                or frozen_world_count != len(frozen_worlds)
                or any(not str(world).strip() for world in frozen_worlds)
                or len(set(map(str, frozen_worlds))) != len(frozen_worlds)
            ):
                failures.append(
                    f"{name} Megacity frozen simulation-world evidence is inconsistent"
                )
            if not bool(scenario_receipt.get("initializationSystemsFrozen")):
                failures.append(
                    f"{name} Megacity scenario did not freeze ECS initialization systems"
                )
            frozen_initialization_worlds = scenario_receipt.get(
                "frozenInitializationWorlds",
                [],
            )
            if not isinstance(frozen_initialization_worlds, list):
                failures.append(
                    f"{name} Megacity frozen initialization worlds must be an array"
                )
                frozen_initialization_worlds = []
            frozen_initialization_count = int(
                scenario_receipt.get("frozenInitializationWorldCount", -1)
            )
            if (
                frozen_initialization_count <= 0
                or frozen_initialization_count != len(frozen_initialization_worlds)
                or any(
                    not str(world).strip()
                    for world in frozen_initialization_worlds
                )
                or len(set(map(str, frozen_initialization_worlds)))
                != len(frozen_initialization_worlds)
            ):
                failures.append(
                    f"{name} Megacity frozen initialization-world evidence "
                    "is inconsistent"
                )
            if not bool(scenario_receipt.get("presentationSystemsRemainActive")):
                failures.append(
                    f"{name} Megacity scenario did not retain ECS presentation systems"
                )
            active_presentation_worlds = scenario_receipt.get(
                "activePresentationWorlds",
                [],
            )
            if not isinstance(active_presentation_worlds, list):
                failures.append(
                    f"{name} Megacity active presentation worlds must be an array"
                )
                active_presentation_worlds = []
            active_presentation_count = int(
                scenario_receipt.get("activePresentationWorldCount", -1)
            )
            if (
                active_presentation_count <= 0
                or active_presentation_count != len(active_presentation_worlds)
                or any(not str(world).strip() for world in active_presentation_worlds)
                or len(set(map(str, active_presentation_worlds)))
                != len(active_presentation_worlds)
            ):
                failures.append(
                    f"{name} Megacity active presentation-world evidence is inconsistent"
                )
            native_timing_coverage[name] = validate_native_frame_timing(
                name,
                scenario_receipt,
                failures,
            )
        for field, expected in (
            ("runDurationSeconds", 12.0),
            ("contentRequestSeconds", 2.0),
            ("contentRevealSeconds", 4.5),
            ("revealWindowEndSeconds", 6.5),
        ):
            observed = float(scenario_receipt.get(field, -1.0))
            if not math.isfinite(observed) or abs(observed - expected) > 0.001:
                failures.append(
                    f"{name} scenario {field} is {observed:.3f}; expected {expected:.3f}"
                )
        if not timed_samples:
            failures.append(f"{name} scenario has no timed frame samples")
        calculated_scenario_maximum = max(
            (
                milliseconds
                for elapsed, milliseconds in timed_samples
                if elapsed <= 12.25
            ),
            default=0.0,
        )
        reported_scenario_maximum = float(
            scenario_receipt.get("maximumMilliseconds", -1.0)
        )
        if not math.isfinite(reported_scenario_maximum) or abs(
            reported_scenario_maximum - calculated_scenario_maximum
        ) > 0.01:
            failures.append(
                f"{name} scenario maximum does not match its gated samples"
            )
        first_post_reveal = float(
            scenario_receipt.get("firstPostRevealFrameMilliseconds", -1.0)
        )
        if not math.isfinite(first_post_reveal) or first_post_reveal <= 0.0:
            failures.append(f"{name} scenario did not retain the first post-reveal frame")
        reported_threshold = float(
            scenario_receipt.get("severeFrameThresholdMilliseconds", -1.0)
        )
        if not math.isfinite(reported_threshold) or abs(
            reported_threshold - args.scheduled_threshold_ms
        ) > 0.001:
            failures.append(
                f"{name} scenario severe-frame threshold is {reported_threshold:.3f}; "
                f"expected {args.scheduled_threshold_ms:.3f}"
            )
        calculated_severe_count = sum(
            milliseconds >= args.scheduled_threshold_ms
            for elapsed, milliseconds in timed_samples
            if elapsed <= 12.25
        )
        if int(scenario_receipt.get("severeFrameCount", -1)) != calculated_severe_count:
            failures.append(
                f"{name} scenario severe-frame count does not match its raw samples"
            )
        if name != "baseline":
            if not bool(scenario_receipt.get("deferredReady")):
                failures.append(f"{name} deferred hot set was not ready")
            if bool(scenario_receipt.get("deadlineMissed")):
                failures.append(f"{name} scenario missed its deferred deadline")
            deferred_ready_seconds = float(
                scenario_receipt.get("deferredReadySeconds", -1.0)
            )
            if (
                not math.isfinite(deferred_ready_seconds)
                or deferred_ready_seconds < 2.0
                or deferred_ready_seconds >= 4.5
            ):
                failures.append(
                    f"{name} deferred-ready timestamp is outside request/deadline "
                    f"({deferred_ready_seconds:.3f} s)"
                )

    reported_baseline_reveal_maximum = float(
        baseline_scenario.get("revealWindowMaximumMilliseconds", 0.0)
    )
    if not math.isfinite(reported_baseline_reveal_maximum) or abs(
        reported_baseline_reveal_maximum - baseline_reveal_maximum
    ) > 0.01:
        failures.append("baseline reveal-window maximum does not match its timed samples")
    scenario_devices = {
        (
            str(receipt.get("graphicsDeviceType", "")),
            str(receipt.get("graphicsDeviceName", "")),
        )
        for receipt in (baseline_scenario, naive_scenario, scheduled_scenario)
    }
    if len(scenario_devices) != 1:
        failures.append(
            f"{args.scenario} scenario receipts were captured on different GPUs/APIs"
        )
    if any(device_type != "Direct3D12" for device_type, _ in scenario_devices):
        failures.append(f"{args.scenario} visual acceptance requires Direct3D12")

    if args.scenario.casefold() == "megacity-metro":
        camera_origins = []
        for receipt in (baseline_scenario, naive_scenario, scheduled_scenario):
            position = tuple(
                float(receipt.get(field, math.nan))
                for field in (
                    "cameraOriginWorldX",
                    "cameraOriginWorldY",
                    "cameraOriginWorldZ",
                )
            )
            rotation = tuple(
                float(receipt.get(field, math.nan))
                for field in (
                    "cameraOriginRotationX",
                    "cameraOriginRotationY",
                    "cameraOriginRotationZ",
                    "cameraOriginRotationW",
                )
            )
            camera_origins.append((position, rotation))
        reference_position, reference_rotation = camera_origins[0]
        for index, (position, rotation) in enumerate(camera_origins[1:], start=1):
            position_delta = math.sqrt(
                sum(
                    (position[axis] - reference_position[axis]) ** 2
                    for axis in range(3)
                )
            )
            reference_norm = math.sqrt(
                sum(value * value for value in reference_rotation)
            )
            rotation_norm = math.sqrt(sum(value * value for value in rotation))
            quaternion_dot = (
                abs(
                    sum(
                        rotation[axis] * reference_rotation[axis]
                        for axis in range(4)
                    )
                )
                / (reference_norm * rotation_norm)
                if reference_norm > 0.0 and rotation_norm > 0.0
                else -1.0
            )
            if position_delta > 0.05 or quaternion_dot < 0.999:
                labels = ("baseline", "naive", "scheduled")
                failures.append(
                    f"{labels[index]} Megacity camera origin differs from baseline "
                    f"(position={position_delta:.4f} m, |qdot|={quaternion_dot:.6f})"
                )

    for name, benchmark, scenario_receipt in (
        ("baseline", baseline, baseline_scenario),
        ("naive", naive, naive_scenario),
        ("scheduled", scheduled, scheduled_scenario),
    ):
        environment = benchmark.get("environment", {})
        if not isinstance(environment, dict):
            failures.append(f"{name} benchmark has no environment object")
            continue
        benchmark_device = (
            str(environment.get("graphicsDeviceType", "")),
            str(environment.get("graphicsDeviceName", "")),
        )
        scenario_device = (
            str(scenario_receipt.get("graphicsDeviceType", "")),
            str(scenario_receipt.get("graphicsDeviceName", "")),
        )
        if benchmark_device != scenario_device:
            failures.append(f"{name} benchmark/scenario GPU identity differs")
        scenario_quality = str(scenario_receipt.get("qualityLevelName", ""))
        if scenario_quality and scenario_quality != str(
            environment.get("qualityLevelName", "")
        ):
            failures.append(f"{name} benchmark/scenario quality level differs")

    if baseline_reveal_maximum < args.baseline_min_ms:
        failures.append(
            f"cold reveal-window maximum {baseline_reveal_maximum:.3f} ms is below "
            f"the {args.baseline_min_ms:.3f} ms visible-stall requirement"
        )
    if scheduled_missed_frames != 0 or scheduled_scenario_missed_frames != 0:
        failures.append(
            "scheduled contains frame(s) at or above "
            f"{args.scheduled_threshold_ms:.3f} ms "
            f"(benchmark={scheduled_missed_frames}, "
            f"scenario={scheduled_scenario_missed_frames})"
        )

    phase = find_phase(scheduled_warmup, args.phase)
    if phase is None:
        failures.append(f"scheduled warmup receipt has no '{args.phase}' phase")
        phase = {}
    else:
        if not bool(phase.get("completed")):
            failures.append(f"scheduled phase '{args.phase}' is incomplete")
        if bool(phase.get("deadlineMissed")):
            failures.append(f"scheduled phase '{args.phase}' missed its content deadline")
        if int(phase.get("budgetViolationCount", 0)) != 0:
            failures.append(f"scheduled phase '{args.phase}' has budget violations")
        if not bool(phase.get("hardFrameBudgetMet")):
            failures.append(f"scheduled phase '{args.phase}' did not meet its hard budget")
        if not bool(phase.get("hardFrameBudgetFeasible")):
            failures.append(f"scheduled phase '{args.phase}' was not budget-feasible")
        if not bool(phase.get("schedulerAdmissionBudgetMet")):
            failures.append(
                f"scheduled phase '{args.phase}' admitted work above the modeled budget"
            )
        observed_deadline = float(phase.get("deadlineMilliseconds", -1.0))
        if not math.isfinite(observed_deadline) or abs(
            observed_deadline - 2500.0
        ) > 0.001:
            failures.append(
                f"scheduled phase deadline is {observed_deadline:.3f} ms; expected 2500 ms"
            )
        observed_budget = float(phase.get("hardFrameBudgetMilliseconds", -1.0))
        if not math.isfinite(observed_budget) or abs(
            observed_budget - args.scheduled_threshold_ms
        ) > 0.001:
            failures.append(
                f"scheduled hard frame budget is {observed_budget:.3f} ms; "
                f"expected {args.scheduled_threshold_ms:.3f} ms"
            )
        completed_states = int(phase.get("completedGraphicsStates", 0))
        total_states = int(phase.get("totalGraphicsStates", 0))
        controlled_states = int(
            scheduled_scenario.get("controlledGraphicsStateCount", 0)
        )
        if controlled_states > 0 and total_states != controlled_states:
            failures.append(
                f"scheduled plan has {total_states} states but the scenario "
                f"declares {controlled_states} controlled states"
            )
        if total_states <= 0:
            failures.append(
                f"scheduled phase state completion is {completed_states}/{total_states}"
            )
        elif "backendReportedWarmedUp" in phase:
            if not bool(phase.get("backendReportedWarmedUp")):
                failures.append(
                    f"scheduled phase backend did not report full warmup "
                    f"({completed_states}/{total_states} derived states; "
                    f"{int(phase.get('completedWarmupPermutations', 0))} permutations)"
                )
        elif completed_states != total_states:
            failures.append(
                f"scheduled phase state completion is {completed_states}/{total_states}"
            )

    cache_miss_trace = scheduled_warmup.get("cacheMissTrace", {})
    if not isinstance(cache_miss_trace, dict):
        failures.append("scheduled warmup has no plan-scoped cache-miss evidence")
        cache_miss_trace = {}
    if not bool(cache_miss_trace.get("requested")):
        failures.append("scheduled plan-scoped cache-miss trace was not requested")
    if not bool(cache_miss_trace.get("armed")):
        failures.append("scheduled plan-scoped cache-miss trace was not armed")
    if str(cache_miss_trace.get("error", "")).strip():
        failures.append(
            "scheduled plan-scoped cache-miss trace reported an error: "
            f"{cache_miss_trace.get('error')}"
        )
    baseline_states = int(cache_miss_trace.get("baselineGraphicsStates", -1))
    observed_states = int(cache_miss_trace.get("observedGraphicsStates", -1))
    cache_misses = int(cache_miss_trace.get("cacheMissGraphicsStates", -1))
    if (
        baseline_states <= 0
        or observed_states < baseline_states
        or cache_misses != observed_states - baseline_states
        or cache_misses != 0
    ):
        failures.append(
            "scheduled plan-scoped cache-miss evidence is inconsistent "
            f"(baseline={baseline_states}, observed={observed_states}, "
            f"misses={cache_misses})"
        )

    result: dict[str, Any] = {
        "schemaVersion": 2,
        "scenario": args.scenario,
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "passed": not failures,
        "requirements": {
            "baselineMinimumVisibleStallMilliseconds": args.baseline_min_ms,
            "scheduledMissThresholdMilliseconds": args.scheduled_threshold_ms,
            "scheduledAllowedMissedFrames": 0,
            "requiredPhase": args.phase,
            "requiredGraphicsDeviceType": "Direct3D12",
            "timelineSeconds": {
                "runDuration": 12.0,
                "contentRequest": 2.0,
                "contentReveal": 4.5,
                "revealWindowEnd": 6.5,
            },
            "workloadPolicy": {
                "syntheticDelayAllowed": False,
                "measuredWindowRuntimeInstantiationAllowed": False,
                "assetStreamingAllowed": False,
                "measurementBoundary": (
                    "include first presented frame crossing 12.00 s; no later frames"
                ),
                "deterministicCameraPassRequired": (
                    args.scenario.casefold() == "megacity-metro"
                ),
                "simulationTimeScale": (
                    0.0 if args.scenario.casefold() == "megacity-metro" else None
                ),
                "targetFrameRate": (
                    120 if args.scenario.casefold() == "megacity-metro" else None
                ),
                "outputResolution": (
                    "1280x720"
                    if args.scenario.casefold() == "megacity-metro"
                    else None
                ),
                "internalRenderScale": (
                    1.0 if args.scenario.casefold() == "megacity-metro" else None
                ),
                "cameraFarClipMeters": (
                    150.0 if args.scenario.casefold() == "megacity-metro" else None
                ),
                "diagnosticWorkloadShortcutsAllowed": (
                    False if args.scenario.casefold() == "megacity-metro" else None
                ),
                "vSyncCount": (
                    0 if args.scenario.casefold() == "megacity-metro" else None
                ),
                "ecsSimulationSystemsFrozen": (
                    True if args.scenario.casefold() == "megacity-metro" else None
                ),
                "ecsInitializationSystemsFrozen": (
                    True if args.scenario.casefold() == "megacity-metro" else None
                ),
                "ecsPresentationSystemsRemainActive": (
                    True if args.scenario.casefold() == "megacity-metro" else None
                ),
                "startupQuiescence": (
                    {
                        "requiredContinuousSeconds": 3.0,
                        "frameCeilingMilliseconds": 14.0,
                        "cameraMotionActiveDuringGate": True,
                    }
                    if args.scenario.casefold() == "megacity-metro"
                    else None
                ),
                "nativeFrameTimingCoverageMinimum": (
                    0.95 if args.scenario.casefold() == "megacity-metro" else None
                ),
                "cameraCircuit": (
                    {
                        "durationSeconds": 12.0,
                        "measurementStartPhaseSeconds": 4.0,
                        "fullCircuitPreconditioned": True,
                        "captureConditioningRequired": True,
                    }
                    if args.scenario.casefold() == "megacity-metro"
                    else None
                ),
                "managedGcCollectionsAllowed": (
                    0 if args.scenario.casefold() == "megacity-metro" else None
                ),
            },
        },
        "metrics": {
            "sampleCount": {
                "baseline": len(baseline_samples),
                "naive": len(naive_samples),
                "scheduled": len(scheduled_samples),
            },
            "maximumMilliseconds": {
                "baseline": baseline_maximum,
                "naive": naive_maximum,
                "scheduled": scheduled_maximum,
            },
            "benchmarkMeasurementDurationSeconds": {
                "baseline": float(
                    baseline.get("measurementDurationSeconds", 0.0)
                ),
                "naive": float(naive.get("measurementDurationSeconds", 0.0)),
                "scheduled": float(
                    scheduled.get("measurementDurationSeconds", 0.0)
                ),
            },
            "scenarioMaximumMilliseconds": {
                "baselineRevealWindow": baseline_reveal_maximum,
                "scheduledFullRun": scheduled_scenario_maximum,
            },
            "nativeFrameTimingSamples": {
                name: {"available": counts[0], "exactWindow": counts[1]}
                for name, counts in native_timing_coverage.items()
            },
            "firstPostRevealFrameMilliseconds": {
                "baseline": float(
                    baseline_scenario.get("firstPostRevealFrameMilliseconds", -1.0)
                ),
                "naive": float(
                    naive_scenario.get("firstPostRevealFrameMilliseconds", -1.0)
                ),
                "scheduled": float(
                    scheduled_scenario.get("firstPostRevealFrameMilliseconds", -1.0)
                ),
            },
            "baselineVisibleStallCount": baseline_visible_stalls,
            "missedFramesAtScheduledThreshold": {
                "naive": naive_missed_frames,
                "scheduled": scheduled_missed_frames,
                "scheduledScenario": scheduled_scenario_missed_frames,
            },
            "managedGcCollectionsDuringRun": {
                "baseline": int(
                    baseline_scenario.get("managedGcCollectionsDuringRun", 0)
                ),
                "naive": int(
                    naive_scenario.get("managedGcCollectionsDuringRun", 0)
                ),
                "scheduled": int(
                    scheduled_scenario.get("managedGcCollectionsDuringRun", 0)
                ),
            },
        },
        "scheduledPhase": {
            "phase": phase.get("phase", args.phase),
            "completedGraphicsStates": int(phase.get("completedGraphicsStates", 0)),
            "totalGraphicsStates": int(phase.get("totalGraphicsStates", 0)),
            "completedWarmupPermutations": int(
                phase.get("completedWarmupPermutations", 0)
            ),
            "backendReportedWarmedUp": bool(
                phase.get(
                    "backendReportedWarmedUp",
                    int(phase.get("completedGraphicsStates", 0))
                    == int(phase.get("totalGraphicsStates", 0)),
                )
            ),
            "backendSchedulingMode": str(phase.get("backendSchedulingMode", "")),
            "batchCount": int(phase.get("batchCount", 0)),
            "elapsedMilliseconds": float(phase.get("elapsedMilliseconds", 0.0)),
            "deadlineMilliseconds": float(phase.get("deadlineMilliseconds", 0.0)),
            "deadlineMissed": bool(phase.get("deadlineMissed", False)),
            "budgetViolationCount": int(phase.get("budgetViolationCount", 0)),
            "hardFrameBudgetMet": bool(phase.get("hardFrameBudgetMet", False)),
            "hardFrameBudgetFeasible": bool(
                phase.get("hardFrameBudgetFeasible", False)
            ),
            "hardFrameBudgetMilliseconds": float(
                phase.get("hardFrameBudgetMilliseconds", 0.0)
            ),
            "schedulerAdmissionBudgetMet": bool(
                phase.get("schedulerAdmissionBudgetMet", False)
            ),
            "cacheMissGraphicsStates": int(
                cache_miss_trace.get("cacheMissGraphicsStates", -1)
            ),
        },
        "failures": failures,
        "inputs": {
            "baseline": str(args.baseline.resolve()),
            "naive": str(args.naive.resolve()),
            "scheduled": str(args.scheduled.resolve()),
            "scheduledWarmup": str(args.scheduled_warmup.resolve()),
            "baselineScenario": str(args.baseline_scenario.resolve()),
            "naiveScenario": str(args.naive_scenario.resolve()),
            "scheduledScenario": str(args.scheduled_scenario.resolve()),
        },
    }
    return result, failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--naive", required=True, type=Path)
    parser.add_argument("--scheduled", required=True, type=Path)
    parser.add_argument("--scheduled-warmup", required=True, type=Path)
    parser.add_argument("--baseline-scenario", required=True, type=Path)
    parser.add_argument("--naive-scenario", required=True, type=Path)
    parser.add_argument("--scheduled-scenario", required=True, type=Path)
    parser.add_argument("--scenario", default="deadline-run")
    parser.add_argument("--phase", default="deadline-run-reveal")
    parser.add_argument("--baseline-min-ms", type=float, default=80.0)
    parser.add_argument("--scheduled-threshold-ms", type=float, default=16.67)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if not math.isfinite(args.baseline_min_ms) or args.baseline_min_ms <= 0.0:
        parser.error("--baseline-min-ms must be a positive finite value")
    if not args.scenario.strip():
        parser.error("--scenario must not be empty")
    if (
        not math.isfinite(args.scheduled_threshold_ms)
        or args.scheduled_threshold_ms <= 0.0
    ):
        parser.error("--scheduled-threshold-ms must be a positive finite value")

    result, failures = validate(args)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(result, stream, indent=2)
        stream.write("\n")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 2
    print(
        f"PASS: {args.scenario} cold visibility and scheduled 60 FPS contract "
        f"({result['metrics']['scenarioMaximumMilliseconds']['baselineRevealWindow']:.3f} ms -> "
        f"{result['metrics']['maximumMilliseconds']['scheduled']:.3f} ms)."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
