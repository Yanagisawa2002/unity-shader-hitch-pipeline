"""Bounded generation-2 file preparation for one pinned Unity NSISBI module."""
from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import lzma
import os
from pathlib import Path, PureWindowsPath
import shutil
import stat
import struct
import time
import zlib

ROOT = Path(__file__).resolve().parents[1]
LEASE = Path(r"D:\CodexWork\whole-task-validation-20260915\coordination\local-preparation-lease.json")
OUTPUT = ROOT / "work/whole-task-20260915/private-module-extraction-g2"
TOOLCHAIN = ROOT / "work/toolchains/unity-6000.5.9f1"
INSTALLER = TOOLCHAIN / "downloads/UnitySetup-Linux-IL2CPP-Support-for-Editor-6000.5.9f1.exe"
TARGET = TOOLCHAIN / "Editor/Data/PlaybackEngines/LinuxStandaloneSupport"
STAGING = OUTPUT / "staging/Editor/Data/PlaybackEngines/LinuxStandaloneSupport"
SLICE = 65536
MAX_BLOCK = 8 * 1024 * 1024
MAX_WRITE = 1024 ** 3 - 16 * 1024 ** 2
EXPECTED_SHA = "6969513ad3ce6549d04dab5653e517337ee14319e21364eb1f2b161ae5ccb493"
EXPECTED_MD5 = "7de09886346cee6228c7754635ee378e"


def require(ok, message):
    if not ok:
        raise ValueError(message)


def check_length(actual, expected):
    require(actual == expected, f"Raw uint64 length mismatch: actual={actual}, expected={expected}")


def check_crc(actual, expected):
    require(f"{actual:08x}" == expected,
            f"Payload CRC32 mismatch: actual={actual:08x}, expected={expected}")


