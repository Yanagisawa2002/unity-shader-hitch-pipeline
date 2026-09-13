import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pso_system_acceptance as acceptance
import pso_windows_evidence as evidence


class SystemAcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)

    def write(self, name, data):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data) if not isinstance(data, str) else data, encoding="utf-8")
        return path

    def record(self, path):
        return {"path": str(path), "exists": True, "bytes": path.stat().st_size, "sha256": evidence.sha256(path)}

    def valid_run(self, index):
        # Synthetic fixtures test the validator, never enter public evidence.
        start = f"2026-09-02T10:00:{index:02d}"
        pid = 42 + index
        plan = self.write("plan.json", {"phase": "startup", "states": 389})
        player_log = self.write(f"{index}/player.log", "synthetic log")
        receipt = {"completed": True, "strategy": "scheduled", "mode": "scheduled", "frameTimes": {"sampleCount": 180}, "startedUtc": start + ".001Z", "endedUtc": start + ".900Z", "phases": [{"phase": "startup", "completed": True, "totalGraphicsStates": 389, "completedGraphicsStates": 389}],
                   "environment": {"unityVersion": "test", "graphicsDeviceType": "Direct3D12"}}
        receipt["planSha256"] = evidence.sha256(plan)
        warmup = self.write(f"{index}/warmup.json", receipt)
        benchmark = self.write(f"{index}/benchmark.json", receipt)
        marker = {"processId": pid, "sessionId": str(index), "phase": "startup", "qpcFrequency": 1000}
        markers = self.write(f"{index}/markers.jsonl", "\n".join(json.dumps(dict(marker, name=name, utc=start + suffix, qpc=qpc)) for name, suffix, qpc in [("benchmark-start", ".001Z", 1), ("phase-start", ".002Z", 2), ("phase-end", ".800Z", 800), ("benchmark-end", ".900Z", 900)]))
        csv = self.write(f"{index}/presentmon.csv", f"ProcessID,SwapChainAddress,CPUStartDateTime,FrameTime\n{pid},0xA,{start}.100Z,16\n{pid},0xA,{start}.200Z,17\n")
        summary = self.write(f"{index}/summary.json", evidence.build_summary(csv, warmup, 16.67, 0, pid, markers))
        binary = self.write("player.exe", "synthetic executable identity")
        build = self.write("build.json", {"sourceRevision": "a" * 40, "sourceDirty": False, "playerSha256": evidence.sha256(binary), "workloadId": "fixture", "expectedBenchmarkFrames": 180, "expectedPhaseStates": {"startup": 389}})
        etl = self.write(f"{index}/gpu.etl", "synthetic container used only by validator unit test")
        artifacts = {k: self.record(p) for k, p in dict(presentMonCsv=csv, presentMonSummary=summary, warmupReceipt=warmup, benchmarkReceipt=benchmark, markers=markers, buildManifest=build, etl=etl, warmupPlan=plan, playerLog=player_log).items()}
        tool = self.write("tool", "synthetic tool identity")
        manifest = {"schemaVersion": 2, "completed": True, "sessionId": str(index), "artifacts": artifacts,
                    "environment": {"operatingSystem": "test", "processor": "test", "gpuDevices": [{"Name": "test", "DriverVersion": "test", "PNPDeviceID": "test"}]},
                    "cache": {"process": "process-cold", "driver": "unknown-preserved", "globalCachesModified": False},
                    "target": {"processId": pid, "startedUtc": start + ".000Z", "endedUtc": start + ".999Z", "exitCode": 0, "buildFiles": [self.record(binary)], "player": str(binary), "playerSha256": evidence.sha256(binary)},
                    "tools": {**{k: self.record(tool) for k in ("presentMon", "wpr", "analyzer", "wrapper")}, "presentMonExitCode": 0, "analyzerExitCode": 0, "wprCommands": [{"arguments": ["-start", "GPU", "-filemode"], "exitCode": 0}, {"arguments": ["-stop", str(etl)], "exitCode": 0}]}}
        return self.write(f"{index}/manifest.json", manifest)

    def test_five_unique_valid_fixtures_pass_and_duplicate_is_rejected(self):
        runs = [self.valid_run(i) for i in range(1, 6)]
        self.assertTrue(acceptance.validate_group(runs)["completed"])
        self.assertFalse(acceptance.validate_group([runs[0]] * 5)["completed"])

    def test_tampered_raw_csv_and_false_exists_are_rejected(self):
        path = self.valid_run(1)
        m = acceptance.load(path)
        Path(m["artifacts"]["presentMonCsv"]["path"]).write_text("tamper")
        self.assertFalse(acceptance.validate_manifest(path)["valid"])
        m["artifacts"]["presentMonCsv"]["path"] = str(self.root / "absent")
        path.write_text(json.dumps(m))
        self.assertFalse(acceptance.validate_manifest(path)["valid"])

    def test_summary_forgery_is_rejected_even_when_rehashed(self):
        path = self.valid_run(1)
        m = acceptance.load(path)
        summary_path = Path(m["artifacts"]["presentMonSummary"]["path"])
        summary = acceptance.load(summary_path)
        summary["metrics"]["presentedFrameMilliseconds"]["maximumMilliseconds"] = 1
        summary_path.write_text(json.dumps(summary))
        m["artifacts"]["presentMonSummary"] = self.record(summary_path)
        path.write_text(json.dumps(m))
        self.assertFalse(acceptance.validate_manifest(path)["valid"])

    def test_receipt_outside_process_and_wrong_build_are_rejected(self):
        path = self.valid_run(1)
        m = acceptance.load(path)
        m["target"]["endedUtc"] = m["target"]["startedUtc"]
        m["target"]["playerSha256"] = "b" * 64
        path.write_text(json.dumps(m))
        self.assertFalse(acceptance.validate_manifest(path)["valid"])

    def test_five_copies_of_historical_claim_never_pass(self):
        path = self.write("historical.json", {"completed": True, "schemaVersion": 1, "artifacts": {"etl": {"exists": True}}})
        self.assertFalse(acceptance.validate_group([path] * 5)["completed"])

    def test_no_warmup_overlap_does_not_fall_back_to_full_csv(self):
        fixture = Path(__file__).parent / "fixtures/presentmon-v2.csv"
        receipt = self.write("receipt.json", {"startedUtc": "2027-01-01T00:00:00Z", "endedUtc": "2027-01-01T00:00:01Z"})
        summary = evidence.build_summary(fixture, receipt, 16.67, 0, 42)
        self.assertEqual(summary["rowSelection"]["analyzedRows"], 0)
        self.assertIsNone(summary["metrics"]["presentedFrameMilliseconds"]["maximumMilliseconds"])
        self.assertFalse(summary["budgetVerdict"]["presentMonPresentedFramesMet"])

    def test_process_filter_and_local_utc_offset(self):
        fixture = Path(__file__).parent / "fixtures/presentmon-v2.csv"
        receipt = self.write("receipt.json", {"startedUtc": "2026-09-02T02:00:00Z", "endedUtc": "2026-09-02T02:00:00.020Z"})
        summary = evidence.build_summary(fixture, receipt, 16.67, 480, 42)
        self.assertEqual(summary["rowSelection"]["analyzedRows"], 2)
        with self.assertRaises(ValueError): evidence.build_summary(fixture, None, 16.67, 0, 999)

    def test_missing_metrics_are_null(self):
        path = self.valid_run(1)
        summary = acceptance.load(acceptance.load(path)["artifacts"]["presentMonSummary"]["path"])
        self.assertIsNone(summary["metrics"]["gpuBusyMilliseconds"]["maximumMilliseconds"])

    def test_clock_jump_and_foreign_marker_pid_rejected(self):
        path = self.valid_run(1)
        m = acceptance.load(path)
        a = m["artifacts"]
        markers_path = Path(a["markers"]["path"])
        markers = [json.loads(line) for line in markers_path.read_text().splitlines()]
        markers[-1]["qpc"] += 1000
        markers_path.write_text("\n".join(map(json.dumps, markers)))
        with self.assertRaises(ValueError): evidence.build_summary(Path(a["presentMonCsv"]["path"]), Path(a["warmupReceipt"]["path"]), 16.67, 0, 43, markers_path)
        markers[-1]["processId"] = 999
        markers_path.write_text("\n".join(map(json.dumps, markers)))
        with self.assertRaises(ValueError): evidence.build_summary(Path(a["presentMonCsv"]["path"]), None, 16.67, 0, 43, markers_path)

    def test_successful_exit_without_declared_workload_is_rejected(self):
        path = self.valid_run(1)
        manifest = acceptance.load(path)
        receipt_path = Path(manifest["artifacts"]["benchmarkReceipt"]["path"])
        receipt = acceptance.load(receipt_path)
        receipt["frameTimes"]["sampleCount"] = 1
        receipt_path.write_text(json.dumps(receipt))
        manifest["artifacts"]["benchmarkReceipt"] = self.record(receipt_path)
        path.write_text(json.dumps(manifest))
        result = acceptance.validate_manifest(path)
        self.assertFalse(result["valid"])
        self.assertTrue(result["engineWorkloadErrors"])

    def test_changed_binary_inventory_and_absent_etl_block_group(self):
        runs = [self.valid_run(i) for i in range(1, 6)]
        for path in runs:
            manifest = acceptance.load(path)
            manifest["artifacts"]["etl"]["exists"] = False
            path.write_text(json.dumps(manifest))
        self.assertFalse(acceptance.validate_group(runs)["completed"])
        changed = self.write("changed-dll", "different build component")
        manifest = acceptance.load(runs[-1])
        manifest["target"]["buildFiles"].append(self.record(changed))
        runs[-1].write_text(json.dumps(manifest))
        self.assertIn("incomparable buildIdentity", acceptance.validate_group(runs)["errors"])


if __name__ == "__main__": unittest.main()
