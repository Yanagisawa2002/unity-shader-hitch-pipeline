"""Prepare a pinned external application overlay. No Unity, Player or benchmark execution.

All Git subprocesses are read-only; missing partial-clone blobs cannot trigger a fetch.
Objects-only preparation intentionally does not certify a dirty or missing asset worktree.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
LOCK = ROOT / "Integrations/MegacityMetroNative/source-lock.json"


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def child(root: Path, relative: str) -> Path:
    if not isinstance(relative, str) or "\\" in relative or ":" in relative:
        raise ValueError("Expected a portable relative path")
    path = PurePosixPath(relative)
    if path.is_absolute() or not path.parts or ".." in path.parts:
        raise ValueError("Path escapes its root")
    result = (root / relative).resolve()
    if not result.is_relative_to(root.resolve()) or result == root.resolve():
        raise ValueError("Path escapes its root")
    return result


def git(repo: Path, *arguments: str, input_data: bytes | None = None) -> bytes:
    env = dict(os.environ, GIT_NO_LAZY_FETCH="1", GIT_OPTIONAL_LOCKS="0", GIT_LFS_SKIP_SMUDGE="1")
    result = subprocess.run(["git", "-C", str(repo), *arguments], check=True,
                            capture_output=True, env=env, input=input_data)
    return result.stdout


def read_lock(path: Path) -> dict:
    lock = json.loads(path.read_text(encoding="utf-8"))
    if lock.get("schemaVersion") != 1 or lock.get("kind") != "external-application-scene":
        raise ValueError("Unsupported external source lock")
    if not re.fullmatch(r"[0-9a-f]{40}", lock.get("commit", "")):
        raise ValueError("External source must use an immutable full Git commit")
    if not re.fullmatch(r"[0-9a-f]{40}", lock.get("tree", "")):
        raise ValueError("External source must bind its full Git tree")
    for entry in lock["files"]:
        child(ROOT, entry["path"])
        if not re.fullmatch(r"[0-9a-f]{64}", entry.get("sha256", "")):
            raise ValueError("Invalid source digest")
    return lock


def inspect(repo: Path, lock: dict, objects_only: bool = False) -> tuple[dict, dict[str, bytes]]:
    repo = repo.resolve(strict=True)
    if Path(git(repo, "rev-parse", "--show-toplevel").decode().strip()).resolve() != repo:
        raise ValueError("Specify the external repository root")
    commit = lock["commit"]
    if git(repo, "rev-parse", commit + "^{tree}").decode().strip() != lock["tree"]:
        raise ValueError("Pinned Git tree differs from source lock")
    blobs = {}
    for entry in lock["files"]:
        data = git(repo, "show", commit + ":" + entry["path"])
        if digest(data) != entry["sha256"]:
            raise ValueError("Pinned source bytes differ: " + entry["path"])
        blobs[entry["path"]] = data
    # Hash the original dependency graph and scene identities, never the old local experiment.
    issues = []
    if git(repo, "rev-parse", "HEAD").decode().strip() != commit:
        issues.append("Checkout HEAD does not match source commit")
    if git(repo, "status", "--porcelain=v1", "--untracked-files=all"):
        issues.append("Checkout contains local changes or untracked files")
    lfs_verified = False
    if not objects_only and not issues:
        # Read each small Git blob once; LFS pointers bind actual large asset payloads.
        tree = git(repo, "ls-tree", "-r", "--long", "-z", commit)
        records = []
        for record in tree.split(b"\0"):
            if not record:
                continue
            metadata, name = record.split(b"\t", 1)
            mode, kind, oid, size = metadata.split()
            relative = name.decode("utf-8")
            if mode not in (b"100644", b"100755") or kind != b"blob":
                raise ValueError("Unsupported external file mode: " + relative)
            target = child(repo, relative)
            if not target.is_file():
                raise ValueError("Missing upstream asset: " + relative)
            # Any worktree symlink is rejected, even if it points back inside the root.
            if (repo / relative).is_symlink():
                raise ValueError("Unexpected worktree symlink: " + relative)
            records.append((oid, int(size), relative, target))
        small_ids = list(dict.fromkeys(oid for oid, size, _, _ in records if size <= 1024))
        small = {}
        if small_ids:
            batch = git(repo, "cat-file", "--batch", input_data=b"\n".join(small_ids) + b"\n")
            offset = 0
            for expected_oid in small_ids:
                end = batch.index(b"\n", offset)
                header = batch[offset:end].split()
                if len(header) != 3 or header[0] != expected_oid or header[1] != b"blob":
                    raise ValueError("Missing or invalid local Git blob")
                size = int(header[2])
                small[expected_oid] = batch[end + 1:end + 1 + size]
                offset = end + size + 2
        for oid, size, relative, target in records:
            data = small.get(oid, b"")
            if data.startswith(b"version https://git-lfs.github.com/spec/v1\n"):
                match = re.fullmatch(rb"version https://git-lfs.github.com/spec/v1\noid sha256:([0-9a-f]{64})\nsize ([0-9]+)\n", data)
                if not match:
                    raise ValueError("Unsupported LFS pointer: " + relative)
                with target.open("rb") as stream:
                    actual = hashlib.file_digest(stream, "sha256").hexdigest()
                if target.stat().st_size != int(match[2]) or actual != match[1].decode():
                    raise ValueError("Unhydrated or corrupt LFS asset: " + relative)
            else:
                actual = target.read_bytes()
                def object_id(value):
                    return hashlib.sha1(b"blob " + str(len(value)).encode() + b"\0" + value).hexdigest().encode()
                if object_id(actual) != oid:
                    # Only permit the normal text-checkout CRLF conversion, not custom Git filters.
                    if b"\0" in actual or object_id(actual.replace(b"\r\n", b"\n")) != oid:
                        raise ValueError("Worktree bytes differ from locked source: " + relative)
        lfs_verified = True
    if issues and not objects_only:
        raise ValueError("; ".join(issues))
    return dict(schemaVersion=1, kind=lock["kind"], repository=lock["repository"],
                sourceCommit=commit, sourceTree=lock["tree"], sourceVerified=True,
                checkoutVerified=not objects_only and not issues, lfsPayloadsVerified=lfs_verified,
                checkoutIssues=issues, preparationMode="git-objects-only" if objects_only else "verified-checkout",
                measurementStatus="Unmeasured", preparationOnly=True,
                compileStatus="NotCompiled", runtimeStatus="NotRun"), blobs


def prepare(repo: Path, output: Path, lock: dict, objects_only: bool = False) -> dict:
    report, blobs = inspect(repo, lock, objects_only)
    output = output.resolve()
    if output.is_relative_to(repo.resolve()) or repo.resolve().is_relative_to(output):
        raise ValueError("Preparation output must be separate from the external checkout")
    if not output.is_relative_to(ROOT):
        raise ValueError("Preparation output must be within this adapter repository")
    if output.exists():
        raise ValueError("Use a fresh preparation directory")
    manifest = json.loads(blobs["Packages/manifest.json"])
    dependencies = manifest["dependencies"]
    for name, path in {
        "com.yanagisawa.shader-hitch-pipeline": ROOT / "Packages/com.yanagisawa.shader-hitch-pipeline",
        "com.yanagisawa.shader-hitch-native-scenes": ROOT / "Integrations/MegacityMetroNative/Package",
    }.items():
        if name in dependencies:
            raise ValueError("Upstream unexpectedly contains adapter dependency: " + name)
        dependencies[name] = "file:" + path.as_posix()
    output.mkdir(parents=True)
    # No scene/material copy or scene rewrite. This is a reviewable UPM manifest overlay only.
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    recipe = json.loads((ROOT / "Integrations/MegacityMetroNative/workload-contract.json").read_text(encoding="utf-8"))
    report["manifestOverlaySha256"] = digest((output / "manifest.json").read_bytes())
    report["sourceLockSha256"] = digest(LOCK.read_bytes())
    report["workloadContractSha256"] = digest((ROOT / "Integrations/MegacityMetroNative/workload-contract.json").read_bytes())
    report["upstreamManifestSha256"] = digest(blobs["Packages/manifest.json"])
    report["upstreamPackageLockSha256"] = digest(blobs["Packages/packages-lock.json"])
    report["adapterSourceCommit"] = git(ROOT, "rev-parse", "HEAD").decode().strip()
    report["adapterSourceDirty"] = bool(git(ROOT, "status", "--porcelain=v1", "--untracked-files=all"))
    report["adapterPackageFiles"] = []
    for package in (ROOT / "Packages/com.yanagisawa.shader-hitch-pipeline", ROOT / "Integrations/MegacityMetroNative/Package"):
        for file in sorted(package.rglob("*")):
            if file.is_file():
                report["adapterPackageFiles"].append({"path": file.relative_to(ROOT).as_posix(), "sha256": digest(file.read_bytes())})
    report["nextAction"] = "Review overlay; verify a pristine hydrated checkout, resolve the dependency graph, and use the separate build/runtime commands"
    (output / "workload-contract.json").write_text(json.dumps(recipe, indent=2) + "\n", encoding="utf-8")
    (output / "preparation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("verify", "prepare"))
    parser.add_argument("--checkout", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--git-objects-only", action="store_true",
                        help="Prepare from locked Git objects without certifying local asset readiness")
    args = parser.parse_args()
    try:
        lock = read_lock(LOCK)
        if args.action == "prepare":
            if args.output is None:
                parser.error("prepare requires --output")
            report = prepare(args.checkout, args.output, lock, args.git_objects_only)
        else:
            report, _ = inspect(args.checkout, lock, args.git_objects_only)
        print(json.dumps(report, indent=2))
        return 0
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(json.dumps({"result": "Rejected", "error": str(error), "measurementStatus": "Unmeasured",
                          "preparationOnly": True}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
