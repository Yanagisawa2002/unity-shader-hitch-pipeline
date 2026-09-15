# Unity Linux module: generation-2 extraction closeout

## Result

**The bounded extraction failed its first CRC check. No module payload files
were written or copied; `nativeExecutionReady=false`.** The failed process and
its receipts are retained. The private Editor copy completed in generation 1
remains available, but its Linux support module is absent.

The grant expired at **2026-09-15 10:43:08.000166 UTC**. The only extraction
worker had already exited, and its stage released the actual mutex at
**10:36:57.5966648 UTC**. There was no retry, renewal or payload work after the
deadline. Post-deadline work consisted of read-only investigation, an ownership
probe, offline fixture tests and this evidence closeout.

## Attempt and integrity boundary

| Item | Recorded result |
|---|---|
| Source commit before the attempt | `8ed73a0d282b981166889b300ae156d253be7e2f` |
| Executed extractor SHA-256 | `2d1e089f7b341dcec597f71cf69e7e4b47a1da303e9d77f33aad1ca12c415ef5` |
| Installer bytes | 59,296,160 |
| Installer SHA-256 | `6969513ad3ce6549d04dab5653e517337ee14319e21364eb1f2b161ae5ccb493` |
| Fresh Authenticode check | Valid, Unity Technologies SF |
| Stage start | 10:36:54.9092725 UTC |
| Worker identity | PID 26820, created 10:36:56.2188560 UTC |
| Worker failure | 10:36:56.648675 UTC, `ValueError`, exit code 1 |
| Supervisor reaped worker | 10:36:57.1352973 UTC, `allOwnedExited=true` |
| Stage end / actual mutex release | 10:36:57.5966648 UTC; acquired and released, not abandoned |

The failure was on logical offset **0**, the installer resource
`$PLUGINSDIR\modern-wizard.bmp`. Its inferred length was **154,544 bytes**.
The code reached the CRC comparison only after the raw uint64 length had
matched that value and all those data bytes had been consumed. The worker did
not persist a separate raw-prefix receipt before failing; this length conclusion
is supported by the pinned mapping, executed control flow and traceback.

- Computed data-only IEEE CRC32: **`0ccccd4c`**.
- Stored final instruction argument, interpreted as the file CRC: **`afec0faf`**.
- Result: immediate failure; the eight installer resources are never written
  as payload files, and no module record had been reached.

The metadata block's previous exact EOF and hash checks were reproduced.
The first payload block was only partially consumed; complete payload-stream
EOF, all 529 record checks, final module hashes and the post-copy input hash
were **not reached**. No success manifest or extraction result exists.
This is not evidence that the signed installer is corrupt: the CRC field's
coverage, initialization or interpretation remains unresolved.

The earlier static proposal assumed ordinary CRC32 over the file data.
[NSIS's implementation](https://raw.githubusercontent.com/kichik/nsis/master/Source/crc32.c)
uses the IEEE polynomial and usual input/output inversion.
[The NSISBI maintainer](https://reverseengineering.stackexchange.com/questions/27136/unpacking-nsisbi-compressed-data)
describes wider data lengths and additional CRC instruction slots, but does
not specify enough coverage detail to resolve this mismatch. The examined
[independent NData codec](https://raw.githubusercontent.com/KokerZhou/NSISExtractor/main/nsis_extractor/codecs/ndata.py)
handles framed DEFLATE streams and supplies no CRC rule for this LZMA package.
No downloaded code was executed, and no alternative checksum was substituted.

The opaque eight-byte archive trailer remains uninterpreted. The coordinator
explicitly allowed that limitation if the full stream and file checks passed;
the first file CRC failure alone prevented copying.

## Authorization and observed resources

Generation 2 allowed one file-only worker, a 15-minute absolute deadline,
1 GiB of additional logical growth, at least 20 GiB free on D:, at least 8 GiB
free RAM, and a **512 MiB process memory limit**. This superseded the earlier
static proposal's 256 MiB estimate. The supervisor held
`Local\CodexR9700VNextUnityGpu` during the worker, checked the live lease and
capacity, and retained owned-process identity and exit evidence.

An unnamed Windows Job Object was successfully assigned to the current Python
process with `JOB_OBJECT_LIMIT_PROCESS_MEMORY=536870912` bytes. Its recorded
**76,115,968-byte peak is the value at the early Job query**, before payload
decoding. The failed branch did not save a final Job query. Thus the whole-run
observed peak is **unavailable**, although the hard memory limit was applied.
The supervisor's only resource sample occurred after process exit, so its
`processPrivateBytes=null` is not a zero-memory claim.

| Measurement | Preflight | Final supervisor sample |
|---|---:|---:|
| Free physical RAM | 8,829,722,624 bytes | 8,773,611,520 bytes |
| D: available bytes | 246,251,765,760 | 246,251,757,568 |
| New logical bytes observed by supervisor | Not separately recorded | 5,564 |

These are samples, not a continuous peak measurement. Final receipt files
were written after the last growth sample. Whole-volume changes are not
attributed to this task. The fresh staging tree contains empty module
directories only; all logs are retained.

At **10:45:34.2879286 UTC**, a separate read-only closeout probe confirmed:

- PID 26820 was absent; its recorded process identity was no longer present.
- The actual mutex could be acquired immediately and released normally;
  `mutexProbeAbandoned=false`.
- Staging contained **0 payload files / 0 payload bytes**.
- The private `Editor/Data/PlaybackEngines/LinuxStandaloneSupport` destination
  did not exist.

The raw probe has an unused null `mutex` field; `mutexName` records the actual
name used. No cleanup or unrelated process termination was performed.

## Offline validation

The exact attempted extractor passed **21 rejection cases and 4 positive
cases**, both before the attempt and again during closeout. These cover unsafe
paths, a mocked Windows reparse attribute, malformed or oversized LZMA fixtures,
logical truncation/trailing data, incorrect lengths/CRC, case collisions and
overlapping mapped offsets. They decode generated fixtures, not module payloads.
The real parent-path checks ran during the attempt; the fixture result alone
does not establish general Windows race safety or full installer compatibility.

The first full offline-suite invocation used the base Python runtime without
`jsonschema`, producing two import errors and a failed 114-test run. Its log is
retained. Reusing the already-present validation environment required no
installation or download and passed **all 122 tests**. Python 3.10 grammar
parsing and PowerShell parsing of the supervisor and stage adapter also passed.
These checks do not establish native Unity, Player, GPU or Linux behavior.

## Evidence and remaining work

[The verification index](Verification/private-module-extraction-g2-20260915.json)
binds the report, attempted extractor, exact supervisor/adapter snapshots,
authorization, signature, process, resource and failure receipts, closeout probe,
and both unsuccessful and successful offline-suite logs.
The [attempt source](../Tools/prepare_pso_linux_module.py) is retained unchanged
for audit, with its exact preflight hash. It is a failed, grant-specific prototype;
its run mode is not a general installation command and this grant is expired.

Before another extraction attempt, the file CRC rule needs source-level or
equivalent evidence and consistent checks across multiple records. A later
attempt would need a fresh coordinator allocation and new evidence/staging
paths. Full module-file verification, separate UPM toolchain/sysroot preparation,
Unity import/build, Linux execution and Vulkan capability/route acceptance
remain pending. No installer/plugin, Unity/Player, GPU or SSH operation ran in
this generation, and no shared Editor, registry, license or driver was changed.
