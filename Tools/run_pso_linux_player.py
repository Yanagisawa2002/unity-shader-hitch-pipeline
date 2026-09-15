"""Foreground Linux Player supervisor, inside remote_campaign_ssh.py run --lock.

No SSH, installations, cache resets, display service setup, queue mutation or
arbitrary command execution. Import is portable; main refuses non-Linux hosts.
All native validation is pending until this runs on the granted target host.
"""
import argparse
import ctypes as C
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import shutil
import signal
import stat
import subprocess
import sys
import time

from pso_linux_contract import (CAMPAIGN, CONTRACT, GIB, cpu_quota_percent, foreign_cpu_quota_percent, digest,
    disk_stop, identity, inherited_lock_line, owned_snapshot, parse_proc_stat,
    player_arguments, read_json, require, safe_path, safe_signal_groups, utc,
    validate_spec, verify_payload, write_json)


def proc(pid):
    return parse_proc_stat(Path(f'/proc/{pid}/stat').read_text())


def process_snapshot():
    rows = {}
    for path in Path('/proc').iterdir():
        if not path.name.isdigit():
            continue
        try:
            row = parse_proc_stat((path / 'stat').read_text())
            rows[row['pid']] = row
        except (FileNotFoundError, ProcessLookupError):
            # Normal short-lived process between directory enumeration and read.
            continue
        # Permission/malformed data are NOT treated as no processes.
    return rows


def tree_bytes(root):
    total = 0
    for directory, dirs, files in os.walk(root, followlinks=False):
        for name in dirs + files:
            p = Path(directory) / name
            try:
                s = p.lstat()
                require(not stat.S_ISLNK(s.st_mode), 'Unbudgeted symlink in output/runtime tree')
                if stat.S_ISREG(s.st_mode):
                    # Logical bytes bound raw output; block allocation catches preallocation.
                    total += max(s.st_size, s.st_blocks * 512)
                else:
                    require(stat.S_ISDIR(s.st_mode), 'Unbudgeted special file in output/runtime tree')
            except FileNotFoundError:
                continue  # Player may atomically rename/delete its temporary file.
    return total


def file_limit_reached(roots, limit):
    for root in roots:
        for directory, _, files in os.walk(root, followlinks=False):
            for name in files:
                path = Path(directory) / name
                try:
                    if path.stat().st_size >= limit:
                        return str(path)
                except FileNotFoundError:
                    continue
    return None


class InheritedLock:
    def __init__(self):
        import fcntl
        self.fcntl = fcntl
        self.path = Path(CAMPAIGN) / '.hardware.lock'
        require(not self.path.is_symlink(), 'Shared lock must not be a symlink')
        target = self.path.stat()
        require(stat.S_ISREG(target.st_mode), 'Shared lock is not a regular file')
        candidates = []
        for item in list(Path('/proc/self/fd').iterdir()):
            try:
                fd = int(item.name)
                actual = os.fstat(fd)
                if (actual.st_dev, actual.st_ino) == (target.st_dev, target.st_ino):
                    record = inherited_lock_line(Path(f'/proc/self/fdinfo/{fd}').read_text(),
                        os.major(target.st_dev), os.minor(target.st_dev), target.st_ino)
                    if record:
                        candidates.append((fd, record))
            except (FileNotFoundError, OSError):
                continue
        require(bool(candidates), 'No inherited exclusive FLOCK; use the existing SSH helper --lock (without --close)')
        original, record = candidates[0]
        # dup shares the lock description. NEVER reopen + LOCK_EX or convert it.
        self.fd = os.dup(original)
        self.device, self.inode = target.st_dev, target.st_ino
        self.receipt = dict(path=str(self.path), device=self.device, inode=self.inode,
            inheritedFd=original, heldFd=self.fd, inheritedVerified=True,
            mechanism='inherited FLOCK open-file-description; no reacquisition', **record)
        self.assert_held()

    def assert_held(self):
        current = self.path.stat()
        require((current.st_dev, current.st_ino) == (self.device, self.inode), 'Shared lock pathname replaced')
        record = inherited_lock_line(Path(f'/proc/self/fdinfo/{self.fd}').read_text(),
                                     os.major(self.device), os.minor(self.device), self.inode)
        require(record is not None, 'Inherited kernel FLOCK no longer held')

    def release(self, cleanup):
        require(cleanup.get('allOwnedExited') is True and cleanup.get('waitidNoChildren') is True and
                not cleanup.get('remaining') and not cleanup.get('errors'), 'Refusing release while cleanup is unknown')
        self.assert_held()
        self.fcntl.flock(self.fd, self.fcntl.LOCK_UN)
        require(inherited_lock_line(Path(f'/proc/self/fdinfo/{self.fd}').read_text(),
            os.major(self.device), os.minor(self.device), self.inode) is None, 'Unlock not observed on inherited descriptor')
        os.close(self.fd)
        return utc()


