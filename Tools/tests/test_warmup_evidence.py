"""Synthetic receipt contract checks only; never run the report/plot/benchmark entrypoint."""
import unittest
from pso_report import summarize_warmup


class WarmupEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.benchmark = dict(planSha256="a" * 64)
        self.receipt = dict(schemaVersion=3, strategy="observed-budget", completed=True, error="", planSha256="a" * 64,
                            phases=[dict(phase="city", completed=True, backendReportedWarmedUp=True,
                                         completedGraphicsStates=3, totalGraphicsStates=3,
                                         hardFrameBudgetMet=True, schedulerAdmissionBudgetMet=True, hardFrameBudgetFeasible=True)],
                            cacheMissTrace=dict(requested=True, armed=True, error="", scope="plan", collectionContainsBaseline=True,
                                                baselineGraphicsStates=3, observedGraphicsStates=3, cacheMissGraphicsStates=0))

    def result(self): return summarize_warmup(self.receipt, self.benchmark)

    def test_complete_armed_trace_can_prove_zero_misses(self):
        result = self.result()
        self.assertTrue(result["valid"])
        self.assertEqual(result["cacheMissGraphicsStates"], 0)

    def test_unarmed_and_unrequested_feedback_are_unavailable(self):
        for field in ("requested", "armed"):
            with self.subTest(field=field):
                self.receipt["cacheMissTrace"][field] = False
                result = self.result()
                self.assertFalse(result["valid"])
                self.assertIsNone(result["cacheMissGraphicsStates"])
                self.assertEqual(result["feedbackStatus"], "Unavailable")
                self.receipt["cacheMissTrace"][field] = True

    def test_partial_inconsistent_or_errored_baseline_is_not_zero_misses(self):
        for field, value in (("baselineGraphicsStates", 0), ("observedGraphicsStates", 2),
                             ("cacheMissGraphicsStates", True), ("error", "unresolved shaders"), ("scope", "unknown")):
            old = self.receipt["cacheMissTrace"][field]
            self.receipt["cacheMissTrace"][field] = value
            self.assertIsNone(self.result()["cacheMissGraphicsStates"])
            self.receipt["cacheMissTrace"][field] = old

    def test_cancelled_run_and_missing_plan_identity_fail(self):
        self.receipt["completed"] = False
        self.assertFalse(self.result()["valid"])
        self.receipt["completed"] = True
        self.receipt["planSha256"] = None
        self.benchmark["planSha256"] = None
        self.assertFalse(self.result()["valid"])

    def test_missing_warmup_receipt_is_not_valid_coverage(self):
        self.assertFalse(summarize_warmup(None, self.benchmark)["valid"])

    def test_new_strategies_cannot_bypass_budget_gate(self):
        self.receipt["phases"][0]["hardFrameBudgetMet"] = False
        for strategy in ("scheduled", "observed-budget", "fixed-progressive"):
            self.receipt["strategy"] = strategy
            self.assertFalse(self.result()["valid"])


if __name__ == "__main__": unittest.main()
