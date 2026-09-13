import copy
import json
import unittest
from pathlib import Path

from jsonschema import Draft202012Validator


class CompatibilitySchemaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(__file__).resolve().parents[2] / "Schemas"
        cls.plan = json.loads((root / "warmup-plan.schema.json").read_text())
        cls.trace = json.loads((root / "trace-session.schema.json").read_text())
        cls.cache = json.loads((root / "cost-cache.schema.json").read_text())

    def identity(self):
        return dict(version=1, source="unity-build-inputs-v1", buildInputSha256="a" * 64,
                    shaderSha256="b" * 64, contentSha256="c" * 64,
                    contentId="player-content", contentRevision="r1")

    def environment(self):
        return dict(engineName="Unity", engineVersion="6000.5.2f1", unityVersion="6000.5.2f1",
                    runtimePlatform="WindowsPlayer", graphicsDeviceType="Direct3D12",
                    qualityLevelName="High", graphicsDeviceId=123, graphicsDeviceVendorId=4098,
                    graphicsDeviceName="R9700", graphicsDeviceVendor="AMD", identity=self.identity())

    def test_schemas_are_valid(self):
        for schema in (self.plan, self.trace, self.cache):
            Draft202012Validator.check_schema(schema)

    def test_legacy_absent_or_null_contract_allowed(self):
        validator = Draft202012Validator(self.plan["properties"]["compatibility"])
        self.assertFalse(list(validator.iter_errors(None)))
        self.assertNotIn("compatibility", self.plan["required"])

    def test_identity_bound_contract(self):
        validator = Draft202012Validator(self.plan["properties"]["compatibility"])
        document = dict(version=1, collectionEnvironment=self.environment(), costEnvironment=None,
                        costModelVersion="adaptive-online-v1")
        self.assertFalse(list(validator.iter_errors(document)))
        for field in self.identity():
            bad = copy.deepcopy(document)
            del bad["collectionEnvironment"]["identity"][field]
            self.assertTrue(list(validator.iter_errors(bad)), field)
        for value in (0, 2):
            bad = copy.deepcopy(document)
            bad["version"] = value
            self.assertTrue(list(validator.iter_errors(bad)))

    def test_bad_and_zero_hashes_rejected(self):
        validator = Draft202012Validator(self.plan["properties"]["compatibility"])
        for value in ("", "corrupt", "0" * 64, "g" * 64):
            environment = self.environment()
            environment["identity"]["shaderSha256"] = value
            self.assertTrue(list(validator.iter_errors(dict(version=1, collectionEnvironment=environment))))

    def test_cache_needs_measured_entries_and_known_version(self):
        validator = Draft202012Validator(self.cache)
        document = dict(version=1, modelVersion="adaptive-online-v1", planSha256="e" * 64,
                        environment=self.environment(), cacheSha256="f" * 64,
                        entries=[dict(phase="startup", collectionSha256="d" * 64,
                                      millisecondsPerState=0.5, observedBatches=2)])
        self.assertFalse(list(validator.iter_errors(document)))
        document["entries"][0]["observedBatches"] = 0
        self.assertTrue(list(validator.iter_errors(document)))
        document["entries"] = []
        self.assertTrue(list(validator.iter_errors(document)))


if __name__ == "__main__":
    unittest.main()