class GpuMemory(C.Structure):
    _fields_ = [('total', C.c_ulonglong), ('free', C.c_ulonglong), ('used', C.c_ulonglong)]


class GpuUtilization(C.Structure):
    _fields_ = [('gpu', C.c_uint), ('memory', C.c_uint)]


class GpuProcess(C.Structure):
    # NVIDIA nvmlProcessInfo_t / nvmlProcessInfo_v2_t, used by *_v3 queries.
    _fields_ = [('pid', C.c_uint), ('usedGpuMemory', C.c_ulonglong),
                ('gpuInstanceId', C.c_uint), ('computeInstanceId', C.c_uint)]


class Nvml:
    """Read-only driver API: both GRAPHICS and COMPUTE process lists are required.

    No probe subprocesses can get mistaken for subreaper-adopted Player children.
    Unsupported queries fail closed; NVML PID namespace mismatch stays unknown.
    A hung driver call can stall this supervisor: no release receipt then exists.
    """
    def __init__(self, gpu):
        self.lib = C.CDLL('libnvidia-ml.so.1')
        self.bind('nvmlInit_v2', [])()
        self.bind('nvmlDeviceGetHandleByUUID', [C.c_char_p, C.POINTER(C.c_void_p)])
        self.bind('nvmlSystemGetDriverVersion', [C.c_void_p, C.c_uint])
        for name in ('nvmlDeviceGetName', 'nvmlDeviceGetUUID'):
            self.bind(name, [C.c_void_p, C.c_void_p, C.c_uint])
        self.bind('nvmlDeviceGetMemoryInfo', [C.c_void_p, C.POINTER(GpuMemory)])
        self.bind('nvmlDeviceGetUtilizationRates', [C.c_void_p, C.POINTER(GpuUtilization)])
        for kind in ('Compute', 'Graphics'):
            self.bind('nvmlDeviceGet' + kind + 'RunningProcesses_v3',
                      [C.c_void_p, C.POINTER(C.c_uint), C.POINTER(GpuProcess)], raw=True)
        self.handle = C.c_void_p()
        self.lib.nvmlDeviceGetHandleByUUID(gpu['uuid'].encode('ascii'), C.byref(self.handle))

    def bind(self, name, types, raw=False):
        fn = getattr(self.lib, name)
        fn.argtypes, fn.restype = types, C.c_int
        if not raw:
            def checked(value, function, arguments):
                if value != 0:
                    raise RuntimeError(f'{name}: NVML error {value}; telemetry unknown')
                return value
            fn.errcheck = checked
        return fn

    def string(self, name, device=True):
        buffer = C.create_string_buffer(256)
        args = [self.handle] if device else []
        getattr(self.lib, name)(*args, buffer, len(buffer))
        return buffer.value.decode('utf-8', errors='strict')

    def processes(self, kind):
        fn = getattr(self.lib, 'nvmlDeviceGet' + kind + 'RunningProcesses_v3')
        capacity = 32
        for _ in range(4):
            count = C.c_uint(capacity)
            rows = (GpuProcess * capacity)()
            code = fn(self.handle, C.byref(count), rows)
            if code == 7:  # NVML_ERROR_INSUFFICIENT_SIZE, includes racing new contexts.
                capacity = max(capacity * 2, count.value + 16)
                require(capacity <= 16384, 'Unbounded NVML process list')
                continue
            require(code == 0 and count.value <= capacity, f'NVML {kind} list unavailable: {code}')
            result = []
            for row in rows[:count.value]:
                require(row.usedGpuMemory != 2 ** 64 - 1, 'GPU process memory unavailable')
                result.append(dict(pid=row.pid, usedGpuMemoryBytes=row.usedGpuMemory,
                    gpuInstanceId=None if row.gpuInstanceId == 2 ** 32 - 1 else row.gpuInstanceId,
                    computeInstanceId=None if row.computeInstanceId == 2 ** 32 - 1 else row.computeInstanceId))
            return result
        raise RuntimeError('Unstable NVML process list')

    def sample(self):
        memory, utilization = GpuMemory(), GpuUtilization()
        self.lib.nvmlDeviceGetMemoryInfo(self.handle, C.byref(memory))
        self.lib.nvmlDeviceGetUtilizationRates(self.handle, C.byref(utilization))
        require(0 <= utilization.gpu <= 100 and 0 <= utilization.memory <= 100 and
                0 < memory.total and memory.used <= memory.total, 'Invalid GPU sample')
        return dict(uuid=self.string('nvmlDeviceGetUUID'), name=self.string('nvmlDeviceGetName'),
            driver=self.string('nvmlSystemGetDriverVersion', False), gpuPercent=utilization.gpu,
            memoryPercent=utilization.memory, memoryTotalBytes=memory.total, memoryUsedBytes=memory.used,
            compute=self.processes('Compute'), graphics=self.processes('Graphics'), source='NVML read-only API')


