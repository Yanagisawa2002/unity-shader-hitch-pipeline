import importlib.util
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "pso_windows_evidence", ROOT / "Tools" / "pso_windows_evidence.py"
)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class PresentMonEvidenceTests(unittest.TestCase):
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
