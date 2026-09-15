"""Offline fixtures/mocked syscalls only. Never launches Linux, Unity or a GPU job."""
from copy import deepcopy
import ctypes
from datetime import datetime, timezone
import io
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pso_linux_contract as contract
import run_pso_linux_player as runner


def spec():
    root = contract.CAMPAIGN + '/shader'
    return dict(schemaVersion=1, kind=contract.CONTRACT, owner='shader', campaignRoot=contract.CAMPAIGN,
        stage=root + '/stages/d0', payloadRoot=root + '/payloads/build01',
        runtimeState=root + '/runtime-state/build01', playerRelativePath='Player.x86_64',
        manifestSha256='a' * 64, runnerSha256='b' * 64, contractSha256='c' * 64,
        mode='profile', session='shader-discovery-01', gpu=dict(uuid='GPU-fixture', name='fixture', driver='fixture'),
        environment={'DISPLAY': ':99'}, allowedGpuProcesses=[], timeoutSeconds=600, sampleSeconds=2,
        termGraceSeconds=10, killGraceSeconds=10, budget=dict(outputBytes=8 * contract.GIB,
            runtimeGrowthBytes=2 * contract.GIB, freeReserveBytes=10 * contract.GIB,
            stopHeadroomBytes=contract.GIB, perFileBytes=7 * contract.GIB))


def row(pid, birth, ppid=10, pgid=None, sid=None, state='S'):
    return dict(pid=pid, startTicks=birth, ppid=ppid, pgid=pid if pgid is None else pgid,
                sid=pid if sid is None else sid, state=state, userTicks=3, systemTicks=2, rssPages=7)