def cgroup_sample():
    namespace = Path('/proc/self/cgroup').read_text().strip()
    require(namespace == '0::/', 'Only cgroup-v2 namespace root is supported; effective allocation unknown')
    root = Path('/sys/fs/cgroup')
    raw = {name: (root / name).read_text().strip() for name in
           ('cpu.max', 'cpuset.cpus.effective', 'cpu.stat', 'cpu.pressure',
            'memory.max', 'memory.current', 'memory.events')}
    quota, period = raw['cpu.max'].split()
    require(quota != 'max' and raw['memory.max'] != 'max' and bool(raw['cpuset.cpus.effective']),
            'Finite effective CPU/memory allocation unavailable')
    counters = {k: int(v) for k, v in (line.split() for line in raw['cpu.stat'].splitlines())}
    for name in ('usage_usec', 'nr_periods', 'nr_throttled', 'throttled_usec'):
        require(name in counters, 'Missing cgroup CPU accounting: ' + name)
    require(int(quota) > 0 and int(period) > 0 and int(raw['memory.max']) > 0, 'Invalid cgroup limits')
    return dict(monotonicSeconds=time.monotonic(), cgroup=namespace, quotaCores=int(quota) / int(period),
        cpuset=raw['cpuset.cpus.effective'], usageUsec=counters['usage_usec'], cpuStat=counters,
        memoryMaxBytes=int(raw['memory.max']), memoryCurrentBytes=int(raw['memory.current']), raw=raw)


