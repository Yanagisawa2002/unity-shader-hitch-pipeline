from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace


TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import validate_deadline_run  # noqa: E402


class DeadlineRunAcceptanceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def _environment() -> dict[str, object]:
        return {
            "graphicsDeviceType": "Direct3D12",
            "graphicsDeviceName": "Test GPU",
            "qualityLevelName": "Medium",
        }

    @classmethod
    def _benchmark(cls, values: list[float]) -> dict[str, object]:
        return {
            "completed": True,
            "scenarioMeasurementGated": True,
            "measurementDurationSeconds": 12.0,
            "requestedSampleFrames": 720,
            "actualSampleFrames": len(values),
            "environment": cls._environment(),
            "frameTimes": {"maximumMilliseconds": max(values)},
            "frameTimeSamplesMilliseconds": values,
        }

    @classmethod
    def _scenario(
        cls,
        mode: str,
        values: list[float],
    ) -> dict[str, object]:
        elapsed = [0.0, 4.5, 12.0]
        samples = []
        for seconds, milliseconds in zip(elapsed, values, strict=True):
            samples.append(
                {
                    "elapsedSeconds": seconds,
                    "milliseconds": milliseconds,
                    "nativeTimingAvailable": True,
                    "cpuTotalMilliseconds": milliseconds,
                    "cpuMainThreadMilliseconds": milliseconds,
                    "cpuMainThreadPresentWaitMilliseconds": 0.5,
                    "cpuRenderThreadMilliseconds": 1.0,
                    "gpuMilliseconds": 2.0,
                }
            )
        return {
            "schemaVersion": 6,
            "scenario": "megacity-metro",
            "mode": mode,
            "phase": "megacity-metro-reveal",
            "completed": True,
            "runDurationSeconds": 12.0,
            "contentRequestSeconds": 2.0,
            "contentRevealSeconds": 4.5,
            "revealWindowEndSeconds": 6.5,
            "maximumMilliseconds": max(values),
            "revealWindowMaximumMilliseconds": values[1],
            "firstPostRevealFrameMilliseconds": values[1],
            "severeFrameThresholdMilliseconds": 16.67,
            "severeFrameCount": sum(value >= 16.67 for value in values),
            "graphicsDeviceType": "Direct3D12",
            "graphicsDeviceName": "Test GPU",
            "qualityLevelName": "Medium",
            "outputWidth": 1280,
            "outputHeight": 720,
            "internalRenderScale": 1.0,
            "internalRenderScaleApplied": True,
            "cameraFarClipPlane": 150.0,
            "diagnosticHudDisabled": False,
            "diagnosticStaticCamera": False,
            "diagnosticFarClipPlane": 0.0,
            "diagnosticTargetFrameRate": 0,
            "controlledGraphicsStateCount": 48,
            "deterministicCameraPass": True,
            "simulationTimeScale": 0.0,
            "cameraMotion": "12-second closed circuit",
            "cameraMotionPreconditioned": True,
            "cameraCircuitSeconds": 12.0,
            "cameraCircuitCompletedBeforeMeasurement": True,
            "cameraCircuitPhaseAtArmSeconds": 4.0,
            "cameraOriginWorldX": 1.0,
            "cameraOriginWorldY": 2.0,
            "cameraOriginWorldZ": 3.0,
            "cameraOriginRotationX": 0.0,
            "cameraOriginRotationY": 0.0,
            "cameraOriginRotationZ": 0.0,
            "cameraOriginRotationW": 1.0,
            "captureConditioned": True,
            "captureConditioningLeadSeconds": 4.0,
            "startupQuiescenceRequired": True,
            "startupQuiescenceRequiredSeconds": 3.0,
            "startupQuiescenceFrameCeilingMilliseconds": 14.0,
            "startupQuiescenceObservedSeconds": 3.5,
            "startupQuiescenceResetCount": 0,
            "controlledPreflightSeconds": 16.0,
            "targetFrameRate": 120,
            "vSyncCount": 0,
            "frameTimingStatsEnabled": True,
            "frameTimingSampleCount": len(samples),
            "maximumCpuTotalMilliseconds": max(values),
            "maximumCpuMainThreadMilliseconds": max(values),
            "maximumCpuMainThreadPresentWaitMilliseconds": 0.5,
            "maximumCpuRenderThreadMilliseconds": 1.0,
            "maximumGpuMilliseconds": 2.0,
            "managedGcCollectionCountAtStart": 4,
            "managedGcCollectionCountAtEnd": 4,
            "managedGcCollectionsDuringRun": 0,
            "simulationSystemsFrozen": True,
            "frozenSimulationWorldCount": 1,
            "frozenSimulationWorlds": ["LocalWorld"],
            "initializationSystemsFrozen": True,
            "frozenInitializationWorldCount": 1,
            "frozenInitializationWorlds": ["LocalWorld"],
            "presentationSystemsRemainActive": True,
            "activePresentationWorldCount": 1,
            "activePresentationWorlds": ["LocalWorld"],
            "deferredReady": mode != "baseline",
            "deadlineMissed": False,
            "deferredReadySeconds": 2.1 if mode != "baseline" else -1.0,
            "frameSamples": samples,
        }

    @staticmethod
    def _warmup() -> dict[str, object]:
        return {
            "completed": True,
            "error": "",
            "phases": [
                {
                    "phase": "megacity-metro-reveal",
                    "completed": True,
                    "deadlineMissed": False,
                    "budgetViolationCount": 0,
                    "hardFrameBudgetMet": True,
                    "hardFrameBudgetFeasible": True,
                    "schedulerAdmissionBudgetMet": True,
                    "deadlineMilliseconds": 2500.0,
                    "hardFrameBudgetMilliseconds": 16.67,
                    "completedGraphicsStates": 48,
                    "totalGraphicsStates": 48,
                    "completedWarmupPermutations": 48,
                    "backendReportedWarmedUp": True,
                }
            ],
            "cacheMissTrace": {
                "requested": True,
                "armed": True,
                "error": "",
                "baselineGraphicsStates": 48,
                "observedGraphicsStates": 48,
                "cacheMissGraphicsStates": 0,
            },
        }

    def _write(self, name: str, value: dict[str, object]) -> Path:
        path = self.root / name
        path.write_text(json.dumps(value), encoding="utf-8")
        return path

    def _args(self, scheduled_scenario: dict[str, object]) -> SimpleNamespace:
        baseline_values = [8.0, 100.0, 8.0]
        naive_values = [8.0, 12.0, 8.0]
        scheduled_values = [8.0, 9.0, 8.0]
        return SimpleNamespace(
            baseline=self._write(
                "baseline.json", self._benchmark(baseline_values)
            ),
            naive=self._write("naive.json", self._benchmark(naive_values)),
            scheduled=self._write(
                "scheduled.json", self._benchmark(scheduled_values)
            ),
            scheduled_warmup=self._write("warmup.json", self._warmup()),
            baseline_scenario=self._write(
                "baseline-scenario.json",
                self._scenario("baseline", baseline_values),
            ),
            naive_scenario=self._write(
                "naive-scenario.json",
                self._scenario("throughput", naive_values),
            ),
            scheduled_scenario=self._write(
                "scheduled-scenario.json", scheduled_scenario
            ),
            scenario="megacity-metro",
            phase="megacity-metro-reveal",
            baseline_min_ms=80.0,
            scheduled_threshold_ms=16.67,
            output=self.root / "acceptance.json",
        )

    def test_valid_megacity_contract_passes(self) -> None:
        scenario = self._scenario("scheduled", [8.0, 9.0, 8.0])
        result, failures = validate_deadline_run.validate(self._args(scenario))
        self.assertEqual([], failures)
        self.assertTrue(result["passed"])

    def test_scheduled_deadline_miss_is_rejected(self) -> None:
        scenario = self._scenario("scheduled", [8.0, 17.0, 8.0])
        result, failures = validate_deadline_run.validate(self._args(scenario))
        self.assertFalse(result["passed"])
        self.assertTrue(any("scheduled contains frame" in item for item in failures))

    def test_measured_window_gc_is_rejected(self) -> None:
        scenario = self._scenario("scheduled", [8.0, 9.0, 8.0])
        scenario["managedGcCollectionCountAtEnd"] = 5
        scenario["managedGcCollectionsDuringRun"] = 1
        result, failures = validate_deadline_run.validate(self._args(scenario))
        self.assertFalse(result["passed"])
        self.assertTrue(any("managed GC" in item for item in failures))

    def test_camera_origin_drift_is_rejected(self) -> None:
        scenario = self._scenario("scheduled", [8.0, 9.0, 8.0])
        scenario["cameraOriginWorldX"] = 1.1
        result, failures = validate_deadline_run.validate(self._args(scenario))
        self.assertFalse(result["passed"])
        self.assertTrue(any("camera origin differs" in item for item in failures))

    def test_generic_sampler_must_use_scenario_gate(self) -> None:
        scenario = self._scenario("scheduled", [8.0, 9.0, 8.0])
        args = self._args(scenario)
        benchmark = json.loads(args.scheduled.read_text(encoding="utf-8"))
        benchmark["scenarioMeasurementGated"] = False
        args.scheduled.write_text(json.dumps(benchmark), encoding="utf-8")
        result, failures = validate_deadline_run.validate(args)
        self.assertFalse(result["passed"])
        self.assertTrue(any("not synchronized" in item for item in failures))


if __name__ == "__main__":
    unittest.main()
