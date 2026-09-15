# Linux Player launch and monitoring preparation

## Result and scope

The Linux foreground supervisor and offline evidence guard are implemented. They
have **not** run on Linux or against a Player. This change does not establish
build readiness, Vulkan rendering, a complete original route, or performance gain.
The existing Draft PR remains the delivery vehicle; no runtime C# was changed in
this addition. The prior 93-test/shared-API compilation receipt remains historical.

Current local validation: **122 offline tests passed** (29 added Linux-contract
tests), plus Python syntax and Git whitespace checks. The new source/metadata/log
bindings are recorded separately in
`Docs/Verification/linux-launcher-preparation-20260915.json`. No Linux kernel,
actual flock/subreaper/pidfd/NVML, display, module installation or Unity acceptance
is inferred from these tests. The stdlib source targets Python 3.10+ on Linux
x86-64; host availability remains to be checked during its granted setup turn.

This preparation used local source reads, small official metadata responses,
directory-size inventory and mocked/offline checks. No SSH connection, upload,
Editor/module installation, large scene download, Unity build or native workload
was performed. Data Layout remains the first remote owner; local heavy work still
requires the HLSL, SUMMIT and Data Layout handoffs and the local mutex.

## Source and integration

- `Tools/run_pso_linux_player.py`: Linux x86-64 foreground launcher, process owner,
  inherited lock verification, resource sampler and release receipts.
- `Tools/pso_linux_contract.py`: portable payload/spec/argument/ownership contracts,
  evidence reader and two offline preparation commands.
- `Tools/pso_urp_capture.py`: Linux stages require this supervisor evidence in
  addition to the unchanged original four-route, native CSV and normal-quit checks.
- `Tools/tests/test_pso_linux_stage.py`: fixtures and mocked syscalls; no native
  process, display or GPU execution. The explicit CPU test allowlist includes it.
- `Docs/Examples/linux-player-launch-spec.example.json`: deliberately incomplete
  template. Missing hashes/GPU identity must be replaced with actual pinned inputs.

Read-only integration references were the coordinator's
`remote_campaign_ssh.py`, `REMOTE_EXECUTION.md`, Data Layout's `linux_stage.py` and
SUMMIT's `run_process.py`. Their source/evidence was not modified or credited as
Shader validation. The implementation does not read credentials or store their
locations in launch bundles.

### Holding the existing lock

The existing SSH helper executes `flock -n <campaign>/.hardware.lock bash -s`.
The generated command body uses `exec python3 -B ...`; it has no nested lock,
background process, `nohup` or detached shell. The helper's **`--lock` remains
mandatory**. A coordinator grant and current host preflight are separate required
operational gates; this tool does not grant itself a turn or alter queue files.

The supervisor finds an inherited descriptor for the exact lock device/inode,
then checks the descriptor's kernel `FLOCK ADVISORY WRITE` entry in `fdinfo`.
It duplicates that existing descriptor and passes it to the Player. It never
opens the lock again to acquire or convert it. Linux duplicates share the same
open-file-description lock, so re-acquiring through another open could conflict
with the wrapper's lock. [Linux flock semantics](https://man7.org/linux/man-pages/man2/flock.2.html)

The Player starts in a dedicated session/process group. The single-threaded
supervisor becomes a child subreaper and launches no telemetry helper processes.
It records PID plus kernel start ticks, follows ancestry and adopted descendants,
and keeps root/group-leader zombies unreaped until live descendants are gone.
A root exit alone cannot release the lock.