class PayloadAndCommandTests(unittest.TestCase):
    def test_manifest_records_every_file_and_intended_permissions(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'Player.x86_64').write_bytes(b'fixture executable')
            (root / 'Player_Data').mkdir()
            (root / 'Player_Data' / 'level0').write_bytes(b'level data')
            result = contract.payload_index(root, ['Player.x86_64'])
            self.assertEqual(len(result['files']), 2)
            self.assertEqual(result['totalBytes'], 28)
            self.assertEqual({r['path']: r['mode'] for r in result['files']},
                             {'Player.x86_64': 0o755, 'Player_Data/level0': 0o644})
            self.assertTrue(all(len(r['sha256']) == 64 for r in result['files']))

    def test_manifest_rejects_payload_alias(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'a').write_bytes(b'a')
            os.link(root / 'a', root / 'b')
            with self.assertRaisesRegex(ValueError, 'aliased'):
                contract.payload_index(root, ['a'])

    def test_full_manifest_drift_extra_and_permission_changes_fail(self):
        original = dict(schemaVersion=1, kind='complete-player-payload-v1', totalBytes=1,
                        files=[dict(path='Player.x86_64', bytes=1, sha256='a' * 64, mode=0o755)])
        variants = []
        for key, value in (('sha256', 'b' * 64), ('bytes', 2), ('mode', 0o644)):
            changed = deepcopy(original)
            changed['files'][0][key] = value
            variants.append(changed)
        changed = deepcopy(original)
        changed['files'].append(dict(path='extra', bytes=1, sha256='c' * 64, mode=0o644))
        variants.append(changed)
        for changed in variants:
            with self.subTest(changed=changed), patch.object(contract, 'payload_index', return_value=changed):
                with self.assertRaisesRegex(ValueError, 'Complete payload differs'):
                    contract.verify_payload(Path('/fixture'), original)
        with patch.object(contract, 'payload_index', return_value=original):
            self.assertTrue(contract.verify_payload(Path('/fixture'), original)['verified'])

    def test_closed_modes_keep_the_same_original_route_arguments(self):
        original = spec()
        profiled = contract.player_arguments(original)
        original['mode'] = 'unprofiled'
        plain = contract.player_arguments(original)
        self.assertEqual(profiled, plain + ['-pso-whole-task-profile'])
        for arg in ('-force-vulkan', '-pso-external-observer-only', '-pso-disable-warmup'):
            self.assertIn(arg, plain)
        for forbidden in ('-nographics', '-batchmode', '-force-glcore'):
            self.assertNotIn(forbidden, plain)
        original['mode'] = 'checkpoints'
        self.assertEqual(contract.player_arguments(original), plain + ['-pso-fixed-checkpoints'])

    def test_native_control_needs_a_pinned_payload_plan_and_count(self):
        value = spec()
        value.update(mode='native-progressive', nativeCount=16, nativePlanRelativePath='Player_Data/StreamingAssets/plan.json')
        args = contract.player_arguments(value)
        self.assertIn('-pso-native-progressive-control', args)
        self.assertEqual(args[-1], value['payloadRoot'] + '/' + value['nativePlanRelativePath'])
        value['nativeCount'] = 0
        with self.assertRaises(ValueError):
            contract.player_arguments(value)

    def test_unknown_gpu_surface_escape_and_bad_budget_fail_before_launch(self):
        for mutation in (lambda s: s['gpu'].update(name='unknown'),
                         lambda s: s.update(environment={}),
                         lambda s: s.update(mode='shell'),
                         lambda s: s.update(stage=contract.CAMPAIGN + '/summit/stages/other'),
                         lambda s: s.update(payloadRoot=s['payloadRoot'] + '/../other'),
                         lambda s: s['environment'].update(HOME='/tmp'),
                         lambda s: s['budget'].update(freeReserveBytes=1),
                         lambda s: s.update(sampleSeconds=float('nan'))):
            value = spec()
            mutation(value)
            with self.assertRaises(ValueError):
                contract.validate_spec(value)

    def test_new_outputs_cannot_overwrite_and_helper_stays_foreground(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'receipt.json'
            contract.write_json(path, {'first': True})
            with self.assertRaises(FileExistsError):
                contract.write_json(path, {'first': False})
            self.assertTrue(contract.read_json(path)['first'])
        body = contract.helper_body(contract.CAMPAIGN + '/shader/tools/run_pso_linux_player.py',
                                    contract.CAMPAIGN + '/shader/specs/d0.json', 'd' * 64)
        self.assertIn('exec python3 -B ', body)
        self.assertNotIn('flock', body)
        self.assertNotIn('&', body)
        self.assertNotIn('nohup', body)


class LinuxOwnershipFixtures(unittest.TestCase):
    def test_proc_stat_comm_parentheses_and_spaces_do_not_shift_birth(self):
        fields = ['S', '10', '20', '20'] + ['0'] * 18
        fields[11], fields[12], fields[19], fields[21] = '100', '200', '999', '8'
        parsed = contract.parse_proc_stat('20 (Player (worker) name) ' + ' '.join(fields))
        self.assertEqual(contract.identity(parsed), (20, 999))
        self.assertEqual((parsed['pgid'], parsed['userTicks'], parsed['rssPages']), (20, 100, 8))

    def test_root_exit_does_not_hide_live_or_adopted_descendants(self):
        snapshot = {20: row(20, 100, state='Z'), 21: row(21, 101, ppid=20, pgid=20, sid=20),
                    30: row(30, 110, ppid=10), 90: row(90, 500, ppid=1)}
        owned = contract.owned_snapshot(snapshot, {(20, 100)}, 10)
        self.assertEqual(set(owned), {20, 21, 30})
        self.assertEqual(contract.safe_signal_groups(snapshot, owned, 9, 10), ([(20, 100), (30, 110)], []))

    def test_pid_reuse_and_foreign_group_members_are_never_killed(self):
        snapshot = {20: row(20, 999, ppid=1), 21: row(21, 101, ppid=1, pgid=20, sid=20)}
        owned = contract.owned_snapshot(snapshot, {(20, 100), (21, 101)}, 10)
        self.assertEqual(set(owned), {21})
        self.assertEqual(contract.safe_signal_groups(snapshot, owned, 9, 10), ([], [20]))
        snapshot[20] = row(20, 100)
        snapshot[99] = row(99, 900, ppid=1, pgid=20, sid=20)
        owned = contract.owned_snapshot(snapshot, {(20, 100), (21, 101)}, 10)
        self.assertEqual(contract.safe_signal_groups(snapshot, owned, 9, 10), ([], [20]))

    def test_leaderless_or_not_waitable_group_and_supervisor_group_refused(self):
        for snapshot in ({21: row(21, 101, ppid=10, pgid=20, sid=20)},
                         {20: row(20, 100, ppid=50)},
                         {9: row(9, 100, ppid=10)}):
            owned = dict(snapshot)
            groups, refused = contract.safe_signal_groups(snapshot, owned, 9, 10)
            self.assertEqual(groups, [])
            self.assertTrue(refused)

    def test_fdinfo_requires_inherited_exclusive_flock_on_exact_inode(self):
        line = 'lock:\t1: FLOCK  ADVISORY  WRITE 123 00:2d:998 0 EOF\n'
        self.assertEqual(contract.inherited_lock_line(line, 0, 45, 998)['lockOwnerPid'], 123)
        for bad in (line.replace('FLOCK', 'POSIX'), line.replace('WRITE', 'READ'),
                    line.replace('998', '999'), line.replace('00:2d', '00:2e'), 'pos: 0\nflags: 0100000\n'):
            self.assertIsNone(contract.inherited_lock_line(bad, 0, 45, 998))

    def test_none_waitid_result_is_a_live_child_not_a_release(self):
        tree = object.__new__(runner.OwnedTree)
        with patch.object(runner.os, 'waitid', return_value=None, create=True), \
             patch.multiple(runner.os, P_ALL=0, WEXITED=4, WNOHANG=1, WNOWAIT=0x1000000, create=True):
            self.assertFalse(tree.no_children())
        with patch.object(runner.os, 'waitid', side_effect=ChildProcessError, create=True), \
             patch.multiple(runner.os, P_ALL=0, WEXITED=4, WNOHANG=1, WNOWAIT=0x1000000, create=True):
            self.assertTrue(tree.no_children())

    def test_release_requires_cleanup_and_only_unlocks_inherited_description(self):
        lock = object.__new__(runner.InheritedLock)
        lock.fd, lock.device, lock.inode = 9, 5, 7
        lock.assert_held = Mock()
        lock.fcntl = SimpleNamespace(LOCK_UN=8, flock=Mock())
        for invalid in ({}, {'allOwnedExited': True},
                        {'allOwnedExited': True, 'waitidNoChildren': True, 'remaining': [20]}):
            with self.assertRaises(ValueError):
                lock.release(invalid)
            lock.fcntl.flock.assert_not_called()
        with patch.object(Path, 'read_text', return_value=''), \
             patch.object(runner.os, 'major', return_value=0, create=True), \
             patch.object(runner.os, 'minor', return_value=5, create=True), \
             patch.object(runner.os, 'close') as close:
            released = lock.release(dict(allOwnedExited=True, waitidNoChildren=True, remaining=[], errors=[]))
            self.assertIsNotNone(datetime.fromisoformat(released))
            lock.fcntl.flock.assert_called_once_with(9, 8)
            close.assert_called_once_with(9)


class ResourceFixtures(unittest.TestCase):
    def test_file_at_hard_limit_cannot_hide_below_aggregate_threshold(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'capture.raw'
            path.write_bytes(b'12345678')
            self.assertIsNone(runner.file_limit_reached([Path(tmp)], 9))
            self.assertEqual(runner.file_limit_reached([Path(tmp)], 8), str(path))

    def test_foreign_cpu_sample_retains_birth_identity_and_quota(self):
        a, b = row(99, 1, ppid=1), row(99, 1, ppid=1)
        b['userTicks'] = a['userTicks'] + 250
        before = dict(monotonicSeconds=10, otherProcesses=[a])
        after = dict(monotonicSeconds=11, otherProcesses=[b], processClockTicksPerSecond=100, cgroup={'quotaCores': 25})
        self.assertEqual(contract.foreign_cpu_quota_percent(before, after, set(), 10), 10)
        self.assertEqual(contract.foreign_cpu_quota_percent(before, after, {(99, 1)}, 10), 0)
        b['startTicks'] = 500
        self.assertEqual(contract.foreign_cpu_quota_percent(before, after, set(), 10), 0)

    def test_quota_uses_25_core_time_not_208_host_threads(self):
        before = dict(monotonicSeconds=10, quotaCores=25, cpuset='0-207', usageUsec=1000000)
        after = dict(monotonicSeconds=11, quotaCores=25, cpuset='0-207', usageUsec=3500000)
        self.assertEqual(contract.cpu_quota_percent(before, after), 10)
        after['quotaCores'] = 208
        with self.assertRaisesRegex(ValueError, 'allocation changed'):
            contract.cpu_quota_percent(before, after)

    def test_disk_thresholds_fail_before_declared_cap_or_reserve(self):
        b = spec()['budget']
        self.assertIsNone(contract.disk_stop(b, 20 * contract.GIB, 0, 0))
        self.assertEqual(contract.disk_stop(b, 10 * contract.GIB, 0, 0), 'data-disk-reserve-threshold')
        self.assertEqual(contract.disk_stop(b, 20 * contract.GIB, 7 * contract.GIB, 0), 'stage-output-stop-threshold')
        self.assertEqual(contract.disk_stop(b, 20 * contract.GIB, 0, 2 * contract.GIB), 'runtime-growth-stop-threshold')

    def test_missing_graphics_process_list_is_unknown_not_empty(self):
        gpu = object.__new__(runner.Nvml)
        gpu.lib = SimpleNamespace(nvmlDeviceGetGraphicsRunningProcesses_v3=Mock(return_value=3))
        gpu.handle = ctypes.c_void_p()
        with self.assertRaisesRegex(ValueError, 'Graphics list unavailable'):
            gpu.processes('Graphics')

    def test_gpu_process_buffer_growth_and_unknown_memory(self):
        gpu = object.__new__(runner.Nvml)
        gpu.handle = ctypes.c_void_p()
        calls = []
        def call(handle, count, rows):
            calls.append(count._obj.value)
            if len(calls) == 1:
                count._obj.value = 40
                return 7
            count._obj.value = 1
            rows[0].pid = 20
            rows[0].usedGpuMemory = 1024
            rows[0].gpuInstanceId = rows[0].computeInstanceId = 2 ** 32 - 1
            return 0
        gpu.lib = SimpleNamespace(nvmlDeviceGetGraphicsRunningProcesses_v3=call)
        result = gpu.processes('Graphics')
        self.assertEqual(calls, [32, 64])
        self.assertEqual(result[0]['usedGpuMemoryBytes'], 1024)
        def unknown(handle, count, rows):
            count._obj.value = 1
            rows[0].usedGpuMemory = 2 ** 64 - 1
            return 0
        gpu.lib.nvmlDeviceGetGraphicsRunningProcesses_v3 = unknown
        with self.assertRaisesRegex(ValueError, 'memory unavailable'):
            gpu.processes('Graphics')

    def test_linux_cgroup_namespace_unknown_is_not_host_root(self):
        with patch.object(Path, 'read_text', return_value='0::/hidden/nested'):
            with self.assertRaisesRegex(ValueError, 'allocation unknown'):
                runner.cgroup_sample()


class EvidenceGateFixtures(unittest.TestCase):
    def make_receipts(self, stage):
        value = spec()
        manifest = dict(files=[dict(path='Player.x86_64', bytes=1, sha256='f' * 64, mode=0o755)])
        contract.write_json(stage / 'payload-manifest.json', manifest)
        value['manifestSha256'] = contract.digest(stage / 'payload-manifest.json')
        contract.write_json(stage / 'launch-spec.json', value)
        command = dict(arguments=contract.player_arguments(value), player=value['payloadRoot'] + '/Player.x86_64',
                       sha256='f' * 64, specSha256=contract.digest(stage / 'launch-spec.json'),
                       payloadManifestSha256=value['manifestSha256'])
        process = dict(exitCode=0, stopReason=None, rootIdentity=row(20, 100), processStartedUtc=contract.utc(),
                       finishedUtc=contract.utc(), rootExitObservedUtc=contract.utc())
        contract.write_json(stage / 'command.json', command)
        contract.write_json(stage / 'process.json', process)
        for name in ('stdout.log', 'stderr.log', 'events.jsonl'):
            (stage / name).write_text('fixture\n')
        samples = [dict(preflight=i < 6, errors=[], gpu=dict(value['gpu'], gpuPercent=0, compute=[], graphics=[]),
            cgroup=dict(cgroup='0::/', quotaCores=25, memoryMaxBytes=90 * contract.GIB, cpuset='0-207', cpuStat={'usage_usec': 1}),
            ownedProcesses=[], otherProcesses=[], disk=dict(freeBytes=20 * contract.GIB, stageBytes=0, runtimeGrowthBytes=0, fileAtHardLimit=None))
                   for i in range(7)]
        (stage / 'resources.jsonl').write_text(''.join(json.dumps(r) + '\n' for r in samples))
        boundary = dict(kind=contract.CONTRACT, status='completed', mutexReleased=True, lockReleasedUtc=contract.utc(),
            cleanup=dict(allOwnedExited=True, waitidNoChildren=True, remaining=[], errors=[]),
            telemetryQualified=True, payloadBefore={'verified': True}, payloadAfter={'verified': True},
            lock={'inheritedVerified': True}, evidenceSha256={p.name: contract.digest(p) for p in stage.iterdir()})
        return command, process, boundary

    def test_success_shape_is_compatible_but_never_promotes_performance(self):
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp)
            command, process, boundary = self.make_receipts(stage)
            self.assertEqual(contract.stage_failures(stage, command, process, boundary), [])

    def test_timeout_cleanup_unknown_release_missing_and_payload_drift_fail(self):
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp)
            command, process, boundary = self.make_receipts(stage)
            for mutation in (lambda b: b.update(status='failed'),
                             lambda b: b.update(mutexReleased=False),
                             lambda b: b.update(telemetryQualified=None),
                             lambda b: b['cleanup'].update(remaining=[20]),
                             lambda b: b['cleanup'].update(waitidNoChildren=False),
                             lambda b: b['payloadAfter'].update(verified=False),
                             lambda b: b.update(kind='legacy')):
                bad = deepcopy(boundary)
                mutation(bad)
                self.assertTrue(contract.stage_failures(stage, command, process, bad))
            bad_process = dict(process, stopReason='player-timeout')
            self.assertTrue(contract.stage_failures(stage, command, bad_process, boundary))

    def test_tampered_raw_or_shortcut_arguments_fail_bound_gate(self):
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp)
            command, process, boundary = self.make_receipts(stage)
            (stage / 'stdout.log').write_text('truncated/replaced\n')
            self.assertTrue(any('stdout.log' in f for f in contract.stage_failures(stage, command, process, boundary)))
            command['arguments'].append('-nographics')
            self.assertTrue(any('arguments' in f for f in contract.stage_failures(stage, command, process, boundary)))

    def test_rehashed_but_empty_resource_stream_is_still_ineligible(self):
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp)
            command, process, boundary = self.make_receipts(stage)
            (stage / 'resources.jsonl').write_text('')
            boundary['evidenceSha256']['resources.jsonl'] = contract.digest(stage / 'resources.jsonl')
            self.assertTrue(any('coverage' in f for f in contract.stage_failures(stage, command, process, boundary)))