def dump(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8", newline="\n")


def safe_relative(value):
    require(isinstance(value, str) and value, "Empty path")
    path = PureWindowsPath(value)
    require(not path.is_absolute() and not path.drive and not path.root,
            "Rooted path")
    raw_parts = value.replace("\\", "/").split("/")
    for part in raw_parts:
        require(part not in ("", ".", "..") and part[-1:] not in (".", " "),
                "Traversal or aliased path")
        require(not any(c in part for c in ':<>|?*"') and
                all(ord(c) >= 32 for c in part), "Invalid path component")
        stem = part.split(".")[0].upper()
        require(stem not in {"CON", "PRN", "AUX", "NUL"} and
                stem not in {f"{p}{i}" for p in ("COM", "LPT") for i in range(1, 10)},
                "Reserved path")
    return Path(*raw_parts)


def reject_link(info):
    require(not stat.S_ISLNK(info.st_mode) and
            not (getattr(info, "st_file_attributes", 0) & 0x400),
            "Link or Windows reparse point")
    if stat.S_ISREG(info.st_mode):
        require(getattr(info, "st_nlink", 1) == 1, "Hard-linked file")


def check_parents(path, allow_missing=False):
    absolute = Path(os.path.abspath(path))
    require(absolute.is_relative_to(ROOT), "Path outside exact task root")
    # Include ancestors above the task root: no redirected drive/workspace parent.
    chain = list(reversed(absolute.parents)) + [absolute]
    missing = False
    for part in chain:
        if missing:
            continue
        try:
            info = part.lstat()
        except FileNotFoundError:
            require(allow_missing, f"Missing path: {part}")
            missing = True
            continue
        reject_link(info)
    return absolute


def mkdir_checked(path):
    check_parents(path, True)
    if not path.exists():
        mkdir_checked(path.parent)
        path.mkdir()
    check_parents(path)
    require(path.is_dir(), "Directory required")


def plan_check(plan):
    mapping = plan["mapping"]
    names = [safe_relative(f["relativePath"]).as_posix().casefold()
             for f in mapping["files"]]
    require(len(names) == len(set(names)) == 692, "Target count or case collision")
    name_set = set(names)
    for name in names:
        parents = PureWindowsPath(name).parents
        require(not any(p.as_posix().casefold() in name_set for p in parents),
                "File/ancestor collision")
    cursor = 0
    aliases = []
    for record in mapping["allPayloadRecords"]:
        require(record["logicalPayloadOffset"] == cursor, "Data gap or overlap")
        length = record["inferredBytesFromNeighborOffsets"]
        require(0 <= length <= 210802002 - cursor - 8, "Record size outside extent")
        cursor += 8 + length
        for name in record["moduleTargetAliases"]:
            safe_relative(name)
            aliases.append(name.casefold())
    require(cursor == 210802002 and len(mapping["allPayloadRecords"]) == 529,
            "Whole logical extent mismatch")
    require(sorted(aliases) == sorted(names), "Alias mapping mismatch")
    require(sum(f["inferredBytesFromNeighborOffsets"] for f in mapping["files"])
            == 230473980, "Target byte sum mismatch")


def decode_one(coded, dictionary=2307891, output_limit=MAX_BLOCK):
    decoder = lzma.LZMADecompressor(
        format=lzma.FORMAT_RAW,
        filters=[dict(id=lzma.FILTER_LZMA1, dict_size=dictionary, lc=3, lp=0, pb=2)],
    )
    total = 0
    incoming = coded
    while True:
        part = decoder.decompress(incoming, max_length=SLICE)
        incoming = b""
        total += len(part)
        require(total <= output_limit, "Decoded block output limit exceeded")
        if part:
            yield part
        if decoder.eof:
            require(not decoder.unused_data, "Unused compressed bytes after EOF")
            return
        require(not decoder.needs_input, "Truncated LZMA stream")
        require(part, "LZMA decoder made no progress")


class Reader:
    def __init__(self, pieces):
        self.pieces = iter(pieces)
        self.buffer = b""
        self.offset = 0

    def read(self, count):
        require(0 <= count <= SLICE, "Read slice limit")
        parts = []
        remaining = count
        while remaining:
            if not self.buffer:
                self.buffer = next(self.pieces, b"")
                require(self.buffer, "Unexpected logical EOF")
            take = min(remaining, len(self.buffer))
            parts.append(self.buffer[:take])
            self.buffer = self.buffer[take:]
            remaining -= take
            self.offset += take
        return b"".join(parts)

    def finish(self):
        require(not self.buffer and next(self.pieces, None) is None, "Trailing decoded output")


def memory_job():
    class Basic(ctypes.Structure):
        _fields_ = [("process_time", ctypes.c_longlong), ("job_time", ctypes.c_longlong),
                    ("flags", wintypes.DWORD), ("min_ws", ctypes.c_size_t),
                    ("max_ws", ctypes.c_size_t), ("active", wintypes.DWORD),
                    ("affinity", ctypes.c_size_t), ("priority", wintypes.DWORD),
                    ("scheduling", wintypes.DWORD)]
    class IO(ctypes.Structure):
        _fields_ = [(n, ctypes.c_ulonglong) for n in ("r", "w", "o", "rb", "wb", "ob")]
    class Extended(ctypes.Structure):
        _fields_ = [("basic", Basic), ("io", IO), ("process_limit", ctypes.c_size_t),
                    ("job_limit", ctypes.c_size_t), ("peak_process", ctypes.c_size_t),
                    ("peak_job", ctypes.c_size_t)]
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
    kernel.CreateJobObjectW.restype = wintypes.HANDLE
    kernel.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
    kernel.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.QueryInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int,
                                                ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p]
    job = kernel.CreateJobObjectW(None, None)
    require(job, f"CreateJobObject failed: {ctypes.get_last_error()}")
    limits = Extended()
    limits.basic.flags = 0x100  # JOB_OBJECT_LIMIT_PROCESS_MEMORY
    limits.process_limit = 512 * 1024 * 1024
    require(kernel.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits)),
            f"Job memory limit failed: {ctypes.get_last_error()}")
    require(kernel.AssignProcessToJobObject(job, kernel.GetCurrentProcess()),
            f"Assign memory job failed: {ctypes.get_last_error()}")

    def receipt():
        observed = Extended()
        require(kernel.QueryInformationJobObject(job, 9, ctypes.byref(observed),
                                                ctypes.sizeof(observed), None),
                "Query job memory failed")
        require(observed.process_limit == 512 * 1024 * 1024, "Job memory limit drift")
        return {"processMemoryLimitBytes": observed.process_limit,
                "peakProcessCommittedBytes": observed.peak_process,
                "assignedToCurrentProcess": True}
    # The process owns this unnamed job until exit; no child process is launched.
    return receipt


