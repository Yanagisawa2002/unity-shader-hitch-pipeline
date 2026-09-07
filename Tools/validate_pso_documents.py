#!/usr/bin/env python3
"""Validate generated Shader Hitch Pipeline documents against schema v3."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from jsonschema import Draft202012Validator


SCHEMAS = {
    "trace": "trace-session.schema.json",
    "plan": "warmup-plan.schema.json",
    "warmup": "warmup-receipt.schema.json",
}


def validate(document_path: Path, schema_path: Path) -> list[str]:
    document = json.loads(document_path.read_text(encoding="utf-8-sig"))
    schema = json.loads(schema_path.read_text(encoding="utf-8-sig"))
    errors = sorted(
        Draft202012Validator(schema).iter_errors(document),
        key=lambda error: list(error.absolute_path),
    )
    return [
        f"{document_path}: {list(error.absolute_path)}: {error.message}"
        for error in errors
    ]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--schemas", type=Path, default=Path("Schemas"))
    parser.add_argument("--trace", action="append", type=Path, default=[])
    parser.add_argument("--plan", action="append", type=Path, default=[])
    parser.add_argument("--warmup", action="append", type=Path, default=[])
    args = parser.parse_args()

    failures: list[str] = []
    count = 0
    for kind in SCHEMAS:
        schema_path = args.schemas / SCHEMAS[kind]
        for document_path in getattr(args, kind):
            count += 1
            errors = validate(document_path, schema_path)
            failures.extend(errors)
            if not errors:
                print(f"SCHEMA_OK {kind} {document_path}")

    if count == 0:
        parser.error("Provide at least one --trace, --plan, or --warmup document.")
    if failures:
        print("\n".join(failures))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
