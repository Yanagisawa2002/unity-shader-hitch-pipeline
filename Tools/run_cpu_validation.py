"""Explicit offline functional-test allowlist; no Unity or performance runner."""
from pathlib import Path
import sys
import unittest

TESTS = (
    "test_compatibility_schema",
    "test_pso_system_acceptance",
    "test_pso_windows_evidence",
    "test_validate_deadline_run",
    "test_capture_windows",
    "test_external_host",
    "test_execution_policy",
    "test_scheduling_schema",
    "test_warmup_evidence",
)

if __name__ == "__main__":
    root = Path(__file__).resolve().parent
    sys.path[:0] = [str(root), str(root / "tests")]
    suite = unittest.defaultTestLoader.loadTestsFromNames(TESTS)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(0 if result.wasSuccessful() else 1)