def inspect_file(path):
    check_parents(path)
    sha = hashlib.sha256()
    crc = 0
    size = 0
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(SLICE), b""):
            sha.update(part)
            crc = zlib.crc32(part, crc)
            size += len(part)
    check_parents(path)
    return {"bytes": size, "sha256": sha.hexdigest(), "crc32": f"{crc:08x}"}


def self_test():
    rejected = 0
    for name in ("../escape", r"C:\escape", r"\\server\share", "a:stream",
                 "a/../b", "NUL.txt", "a.", "a ", "/root", "a//b"):
        try:
            safe_relative(name)
        except ValueError:
            rejected += 1
        else:
            raise AssertionError(f"Accepted bad path {name}")
    require(safe_relative("Variations/il2cpp/Managed/a.dll").parts[0] == "Variations",
            "Good path rejected")
    fake = type("Info", (), {"st_mode": stat.S_IFDIR, "st_file_attributes": 0x400})()
    try:
        reject_link(fake)
    except ValueError:
        rejected += 1
    else:
        raise AssertionError("Reparse attribute accepted")
    data = b"bounded metadata fixture" * 4000
    coded = lzma.compress(data, format=lzma.FORMAT_RAW,
                          filters=[dict(id=lzma.FILTER_LZMA1, dict_size=2307891,
                                        lc=3, lp=0, pb=2)])
    require(b"".join(decode_one(coded)) == data, "LZMA fixture mismatch")
    for bad, limit in ((coded[:-2], MAX_BLOCK), (coded + b"extra", MAX_BLOCK), (coded, 1024)):
        try:
            b"".join(decode_one(bad, output_limit=limit))
        except (ValueError, lzma.LZMAError):
            rejected += 1
        else:
            raise AssertionError("Invalid compressed fixture accepted")
    reader = Reader([b"abc", b"def"])
    require(reader.read(4) == b"abcd" and reader.read(2) == b"ef", "Split read fixture")
    reader.finish()
    for pieces, count in (([b"a"], 2), ([b"aa"], 1)):
        reader = Reader(pieces)
        try:
            reader.read(count)
            reader.finish()
        except ValueError:
            rejected += 1
        else:
            raise AssertionError("Invalid logical extent accepted")
    for actual, expected, check in (
        (2**63 | 32, 32, check_length),
        (33, 32, check_length),
        (zlib.crc32(b"changed payload"), f"{zlib.crc32(b'expected payload'):08x}", check_crc),
    ):
        try:
            check(actual, expected)
        except ValueError:
            rejected += 1
        else:
            raise AssertionError("Bad raw length or CRC accepted")
    plan = json.loads((ROOT / "Docs/Evidence/module-static-analysis-20260915/metadata-analysis.json").read_text())
    plan_check(plan)
    duplicate = json.loads(json.dumps(plan))
    duplicate["mapping"]["files"][1]["relativePath"] = duplicate["mapping"]["files"][0]["relativePath"].upper()
    overlap = json.loads(json.dumps(plan))
    overlap["mapping"]["allPayloadRecords"][1]["logicalPayloadOffset"] -= 1
    for invalid in (duplicate, overlap):
        try:
            plan_check(invalid)
        except ValueError:
            rejected += 1
        else:
            raise AssertionError("Bad mapping accepted")
    return {"status": "passed", "rejectionCases": rejected,
            "positiveCases": 4, "payloadFilesDecoded": 0,
            "reparseTest": "mocked Windows attribute; actual parents checked during run"}