class OwnedTree:
    def __init__(self, event):
        self.event = event
        self.supervisor = os.getpid()
        self.known = set()
        self.root = None
        self.root_exit = None
        libc = C.CDLL(None, use_errno=True)
        # PR_SET_CHILD_SUBREAPER; descendants that daemonize are adopted here.
        require(libc.prctl(36, 1, 0, 0, 0) == 0, 'Could not become child subreaper')
        require(self.no_children(), 'Supervisor must not inherit unrelated children')
        require(hasattr(os, 'pidfd_open') and hasattr(signal, 'pidfd_send_signal'), 'pidfd identity protection required')

    def no_children(self):
        try:
            os.waitid(os.P_ALL, 0, os.WEXITED | os.WNOHANG | os.WNOWAIT)
            return False  # None means live child, NOT no children.
        except ChildProcessError:
            return True

    def observe(self):
        snapshot = process_snapshot()
        owned = owned_snapshot(snapshot, self.known, self.supervisor)
        for row in owned.values():
            if identity(row) not in self.known:
                self.event('owned-process-observed', identity=row)
                self.known.add(identity(row))
        if self.root is not None and self.root_exit is None:
            result = os.waitid(os.P_PID, self.root['pid'], os.WEXITED | os.WNOHANG | os.WNOWAIT)
            if result is not None:
                code = result.si_status if result.si_code == os.CLD_EXITED else -result.si_status
                self.root_exit = dict(exitCode=code, observedUtc=utc(), waitidCode=result.si_code)
                self.event('root-exit-observed-unreaped', **self.root_exit)
        return snapshot, owned

    def signal_groups(self, sig):
        snapshot, owned = self.observe()
        groups, refused = safe_signal_groups(snapshot, owned, os.getpgrp(), self.supervisor)
        for pgid in refused:
            self.event('signal-refused', pgid=pgid, signal=sig, reason='Group ownership/leader unproven')
        for pgid, birth in groups:
            # Stop the verified group leader using a pidfd before killpg. Retaining
            # all waitable zombies prevents our own reap from recycling a PGID.
            fd = None
            stopped = False
            try:
                fd = os.pidfd_open(pgid)
                leader = proc(pgid)
                require(identity(leader) == (pgid, birth) and leader['ppid'] == self.supervisor,
                        'Group leader PID reused or not our unreaped child')
                if leader['state'] not in ('Z', 'X'):
                    signal.pidfd_send_signal(fd, signal.SIGSTOP)
                    stopped = True
                deadline = time.monotonic() + 2
                while proc(pgid)['state'] not in ('T', 't', 'Z', 'X'):
                    require(time.monotonic() < deadline, 'Group leader stop not observed')
                    time.sleep(.01)
                fresh, current = self.observe()
                safe, _ = safe_signal_groups(fresh, current, os.getpgrp(), self.supervisor)
                require((pgid, birth) in safe, 'Group membership changed before cleanup signal')
                # killpg on a stopped/unreaped, birth-bound leader. A live member
                # of a different session cannot join this dedicated session.
                os.killpg(pgid, sig)
                self.event('owned-group-signal', pgid=pgid, startTicks=birth, signal=sig)
            except (ProcessLookupError, FileNotFoundError):
                self.event('group-exited-before-signal', pgid=pgid, startTicks=birth)
            except Exception as error:
                self.event('signal-refused', pgid=pgid, signal=sig, reason=str(error))
            finally:
                if fd is not None:
                    # Resume only this exact leader after queuing TERM. It may
                    # process TERM/exit; a reused numeric PID is never targeted.
                    if stopped:
                        try:
                            signal.pidfd_send_signal(fd, signal.SIGCONT)
                        except ProcessLookupError:
                            pass
                    os.close(fd)

    def finish_if_exited(self):
        _, owned = self.observe()
        live = [r for r in owned.values() if r['state'] not in ('Z', 'X')]
        if live:
            return None
        # Only now reap. A remaining live child makes waitpid return (0, 0), and
        # keeps the lease, even if a concurrent /proc scan momentarily missed it.
        while True:
            try:
                pid, status = os.waitpid(-1, os.WNOHANG)
            except ChildProcessError:
                break
            if pid == 0:
                return None
            self.event('owned-child-reaped', pid=pid, waitStatus=status)
        _, owned = self.observe_without_root_wait()
        if owned or not self.no_children():
            return None
        return dict(allOwnedExited=True, waitidNoChildren=True, remaining=[], errors=[],
                    verifiedUtc=utc(), observedIdentities=[list(i) for i in sorted(self.known)])

    def observe_without_root_wait(self):
        snapshot = process_snapshot()
        return snapshot, owned_snapshot(snapshot, self.known, self.supervisor)


