"""Inspect one pinned Unity module installer; never execute or extract payload files.

This is an evidence reproducer for the 2026-09-15 static investigation, not a
general NSIS extractor. Only the first compressed metadata block is decoded.
The complete EXE is read for its hash and container boundaries. Output is JSON.
"""

from __future__ import annotations

import argparse
import collections
import hashlib
import json
import lzma
from pathlib import Path, PureWindowsPath
import struct


EXPECTED_SIZE = 59_296_160
EXPECTED_SHA256 = "6969513ad3ce6549d04dab5653e517337ee14319e21364eb1f2b161ae5ccb493"
EXPECTED_METADATA_SHA256 = "15f36a05704e145cd4055d7fa68b5255675af6993df1ec7cc8305963f92423b3"
EXPECTED_INSTALLED_BYTES = 230_473_980
ROOT_EXPRESSION = r"$INSTDIR\Editor\Data\PlaybackEngines\LinuxStandaloneSupport"
INTERNAL_VARIABLES = (
    "CMDLINE", "INSTDIR", "OUTDIR", "EXEDIR", "LANGUAGE", "TEMP",
    "PLUGINSDIR", "EXEPATH", "EXEFILE", "HWNDPARENT", "_CLICK", "_OUTDIR",
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def inspect(path: Path) -> dict:
    require(path.stat().st_size == EXPECTED_SIZE, "Unexpected installer size")
    with path.open("rb") as stream:
        data = stream.read(EXPECTED_SIZE + 1)
    require(len(data) == EXPECTED_SIZE, "Installer changed during read")
    require(sha256(data) == EXPECTED_SHA256, "Unexpected installer SHA-256")
    u16 = lambda offset: struct.unpack_from("<H", data, offset)[0]
    u32 = lambda offset: struct.unpack_from("<I", data, offset)[0]
    require(data[:2] == b"MZ", "Missing DOS signature")
    pe_offset = u32(0x3C)
    require(data[pe_offset:pe_offset + 4] == b"PE\0\0", "Missing PE signature")
    optional = pe_offset + 24
    require(u16(optional) == 0x10B, "Expected PE32")
    table = optional + u16(pe_offset + 20)
    sections = []
    for i in range(u16(pe_offset + 6)):
        offset = table + 40 * i
        name, virtual_size, virtual_address, raw_size, raw_offset = (
            struct.unpack_from("<8sIIII", data, offset)
        )
        sections.append({
            "name": name.rstrip(b"\0").decode("ascii"),
            "virtualAddress": virtual_address,
            "virtualBytes": virtual_size,
            "fileOffset": raw_offset,
            "fileBytes": raw_size,
        })
    overlay = max(s["fileOffset"] + s["fileBytes"] for s in sections)
    certificate_offset, certificate_size = struct.unpack_from(
        "<II", data, optional + 96 + 4 * 8
    )
    require(overlay == 56832, "Unexpected overlay")
    require(certificate_offset + certificate_size == len(data), "Certificate bounds")
    require(
        data[overlay + 4:overlay + 20] == bytes.fromhex("efbeadde") + b"NullsoftInst",
        "Missing NSIS signature",
    )
    flags, header_bytes, archive_bytes = (
        u32(overlay), u32(overlay + 20), u32(overlay + 24)
    )
    logical_data_bytes = struct.unpack_from("<Q", data, overlay + 28)[0]
    require((flags, header_bytes, archive_bytes, logical_data_bytes)
            == (0x70, 86096, 59228658, 210802002), "Unexpected NSISBI header")
    archive_end = overlay + archive_bytes
    require(archive_end <= certificate_offset, "Archive extends into certificate")

    # Observed NSISBI block framing: a three-byte LE compressed length followed
    # by five LZMA1 property bytes and one complete range-coded stream.
    # This checks framing only. It does not decompress the payload blocks.
    chunks = []
    cursor = overlay + 36
    for _ in range(256):
        compressed_bytes = int.from_bytes(data[cursor:cursor + 3], "little")
        if compressed_bytes == 0:
            break
        require(5 <= compressed_bytes <= 8 * 1024 * 1024, "Compressed block bound")
        require(cursor + 3 + compressed_bytes <= archive_end, "Compressed block overrun")
        properties = data[cursor + 3:cursor + 8]
        require(properties == bytes.fromhex("5d33372300"), "Unexpected LZMA properties")
        chunks.append({
            "fileOffset": cursor,
            "lengthPrefixBytes": 3,
            "compressedBytesWithProperties": compressed_bytes,
            "propertiesHex": properties.hex(),
        })
        cursor += 3 + compressed_bytes
    require(len(chunks) == 102, "Unexpected number of compressed blocks")
    require(archive_end - cursor == 11, "Unexpected end marker/trailer")
    require(data[cursor:cursor + 3] == b"\0\0\0", "Missing block terminator")

    first = chunks[0]
    require(first["compressedBytesWithProperties"] == 11688, "Metadata input bound")
    first_stream = first["fileOffset"] + 8
    first_end = first["fileOffset"] + 3 + first["compressedBytesWithProperties"]
    dictionary_bytes = int.from_bytes(data[first["fileOffset"] + 4:first_stream], "little")
    require(dictionary_bytes == 2307891, "Metadata dictionary bound")
    decoder = lzma.LZMADecompressor(
        format=lzma.FORMAT_RAW,
        filters=[{
            "id": lzma.FILTER_LZMA1, "dict_size": dictionary_bytes,
            "lc": 3, "lp": 0, "pb": 2,
        }],
    )
    decoded = decoder.decompress(
        data[first_stream:first_end], max_length=header_bytes + 9
    )
    require(decoder.eof and not decoder.unused_data, "Metadata stream must end exactly")
    require(len(decoded) == header_bytes + 8, "Metadata output bound")
    require(struct.unpack_from("<Q", decoded)[0] == header_bytes, "Metadata length mismatch")
    header = decoded[8:]
    require(sha256(header) == EXPECTED_METADATA_SHA256, "Unexpected decoded metadata")
    blocks = [struct.unpack_from("<II", header, 4 + i * 8) for i in range(8)]
    require(blocks == [
        (300, 8), (812, 2), (4956, 1543), (60504, 0),
        (85678, 1), (86024, 0), (0, 0), (0, 0),
    ], "Unexpected metadata block table")
    require(blocks[2][0] + blocks[2][1] * 36 == blocks[3][0], "Instruction table bounds")
    string_data = header[blocks[3][0]:blocks[4][0]]
    units = struct.unpack("<" + "H" * (len(string_data) // 2), string_data)

    def nsis_string(index: int) -> str:
        # Decode the NSIS3 Unicode variable encoding needed by these paths.
        # Other special codes are kept visibly unresolved, never expanded.
        require(0 <= index < len(units), "String index outside table")
        result = []
        while index < len(units) and units[index]:
            char = units[index]
            index += 1
            if char in (1, 2, 3, 4):
                require(index < len(units), "Truncated special string code")
                value = units[index]
                index += 1
                number = (value & 0x7F) | (((value >> 8) & 0x7F) << 7)
                if char == 3:
                    name = (
                        str(number) if number < 10 else
                        "R" + str(number - 10) if number < 20 else
                        INTERNAL_VARIABLES[number - 20] if number < 32 else
                        "VAR" + str(number)
                    )
                    result.append("$" + name)
                else:
                    result.append(f"<special-{char}-{value:04x}>")
            else:
                result.append(chr(char))
        require(index < len(units), "Unterminated string")
        return "".join(result)

    instructions = []
    for i in range(blocks[2][1]):
        values = struct.unpack_from("<9I", header, blocks[2][0] + i * 36)
        instructions.append({"index": i, "opcode": values[0], "arguments": list(values[1:])})
    require(instructions[658]["arguments"][:2] == [95, 1728], "Root suffix assignment")
    require(nsis_string(1728) == ROOT_EXPRESSION[len("$INSTDIR"):], "Root suffix string")
    require(instructions[696]["arguments"][:2] == [31, 2372], "Saved output base")
    require(nsis_string(2372) == "$OUTDIR", "Saved output string")

    # This exact straight-line file block uses only SetOutPath, the single
    # saved-output assignment, and File records. No arbitrary VM interpretation.
    files = []
    current_directory = ""
    for record in instructions[695:1414]:
        opcode, arguments = record["opcode"], record["arguments"]
        if opcode == 11:
            current_directory = (
                nsis_string(arguments[0])
                .replace("$INSTDIR$VAR95", ROOT_EXPRESSION)
                .replace("$_OUTDIR", ROOT_EXPRESSION)
            )
            require(current_directory == ROOT_EXPRESSION or
                    current_directory.startswith(ROOT_EXPRESSION + "\\"),
                    "Directory escaped module root")
        elif opcode == 27:
            require(record["index"] == 696, "Unexpected variable assignment")
        elif opcode == 20:
            filename = nsis_string(arguments[1])
            combined = current_directory + "\\" + filename
            require(combined.startswith(ROOT_EXPRESSION + "\\"), "File root mismatch")
            relative = PureWindowsPath(combined[len(ROOT_EXPRESSION) + 1:])
            require(not relative.is_absolute() and not relative.drive
                    and ".." not in relative.parts and ":" not in str(relative)
                    and "$" not in str(relative) and "<" not in str(relative),
                    "Unsafe or unresolved module path")
            files.append({
                "instructionIndex": record["index"],
                "relativePath": relative.as_posix(),
                "logicalPayloadOffset": arguments[2] | (arguments[3] << 32),
                "storedCrc32": f"{arguments[7]:08x}",
            })
        else:
            raise ValueError("Unexpected opcode in file block")
    require(len(files) == 692, "Module file count")
    require(len({f["relativePath"].casefold() for f in files}) == 692, "Duplicate target")
    offsets = sorted({
        r["arguments"][2] | (r["arguments"][3] << 32)
        for r in instructions if r["opcode"] == 20
    })
    require(len(offsets) == 529 and offsets[0] == 0, "Payload offset inventory")
    bounds = offsets + [logical_data_bytes]
    inferred_sizes = {a: b - a - 8 for a, b in zip(bounds, bounds[1:])}
    require(all(size >= 0 for size in inferred_sizes.values()), "Negative inferred file size")
    references = collections.defaultdict(list)
    for record in instructions:
        if record["opcode"] == 20:
            args = record["arguments"]
            offset = args[2] | (args[3] << 32)
            references[offset].append({
                "instructionIndex": record["index"],
                "nameExpression": nsis_string(args[1]),
                "storedCrc32": f"{args[7]:08x}",
            })
    payload_records = []
    for offset in offsets:
        crcs = {reference["storedCrc32"] for reference in references[offset]}
        require(len(crcs) == 1, "Conflicting CRC fields for a shared data offset")
        aliases = [file["relativePath"] for file in files
                   if file["logicalPayloadOffset"] == offset]
        payload_records.append({
            "logicalPayloadOffset": offset,
            "inferredBytesFromNeighborOffsets": inferred_sizes[offset],
            "expectedRawLengthPrefixBytes": 8,
            "rawLengthPrefixRead": False,
            "storedCrc32": next(iter(crcs)),
            "crc32VerifiedAgainstPayload": False,
            "moduleTargetAliases": aliases,
            "instructionReferences": references[offset],
        })
    for file in files:
        file["inferredBytesFromNeighborOffsets"] = inferred_sizes[file["logicalPayloadOffset"]]
    module_bytes = sum(f["inferredBytesFromNeighborOffsets"] for f in files)
    require(module_bytes == EXPECTED_INSTALLED_BYTES, "Official installed-size mismatch")
    require(len({f["logicalPayloadOffset"] for f in files}) == 521, "Module offset count")
    groups = collections.defaultdict(lambda: {"files": 0, "inferredBytes": 0})
    for file in files:
        parts = file["relativePath"].split("/")
        group = "/".join(parts[:2]) if parts[0] == "Variations" else (
            parts[0] if len(parts) > 1 else "(root)"
        )
        groups[group]["files"] += 1
        groups[group]["inferredBytes"] += file["inferredBytesFromNeighborOffsets"]

    manifest_start = data.find(b'<?xml version="1.0"', 40960, overlay)
    require(manifest_start >= 0, "Missing PE manifest")
    manifest_end = data.index(b"\0", manifest_start, overlay)
    manifest = data[manifest_start:manifest_end].decode("utf-8")
    require('level="highestAvailable"' in manifest, "Unexpected requested execution level")
    selected_indexes = [658, 689, 695, 696, 1417, 1418, 1422, 1478, 1479, 1480, 1503, 1512, 1521, 1530, 1534]
    selected_strings = [1725, 1728, 1818, 2206, 2372, 9589, 9608, 9638, 9902, 9957, 9966, 9979, 10168, 10186, 10197, 10210, 10329, 10339]
    return {
        "schemaVersion": 1,
        "kind": "pinned-installer-static-metadata-inspection",
        "installer": {
            "bytes": len(data), "sha256": EXPECTED_SHA256,
            "md5": hashlib.md5(data).hexdigest(),
        },
        "pe": {
            "machine": hex(u16(pe_offset + 4)), "sections": sections,
            "overlayOffset": overlay, "certificateOffset": certificate_offset,
            "certificateBytes": certificate_size,
            "manifest": manifest,
        },
        "nsisbi": {
            "flags": hex(flags), "standard7Zip2501Mask": "0x0f",
            "standard7ZipRejectsFlags": bool(flags & ~0xF),
            "firstHeaderBytes": 36, "metadataBytes": header_bytes,
            "declaredArchiveBytes": archive_bytes, "logicalDataBytes": logical_data_bytes,
            "chunkCount": len(chunks), "chunks": chunks,
            "trailerOffset": cursor, "uninterpretedTrailerHex": data[cursor:archive_end].hex(),
            "alignmentBytesBeforeCertificate": certificate_offset - archive_end,
        },
        "metadata": {
            "sha256": sha256(header), "decodedBytesIncludingLength": len(decoded),
            "decoderOutputLimit": header_bytes + 9,
            "lzmaDictionaryBytes": dictionary_bytes, "compressedInputBytes": first_end - first_stream,
            "blockTable": blocks, "instructionCount": len(instructions),
            "instructionBytes": 36, "opcodeCounts": dict(sorted(collections.Counter(
                r["opcode"] for r in instructions).items())),
            "selectedInstructionRecords": [instructions[i] for i in selected_indexes],
            "selectedStrings": {str(i): nsis_string(i) for i in selected_strings},
        },
        "mapping": {
            "status": "static-structural-candidate-payload-integrity-unverified",
            "rootExpression": ROOT_EXPRESSION,
            "fileCount": len(files), "uniqueTargetPaths": 692,
            "uniqueModuleDataOffsets": 521, "allUniqueDataOffsets": len(offsets),
            "inferredModuleBytes": module_bytes, "officialInstalledBytes": EXPECTED_INSTALLED_BYTES,
            "inferredSumMatchesOfficialMetadata": True,
            "aliasCrcFieldsConsistent": True,
            "maxModuleAliasesForOneDataOffset": max(
                len(record["moduleTargetAliases"]) for record in payload_records),
            "groups": dict(groups), "files": files,
            "allPayloadRecords": payload_records,
        },
        "scope": {
            "installerExecuted": False, "metadataBlocksDecoded": 1,
            "payloadBlocksDecoded": 0, "payloadFilesWritten": 0,
            "registryOrLicenseActionsInvoked": False,
            "payloadCrcVerified": False, "payloadSha256Available": False,
            "nativeArchiveTrailerChecksumInterpreted": False,
            "nativeExecutionReady": False,
        },
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installer", type=Path)
    arguments = parser.parse_args()
    print(json.dumps(inspect(arguments.installer), ensure_ascii=True, indent=2))
