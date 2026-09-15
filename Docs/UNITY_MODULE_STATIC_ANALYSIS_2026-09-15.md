# Unity Linux module: bounded static format analysis

## Result and current boundary

**The 7-Zip failure is explained, and a complete candidate file mapping is now
available. The module has not been extracted or installed.**
The installer is an NSISBI-derived, Unicode, extended-instruction package. Its
first-header flags are `0x70`; 7-Zip 25.01 accepts only `0x0F` and returns failure
for the other bits. This independently explains both the original outer-PE
listing and the coordinator's forced-NSIS failure. Changing the type selector
does not add support for the different format.

The input remains exactly **59,296,160 bytes**, MD5
`7de09886346cee6228c7754635ee378e`, SHA-256
`6969513ad3ce6549d04dab5653e517337ee14319e21364eb1f2b161ae5ccb493`.
The preceding acquisition receipt has a Valid Unity Authenticode signature.
This static pass rechecked the complete input hash.

Only the first compression block was decoded: **86,096 metadata bytes plus an
8-byte length prefix**. The remaining **101 payload blocks were not decoded**.
No installer, Unity, Player, GPU, SSH, registry or license operation was invoked.
Generation 1 was independently released by the coordinator; this investigation
did not obtain another heavy-work lease.

The reusable, input-hash-pinned parser is
[inspect_unity_module_metadata_20260915.py](Verification/inspect_unity_module_metadata_20260915.py).
It prints JSON and has no payload writer, installer invocation or network code.
The complete output is
[metadata-analysis.json](Evidence/module-static-analysis-20260915/metadata-analysis.json).
This is an evidence reproducer for this exact file, not a general NSIS extractor.

## Physical layout and parser rejection

| Region | File offset | Bytes / observation |
|---|---:|---|
| PE32 x86 stub and resources | 0 | 56,832 |
| NSISBI first header | 56,832 | 36; standard NSIS signature begins at 56,836 |
| First block length prefix | 56,868 | 3-byte little-endian value 11,688 |
| First block LZMA1 properties | 56,871 | `5d33372300`; dictionary 2,307,891 bytes |
| First block coded stream | 56,876 | 11,683; exact end at 68,559 |
| Following compressed blocks | 68,559 | 101 blocks, boundaries recorded individually |
| End marker and opaque trailer | 59,285,479 | `00000028438ab7d923853a`, 11 bytes |
| Declared archive end | 59,285,490 | Six alignment bytes precede the certificate |
| Authenticode certificate | 59,285,496 | 10,664, reaching the exact file end |

All 102 blocks have a bounded 3-byte compressed length and the same five LZMA
property bytes. The first block's LZMA end marker consumes its input exactly;
its decoded 64-bit length equals the first-header metadata length. The
86,096-byte metadata SHA-256 is
`15f36a05704e145cd4055d7fa68b5255675af6993df1ec7cc8305963f92423b3`.
Its table holds **1,543 records of 36 bytes**, with eight argument words each.
The instruction table ends exactly where the Unicode string table starts.