def run():
    lease_bytes = LEASE.read_bytes()
    lease = json.loads(lease_bytes.decode("utf-8-sig"))
    require(lease["generation"] == 2 and lease["owner"] == "shader"
            and lease["status"] == "granted"
            and lease["ownerThreadId"] == "01a0a3de-7f19-7be2-b268-b478036a9263"
            and lease["scope"] == "pinned-nsisbi-private-module-extraction-only",
            "Wrong generation2 lease")
    deadline = datetime.fromisoformat(lease["expiresAtUtc"]).timestamp()
    require(Path(lease["destinationRoot"]) == TOOLCHAIN and
            Path(lease["stagingRoot"]) == OUTPUT, "Lease path mismatch")
    require(lease["pinnedInstallerSha256"] == EXPECTED_SHA and
            lease["growthEnvelopeGiB"] == 1 and lease["minFreeMemoryGiB"] == 8 and
            lease["freeDataReserveGiB"] == 20 and lease["decodeProcessMaxMiB"] == 512 and
            lease["copyWorkersMax"] == 1, "Lease bounds mismatch")
    job = memory_job()
    written = 0
    last_poll = 0

    def guard(force=False):
        nonlocal last_poll
        require(time.time() < deadline - 2, "Absolute lease deadline")
        require(written <= MAX_WRITE, "Task logical write budget")
        if force or time.monotonic() - last_poll >= 1:
            require(LEASE.read_bytes() == lease_bytes, "Lease changed or revoked")
            require(shutil.disk_usage(TOOLCHAIN).free >= 20 * 1024 ** 3, "D reserve below 20 GiB")
            last_poll = time.monotonic()
        check_parents(OUTPUT)

    guard(True)
    require(not STAGING.exists() and not TARGET.exists(), "Fresh staging and absent target required")
    check_parents(STAGING, True)
    check_parents(TARGET, True)
    script = ROOT / "Docs/Verification/inspect_unity_module_metadata_20260915.py"
    spec = importlib.util.spec_from_file_location("pinned_module_metadata", script)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    plan = module.inspect(INSTALLER)
    plan_check(plan)
    with INSTALLER.open("rb") as stream:
        raw = stream.read(59296161)
    require(len(raw) == 59296160, "Bounded installer read length mismatch")
    require(hashlib.sha256(raw).hexdigest() == EXPECTED_SHA and
            hashlib.md5(raw).hexdigest() == EXPECTED_MD5, "Input identity mismatch")
    dump(OUTPUT / "memory-job.json", job())
    dump(OUTPUT / "authorization.json", lease)
    mkdir_checked(STAGING)
    for file in plan["mapping"]["files"]:
        mkdir_checked((STAGING / safe_relative(file["relativePath"])).parent)
    streams = [{"index": 0, "decodedBytes": 86104, "eof": True, "unusedBytes": 0,
                "verifiedBy": "pinned metadata inspector"}]

    def pieces():
        for i, chunk in enumerate(plan["nsisbi"]["chunks"][1:], 1):
            guard()
            start = chunk["fileOffset"] + 8
            end = chunk["fileOffset"] + 3 + chunk["compressedBytesWithProperties"]
            total = 0
            for part in decode_one(memoryview(raw)[start:end]):
                guard()
                total += len(part)
                yield part
            streams.append({"index": i, "decodedBytes": total, "eof": True, "unusedBytes": 0})

    reader = Reader(pieces())
    logical_records = []
    manifest = []
    for index, record in enumerate(plan["mapping"]["allPayloadRecords"]):
        guard()
        require(reader.offset == record["logicalPayloadOffset"], "Actual offset mismatch")
        size = struct.unpack("<Q", reader.read(8))[0]
        check_length(size, record["inferredBytesFromNeighborOffsets"])
        paths = [STAGING / safe_relative(name) for name in record["moduleTargetAliases"]]
        handles = []
        digest = hashlib.sha256()
        crc = 0
        try:
            for path in paths:
                check_parents(path, True)
                require(not path.exists(), "Staging file already exists")
                handles.append(path.open("xb"))
            remaining = size
            while remaining:
                guard()
                part = reader.read(min(remaining, SLICE))
                remaining -= len(part)
                digest.update(part)
                crc = zlib.crc32(part, crc)
                for path, handle in zip(paths, handles):
                    check_parents(path)
                    require(written + len(part) <= MAX_WRITE, "Write would exceed envelope")
                    handle.write(part)
                    written += len(part)
        finally:
            for handle in handles:
                handle.close()
        check_crc(crc, record["storedCrc32"])
        value = {"bytes": size, "sha256": digest.hexdigest(), "crc32": f"{crc:08x}"}
        logical_records.append({"logicalPayloadOffset": record["logicalPayloadOffset"],
                                "rawLength": size, "crcMatched": True, **value,
                                "moduleAliases": record["moduleTargetAliases"]})
        for name in record["moduleTargetAliases"]:
            manifest.append({"relativePath": name, **value})
        if index % 50 == 0:
            print(json.dumps({"phase": "decode", "recordsVerified": index + 1,
                              "logicalOffset": reader.offset, "writtenBytes": written}), flush=True)
    reader.finish()
    require(reader.offset == 210802002 and len(streams) == 102, "Full stream extent mismatch")
    require(len(manifest) == 692 and sum(f["bytes"] for f in manifest) == 230473980,
            "Module manifest totals")
    dump(OUTPUT / "streams.json", streams)
    dump(OUTPUT / "logical-records.json", logical_records)

    def verify_tree(root):
        guard(True)
        actual = set()
        for directory, dirs, files in os.walk(root, followlinks=False):
            check_parents(Path(directory))
            for name in dirs + files:
                check_parents(Path(directory) / name)
            for name in files:
                actual.add((Path(directory) / name).relative_to(root).as_posix().casefold())
        require(actual == {f["relativePath"].casefold() for f in manifest}, "Tree membership mismatch")
        for entry in manifest:
            guard()
            observed = inspect_file(root / safe_relative(entry["relativePath"]))
            require(observed == {k: entry[k] for k in ("bytes", "sha256", "crc32")},
                    f"Written file identity mismatch: {entry['relativePath']}")

    verify_tree(STAGING)
    require(inspect_file(INSTALLER)["sha256"] == EXPECTED_SHA, "Installer changed before copy")
    dump(OUTPUT / "staging-manifest.json", manifest)
    dump(OUTPUT / "staging-verified.json", {"files": 692, "bytes": 230473980,
                                           "all529RawLengthsAndCrcMatched": True})
    guard(True)
    require(not TARGET.exists(), "Destination appeared before copy")
    mkdir_checked(TARGET)
    for entry in manifest:
        guard()
        source = STAGING / safe_relative(entry["relativePath"])
        destination = TARGET / safe_relative(entry["relativePath"])
        mkdir_checked(destination.parent)
        check_parents(source)
        check_parents(destination, True)
        with source.open("rb") as src, destination.open("xb") as dst:
            for part in iter(lambda: src.read(SLICE), b""):
                guard()
                check_parents(destination)
                require(written + len(part) <= MAX_WRITE, "Copy would exceed envelope")
                dst.write(part)
                written += len(part)
    verify_tree(TARGET)
    require(inspect_file(INSTALLER)["sha256"] == EXPECTED_SHA, "Installer changed after copy")
    guard(True)
    dump(OUTPUT / "destination-manifest.json", manifest)
    result = {"status": "completed", "utc": datetime.now(timezone.utc).isoformat(),
              "streamsVerified": len(streams), "logicalRecordsVerified": len(logical_records),
              "moduleFiles": len(manifest), "moduleBytes": 230473980,
              "writtenPayloadBytes": written, "sourceInstallerSha256BeforeAndAfter": EXPECTED_SHA,
              "staging": str(STAGING), "destination": str(TARGET),
              "memory": job(), "opaqueTrailerInterpreted": False,
              "installerExecuted": False, "unityLaunched": False, "nativeExecutionReady": False}
    dump(OUTPUT / "result.json", result)
    print(json.dumps(result), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--run-granted-generation2", action="store_true")
    args = parser.parse_args()
    require(args.self_test != args.run_granted_generation2, "Select exactly one mode")
    if args.self_test:
        print(json.dumps(self_test()))
    else:
        try:
            run()
        except Exception as error:
            if OUTPUT.is_dir():
                dump(OUTPUT / "worker-failure.json",
                     {"utc": datetime.now(timezone.utc).isoformat(),
                      "type": type(error).__name__, "error": str(error),
                      "allEvidenceRetained": True})
            raise
