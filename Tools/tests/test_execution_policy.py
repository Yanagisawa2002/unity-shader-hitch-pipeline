"""Inspect entrypoints without importing/invoking a performance script."""
from pathlib import Path
import unittest


class ExecutionPolicyTests(unittest.TestCase):
    def test_historical_runtime_entrypoints_remain_normal_explicit_commands(self):
        root = Path(__file__).resolve().parents[1]
        names = ("Invoke-PsoShowcase", "Find-PsoWarmupPolicy", "Invoke-PsoDeadlineRun", "Invoke-PsoMegacityMetro",
                 "Invoke-PsoAddressablesSmoke", "Invoke-PsoHotsetFixture", "Invoke-PsoSystemMatrix",
                 "Invoke-PsoWindowsEvidence", "Invoke-PsoCapturePairs", "Invoke-PsoIntegratedRegression")
        for name in names:
            with self.subTest(script=name):
                text = (root / (name + ".ps1")).read_text(encoding="utf-8-sig")
                self.assertNotIn("AllowPerformanceExecution", text)
                self.assertNotIn("PsoExecutionPolicy", text)
                self.assertNotIn("user authorization", text)

    def test_safe_entry_does_not_call_unity_or_discover_tests(self):
        root = Path(__file__).resolve().parents[1]
        text = (root / "Invoke-PsoValidation.ps1").read_text()
        self.assertNotIn("Start-Process", text)
        self.assertNotIn("& $editor", text)
        self.assertNotIn("-executeMethod", text)
        self.assertNotIn("-runTests", text)
        self.assertNotIn("unittest discover", text)
        self.assertNotIn("AllowPerformanceExecution", text)


if __name__ == "__main__": unittest.main()
