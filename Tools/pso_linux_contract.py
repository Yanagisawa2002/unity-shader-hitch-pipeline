"""Portable contracts for the Linux Player supervisor; never launches a process.

Receipts describe observation and cleanup, not route/visual/performance acceptance.
The CLI can index a completed payload offline or print an SSH-helper command body.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import shlex
import stat

CAMPAIGN = '/root/autodl-tmp/codex-whole-task-20260915'
CONTRACT = 'shader-linux-player-stage-v1'
GIB = 2 ** 30
TOOL_FILES = ('run_pso_linux_player.py', 'pso_linux_contract.py')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def utc():
    return datetime.now(timezone.utc).isoformat()


def digest(path):
    result = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write_json(path, value):
    # New immutable files only. Live progress belongs in events.jsonl.
    with Path(path).open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write('\n')
        stream.flush()


def relative_file(value):
    require(isinstance(value, str) and '\\' not in value and ':' not in value and
            not any(ord(c) < 32 for c in value), 'Portable POSIX relative file path required')
    p = PurePosixPath(value)
    require(not p.is_absolute() and value == p.as_posix() and
            all(x not in ('', '.', '..') for x in p.parts), 'Noncanonical relative file path')
    require(bool(p.parts), 'Empty file path')
    return p


def safe_path(value, parent):
    """Check lexical and resolved ancestry, including every existing symlink."""
    p, root = Path(value), Path(parent)
    require(p.is_absolute() and p != root and p.is_relative_to(root), 'Path outside task directory')
    require('..' not in p.parts and p.resolve().is_relative_to(root.resolve()), 'Path escapes task directory')
    for item in (p, *p.parents):
        require(not item.is_symlink(), 'Symlink in task path: ' + str(item))
    return p


def payload_index(root, executable_paths=None):
    """Hash every regular file. Windows callers must declare intended executable paths.

    Permission values are target POSIX modes, verified after transfer, never applied
    here. No symlink, hard-link alias, device, FIFO or socket can enter a payload.
    """
    root = Path(root)
    require(root.is_dir() and not root.is_symlink(), 'Payload must be a real directory')
    executable_paths = set(executable_paths) if executable_paths is not None else None
    if executable_paths is not None:
        for name in executable_paths:
            relative_file(name)
    rows = []
    for p in sorted(root.rglob('*'), key=lambda p: p.relative_to(root).as_posix()):
        s = p.lstat()
        require(not stat.S_ISLNK(s.st_mode), 'Payload symlink: ' + str(p))
        if stat.S_ISDIR(s.st_mode):
            continue
        require(stat.S_ISREG(s.st_mode) and s.st_nlink == 1, 'Payload contains nonregular/aliased file')
        name = p.relative_to(root).as_posix()
        mode = stat.S_IMODE(s.st_mode) if executable_paths is None else (0o755 if name in executable_paths else 0o644)
        require(mode in (0o644, 0o755), 'Normalize target payload permissions before indexing')
        rows.append(dict(path=name, bytes=s.st_size, sha256=digest(p), mode=mode))
    require(bool(rows), 'Empty payload')
    if executable_paths is not None:
        require(executable_paths <= {r['path'] for r in rows}, 'Declared executable missing')
    return dict(schemaVersion=1, kind='complete-player-payload-v1', files=rows,
                totalBytes=sum(r['bytes'] for r in rows))


def verify_payload(root, manifest):
    require(manifest.get('kind') == 'complete-player-payload-v1', 'Wrong payload manifest kind')
    expected = manifest.get('files', [])
    require(expected and len({r['path'] for r in expected}) == len(expected), 'Empty/duplicate payload manifest')
    for row in expected:
        relative_file(row['path'])
        require(row['mode'] in (0o644, 0o755), 'Invalid payload mode')
        require(re.fullmatch('[0-9a-f]{64}', row['sha256']) is not None, 'Invalid payload hash')
    actual = payload_index(root)
    require(actual == manifest, 'Complete payload differs (file set, bytes, hash or permissions)')
    return dict(verified=True, fileCount=len(expected), bytes=actual['totalBytes'], verifiedUtc=utc())


def positive_number(value, name, low, high):
    require(type(value) in (int, float) and math.isfinite(value) and low <= value <= high,
            'Invalid ' + name)


def validate_spec(spec):
    require(spec.get('kind') == CONTRACT and spec.get('schemaVersion') == 1, 'Unsupported stage spec')
    require(spec.get('campaignRoot') == CAMPAIGN and spec.get('owner') == 'shader', 'Wrong campaign/owner')
    require(set(spec) <= {'kind', 'schemaVersion', 'owner', 'campaignRoot', 'stage', 'payloadRoot',
        'runtimeState', 'playerRelativePath', 'manifestSha256', 'runnerSha256', 'contractSha256',
        'mode', 'session', 'gpu', 'environment', 'allowedGpuProcesses', 'timeoutSeconds', 'sampleSeconds',
        'termGraceSeconds', 'killGraceSeconds', 'budget', 'nativePlanRelativePath', 'nativeCount'},
        'Unknown launch-spec field (arbitrary extra arguments are not supported)')
    root = PurePosixPath(CAMPAIGN) / 'shader'
    for name, sub in (('stage', 'stages'), ('payloadRoot', 'payloads'), ('runtimeState', 'runtime-state')):
        value = spec[name]
        p = PurePosixPath(value)
        require(p.is_relative_to(root / sub) and p != root / sub and '..' not in p.parts and
                p.as_posix() == value and '\\' not in value, 'Invalid ' + name)
    relative_file(spec['playerRelativePath'])
    for field in ('manifestSha256', 'runnerSha256', 'contractSha256'):
        require(re.fullmatch('[0-9a-f]{64}', spec[field]) is not None, 'Missing pinned ' + field)
    require(spec['mode'] in ('profile', 'unprofiled', 'checkpoints', 'native-progressive'), 'Unsupported mode')
    require(re.fullmatch('[a-zA-Z0-9_-]{1,100}', spec['session']) is not None, 'Invalid frozen session label')
    if spec['mode'] == 'native-progressive':
        relative_file(spec['nativePlanRelativePath'])
        require(type(spec['nativeCount']) is int and 1 <= spec['nativeCount'] <= 65536, 'Invalid native count')
    else:
        require('nativeCount' not in spec and 'nativePlanRelativePath' not in spec, 'Unexpected native plan/count')
    for key in ('uuid', 'name', 'driver'):
        require(isinstance(spec['gpu'][key], str) and spec['gpu'][key].strip() not in ('', 'unknown'),
                'GPU identity unavailable: ' + key)
    require(spec['gpu']['uuid'].startswith('GPU-'), 'Whole physical GPU UUID required')
    require(set(spec['environment']) <= {'DISPLAY', 'XAUTHORITY'}, 'Only an existing X11 surface is supported')
    require(bool(spec['environment'].get('DISPLAY')), 'No previously validated graphics surface')
    for value in spec['environment'].values():
        require(isinstance(value, str) and value and '\x00' not in value, 'Invalid display environment')
    for p in spec.get('allowedGpuProcesses', []):
        require(type(p['pid']) is int and p['pid'] > 1 and type(p['startTicks']) is int and p['startTicks'] > 0,
                'Display process needs a pinned birth identity')
        require(re.fullmatch('[0-9a-f]{64}', p['exeSha256']) is not None, 'Display executable not pinned')
    b = spec['budget']
    for key in ('outputBytes', 'runtimeGrowthBytes', 'freeReserveBytes', 'stopHeadroomBytes', 'perFileBytes'):
        require(type(b[key]) is int and b[key] > 0, 'Invalid byte budget: ' + key)
    require(b['freeReserveBytes'] >= 10 * GIB, 'Data reserve below shared 10 GiB minimum')
    require(b['stopHeadroomBytes'] >= GIB, 'At least 1 GiB monitored stop headroom required')
    require(b['outputBytes'] > b['stopHeadroomBytes'] and
            b['perFileBytes'] <= b['outputBytes'] - b['stopHeadroomBytes'], 'Inconsistent file/output limits')
    for key, low, high in (('timeoutSeconds', 30, 3600), ('sampleSeconds', .25, 5),
                          ('termGraceSeconds', 1, 30), ('killGraceSeconds', 1, 30)):
        positive_number(spec[key], key, low, high)
    return spec


def player_arguments(spec):
    validate_spec(spec)
    stage = PurePosixPath(spec['stage'])
    args = ['-force-vulkan', '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080',
            '-logFile', str(stage / 'player.log'), '-pso-external-capture', '-pso-output', str(stage / 'capture'),
            '-pso-session', spec['session'], '-pso-external-observer-only', '-pso-disable-warmup',
            '-pso-whole-task-cell', 'linux-vulkan-v1']
    if spec['mode'] == 'profile':
        args.append('-pso-whole-task-profile')
    elif spec['mode'] == 'checkpoints':
        args.append('-pso-fixed-checkpoints')
    elif spec['mode'] == 'native-progressive':
        args += ['-pso-native-progressive-control', '-pso-native-progressive-count', str(spec['nativeCount']),
                 '-pso-native-progressive-plan', str(PurePosixPath(spec['payloadRoot']) / spec['nativePlanRelativePath'])]
    return args


def helper_body(runner, spec_path, spec_sha256):
    require(re.fullmatch('[0-9a-f]{64}', spec_sha256) is not None, 'Spec hash required')
    for path in (runner, spec_path):
        p = PurePosixPath(path)
        require(p.is_relative_to(PurePosixPath(CAMPAIGN) / 'shader') and '..' not in p.parts,
                'Remote helper paths outside Shader')
    # The EXISTING SSH helper --lock is mandatory. No nested flock, nohup or &.
    return '#!/bin/bash\nset -eu\nexec python3 -B ' + shlex.join(
        [runner, '--spec', spec_path, '--spec-sha256', spec_sha256]) + '\n'


def parse_proc_stat(text):
    closing = text.rfind(')')
    require(closing > 0 and '(' in text, 'Malformed proc stat')
    fields = text[closing + 1:].split()
    require(len(fields) >= 22, 'Short proc stat')
    return dict(pid=int(text.split('(', 1)[0]), state=fields[0], ppid=int(fields[1]),
                pgid=int(fields[2]), sid=int(fields[3]), userTicks=int(fields[11]),
                systemTicks=int(fields[12]), startTicks=int(fields[19]), rssPages=int(fields[21]))


def identity(row):
    return row['pid'], row['startTicks']


def owned_snapshot(snapshot, known, supervisor_pid):
    """The single-threaded subreaper launches only the Player. No probe children.

    Direct adopted children are therefore ours, including double-fork/setsid cases.
    A reused PID is never considered owned through its old start time.
    """
    owned = {pid: row for pid, row in snapshot.items() if identity(row) in known}
    changed = True
    while changed:
        changed = False
        for pid, row in snapshot.items():
            if pid not in owned and (row['ppid'] == supervisor_pid or row['ppid'] in owned):
                owned[pid] = row
                changed = True
    return owned


def safe_signal_groups(snapshot, owned, supervisor_pgid, supervisor_pid):
    """Only signal groups wholly in the recorded descendant tree.

    Runtime uses stopped, unreaped group leaders to prevent group-ID reuse while
    killpg executes. A leaderless/escaped/unobservable group is refused and keeps
    the lease unresolved; it is never broadened to a process-name kill.
    """
    groups, refused = [], []
    for pgid in sorted({r['pgid'] for r in owned.values() if r['state'] not in ('Z', 'X')}):
        members = [r for r in snapshot.values() if r['pgid'] == pgid]
        leader = snapshot.get(pgid)
        if pgid <= 1 or pgid == supervisor_pgid or leader is None or pgid not in owned or leader['ppid'] != supervisor_pid or any(
                r['pid'] not in owned or identity(owned[r['pid']]) != identity(r) for r in members):
            refused.append(pgid)
        else:
            groups.append((pgid, leader['startTicks']))
    return groups, refused


def inherited_lock_line(text, major, minor, inode):
    """fdinfo lock entries describe locks attached to THIS open description.

    An independently opened fd for the same inode is insufficient. No acquisition
    or lock conversion is attempted here or by the runtime supervisor.
    """
    pattern = r'^lock:\s+\d+:\s+FLOCK\s+ADVISORY\s+WRITE\s+(-?\d+)\s+([0-9a-f]+):([0-9a-f]+):(\d+)\s+0\s+EOF\s*$'
    for line in text.splitlines():
        m = re.fullmatch(pattern, line, re.IGNORECASE)
        if m and (int(m[2], 16), int(m[3], 16), int(m[4])) == (major, minor, inode):
            return dict(lockOwnerPid=int(m[1]), raw=line)
    return None


def disk_stop(budget, free_bytes, output_bytes, runtime_growth):
    if free_bytes < budget['freeReserveBytes'] + budget['stopHeadroomBytes']:
        return 'data-disk-reserve-threshold'
    if output_bytes >= budget['outputBytes'] - budget['stopHeadroomBytes']:
        return 'stage-output-stop-threshold'
    if runtime_growth >= budget['runtimeGrowthBytes']:
        return 'runtime-growth-stop-threshold'
    return None


def cpu_quota_percent(before, after):
    elapsed = after['monotonicSeconds'] - before['monotonicSeconds']
    require(elapsed > 0 and after['quotaCores'] > 0, 'Invalid CPU sample span/quota')
    require(before['quotaCores'] == after['quotaCores'] and before['cpuset'] == after['cpuset'], 'Cgroup allocation changed')
    usage = after['usageUsec'] - before['usageUsec']
    require(usage >= 0, 'Cgroup usage counter regressed')
    return 100 * usage / 1e6 / elapsed / after['quotaCores']


def foreign_cpu_quota_percent(before, after, owned_identities, supervisor_pid):
    """Observed non-Player process deltas, normalized to the actual cgroup quota.

    This is a sampled lower bound; already-exited short processes are not invented.
    New or already-exited processes lack a full interval here. Unbounded host CPU
    count is never a denominator; preflight additionally observes total cgroup CPU.
    """
    previous = {identity(r): r for r in before['otherProcesses']}
    total = 0
    for row in after['otherProcesses']:
        if identity(row) in owned_identities or row['pid'] == supervisor_pid:
            continue
        old = previous.get(identity(row))
        if old is not None:
            delta = row['userTicks'] + row['systemTicks'] - old['userTicks'] - old['systemTicks']
            require(delta >= 0, 'Process CPU counter regressed')
            total += delta
    elapsed = after['monotonicSeconds'] - before['monotonicSeconds']
    require(elapsed > 0, 'Invalid process sample span')
    return 100 * total / after['processClockTicksPerSecond'] / elapsed / after['cgroup']['quotaCores']


def stage_failures(stage, command, process, boundary):
    """Additional mandatory guard for Linux; copied legacy success cannot pass."""
    failures = []
    def check(value, message):
        if not value:
            failures.append(message)
    check(boundary.get('kind') == CONTRACT, 'Missing Linux supervisor contract')
    check(boundary.get('status') == 'completed' and boundary.get('mutexReleased') is True,
          'Linux stage failed or lock release unproven')
    cleanup = boundary.get('cleanup') or {}
    check(cleanup.get('allOwnedExited') is True and cleanup.get('waitidNoChildren') is True,
          'Linux descendant cleanup is unproven')
    check(not cleanup.get('remaining') and not cleanup.get('errors'), 'Linux cleanup incomplete')
    check(boundary.get('telemetryQualified') is True, 'Linux resource telemetry unavailable/ineligible')
    check((boundary.get('payloadBefore') or {}).get('verified') is True and
          (boundary.get('payloadAfter') or {}).get('verified') is True, 'Complete Linux payload not verified at both boundaries')
    check((boundary.get('lock') or {}).get('inheritedVerified') is True and bool(boundary.get('lockReleasedUtc')),
          'Inherited Linux flock/release receipt missing')
    check(type(process.get('exitCode')) is int and process.get('exitCode') == 0 and not process.get('stopReason'),
          'Linux Player stopped abnormally')
    check(process.get('rootIdentity') is not None and process.get('processStartedUtc') and
          process.get('finishedUtc') and process.get('rootExitObservedUtc'), 'Actual Linux process boundaries missing')
    for key in ('stdout.log', 'stderr.log', 'resources.jsonl', 'events.jsonl', 'payload-manifest.json', 'launch-spec.json'):
        path = Path(stage) / key
        check(path.is_file() and boundary.get('evidenceSha256', {}).get(key) == (digest(path) if path.is_file() else None),
              'Missing/changed Linux evidence: ' + key)
    check(command.get('payloadManifestSha256') == boundary.get('evidenceSha256', {}).get('payload-manifest.json') and
          command.get('specSha256') == boundary.get('evidenceSha256', {}).get('launch-spec.json'),
          'Linux command identity differs from preserved inputs')
    for name, document in (('command.json', command), ('process.json', process)):
        p = Path(stage) / name
        check(p.is_file() and boundary.get('evidenceSha256', {}).get(name) == (digest(p) if p.is_file() else None),
              'Missing/changed Linux receipt: ' + name)
    try:
        spec = validate_spec(read_json(Path(stage) / 'launch-spec.json'))
        check(command.get('arguments') == player_arguments(spec), 'Actual Linux arguments differ from frozen spec')
        check(command.get('player') == str(PurePosixPath(spec['payloadRoot']) / spec['playerRelativePath']),
              'Linux Player path differs from frozen payload')
        manifest = read_json(Path(stage) / 'payload-manifest.json')
        players = [r for r in manifest['files'] if r['path'] == spec['playerRelativePath']]
        check(len(players) == 1 and players[0]['sha256'] == command.get('sha256'), 'Linux executable hash differs from full manifest')
        resources = [json.loads(line) for line in (Path(stage) / 'resources.jsonl').read_text(encoding='utf-8').splitlines()]
        check(sum(r.get('preflight') is True for r in resources) >= 6 and
              any(r.get('preflight') is False for r in resources), 'Linux preflight/runtime sample coverage missing')
        for sample in resources:
            check(sample.get('errors') == [], 'Linux resource sample contains unknown/error')
            gpu = sample['gpu']
            check(all(gpu.get(k) == spec['gpu'][k] for k in ('uuid', 'name', 'driver')) and
                  isinstance(gpu.get('compute'), list) and isinstance(gpu.get('graphics'), list),
                  'Linux GPU identity/process telemetry incomplete')
            check(type(gpu['gpuPercent']) in (int, float) and 0 <= gpu['gpuPercent'] <= 100,
                  'Linux GPU utilization unknown')
            cgroup = sample['cgroup']
            check(cgroup['cgroup'] == '0::/' and cgroup['quotaCores'] > 0 and cgroup['memoryMaxBytes'] > 0 and
                  bool(cgroup['cpuset']) and bool(cgroup['cpuStat']), 'Linux cgroup telemetry incomplete')
            check(isinstance(sample.get('ownedProcesses'), list) and isinstance(sample.get('otherProcesses'), list),
                  'Linux process telemetry incomplete')
            disk = sample['disk']
            check('fileAtHardLimit' in disk and disk['fileAtHardLimit'] is None, 'Linux per-file limit reached or unavailable')
            check(disk_stop(spec['budget'], disk['freeBytes'], disk['stageBytes'], disk['runtimeGrowthBytes']) is None,
                  'Linux sampled disk threshold exceeded')
    except (KeyError, ValueError, OSError, TypeError) as error:
        check(False, 'Invalid preserved Linux launch inputs: ' + str(error))
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='operation', required=True)
    p = sub.add_parser('manifest')
    p.add_argument('payload', type=Path)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--executable', action='append', help='Windows: target executable file (repeat)')
    p = sub.add_parser('helper-body')
    p.add_argument('--runner', required=True)
    p.add_argument('--spec', required=True)
    p.add_argument('--spec-sha256', required=True)
    p.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.operation == 'manifest':
        require(not args.output.resolve().is_relative_to(args.payload.resolve()), 'Manifest must be outside payload')
        write_json(args.output, payload_index(args.payload, args.executable))
        print(digest(args.output))
    else:
        with args.output.open('x', encoding='utf-8', newline='\n') as stream:
            stream.write(helper_body(args.runner, args.spec, args.spec_sha256))


if __name__ == '__main__':
    main()