These are observations on this binary. The NSISBI maintainer describes the
extended first header, extra instruction words and wider file lengths in
[the maintainer's format explanation](https://reverseengineering.stackexchange.com/questions/27136/unpacking-nsisbi-compressed-data).
The project documents its multithreaded compressors in
[the 3.10.3 release notes](https://sourceforge.net/projects/nsisbi/files/nsisbi3.10.3/).
The exact Unity build's fork source was not obtained; the resource manifest
identifies `Nullsoft Install System v15-May-2025.cvs`.

The rejection follows directly from
[7-Zip 25.01's flag mask](https://github.com/ip7z/7zip/blob/5e96a8279489832924056b1fa82f29d5837c9469/CPP/7zip/Archive/Nsis/NsisIn.h#L32)
and its
[unsupported-flags check](https://github.com/ip7z/7zip/blob/5e96a8279489832924056b1fa82f29d5837c9469/CPP/7zip/Archive/Nsis/NsisIn.cpp#L6034).
No flags or signed bytes were modified, and neither failed 7-Zip attempt was
repeated.

## Concrete file mapping

The straight-line file section has **692 distinct target paths**, referencing
**521 distinct logical data offsets**. No target is absolute, contains `..` or
has an unresolved variable after root substitution. The largest alias group
has three targets. Across the complete instruction table there are 529 distinct
data records; all references to a shared record agree on its stored CRC32 field.
Eight records belong to installer support resources rather than the module.

Instruction 658 sets the suffix to
`\Editor\Data\PlaybackEngines\LinuxStandaloneSupport`; instructions 695–696
establish and save the base output directory. Instructions 697–1412 enumerate
the module files. Every mapping entry records its instruction index, relative
path, logical payload offset, original CRC32 value and inferred file length.
`mapping.allPayloadRecords` includes all 529 records and the 521-to-692 module
alias mapping, including repeated instruction references.

| Relative group | Files | Inferred logical bytes |
|---|---:|---:|
| Root build programs, extensions and `modules.asset` | 7 | 168,850 |
| `Bee` | 2 | 50,176 |
| `Tools` | 17 | 983,030 |
| `Variations/il2cpp` | 162 | 12,988,260 |
| `Variations/linux64_player_development_il2cpp` | 170 | 134,361,384 |
| `Variations/linux64_player_development_mono` | 2 | 211,400 |
| `Variations/linux64_player_nondevelopment_il2cpp` | 170 | 68,721,524 |
| `Variations/mono` | 162 | 12,989,356 |
| **Total** | **692** | **230,473,980** |

Lengths above are inferred by sorting all 529 logical offsets and subtracting
the expected 8-byte length prefix between adjacent records, using the declared
210,802,002-byte logical data extent for the last record. Their module sum
matches the independently saved official Windows-host `linux-il2cpp`
`installedSize` **exactly**. This is a useful cross-check; no payload length
prefix, file CRC or file SHA-256 has yet been verified.
The [selected official metadata](Evidence/module-static-analysis-20260915/official-module-metadata.json)
is a field-for-field subset of the earlier
[Unity release API receipt](Evidence/linux-launcher-preparation-20260915/unity-release-api.json).

Use these roots in a later, separately granted extraction stage:

- Installer: `work/toolchains/unity-6000.5.9f1/downloads/UnitySetup-Linux-IL2CPP-Support-for-Editor-6000.5.9f1.exe`.
- New staging proposal: `work/toolchains/unity-6000.5.9f1/staging/linux-il2cpp-g2/Editor/Data/PlaybackEngines/LinuxStandaloneSupport`.
- Final private destination: `work/toolchains/unity-6000.5.9f1/Editor/Data/PlaybackEngines/LinuxStandaloneSupport`.

All are relative to `D:/CodexWork/shader-whole-task-20260915`. The final module
directory was absent during this pass. The staging name is a proposal; it must
be newly created, and an existing path must fail rather than be overwritten.

## Required verification for a later extraction

The parser provides the mapping and compressed boundaries. A payload extractor
has **not** been implemented or run in this static pass. Before writing module
files, the later stage must implement and exercise the following checks:

1. Recheck the exact installer size, official MD5, pinned SHA-256 and signature.
   Reproduce and compare the metadata and mapping. Read the fresh lease and use
   the coordinator's real stage/mutex boundary and process-local D: temp paths.
2. Check every existing parent of staging and destination with `lstat` and
   Windows reparse attributes; reject reparse points, junctions and symlinks.
   Resolve within the exact task root, reject absolute paths, drive/UNC paths,
   alternate streams, `..`, reserved names and trailing-dot/space aliases.
   Check case-insensitive duplicates and ancestor/file collisions. Use exclusive
   file creation in a new staging tree and recheck parents at each write boundary.
3. Decode the 102 framed LZMA blocks in order with one worker. Cap each decoded
   block at 8 MiB and each output slice at 64 KiB; require `eof=true`, zero unused
   input and exact compressed consumption for every block. Require the known
   zero-length terminator and exact archive/certificate/alignment boundaries.
   Never treat a decoder stopping at an output limit as a completed stream.
4. After the verified metadata block, treat the remaining decoded bytes as one
   logical data stream. At every one of the 529 recorded offsets, read the
   **actual 8-byte raw length** and require exact agreement with its mapped
   length and next offset. Unknown flags, missing prefixes, overrun, extra
   trailing output or total extent other than **210,802,002 bytes** must fail.
5. Compute ordinary streaming CRC32 from each record's actual data bytes, using
   the stored instruction CRC32, and compute SHA-256 alongside it. Verify all
   529 records, including the eight installer resources that are discarded.
   Write only the 692 allowlisted module paths. Stream each shared record to its
   one-to-three target aliases and require the same final length/CRC/SHA for
   each. Do not execute installer plugins or interpret script actions.
6. Produce a complete staging manifest; re-read every written file for length,
   CRC32 and SHA-256, and verify exact tree membership, all aliases and the
   230,473,980-byte sum. Only after those pass, copy to a newly created private
   module destination and independently re-read/compare the destination
   manifest. Retain staging and all failures. No overwrite, merge, cleanup or
   installer execution is implied.

The ordinary CRC32 calculation is consistent with
[NSIS's published implementation](https://github.com/NSIS-Dev/nsis/blob/master/Source/crc32.c).
**It remains to be checked against actual payload bytes.** Each compressed block
has no separately identified CRC field in this inspected framing, so exact LZMA
termination and the per-record CRCs are the proposed checks.

The eight bytes after the zero-length block terminator are retained as **opaque
trailer bytes**. This pass does not label them as validated native checksums.
Simple standard-NSIS outer-CRC assumptions did not reproduce either four-byte
half. The pinned complete input digest and the earlier signature cover those
bytes, but they do not constitute interpretation of a native trailer checksum.
If native trailer-checksum validation is required in the next grant, that
requirement remains unresolved; do not silently skip it or claim CRC completion.

## Memory and additional disk budget

| Item | Proposed / expected bound |
|---|---:|
| Decoder workers | 1 |
| LZMA dictionary per active decoder | 2,307,891 bytes |
| Maximum decompressed output per block | 8 MiB, abort on excess |
| Streaming write / CRC slice | 64 KiB |
| Process memory budget | 256 MiB; requires enforcement/measurement in the later stage |
| New verified staging files | 230,473,980 bytes |
| Destination copy while staging is retained | Another 230,473,980 bytes |
| Staging + destination logical total | **460,947,960 bytes** |
| Optional full logical-data spool | **210,802,002 bytes**; streaming can avoid it |
| Spool + both trees + 16 MiB evidence allowance | **688,527,178 bytes** |
| Suggested new-stage growth envelope | **1 GiB**, with the coordinator's disk/RAM reserves |

These are additional bytes beyond the already-retained Editor and installer.
The 1 GiB proposal includes headroom for allocation rounding and receipts; it is
not an observed peak or a hard quota already in force. Use bounded reads, decoder
output limits and write counters, plus the actual stage's time/storage/process
controls. The metadata reader itself reads the 59 MB EXE into memory; a later
extractor can hash in chunks and seek to each compressed block.

UPM Linux toolchain/sysroot packages, project Library, Player output and build
temporary files are excluded and still need separate acquisition/build budgets.
The Windows-host module must not be replaced with the Linux-host tarball.

## Official installation route and side effects

[Unity 6000.5's command-line installation documentation](https://docs.unity3d.com/6000.5/Documentation/Manual/InstallingUnity.html)
documents `/S` for silent installation and `/D` for the installation root that
**contains `Editor`**. Thus the documented root for this private copy would be
`D:\CodexWork\shader-whole-task-20260915\work\toolchains\unity-6000.5.9f1`.
These switches execute the installer; they are not a payload-only mode.
[NSIS's installer options](https://nsis.sourceforge.io/Docs/Chapter3.html#installerusage)
likewise describe installation controls, and its
[decompilation guidance](https://nsis.sourceforge.io/Can_I_decompile_an_existing_installer%3F)
does not provide a universal installer extraction mode. No Unity-supported
payload-only command for this EXE was found in the examined official documents.

Concrete static evidence relevant to installer execution:

- The PE manifest requests `highestAvailable`; Windows may elevate an
  administrative user's launch, as described in
  [Microsoft's manifest documentation](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests).
- Instructions 1478–1480 reference Unity installation `Location`, `Location x64`
  and `Location arm64` registry values. These records support registry **reads**,
  not a claim that the installer writes those values.
- The module block targets the chosen installation root. Plugin resources
  target `$PLUGINSDIR`, initialized from `$TEMP` in instructions 1530–1534.
- Instructions 1417–1422 create/use a documentation directory and contain an
  execution request for `DocCombiner.exe -autopaths` with the installation root.
  No `DocCombiner.exe` is among the 692 module files, so launch success is unknown.
- `LockedList.dll` module enumeration/dialog calls appear in the installer.
  This does not establish that another process would be terminated.

No global registry write or license activation/change was established by this
bounded analysis. Imported registry-write functions alone are insufficient
evidence that a particular path invokes them. The exact installer VM and plugin
side effects were not exhaustively audited. Consequently, the documented
installation route is not being represented as equivalent to a file-only copy.

## Evidence and limitations

The verification index
[module-static-analysis-20260915.json](Verification/module-static-analysis-20260915.json)
binds the parser, analysis output, official metadata subset, sources and the
coordinator's original failed NSIS probe/release receipts.
Small exploratory logs, downloaded source text and the decoded metadata header
remain under `work/whole-task-20260915/module-static-analysis-20260915/`.
The header and installer binaries are not committed.

The source archive fetch for NSISBI 3.10.3 returned HTTP 403, and guessed source
repository paths returned 404; no source compiler or downloaded helper ran.
An initial bounded standard-deflate interpretation failed. The later LZMA
metadata decode, exact stream end, structured block table, complete target map
and independent official-size match are the successful static checks.
They are not substitutes for payload length/CRC/SHA checks, an installed
toolchain, a Unity build or native Linux/Vulkan acceptance.
