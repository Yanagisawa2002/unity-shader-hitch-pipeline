import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pso_external_host as host


class ExternalHostTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.payload = b'{"dependencies":{"com.unity.entities":"1.3.14"}}\n'
        self.lock = dict(schemaVersion=1, kind="external-application-scene", repository="https://example.invalid/upstream.git",
                         commit="a" * 40, tree="b" * 40, files=[dict(path="Packages/manifest.json", sha256=host.digest(self.payload))])
        self.status = b""
        self.head = "a" * 40
        self.tree = b""
        self.blobs = {}

    def git(self, repo, *args, input_data=None):
        if args == ("rev-parse", "--show-toplevel"): return str(self.root).encode()
        if args == ("rev-parse", "HEAD"): return self.head.encode()
        if args == ("rev-parse", "a" * 40 + "^{tree}"): return b"b" * 40
        if args[0] == "show": return self.payload
        if args[0] == "status": return self.status
        if args[0] == "ls-tree": return self.tree
        if args == ("cat-file", "--batch"):
            return b"".join(oid + b" blob " + str(len(self.blobs[oid])).encode() + b"\n" + self.blobs[oid] + b"\n"
                            for oid in input_data.splitlines())
        raise AssertionError("Unexpected Git operation " + repr(args))

    def test_pristine_inspection_never_claims_compiled_or_measured(self):
        with patch.object(host, "git", side_effect=self.git):
            report, _ = host.inspect(self.root, self.lock)
        self.assertTrue(report["checkoutVerified"])
        self.assertTrue(report["preparationOnly"])
        self.assertEqual(report["compileStatus"], "NotCompiled")
        self.assertEqual(report["runtimeStatus"], "NotRun")
        self.assertEqual(report["measurementStatus"], "Unmeasured")

    def test_dirty_old_adapter_rejected(self):
        self.status = b" M Assets/Scenes/Main.unity\n?? Assets/ShaderHitchPipelineBenchmark/\n"
        with patch.object(host, "git", side_effect=self.git), self.assertRaisesRegex(ValueError, "local changes"):
            host.inspect(self.root, self.lock)

    def test_objects_only_is_not_asset_readiness(self):
        self.status = b" M Assets/Scenes/Main.unity\n"
        with patch.object(host, "git", side_effect=self.git):
            report, _ = host.inspect(self.root, self.lock, objects_only=True)
        self.assertTrue(report["sourceVerified"])
        self.assertFalse(report["checkoutVerified"])
        self.assertFalse(report["lfsPayloadsVerified"])
        self.assertTrue(report["checkoutIssues"])

    def test_wrong_revision_rejected(self):
        self.head = "c" * 40
        with patch.object(host, "git", side_effect=self.git), self.assertRaisesRegex(ValueError, "HEAD"):
            host.inspect(self.root, self.lock)

    def test_source_digest_mismatch_rejected(self):
        self.lock["files"][0]["sha256"] = "0" * 64
        with patch.object(host, "git", side_effect=self.git), self.assertRaisesRegex(ValueError, "source bytes"):
            host.inspect(self.root, self.lock, True)

    def test_missing_asset_rejected(self):
        self.tree = b"100644 blob " + b"c" * 40 + b" 120\tAssets/missing.bin\0"
        with patch.object(host, "git", side_effect=self.git), self.assertRaisesRegex(ValueError, "Missing upstream"):
            host.inspect(self.root, self.lock)

    def asset(self, original, actual):
        oid = hashlib.sha1(b"blob " + str(len(original)).encode() + b"\0" + original).hexdigest().encode()
        self.blobs[oid] = original
        self.tree = b"100644 blob " + oid + b" " + str(len(original)).encode() + b"\tasset.bin\0"
        (self.root / "asset.bin").write_bytes(actual)

    def test_worktree_hash_detects_change_hidden_from_git_status(self):
        self.asset(b"upstream", b"mutated")
        with patch.object(host, "git", side_effect=self.git), self.assertRaisesRegex(ValueError, "Worktree bytes"):
            host.inspect(self.root, self.lock)

    def test_normal_text_crlf_is_accepted_without_custom_filters(self):
        self.asset(b"upstream\n", b"upstream\r\n")
        with patch.object(host, "git", side_effect=self.git):
            report, _ = host.inspect(self.root, self.lock)
        self.assertTrue(report["checkoutVerified"])

    def test_unhydrated_lfs_is_rejected(self):
        pointer = b"version https://git-lfs.github.com/spec/v1\noid sha256:" + host.digest(b"payload").encode() + b"\nsize 7\n"
        self.asset(pointer, pointer)
        with patch.object(host, "git", side_effect=self.git), self.assertRaisesRegex(ValueError, "LFS asset"):
            host.inspect(self.root, self.lock)

    def test_lfs_payload_digest_verified(self):
        pointer = b"version https://git-lfs.github.com/spec/v1\noid sha256:" + host.digest(b"payload").encode() + b"\nsize 7\n"
        self.asset(pointer, b"payload")
        with patch.object(host, "git", side_effect=self.git):
            report, _ = host.inspect(self.root, self.lock)
        self.assertTrue(report["lfsPayloadsVerified"])

    def test_preparation_cannot_write_to_upstream_checkout(self):
        with patch.object(host, "git", side_effect=self.git), self.assertRaisesRegex(ValueError, "separate"):
            host.prepare(self.root, self.root / "overlay", self.lock, True)
        self.assertFalse((self.root / "overlay").exists())

    def test_portable_path_rejects_traversal_and_windows_drive(self):
        for value in ("../escape", "/rooted", "C:/rooted", "..\\escape", "file:asset", ""):
            with self.subTest(value=value), self.assertRaises(ValueError): host.child(self.root, value)

    def test_lock_requires_immutable_full_commit(self):
        lock = copy.deepcopy(self.lock)
        lock["commit"] = "master"
        path = self.root / "lock.json"
        path.write_text(json.dumps(lock))
        with self.assertRaisesRegex(ValueError, "immutable"): host.read_lock(path)

    def test_checked_in_recipe_retains_unknown_evidence(self):
        contract = json.loads((host.ROOT / "Integrations/MegacityMetroNative/workload-contract.json").read_text())
        self.assertEqual(contract["kind"], "external-application-scene")
        self.assertTrue(contract["preparationOnly"])
        self.assertEqual([x["name"] for x in contract["arms"]], ["cold", "all-at-once", "scheduled", "fixed-progressive", "observed-budget"])
        for key in ("playerBuildGuid", "shaderBuildIdentity", "planSha256", "collectionSha256"):
            self.assertIsNone(contract["sharedInputs"][key])
        self.assertTrue(all(x["status"] == "Unmeasured" for x in contract["arms"]))


if __name__ == "__main__": unittest.main()