class Supervisor:
    def __init__(self, spec_path, expected_hash):
        require(sys.platform == 'linux' and platform.machine() == 'x86_64', 'Linux x86-64 runtime only')
        self.spec_path = safe_path(spec_path, Path(CAMPAIGN) / 'shader')
        require(digest(self.spec_path) == expected_hash, 'Launch spec hash changed')
        self.spec = validate_spec(read_json(self.spec_path))
        self.spec_hash = expected_hash
        self.stage = safe_path(self.spec['stage'], Path(CAMPAIGN) / 'shader/stages')
        self.payload = safe_path(self.spec['payloadRoot'], Path(CAMPAIGN) / 'shader/payloads')
        self.runtime = safe_path(self.spec['runtimeState'], Path(CAMPAIGN) / 'shader/runtime-state')
        self.stage.mkdir(parents=False, exist_ok=False)
        self.events = (self.stage / 'events.jsonl').open('x', encoding='utf-8', buffering=1)
        self.resources = (self.stage / 'resources.jsonl').open('x', encoding='utf-8', buffering=1)
        self.errors = []
        self.stop_reason = None
        self.signal_requested = None
        self.lock = self.tree = self.child = None
        self.cleanup = None
        self.started = None
        self.payload_before = self.payload_after = None
        self.telemetry_qualified = False
        self.runtime_initial = 0
        self.memory_events_initial = None
        self.event('stage-open', supervisorIdentity=proc(os.getpid()), specSha256=expected_hash)
        for sig in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP):
            signal.signal(sig, self.request_stop)

    def request_stop(self, signum, frame):
        self.signal_requested = signum

    def event(self, kind, **data):
        row = dict(kind=kind, utc=utc(), monotonicSeconds=time.monotonic(), **data)
        try:
            self.events.write(json.dumps(row, allow_nan=False) + '\n')
            self.events.flush()
        except OSError as error:
            self.stop_reason = self.stop_reason or 'evidence-write-failed'
            self.errors.append(str(error))
            print(json.dumps(row), file=sys.stderr, flush=True)

    def sample(self, gpu, preflight=False):
        started = time.monotonic()
        row = dict(utc=utc(), preflight=preflight, errors=[])
        try:
            self.lock.assert_held()
            snapshot, owned = self.tree.observe()
            row['ownedProcesses'] = list(owned.values())
            row['otherProcesses'] = [r for pid, r in snapshot.items() if pid not in owned]
            row['monotonicSeconds'] = time.monotonic()
            row['processClockTicksPerSecond'] = os.sysconf('SC_CLK_TCK')
            row['rssPageBytes'] = os.sysconf('SC_PAGE_SIZE')
            row['cgroup'] = cgroup_sample()
            row['gpu'] = gpu.sample()
            require(all(row['gpu'][k] == self.spec['gpu'][k] for k in ('uuid', 'name', 'driver')), 'GPU identity changed')
            allowed = {p['pid']: p for p in self.spec.get('allowedGpuProcesses', [])}
            for item in row['gpu']['compute'] + row['gpu']['graphics']:
                pid = item['pid']
                require(pid in snapshot, 'GPU process identity/namespace unavailable')
                actual = snapshot[pid]
                item['processIdentity'] = actual
                if pid not in owned:
                    require(pid in allowed and actual['startTicks'] == allowed[pid]['startTicks'] and
                            digest(Path(f'/proc/{pid}/exe')) == allowed[pid]['exeSha256'], 'Foreign GPU workload observed')
            if preflight:
                require(row['gpu']['gpuPercent'] <= 10, 'GPU preflight exceeds 10 percent')
            if self.memory_events_initial is None:
                self.memory_events_initial = row['cgroup']['raw']['memory.events']
            require(self.memory_events_initial == row['cgroup']['raw']['memory.events'], 'Cgroup memory event counters changed')
            row['disk'] = self.disk_sample()
            require(row['disk']['fileAtHardLimit'] is None, 'Per-file hard limit reached; output may be incomplete')
            reason = disk_stop(self.spec['budget'], row['disk']['freeBytes'],
                               row['disk']['stageBytes'], row['disk']['runtimeGrowthBytes'])
            require(reason is None, reason or 'Disk threshold')
            row['sampleDurationSeconds'] = time.monotonic() - started
            require(row['sampleDurationSeconds'] <= self.spec['sampleSeconds'], 'Telemetry sample exceeded declared cadence')
        except Exception as error:
            row['errors'].append(str(error))
            self.telemetry_qualified = False
        self.resources.write(json.dumps(row, allow_nan=False) + '\n')
        self.resources.flush()
        require(not row['errors'], '; '.join(row['errors']))
        return row

    def disk_sample(self):
        return dict(freeBytes=shutil.disk_usage(self.stage).free, stageBytes=tree_bytes(self.stage),
                    runtimeGrowthBytes=max(0, tree_bytes(self.runtime) - self.runtime_initial),
                    fileAtHardLimit=file_limit_reached((self.stage, self.runtime), self.spec['budget']['perFileBytes']))

    def prepare(self):
        self.lock = InheritedLock()
        self.event('inherited-lock-verified', lock=self.lock.receipt)
        script = Path(__file__).resolve()
        require(digest(script) == self.spec['runnerSha256'] and
                digest(script.with_name('pso_linux_contract.py')) == self.spec['contractSha256'], 'Supervisor source hash differs')
        self.tree = OwnedTree(self.event)
        manifest_path = self.payload.with_name(self.payload.name + '.manifest.json')
        require(not manifest_path.is_symlink() and digest(manifest_path) == self.spec['manifestSha256'], 'Payload manifest changed')
        self.manifest = read_json(manifest_path)
        with (self.stage / 'payload-manifest.json').open('xb') as stream:
            stream.write(manifest_path.read_bytes())
        with (self.stage / 'launch-spec.json').open('xb') as stream:
            stream.write(self.spec_path.read_bytes())
        self.payload_before = verify_payload(self.payload, self.manifest)
        require(self.payload_before['bytes'] <= 6 * GIB, 'Initial Player payload exceeds declared 6 GiB transfer envelope')
        self.event('payload-before', **self.payload_before)
        player = self.payload / self.spec['playerRelativePath']
        require(player.is_file() and os.access(player, os.X_OK), 'Player executable unavailable')
        with player.open('rb') as stream:
            header = stream.read(20)
        require(len(header) == 20 and header[:6] == b'\x7fELF\x02\x01' and header[18:20] == b'\x3e\x00',
                'Player must be ELF64 little-endian x86-64')
        if self.spec['mode'] == 'native-progressive':
            require((self.payload / self.spec['nativePlanRelativePath']).is_file(), 'Frozen native plan absent')
        self.runtime.mkdir(parents=True, exist_ok=True)
        for name in ('config', 'tmp'):
            (self.runtime / name).mkdir(exist_ok=True)
        self.runtime_initial = tree_bytes(self.runtime)
        require(self.runtime.stat().st_dev == self.stage.stat().st_dev == self.payload.stat().st_dev,
                'Payload/runtime/stage must share the budgeted data filesystem')
        b = self.spec['budget']
        free = shutil.disk_usage(self.stage).free
        require(free >= b['outputBytes'] + b['runtimeGrowthBytes'] + b['freeReserveBytes'],
                'Insufficient measured free space for full declared stage growth and reserve')
        self.event('budget-preflight', freeBytes=free, runtimeInitialBytes=self.runtime_initial,
                   payloadBytes=self.payload_before['bytes'], budget=b)
        gpu = Nvml(self.spec['gpu'])
        previous = self.sample(gpu, True)
        for _ in range(5):
            time.sleep(self.spec['sampleSeconds'])
            current = self.sample(gpu, True)
            percent = cpu_quota_percent(previous['cgroup'], current['cgroup'])
            self.event('cpu-preflight', quotaPercent=percent)
            require(percent <= 20, 'CPU cgroup preflight exceeds 20 percent of quota')
            previous = current
        require(self.signal_requested is None, 'Stop requested before launch')
        self.telemetry_qualified = True
        return player, gpu

    def launch(self, player):
        import resource
        args = player_arguments(self.spec)
        env = dict(os.environ)
        require(not env.get('LD_PRELOAD') and not env.get('LD_AUDIT'), 'Unpinned injected native library environment')
        # Retain existing driver/OS caches. Only task app config and temporary
        # files are redirected. Never change HOME or global GPU cache settings.
        for key in ('DISPLAY', 'XAUTHORITY', 'WAYLAND_DISPLAY'):
            env.pop(key, None)
        env.update(self.spec['environment'])
        env.update(XDG_CONFIG_HOME=str(self.runtime / 'config'), TMPDIR=str(self.runtime / 'tmp'))
        changed = {k: env[k] for k in ('DISPLAY', 'XAUTHORITY', 'XDG_CONFIG_HOME', 'TMPDIR') if k in env}
        self.command = dict(kind=CONTRACT, startedUtc=utc(), player=str(player), sha256=digest(player),
            arguments=args, environmentOverrides=changed, policy='disabled', screenshots=False, trace=False,
            planBaselineForDisabled=False, wholeTaskCell='linux-vulkan-v1', mode=self.spec['mode'],
            specSha256=self.spec_hash, payloadManifestSha256=self.spec['manifestSha256'],
            windowMode='1920x1080 windowed Vulkan on previously validated existing X11 surface',
            cacheCondition='New process; existing app, OS and driver caches retained. Not driver-cold.',
            runtimeState=str(self.runtime), workingDirectory=str(self.payload))
        self.command['inheritedGraphicsEnvironment'] = {key: env.get(key) for key in
            ('LD_LIBRARY_PATH', 'VK_ICD_FILENAMES', 'VK_DRIVER_FILES', 'VK_LAYER_PATH',
             'VK_INSTANCE_LAYERS', '__GL_SHADER_DISK_CACHE', '__GL_SHADER_DISK_CACHE_PATH', 'XDG_CACHE_HOME')}
        write_json(self.stage / 'command.json', self.command)
        per_file = self.spec['budget']['perFileBytes']
        def limits():
            resource.setrlimit(resource.RLIMIT_FSIZE, (per_file, per_file))
            resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
        self.stdout = (self.stage / 'stdout.log').open('xb', buffering=0)
        self.stderr = (self.stage / 'stderr.log').open('xb', buffering=0)
        self.requested_utc = utc()
        self.started = time.monotonic()
        self.child = subprocess.Popen([str(player), *args], cwd=self.payload, env=env, stdin=subprocess.DEVNULL,
            stdout=self.stdout, stderr=self.stderr, start_new_session=True, close_fds=True,
            pass_fds=(self.lock.fd,), preexec_fn=limits)
        # Popen has returned after execve's error pipe closes. Capture the actual
        # kernel birth clock. A very early exit is still waitable (never poll/reap).
        root = proc(self.child.pid)
        self.tree.root = root
        self.tree.known.add(identity(root))
        require(root['pgid'] == self.child.pid and root['sid'] == self.child.pid, 'Dedicated Player session was not created')
        boot = next(int(line.split()[1]) for line in Path('/proc/stat').read_text().splitlines() if line.startswith('btime '))
        self.process_started_utc = datetime.fromtimestamp(boot + root['startTicks'] / os.sysconf('SC_CLK_TCK'), timezone.utc).isoformat()
        write_json(self.stage / 'process-start.json', dict(rootIdentity=root, launchRequestedUtc=self.requested_utc,
            processStartedUtc=self.process_started_utc, birthClock='proc btime (1s precision) + startTicks / CLK_TCK',
            launchMonotonicSeconds=self.started, execReturnedUtc=utc(), player=str(player), arguments=args))
        actual = Path(f'/proc/{self.child.pid}/exe').stat()
        expected = player.stat()
        require((actual.st_dev, actual.st_ino) == (expected.st_dev, expected.st_ino), 'Actual launched executable differs')
        self.event('player-started', rootIdentity=root, processStartedUtc=self.process_started_utc,
                   perFileHardLimitBytes=per_file, coreDumpHardLimitBytes=0)

    def run_player(self, gpu):
        previous = None
        while True:
            if self.signal_requested is not None:
                raise RuntimeError(f'supervisor-signal-{self.signal_requested}')
            if time.monotonic() - self.started >= self.spec['timeoutSeconds']:
                raise TimeoutError('player-timeout')
            sample = self.sample(gpu)
            if previous:
                self.event('cpu-runtime', quotaPercent=cpu_quota_percent(previous['cgroup'], sample['cgroup']))
                foreign = foreign_cpu_quota_percent(previous, sample, self.tree.known, os.getpid())
                self.event('other-process-cpu-runtime', quotaPercent=foreign, scope='sampled lower bound')
                require(foreign <= 10, 'Foreign sampled CPU work exceeds 10 percent of allocated quota')
            previous = sample
            self.cleanup = self.tree.finish_if_exited()
            if self.cleanup is not None:
                require(self.tree.root_exit is not None and self.tree.root_exit['exitCode'] == 0, 'nonzero-player-exit')
                return
            time.sleep(self.spec['sampleSeconds'])

    def settle(self):
        if self.tree is None:
            # No launch was possible before subreaper setup. Verify actual children.
            try:
                os.waitid(os.P_ALL, 0, os.WEXITED | os.WNOHANG | os.WNOWAIT)
            except ChildProcessError:
                return dict(allOwnedExited=True, waitidNoChildren=True, remaining=[], errors=[], verifiedUtc=utc())
            return None
        cleanup = self.tree.finish_if_exited()
        if cleanup:
            return cleanup
        for sig, duration in ((signal.SIGTERM, self.spec['termGraceSeconds']), (signal.SIGKILL, self.spec['killGraceSeconds'])):
            self.tree.signal_groups(sig)
            deadline = time.monotonic() + duration
            while time.monotonic() < deadline:
                cleanup = self.tree.finish_if_exited()
                if cleanup:
                    return cleanup
                time.sleep(.25)
        return None

    def hold_until_clean(self):
        """Unresolved cleanup must not drop the outer lock by returning/exiting."""
        first = True
        while True:
            try:
                cleanup = self.settle()
                if cleanup is not None:
                    return cleanup
            except Exception as error:
                self.errors.append(str(error))
                self.event('cleanup-error', error=str(error))
            if first:
                self.event('cleanup-unresolved-lock-held', supervisorIdentity=proc(os.getpid()),
                    instruction='Do not retry hardware work. Inspect this holder and owned process identities.')
                first = False
            # No repeated broad kill. Re-evaluate bound groups only, retaining the
            # lock indefinitely when a descendant/group cannot be proven safe.
            time.sleep(5)

    def run(self):
        finished = None
        try:
            player, gpu = self.prepare()
            self.launch(player)
            self.run_player(gpu)
            # The final sample also retains post-process GPU/cgroup/disk state.
            self.sample(gpu)
        except Exception as error:
            self.stop_reason = self.stop_reason or str(error)
            self.errors.append(str(error))
            self.event('stage-failed', reason=self.stop_reason)
        finally:
            # Even exception paths after Popen must prove all descendants gone.
            self.cleanup = self.cleanup or self.hold_until_clean()
            finished = utc()
            finished_monotonic = time.monotonic()
            if self.child is not None and self.tree.root_exit is not None:
                self.child.returncode = self.tree.root_exit['exitCode']  # already reaped by owned tree
            for name in ('stdout', 'stderr'):
                stream = getattr(self, name, None)
                if stream is not None:
                    stream.close()
        final_disk = None
        try:
            if self.payload_before:
                self.payload_after = verify_payload(self.payload, self.manifest)
                self.event('payload-after', **self.payload_after)
            final_disk = self.disk_sample() if self.runtime.exists() else None
            if final_disk:
                require(final_disk['fileAtHardLimit'] is None, 'Per-file hard limit reached; output may be incomplete')
                reason = disk_stop(self.spec['budget'], final_disk['freeBytes'], final_disk['stageBytes'], final_disk['runtimeGrowthBytes'])
                require(reason is None, reason or 'Final disk threshold')
            require(self.stop_reason is None and not self.errors, 'Stage already failed')
        except Exception as error:
            self.stop_reason = self.stop_reason or str(error)
            self.errors.append(str(error))
        process = dict(kind=CONTRACT, rootIdentity=self.tree.root if self.tree else None,
            launchRequestedUtc=getattr(self, 'requested_utc', None),
            processStartedUtc=getattr(self, 'process_started_utc', None), finishedUtc=finished,
            rootExitObservedUtc=self.tree.root_exit['observedUtc'] if self.tree and self.tree.root_exit else None,
            exitCode=self.tree.root_exit['exitCode'] if self.tree and self.tree.root_exit else None,
            stopReason=self.stop_reason, errors=self.errors,
            launchToCleanupMonotonicSeconds=finished_monotonic - self.started if self.started else None,
            cleanup=self.cleanup)
        if not (self.stage / 'command.json').exists():
            # Preflight failed before an invocation was recorded. Preserve the
            # three-file analyzer interface without inventing an actual launch.
            write_json(self.stage / 'command.json', dict(kind=CONTRACT, player=None, sha256=None,
                arguments=None, plannedArguments=player_arguments(self.spec), launchInvocationRecorded=False,
                policy='disabled', trace=False, screenshots=False, wholeTaskCell='linux-vulkan-v1',
                specSha256=self.spec_hash, payloadManifestSha256=self.spec['manifestSha256'],
                cacheCondition='No Player invocation recorded'))
        for name in ('stdout.log', 'stderr.log'):
            path = self.stage / name
            if not path.exists():
                with path.open('xb'):
                    pass
        # Keep a pre-release durable receipt; final stage.json exists only after
        # unlock. Disk-write failure yields incomplete evidence, never success.
        write_json(self.stage / 'process.json', process)
        self.event('cleanup-complete', cleanup=self.cleanup, finalDisk=final_disk)
        self.events.close()
        self.resources.close()
        evidence = {p.name: digest(p) for p in self.stage.iterdir() if p.is_file()}
        boundary = dict(schemaVersion=1, kind=CONTRACT, status='failed' if self.stop_reason else 'completed',
            mutexReleased=False, lock=self.lock.receipt if self.lock else None, lockReleasedUtc=None,
            cleanup=self.cleanup, payloadBefore=self.payload_before, payloadAfter=self.payload_after,
            telemetryQualified=self.telemetry_qualified, errors=self.errors, finalDisk=final_disk,
            budget=self.spec['budget'], budgetEnforcement='poll stop thresholds plus inherited RLIMIT_FSIZE per file; aggregate overshoot is possible',
            performanceClaimEligible=False, fullRouteAccepted=False,
            evidenceSha256=evidence, finishedUtc=finished)
        write_json(self.stage / 'before-release.json', boundary)
        if self.lock is not None:
            boundary['lockReleasedUtc'] = self.lock.release(self.cleanup)
            boundary['mutexReleased'] = True
        write_json(self.stage / 'stage.json', boundary)
        print(json.dumps(dict(stage=str(self.stage), status=boundary['status'], mutexReleased=boundary['mutexReleased'],
                              stopReason=self.stop_reason)), flush=True)
        return 0 if boundary['status'] == 'completed' else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--spec', type=Path, required=True)
    parser.add_argument('--spec-sha256', required=True)
    args = parser.parse_args()
    return Supervisor(args.spec, args.spec_sha256).run()


if __name__ == '__main__':
    raise SystemExit(main())