Cleanup signals go only to groups whose live members belong to this observed
tree and whose leader is a birth-bound, unreaped child of the supervisor. A
pidfd stops a live leader before group signaling; a reused PID receives neither
the cleanup signal nor a resume signal. Leaderless, foreign, ambiguous or not-yet-
adopted groups are refused. They keep the holder alive for inspection/re-evaluation.
[Subreaper behavior](https://man7.org/linux/man-pages/man2/PR_SET_CHILD_SUBREAPER.2const.html)

Timeout/signal/failure triggers TERM, grace, then KILL of eligible owned groups.
Unknown cleanup remains in the foreground with `cleanup-unresolved-lock-held`
events. Only a clean descendant snapshot **and `waitid` ECHILD** permit explicit
`LOCK_UN` on the inherited descriptor and the final `mutexReleased=true` receipt.
`waitid` returning no event is treated as a live child, not ECHILD.

SIGKILL of the holder, a hung driver/kernel call, power loss or an SSH observation
timeout cannot be certified by this Python process. Missing final release evidence
requires host/process inspection before another stage; the passed descriptor is
additional retention, not a claim that every arbitrary descendant keeps all FDs.
Actual wrapper/FD inheritance, escaped descendants and interruption behavior remain
mandatory Linux validation before a real Player campaign.

### Pinning the workload and outputs

The payload manifest lives beside the payload directory, named
`<payload-directory>.manifest.json`. It enumerates **every** file with relative
path, logical bytes, SHA-256 and target POSIX mode. The monitor rejects symlinks,
hard-link aliases, special files, missing/extra files and nonmatching permissions.
It verifies the whole tree before launch and after all descendants have exited.
The Player must be executable ELF64 little-endian x86-64; this static check is not
IL2CPP/content/rendering acceptance. Actual `/proc/<pid>/exe` inode must match.

The stage directory must be new and below the task's `shader/stages` directory.
The spec pins the full manifest and both deployed Python file hashes. The exact
actual argument array and executable hash are recorded. Supported modes are
`profile`, `unprofiled`, `checkpoints` and explicit-count `native-progressive`.
Each retains the original observer-only four-route workload, package warmup off,
Vulkan, 1920x1080 and a fixed session label. No arbitrary extra arguments, software
backend, `-nographics`, server or batch mode is offered. Native control needs a
plan inside the same completely indexed payload. Profiler/off pairs must keep
the same payload, runtime-state directory and session.

Only the existing X11 `DISPLAY`/`XAUTHORITY` surface is currently supported. It must
be validated and frozen beforehand. Existing display processes may be allowed by
PID/start ticks/executable hash; an unknown GPU PID/namespace is ineligible. This
runner never creates or stops the display service.

| Output | Meaning |
|---|---|
| `launch-spec.json`, `payload-manifest.json` | Exact preserved pinned inputs |
| `command.json` | Executable/full-payload identity, actual arguments, working directory, selected environment and cache scope |
| `process-start.json` | Actual kernel PID/start ticks, process group/session, launch request and exec-return observations; UTC birth uses btime plus ticks and has btime's 1-second precision |
| `stdout.log`, `stderr.log`, `player.log` | Complete untrimmed Player streams and Unity log |
| `resources.jsonl` | Required GPU, graphics+compute process, proc CPU/RSS, cgroup CPU/memory/throttle/pressure, disk samples and errors |
| `events.jsonl` | Process births, root exit, reaps, cleanup signals/refusals, preflight/CPU observations and failures |
| `process.json` | Actual exit/signal code, stop reason, root-exit observation, cleanup-completion time and monotonic span |
| `before-release.json` | Evidence hashes and cleanup state while the lock is still held |
| `stage.json` | Final completed/failed state and observed explicit lock release; always `fullRouteAccepted=false` and `performanceClaimEligible=false` until independent content analysis |

Failures remain in their own original directory. No raw tail is dropped, failed
route substituted, cache cleared or old evidence deleted. If a disk/process failure
also prevents receipt writing, the partial files plus helper output remain
incomplete evidence; they cannot pass the analyzer.

### Sampling and budget

NVML read-only APIs return the exact GPU UUID/name/driver, utilization, device
memory and **both graphics and compute process lists**. Unsupported/N/A queries
are errors, never empty lists or zero utilization. The source uses NVIDIA's
documented v3 process queries and `nvmlProcessInfo_t` ABI.
[NVIDIA device queries](https://docs.nvidia.com/deploy/nvml-api/api/group__nvmlDeviceQueries.html)

Only the actual cgroup-v2 namespace root is currently supported. Missing/nested,
unlimited or unreadable allocation is unknown. CPU totals use quota/period (the
reported container example is 25 core-time units), not its 208 visible CPU IDs.
Five quiet intervals after an initial sample require GPU <=10% and total cgroup
CPU <=20% of quota. During the Player, other observed processes' CPU deltas are
retained and >10% of quota stops the stage. This is a sampled lower bound: short
processes between samples cannot be reconstructed. Allocation/memory-event drift,
missing proc/GPU data and slow samples are ineligible.

Default proposal: <=6 GiB expanded payload, 8 GiB stage output envelope, 2 GiB
runtime growth, 10 GiB data reserve, 1 GiB early-stop headroom, 7 GiB per-file hard
limit, 600-second Player timeout, 2-second sampling, TERM/KILL grace of 10 seconds
each. These are declared limits, not measurements of actual route/capture size.

Before launch, actual free space must cover the full new output + runtime growth
+ reserve, beyond the already present payload/archives/old evidence. Output and
task runtime directories are monitored; per-process inherited `RLIMIT_FSIZE`
limits individual files, and core dumps are disabled. Any file at its hard limit
is ineligible even if the Player handles the error and exits zero. The aggregate
limit is a **polling stop threshold** and can overshoot. Actual final sizes/free
space are retained; no atomic filesystem quota is claimed.

The task retains its `runtime-state/<build>` across paired processes, redirecting
only app configuration and TMPDIR. HOME and global driver caches are untouched.
Global free-space sampling catches aggregate disk consumption, but writes to
unknown external driver/OS cache paths are not attributed to the task. No cold
cache claim follows from a new process or a new task runtime directory.

## Official Unity module acquisition metadata

Metadata-only retrieval on 2026-09-15 confirmed Unity **6000.5.9f1**, changeset
**b57deb96f08d**, from the official release API and matching release page.
The selected module is the **Windows x86-64 host installer** for Linux IL2CPP
Player cross-builds. The similarly named Linux-host tarball is a different
download and is not the Windows module.
[Official release](https://unity.com/releases/editor/whats-new/6000.5.9f1)

| Component | Download bytes | Advertised installed bytes | Official MD5 |
|---|---:|---:|---|
| Windows x86-64 Editor | 4,062,966,208 | 9,016,556,142 | `01fcb882f538ca03bdb01702adc77ae8` |
| Windows-host Linux IL2CPP module | 59,296,160 | 230,473,980 | `7de09886346cee6228c7754635ee378e` |

The exact URLs, official integrity strings and source JSON hash are saved in
`Docs/Evidence/linux-launcher-preparation-20260915/unity-acquisition-metadata.json`.
Only the 99,058-byte JSON metadata response was retrieved. Installer binary
SHA-256, signature verification and actual installed size are **unknown** until
download/inspection. A source-provided MD5 is not a locally verified installer.

Proposed private root:
`D:/CodexWork/shader-whole-task-20260915/work/toolchains/unity-6000.5.9f1/`.
Editor and module paths are `Editor/` and
`Editor/Data/PlaybackEngines/LinuxStandaloneSupport/` below that root.
Downloads and installer scratch have separate task-private directories on D:.
Do not add the module to the borrowed Editor or change global caches/registry-based
shared installation. A later installer/extraction method must honor that isolation.

Read-only inventory of the borrowed Editor found **68,215 files / 9,967,644,867
logical bytes**. A private copy + module archive + advertised module expansion has
**10,257,415,007 known additional bytes** before scratch/metadata/allocation costs.
A fresh Editor installer + advertised Editor expansion + module archive/expansion
has **13,369,292,490 known bytes**. These alternative sums are not measured peaks.
Observed D: free was 256,875,958,272 bytes; C: free was 17,278,156,800 bytes at
2026-09-15T09:09:34Z. Recheck both before any acquisition.

Propose a **20 GiB Editor/module growth envelope plus the existing 20 GiB local
free reserve**, checked through the local heavy-stage gate. This covers only this
bounded preparation; actual installer scratch, UPM compiler/sysroot package sizes,
original scene import Library, build intermediates and final Player remain
unknown and need separate measured budgets. The three actual matching Editor
package entries remain `com.unity.toolchain.win-x86_64-linux`, `com.unity.sysroot.base`
and `com.unity.sdk.linux-x86_64`, each 1.1.0. None was downloaded here.

## One shared minimal Unity Vulkan capability check (proposal only)

Coordinate **one** small capability run usable by both SUMMIT and Shader. Reuse an
existing authorized small Unity host/probe under 6000.5.9f1 if one is suitable; do
not create another rendering project for this preparation. The runner above is
for the original Shader route and must not silently repurpose that route as a
short capability workload. Agree on the probe's source first, then build one
graphical Linux IL2CPP payload and pin its complete file manifest.

Proposed outputs in one fresh shared capability stage:

1. `capability-input.json`: source commit/file hashes, Editor revision/module and
   package identities, Linux IL2CPP graphical build settings, full payload
   manifest/hash, exact arguments, declared timeout/output budget and surface.
2. `capability-process.json`: actual PID/start ticks/session, start/exit times,
   exit code, timeout/stop reason and complete descendant/release receipt.
3. `capability-runtime.json`: actual Unity version, `LinuxPlayer`, IL2CPP build
   provenance, `Vulkan`, NVIDIA device/vendor/driver string, NVML UUID/driver,
   worker count, actual cgroup allocation and X11/window/swapchain observations.
   Device enumeration alone is not successful surface creation or rendering.
4. `capability-render.json` + `capability.png`: a fixed known nonempty frame from
   that probe, dimensions, submitted frame/readback identity, PNG hash and actual
   visual/expected-color review. Retain the pixel evidence; do not equate a clear
   color, `-nographics`, software Vulkan or a server build with target rendering.
5. Original stdout/stderr/Unity log, shader/compiler/Vulkan errors, resource
   samples, any presentation/swapchain observations and `capability-summary.json`.
   Fields not obtainable are null/unknown. If actual presentation cannot be
   established, label that part unknown rather than upgrading a screenshot into
   presentation evidence. No FPS/latency result is needed from this capability run.

Both consumers may reference the **same raw capability hashes and verified host
conditions**. Neither may borrow the other project's route/correctness/performance
acceptance. SUMMIT's actual render path and Shader's complete original four-scene
Player still need their own builds and acceptance. No capability run was started.

## Remaining execution gates

1. Local heavy-queue turn, private matching Editor/module/toolchain preparation,
   official host inputs and full Linux IL2CPP build success.
2. Complete measured Linux payload/archive sizes and file identities; current
   transfer budget and explicit Shader remote turn from the coordinator.
3. Shared minimal graphical Vulkan capability evidence, with unknown fields kept
   separate, plus actual Linux supervisor lock/process/timeout validation.
4. Frozen executable spec/session/surface/GPU identity and current resource gate.
5. Only then the documented D0, C0 and limited profiler pairs on the full original
   route; native progressive control stays conditional on a supported opportunity.

The Linux launch spec template intentionally cannot run as-is. For later use,
`pso_linux_contract.py manifest` hashes a finished payload offline (Windows callers
explicitly list target executable files). `helper-body` writes an immutable,
foreground command file. The coordinator invokes its existing SSH helper with
`--lock` and an observation timeout longer than preflight + Player timeout +
cleanup + payload hashing. An SSH timeout never authorizes an automatic retry.
