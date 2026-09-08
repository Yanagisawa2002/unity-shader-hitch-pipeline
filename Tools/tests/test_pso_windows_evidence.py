import importlib.util
import json
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "pso_windows_evidence", ROOT / "Tools" / "pso_windows_evidence.py"
)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class PresentMonEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def qpc_fixture(self, column="CPUStartQPC"):
        # Deliberately above 2**53: converting absolute ticks through float loses
        # the one-tick distinction that decides membership of the narrow window.
        self.base = 9007199254740993
        self.markers = [
            {"processId": 42, "sessionId": "native-session", "clockSource": MODULE.NATIVE_QPC_SOURCE,
             "qpcFrequency": 1000000, "qpcBracketTicks": 2, "qpc": self.base + delta,
             "utc": utc, "name": name, "phase": "combat"}
            for delta, utc, name in [
                (0, "2026-09-07T06:49:55.000000Z", "phase-start"),
                (1000000, "2026-09-07T06:49:56.000000Z", "phase-end")]
        ]
        self.marker_path = self.root / "markers.jsonl"
        self.save_markers()
        self.csv_path = self.root / "qpc.csv"
        # The contradictory date column must never override valid native QPC.
        self.csv_path.write_text(
            f"ProcessID,SwapChainAddress,{column},CPUStartDateTime,FrameTime\n"
            f"999,0xA,invalid,invalid,500\n"
            f"42,0xA,{self.base + 1},2026-9-7 22:49:55.000001000,12\n"
            f"42,0xA,{self.base + 2},2026-9-7 22:49:55.000002000,25\n", encoding="utf-8")
        self.receipt_path = self.root / "warmup.json"
        self.receipt_path.write_text(json.dumps({"startedUtc": "2026-09-07T06:49:55.000001Z",
                                               "endedUtc": "2026-09-07T06:49:55.000001Z"}), encoding="utf-8")

    def save_markers(self):
        self.marker_path.write_text("\n".join(map(json.dumps, self.markers)), encoding="utf-8")

    def summary(self):
        return MODULE.build_summary(self.csv_path, self.receipt_path, 16.67, 480, 42, self.marker_path)

    def test_native_qpc_aliases_preserve_integer_ticks_and_select_phase(self):
        for column in ("CPUStartQPC", "TimeInQPC"):
            with self.subTest(column=column):
                self.qpc_fixture(column)
                summary = self.summary()
                self.assertEqual(summary["rowSelection"]["analyzedRows"], 1)
                self.assertEqual(summary["rowSelection"]["timeColumn"], column)
                self.assertEqual(summary["rowSelection"]["clockCorrelation"]["anchorQpc"], self.base)
                self.assertFalse(summary["rowSelection"]["clockCorrelation"]["localUtcOffsetApplied"])
                self.assertEqual(summary["metrics"]["presentedFrameMilliseconds"]["maximumMilliseconds"], 12)
                self.assertEqual(summary["phaseCorrelation"]["windows"][0]["rows"], 2)
                self.assertEqual(summary["tailFrames"][0]["cpuStartUtc"], "2026-09-07T06:49:55.000001Z")

    def test_qpc_does_not_fall_back_when_markers_missing_or_wrong_source(self):
        self.qpc_fixture()
        with self.assertRaisesRegex(ValueError, "requires native"):
            MODULE.build_summary(self.csv_path, self.receipt_path, 16.67, 480, 42)
        for source in (None, "stopwatch", "windows-query-performance-counter-v0"):
            self.qpc_fixture()
            self.markers[0]["clockSource"] = source
            self.save_markers()
            with self.subTest(source=source), self.assertRaisesRegex(ValueError, "clockSource"):
                self.summary()

    def test_qpc_rejects_foreign_pid_and_mixed_sessions(self):
        for key, value in (("processId", 43), ("sessionId", "other-session")):
            self.qpc_fixture()
            self.markers[-1][key] = value
            self.save_markers()
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.summary()

    def test_qpc_requires_positive_constant_frequency_and_valid_bracket(self):
        for key, value in (("qpcFrequency", 0), ("qpcFrequency", 999999),
                           ("qpcBracketTicks", -1), ("qpcBracketTicks", 10001),
                           ("qpcBracketTicks", None), ("qpc", 1.5)):
            self.qpc_fixture()
            self.markers[-1][key] = value
            self.save_markers()
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                self.summary()

    def test_qpc_rejects_clock_discontinuity_and_cumulative_drift(self):
        self.qpc_fixture()
        self.markers[-1]["qpc"] += 20000
        self.save_markers()
        with self.assertRaisesRegex(ValueError, "discontinuity"):
            self.summary()
        self.qpc_fixture()
        anchor = self.markers[0]
        self.markers = [dict(anchor, qpc=self.base + i * 1000000,
                             utc=f"2026-09-07T06:49:{55+i:02d}.{i*6000:06d}Z") for i in range(4)]
        self.save_markers()
        # Adjacent error is only 6 ms; absolute error is 18 ms. Do not fit it away.
        with self.assertRaisesRegex(ValueError, "discontinuity"):
            self.summary()

    def test_qpc_rejects_backwards_or_single_anchors_and_naive_utc(self):
        for mutation in ("backwards", "single", "naive"):
            self.qpc_fixture()
            if mutation == "backwards": self.markers[-1]["qpc"] = self.base - 1
            if mutation == "single": self.markers = self.markers[:1]
            if mutation == "naive": self.markers[-1]["utc"] = "2026-09-07T06:49:56"
            self.save_markers()
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                self.summary()

    def test_qpc_malformed_rows_fail_without_using_date_column(self):
        for value in ("NA", "1.5", "-1", "1e6"):
            self.qpc_fixture()
            self.csv_path.write_text(f"ProcessID,CPUStartQPC,CPUStartDateTime,FrameTime\n42,{value},2026-9-7 14:49:55,10\n")
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, "integer ticks"):
                self.summary()

    def test_qpc_without_matching_window_stays_unavailable(self):
        self.qpc_fixture()
        self.receipt_path.write_text(json.dumps({"startedUtc": "2026-09-07T05:00:00Z", "endedUtc": "2026-09-07T05:00:01Z"}))
        summary = self.summary()
        self.assertEqual(summary["rowSelection"]["analyzedRows"], 0)
        self.assertFalse(summary["budgetVerdict"]["presentMonPresentedFramesMet"])

    def test_unpadded_real_date_syntax_does_not_infer_offset(self):
        parsed = MODULE.parse_presentmon_time("2026-9-7 22:49:55.590537700", 480)
        self.assertEqual(parsed, datetime(2026, 9, 7, 14, 49, 55, 590537, tzinfo=timezone.utc))
        self.assertIsNone(MODULE.parse_presentmon_time("2026-13-7 22:49:55", 480))
        self.qpc_fixture()
        self.csv_path.write_text("ProcessID,CPUStartDateTime,FrameTime\n42,2026-9-7 22:49:55.590537700,12\n")
        summary = MODULE.build_summary(self.csv_path, self.receipt_path, 16.67, 480, 42)
        self.assertEqual(summary["rowSelection"]["analyzedRows"], 0)

    def test_dominant_swap_chain_and_tail_are_reported(self):
        fixture = Path(__file__).parent / "fixtures" / "presentmon-v2.csv"
        summary = MODULE.build_summary(fixture, None, 16.67, 0)

        self.assertEqual(summary["rowSelection"]["dominantSwapChain"], "0xA")
        self.assertEqual(summary["rowSelection"]["analyzedRows"], 4)
        frame = summary["metrics"]["presentedFrameMilliseconds"]
        self.assertEqual(frame["sourceColumn"], "FrameTime")
        self.assertEqual(frame["maximumMilliseconds"], 100.0)
        self.assertEqual(frame["overBudgetCount"], 2)
        self.assertEqual(summary["tailFrames"][0]["cpuBusyMilliseconds"], 91.0)


if __name__ == "__main__":
    unittest.main()
