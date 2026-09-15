# Private Editor and Linux module file preparation — generation 1

## Outcome

**Editor copy and module acquisition are complete; module extraction is blocked.**
All owned processes exited and the actual local mutex was released. The 20-minute
grant ended; no subsequent acquisition, extraction or Unity stage was started.
`nativeExecutionReady=false` and the full preparation stage remains **failed**.

The complete private Editor is at:
`D:/CodexWork/shader-whole-task-20260915/work/toolchains/unity-6000.5.9f1/Editor`.
It has **68,215 files / 9,967,644,867 logical bytes**. Every file's source and copied
SHA-256 matched; 47 already matching files from the failed partial attempt were
reused without overwriting bytes. The borrowed Editor's before/after path, size
and modification-time inventory remained unchanged. Its contents were only read.

Both source and private `Unity.exe` identify
`6000.5.9f1_b57deb96f08d`, SHA-256
`eaf934a441e937b9301ef3fff04abfeff85b631cefbbda9b8c5985eac28a9225`.
This proves file identity, not successful Editor launch, licensing or compilation.

## Download and signature

The Windows-host Linux IL2CPP installer was acquired from the previously pinned
official URL and retained under the private root's `downloads` directory.

| Field | Observed value |
|---|---|
| File | `UnitySetup-Linux-IL2CPP-Support-for-Editor-6000.5.9f1.exe` |
| Actual bytes | 59,296,160; matches official metadata |
| Actual MD5 | `7de09886346cee6228c7754635ee378e`; matches official metadata |
| Actual SHA-256 | `6969513ad3ce6549d04dab5653e517337ee14319e21364eb1f2b161ae5ccb493` |
| Authenticode | `Valid`, signer Unity Technologies SF |
| Signer thumbprint | `228FB6411B0A144478C86AAA3CD9473C43A8ABA7` |
| Embedded file description | Unity 6000.5.9f1 Linux IL2CPP Support Installer |
| Installer execution | False |

The complete download/hash and signature receipts are retained separately. The
official expected metadata receipt from the earlier source preparation is unchanged.

## Exact extraction blocker

Read-only `7-Zip 25.01 l -slt` recognized the signed installer as an **outer PE
image** and listed sections/resources such as `.text`, `.rdata` and
`.rsrc/version.txt`. It did not expose the inner platform payload as explicit
`LinuxStandaloneSupport` paths. The safe extractor therefore failed its mapping
check at **09:53:01.071157 UTC** before issuing any extraction command.

This does not establish that isolated extraction is impossible. The embedded
payload/format and destination mapping were not inspected further during generation
1. The grant expired immediately afterward, so this worker made no forced-format
attempt, extraction or installer execution. The Linux module is **not installed
in the private Editor**. The downloaded installer was retained intact.

After release, the coordinator's read-only forced-NSIS probe also failed.
A separately authorized lightweight static investigation identified the NSISBI
flags rejected by 7-Zip and decoded only the 86,096-byte metadata header. It
produced a candidate 692-file mapping and an inferred byte total matching
official metadata; payload CRC/SHA verification and extraction remain pending.
See [the static analysis and next-stage bounds](UNITY_MODULE_STATIC_ANALYSIS_2026-09-15.md).
This later evidence does not change generation 1's failed extraction status.

## Scope, bounds and release

The coordinator's `local-preparation-lease.json` granted owner `shader`, generation
1, scope `private-editor-copy-and-linux-module-acquisition-only`, from
09:33:03 UTC with a 09:53:03 UTC work deadline. The stage stored the exact source
lease hash. This exception did not change the normal queue, predecessor handoffs,
HLSL frozen source or the original performance entry point.

The task-local stage adapter preserved the same real
`Local\CodexR9700VNextUnityGpu` mutex and other workload checks. Its final service
exception was limited to the coordinator-approved identities:

- PID 6192, `PresentMonService.exe`, creation `2026-09-14T12:18:37.1803250Z`.
- PID 9248, `PresentMonService.exe`, creation `2026-09-14T12:18:38.0572420Z`.

Their executable paths remained unknown. No service was stopped. A new process
with the same name would still be a blocker under the final adapter.

The process-local TEMP/TMP and task cache variables were directed to D: before
calling the stage boundary, then restored after release. Copy concurrency was two.
The worker synchronously waited on the signature-reader and archive-list children;
their actual exit codes were zero. The installer itself was never executed.

| Budget/evidence | Observed result |
|---|---|
| Private Editor + download files | 68,216 files / 10,026,941,027 logical bytes |
| Retained local preparation evidence at closeout | 53,014,155 logical bytes |
| Combined known growth at that observation | 10,079,955,182 bytes, below 20 GiB envelope |
| Minimum sampled free memory | 11,866,394,624 bytes (11.05 GiB), above 8 GiB |
| Minimum sampled D: free | 246,258,155,520 bytes, above 20 GiB reserve |
| Final worker exit | 1; extraction mapping failure |
| Worker finished | `2026-09-15T09:53:02.7052937Z` |
| Final boundary receipt | `2026-09-15T09:53:03.0954189Z`, status failed, mutexReleased true |
| Owned process follow-up | No matching live root/venv child identities |
| Independent mutex probe | Acquired and released, not abandoned, at 09:55:45 UTC |

The work stopped before the deadline; final boundary bookkeeping was recorded
95 ms after it. No copying/download/extraction resumed afterward. Directory sums
are logical bytes and sampling is not an atomic disk quota or a physical allocation
peak. Changes elsewhere on D: are not attributed to this task.

## Earlier attempts retained

| Attempt | Result |
|---|---|
| stage-01 | Original workload-name preflight rejected the two resident monitor services; no action started, mutex released. |
| stage-02 | A DateTime-to-string conversion lost the UTC kind and incorrectly rejected the deadline; no action started, mutex released. The later attempt used DateTimeOffset directly and the original deadline. |
| stage-03 | Initial copy stopped at a destination path guard. A subsequent read-only scan found every planned path inside the private root. The final copy checked stable existing parents before concurrent file work; prior logs/partial bytes were retained, mutex released. |
| stage-04 | Complete copy/hash verification and download/signature success; archive payload mapping blocked, worker exited, mutex released. |

## Evidence and remaining work

Published small receipts and their exact hashes are indexed in
`Docs/Verification/private-editor-preparation-g1-20260915.json`; the text files
under `Docs/Evidence/private-editor-preparation-g1-20260915/` use UTF-8/LF.
Raw originals, all failed attempts, resource samples and the complete per-file
Editor manifest remain in
`work/whole-task-20260915/private-editor-preparation-g1/`.
The full Editor manifest SHA-256 is
`02c50292a913cd07e385995d0daea0978f5e498b87192e90f34164db5d488927`.
Neither proprietary binaries nor the large manifest is included in Git.

The later bounded static format/mapping investigation is recorded separately.
Remaining: authorized private module extraction and actual length/CRC/SHA verification, then the
separately gated toolchain/project/build and graphical Vulkan validation. No
Unity, Player, GPU workload, SSH, global installer or license changes occurred.
No source/native test result or performance acceptance is inferred from this file
preparation. The earlier 122 offline checks remain a separate source-validation result.