class SupervisorFlowFixtures(unittest.TestCase):
    def test_live_descendant_prevents_reaping_and_release_even_after_root_exit(self):
        tree = object.__new__(runner.OwnedTree)
        tree.observe = Mock(return_value=({}, {20: row(20, 100, state='Z'), 21: row(21, 101, pgid=20)}))
        with patch.object(runner.os, 'waitpid', create=True) as wait:
            self.assertIsNone(tree.finish_if_exited())
            wait.assert_not_called()

    def test_unresolved_cleanup_stays_in_foreground_until_proven(self):
        monitor = object.__new__(runner.Supervisor)
        good = dict(allOwnedExited=True, waitidNoChildren=True, remaining=[], errors=[])
        monitor.settle = Mock(side_effect=[None, good])
        monitor.event, monitor.errors = Mock(), []
        with patch.object(runner, 'proc', return_value=row(10, 50)), patch.object(runner.time, 'sleep') as sleep:
            self.assertEqual(monitor.hold_until_clean(), good)
            sleep.assert_called_once_with(5)
            self.assertEqual(monitor.event.call_args.args[0], 'cleanup-unresolved-lock-held')

    def test_pid_changed_before_signal_gets_neither_kill_nor_resume(self):
        tree = object.__new__(runner.OwnedTree)
        tree.supervisor = 10
        tree.event = Mock()
        owned = {20: row(20, 100)}
        tree.observe = Mock(return_value=(owned, owned))
        with patch.object(runner.os, 'getpgrp', return_value=9, create=True), \
             patch.object(runner.os, 'pidfd_open', return_value=1000, create=True), \
             patch.object(runner.os, 'close'), \
             patch.object(runner.os, 'killpg', create=True) as kill, \
             patch.object(runner.signal, 'pidfd_send_signal', create=True) as send, \
             patch.object(runner, 'proc', return_value=row(20, 999, ppid=1)):
            tree.signal_groups(15)
            send.assert_not_called()
            kill.assert_not_called()
            self.assertEqual(tree.event.call_args.args[0], 'signal-refused')

    def test_failed_stage_preserves_all_three_receipts_and_logs_after_cleanup(self):
        for reason in ('player-timeout', 'stage-output-stop-threshold', 'NVML Graphics list unavailable'):
            with self.subTest(reason=reason), tempfile.TemporaryDirectory() as tmp:
                stage = Path(tmp)
                monitor = object.__new__(runner.Supervisor)
                monitor.spec, monitor.spec_hash = spec(), 'd' * 64
                monitor.stage, monitor.runtime = stage, stage / 'runtime'
                monitor.errors, monitor.stop_reason = [], None
                monitor.started, monitor.child = 1, SimpleNamespace(returncode=None)
                monitor.tree = SimpleNamespace(root=row(20, 100), root_exit={'exitCode': -15, 'observedUtc': contract.utc()})
                monitor.payload_before = monitor.payload_after = None
                monitor.telemetry_qualified = False
                monitor.cleanup = None
                monitor.prepare, monitor.launch = Mock(return_value=(Path('fixture-player'), None)), Mock()
                monitor.run_player = Mock(side_effect=RuntimeError(reason))
                cleanup = dict(allOwnedExited=True, waitidNoChildren=True, remaining=[], errors=[])
                monitor.hold_until_clean = Mock(return_value=cleanup)
                monitor.lock = SimpleNamespace(receipt={'inheritedVerified': True}, release=Mock(return_value=contract.utc()))
                monitor.events = (stage / 'events.jsonl').open('x', encoding='utf-8')
                monitor.resources = (stage / 'resources.jsonl').open('x', encoding='utf-8')
                for name in ('stdout.log', 'stderr.log'):
                    (stage / name).write_bytes(b'original output is retained\n')
                if 'NVML' not in reason:
                    contract.write_json(stage / 'command.json', {'arguments': ['fixture']})
                with patch('builtins.print'):
                    self.assertEqual(monitor.run(), 1)
                monitor.lock.release.assert_called_once_with(cleanup)
                boundary = contract.read_json(stage / 'stage.json')
                process = contract.read_json(stage / 'process.json')
                self.assertEqual(boundary['status'], 'failed')
                self.assertTrue(boundary['mutexReleased'])
                self.assertFalse(boundary['fullRouteAccepted'])
                self.assertEqual(process['stopReason'], reason)
                self.assertEqual(process['exitCode'], -15)
                self.assertEqual((stage / 'stdout.log').read_bytes(), b'original output is retained\n')
                if 'NVML' in reason:
                    command = contract.read_json(stage / 'command.json')
                    self.assertFalse(command['launchInvocationRecorded'])
                    self.assertIsNone(command['arguments'])
                    self.assertIn('-force-vulkan', command['plannedArguments'])


if __name__ == '__main__':
    unittest.main()
