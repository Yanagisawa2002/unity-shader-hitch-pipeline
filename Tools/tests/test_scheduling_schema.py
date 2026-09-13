import json
from pathlib import Path
import tempfile
import unittest
from jsonschema import Draft202012Validator
from validate_pso_documents import validate


class SchedulingSchemaTests(unittest.TestCase):
    def setUp(self):
        root = Path(__file__).resolve().parents[2] / "Schemas"
        self.path = root / "scheduling-feedback.schema.json"
        self.schema = json.loads(self.path.read_text())
        self.validator = Draft202012Validator(self.schema)
        self.receipt = json.loads((root / "warmup-receipt.schema.json").read_text())

    def fixture(self):
        phase = dict(phase="native-scene", state="Cancelled", failure="", lastAdmissionReason="waiting-for-fence",
                     costInvalidationReason="", activation=1, completedPermutations=7, totalGraphicsStates=3,
                     costGeneration=0, deferredFrames=0, backendReportedWarmedUp=False, hasInFlightBatch=True,
                     ownerReleased=False, deadlineMissed=False, deadlineFeasible=True,
                     requiresNonInteractiveWindow=False, elapsedMilliseconds=0.0)
        return dict(schemaVersion=1, planSha256="a" * 64, policy="observed-budget", implementation="scheduler-lifecycle-v1",
                    performanceClaim="Unmeasured; no comparative result", latencyGuarantee="No opaque driver latency bound",
                    deadlinesEnabled=True, adaptiveCostEnabled=True, hotSetPriorityEnabled=True, costPriorityEnabled=True,
                    conservativeAdmission=True, completed=False, hasInFlightBatch=True, hasPendingRetirement=False,
                    fixedBatchSize=0, activations=[phase])

    def test_new_schema_is_valid_and_cancelled_fence_is_not_completion(self):
        Draft202012Validator.check_schema(self.schema)
        self.validator.validate(self.fixture())

    def test_completed_requires_fenced_native_completion(self):
        doc = self.fixture()
        doc["activations"][0]["state"] = "Completed"
        self.assertTrue(list(self.validator.iter_errors(doc)))
        doc["activations"][0].update(backendReportedWarmedUp=True, hasInFlightBatch=False)
        doc.update(hasInFlightBatch=False, completed=True)
        self.validator.validate(doc)

    def test_released_owner_cannot_still_hold_inflight_work(self):
        doc = self.fixture()
        doc["activations"][0]["ownerReleased"] = True
        self.assertTrue(list(self.validator.iter_errors(doc)))

    def test_new_warmup_labels_and_historical_labels_remain_valid(self):
        for value in ("scheduled", "throughput", "observed-budget", "fixed-progressive"):
            Draft202012Validator(self.receipt["properties"]["strategy"]).validate(value)
        props = self.receipt["$defs"]["phaseReceipt"]["properties"]
        Draft202012Validator(props["hardFrameBudgetOutcome"]).validate("incomplete")
        Draft202012Validator(props["hardBudgetGuaranteeScope"]).validate(
            "estimated-full-batch-admission; native-async-bulk-not-preemptible; no-hard-latency-bound")

    def test_validator_rejects_nonfinite_json_before_schema(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "feedback.json"
            for number in ("NaN", "Infinity", "1e999"):
                path.write_text('{"schemaVersion": ' + number + '}')
                self.assertIn("Non-finite", validate(path, self.path)[0])


if __name__ == "__main__": unittest.main()
