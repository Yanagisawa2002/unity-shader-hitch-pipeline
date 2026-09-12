#!/usr/bin/env python3
"""Create portable public copies of receipts without local absolute paths."""

from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def normalize(document: dict[str, Any], original: Path) -> dict[str, Any]:
    changed = []
    if document.get("planFile"):
        document["planFile"] = (
            "${PLAYER_DATA}/StreamingAssets/ShaderHitchPipeline/plan.json"
        )
        changed.append("planFile")
    feedback = document.get("cacheMissTrace")
    if isinstance(feedback, dict) and feedback.get("collectionFile"):
        feedback["collectionFile"] = Path(feedback["collectionFile"]).name
        changed.append("cacheMissTrace.collectionFile")
    document["publicationNormalization"] = {
        "generatedUtc": document.get("endedUtc")
        or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "originalSha256": sha256(original),
        "changedFields": changed,
        "reason": "Local absolute paths removed; measurements and plan hash unchanged.",
    }
    return document


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--benchmark", required=True, type=Path)
    parser.add_argument("--warmup", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    args.output.mkdir(parents=True, exist_ok=True)
    for name, source in (
        ("scheduled.benchmark.json", args.benchmark),
        ("scheduled.warmup.json", args.warmup),
    ):
        destination = args.output / name
        destination.write_text(
            json.dumps(normalize(load(source), source), indent=2, ensure_ascii=False)
            + "\n",
            encoding="utf-8",
        )
        print(destination)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
