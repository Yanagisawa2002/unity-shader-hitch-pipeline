"""Verify the pinned official payload, then apply a small declared host overlay.

No Unity process is started. Original source/LFS identities and every changed
byte are retained in the caller's fresh ignored receipt directory.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil

from pso_external_host import ROOT, git, inspect

COMMIT = "6d51b73619199c6dc8266045ea5c494355acf6b5"
TREE = "3dea6169e8c4799e781e64af9c91043670a5df52"
LOCK = dict(schemaVersion=1, kind="external-application-scene",
            repository="https://github.com/Unity-Technologies/BoatAttack.git",
            commit=COMMIT, tree=TREE, files=[])


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def save(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2)
        stream.write("\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("host", type=Path)
    parser.add_argument("receipt", type=Path)
    args = parser.parse_args()
    host, receipt = args.host.resolve(), args.receipt.resolve()
    if not host.is_relative_to(ROOT / "work") or not receipt.is_relative_to(ROOT / "work"):
        raise ValueError("Use separate directories within this repository's ignored work directory")
    if receipt.exists() or receipt.is_relative_to(host) or host.is_relative_to(receipt):
        raise ValueError("A new independent receipt directory is required")
    destination = host / "Assets/PsoBoatAttackAdapter"
    if destination.exists():
        raise ValueError("Adapter destination already exists")
    configuration = host / "ProjectSettings/ShaderHitchPipeline.json"
    if configuration.exists():
        raise ValueError("An upstream project configuration already exists; review it before adapting")
    verified, _ = inspect(host, LOCK)
    receipt.mkdir(parents=True)
    originals = []
    for relative in git(host, "ls-tree", "-r", "--name-only", "-z", COMMIT).decode().split("\0"):
        if relative:
            path = host / relative
            originals.append(dict(path=relative, bytes=path.stat().st_size, sha256=sha(path)))
    save(receipt / "original-files.json", originals)
    verified.update(verifiedUtc=datetime.now(timezone.utc).isoformat(), originalFiles=len(originals),
                    originalBytes=sum(item["bytes"] for item in originals),
                    originalIndexSha256=sha(receipt / "original-files.json"))
    save(receipt / "verified-checkout.json", verified)
    manifest_path = host / "Packages/manifest.json"
    original = manifest_path.read_bytes()
    (receipt / "manifest-original.json").write_bytes(original)
    manifest = json.loads(original)
    package = ROOT / "Packages/com.yanagisawa.shader-hitch-pipeline"
    relative_package = Path(os.path.relpath(package, host / "Packages")).as_posix()
    if (host / "Packages" / relative_package).resolve() != package.resolve():
        raise ValueError("The local package dependency does not resolve to this repository")
    manifest["dependencies"]["com.yanagisawa.shader-hitch-pipeline"] = "file:" + relative_package
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    shutil.copy2(ROOT / "Integrations/BoatAttack/ShaderHitchPipeline.json", configuration)
    adapter = ROOT / "Integrations/BoatAttack/Adapter"
    shutil.copytree(adapter, destination)
    common = ROOT / "Integrations/ExternalScenes/Adapter"
    for path in common.iterdir():
        if path.is_file():
            shutil.copy2(path, destination / path.name)
    applied = []
    for path in sorted(destination.rglob("*")):
        if path.is_file():
            applied.append(dict(path=path.relative_to(host).as_posix(), bytes=path.stat().st_size, sha256=sha(path)))
    package_files = [dict(path=p.relative_to(ROOT).as_posix(), sha256=sha(p), bytes=p.stat().st_size)
                     for p in sorted(package.rglob("*")) if p.is_file()]
    save(receipt / "package-files.json", package_files)
    save(receipt / "applied-overlay.json", dict(appliedUtc=datetime.now(timezone.utc).isoformat(),
         upstreamCommit=COMMIT, upstreamTree=TREE, integrationCommit=git(ROOT, "rev-parse", "HEAD").decode().strip(),
         originalManifestSha256=hashlib.sha256(original).hexdigest(), manifestSha256=sha(manifest_path),
         originalDependencyCount=len(json.loads(original)["dependencies"]),
         projectConfigurationSha256=sha(configuration),
         newDependencies=["com.yanagisawa.shader-hitch-pipeline"], adapterFiles=applied,
         packageIndexSha256=sha(receipt / "package-files.json"), checkoutIsPristineAfterOverlay=False))
    print(json.dumps(verified, indent=2))


if __name__ == "__main__":
    main()
