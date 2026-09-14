"""Independent read-only recomputation of finished official URP captures.

Does not launch Unity, change a receipt, or treat captured frames as independent
experimental repetitions. Run only after the serial native sequence is finished.
"""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import io
import json
import math
from pathlib import Path
from statistics import median


ARMS = ['disabled', 'all-at-once', 'scheduled', 'observed-budget']
ORDER = [0, 1, 3, 2, 1, 2, 0, 3, 2, 3, 1, 0, 3, 0, 2, 1]
SCENES = [('TerminalScene', 'Terminal'), ('GardenScene', 'Garden'),
          ('OasisScene', 'Oasis'), ('CockpitScene', 'Cockpit')]
ENV_KEYS = ('unityVersion', 'buildGuid', 'graphicsDeviceType', 'graphicsDeviceName',
            'driverIdentity', 'driverVersion', 'qualityLevelName',
            'renderingThreadingMode', 'processorCount', 'identity')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def equal(actual, expected):
    assert math.isclose(actual, expected, rel_tol=1e-12, abs_tol=1e-8), (actual, expected)


def check_stats(values, expected):
    values = list(values)
    assert values and all(math.isfinite(v) and v >= 0 for v in values)
    assert len(values) == expected['count']
    equal(sum(values), expected['sumMilliseconds'])
    equal(max(values), expected['maximumMilliseconds'])
    ordered = sorted(values)
    for p, key in [(0.95, 'p95Milliseconds'), (0.99, 'p99Milliseconds')]:
        equal(ordered[math.ceil(p * len(ordered)) - 1], expected[key])
    for threshold, count in expected['hitchCounts'].items():
        assert sum(v > float(threshold) for v in values) == count


def csv_sections(text):
    rows = list(csv.reader(io.StringIO(text)))
    assert rows[0] == ['URP Template Performance Test']
    sections = []
    scene = None
    for i, row in enumerate(rows):
        if row and row[0] == 'Scene':
            scene = row[1]
        if row and row[0] == 'Captured frames':
            count = int(row[1])
            samples = [[float(v) for v in r] for r in rows[i + 2:i + 2 + count]]
            assert len(samples) == count and count > 1
            assert all(len(s) == 6 and all(math.isfinite(v) for v in s) for s in samples)
            sections.append((scene, samples))
    assert [s for s, _ in sections] == [s for s, _ in SCENES]
    return sections


def audit(folder):
    protocol = read(folder / 'protocol.json')
    summary = read(folder / 'comparison-summary.json')
    freeze = read(folder / 'freeze-receipt.json')
    assert summary['completed'] and not summary['unexecuted'] and not summary['verificationFailures']
    assert freeze['frozenBeforeRuns']
    assert freeze['protocolSha256'] == summary['protocolSha256'] == sha(folder / 'protocol.json')
    assert protocol['playerIndexSha256'] == sha(folder / 'player-files.json')
    assert protocol['sourceInputIndexSha256'] == sha(folder / 'adapter-package-inputs.json')
    post = read(folder / 'player-after.json')
    assert post['playerUnchanged'] and post['playerIndexSha256'] == protocol['playerIndexSha256']
    assert [r['policy'] for r in protocol['runs']] == [ARMS[a] for a in ORDER]
    assert len(summary['allSamples']) == 16
    assert {s['index'] for s in summary['allSamples']} == set(range(1, 17))
    audited = []
    for planned in protocol['runs']:
        stage = folder / planned['directory']
        v = read(stage / 'validation.json')
        sample = next(s for s in summary['allSamples'] if s['index'] == planned['index'])
        assert sha(stage / 'validation.json') == sample['validationSha256']
        assert v['accepted'] and v['contentAccepted'] and v['nativePolicyValidated'] and sample['accepted']
        assert not v['failures'] and not sample['failures'] and not v['persistentAllocationWarnings'] and not v['logErrors']
        assert not v['screenshotsEnabled'] and not v['traceEnabled']
        assert v['policy'] == sample['policy'] == planned['policy']
        assert v['buildGuid'] == protocol['buildGuid']
        assert v['cacheCondition'] == protocol['processCacheCondition']
        assert len(v['warmupReceipts']) == 1
        assert len(v['schedulingFeedback']) == 1
        scheduler_record = v['schedulingFeedback'][0]
        assert sha(stage / scheduler_record['file']) == scheduler_record['sha256']
        assert read(stage / scheduler_record['file']) == scheduler_record['data']
        scheduler = scheduler_record['data']
        expected_strategy = {'disabled': 'scheduled', 'all-at-once': 'throughput',
                             'scheduled': 'scheduled', 'observed-budget': 'observed-budget'}[planned['policy']]
        assert scheduler['policy'] == expected_strategy
        assert not scheduler['hasInFlightBatch'] and not scheduler['hasPendingRetirement']
        for receipt in v['warmupReceipts']:
            assert sha(stage / receipt['file']) == receipt['sha256']
            assert read(stage / receipt['file']) == receipt['data']
            assert receipt['data']['planSha256'] == protocol['planFileSha256']
            assert receipt['data']['strategy'] == expected_strategy
            if planned['policy'] == 'disabled':
                assert not receipt['data']['phases'] and not scheduler['activations']
            else:
                expected_states = {p['phase']: p['states'] for p in protocol['collections']}
                phases = receipt['data']['phases']
                assert len(phases) == len(expected_states)
                assert {p['phase']: p['totalGraphicsStates'] for p in phases} == expected_states
                assert {p['phase']: p['completedGraphicsStates'] for p in phases} == expected_states
                assert all(p['backendReportedWarmedUp'] and not p['error'] for p in phases)
                assert len(scheduler['activations']) == len(expected_states)
                assert all(a['ownerReleased'] and a['state'] == 'Completed' and not a['hasInFlightBatch']
                           and a['backendReportedWarmedUp'] and not a['failure'] for a in scheduler['activations'])
            for key in ENV_KEYS:
                assert receipt['data']['environment'][key] == protocol['environment'][key]
        for key, file in [('captureSha256', 'capture/external-capture.json'),
                          ('nativeCsvSha256', 'capture/upstream-results.csv'), ('playerLogSha256', 'player.log')]:
            assert sha(stage / file) == v[key] == sample[key]
        process, boundary = read(stage / 'process.json'), read(stage / 'stage.json')
        assert process['exitCode'] == 0 and not process.get('stopReason')
        assert boundary['status'] == 'completed' and boundary['mutexReleased']
        c = read(stage / 'capture/external-capture.json')
        assert c['applicationQuit'] and c['originalBenchmarkFinished'] and c['csvPublications'] == 1
        assert c['errors'] == c['exceptions'] == 0
        frames = c['frames']
        assert all(b['frame'] > a['frame'] and b['seconds'] > a['seconds'] for a, b in zip(frames, frames[1:]))
        for a, b in zip(frames, frames[1:]):
            equal((b['seconds'] - a['seconds']) * 1000, b['updateIntervalMilliseconds'])
        values = [f['updateIntervalMilliseconds'] for f in frames[1:]]
        check_stats(values, v['allUpdateIntervals'])
        check_stats(values, sample['allUpdates'])
        equal(c['elapsedSeconds'], v['observerElapsedSeconds'])
        equal(c['elapsedSeconds'], sample['elapsedSeconds'])
        equal(frames[0]['seconds'], v['observerToFirstUpdateSeconds'])
        equal(frames[0]['seconds'], sample['observerToFirstUpdateSeconds'])
        quit_gap = c['elapsedSeconds'] - frames[-1]['seconds']
        assert quit_gap >= 0
        equal(frames[0]['seconds'] + sum(values) / 1000 + quit_gap, c['elapsedSeconds'])
        for key, field in [('peakAllocatedBytes', 'allocatedBytes'), ('peakReservedBytes', 'reservedBytes')]:
            assert max(f[field] for f in frames) == v[key] == sample[key]
        by_frame = {f['frame']: f for f in frames}
        native = csv_sections((stage / 'capture/upstream-results.csv').read_text(encoding='utf-8-sig'))
        for i, (name, directory) in enumerate(SCENES):
            route = v['routes'][i]
            assert route == sample['routes'][i] and route['stage'] == name
            scene = f'Assets/Scenes/{directory}/{name}.unity'
            renders = [r for r in c['renders'] if r['scene'] == scene and r['stage'] == name
                       and r['cameraType'] == 'Game' and not r['targetTexture']
                       and (r['pixelWidth'], r['pixelHeight']) == (1920, 1080)
                       and r['timelineBoundToRenderingCamera']]
            camera_ids = {r['cameraId'] for r in renders if r['status'] == 'Running'}
            assert len(camera_ids) == 1
            main = next(iter(camera_ids))
            other = [dict(frame=r['frame'], camera=r['camera'], cameraId=r['cameraId'], status=r['status'])
                     for r in renders if r['cameraId'] != main]
            assert other == route['otherOriginalCameraSubmissions']
            for phase in route['passes']:
                selected = [r for r in renders if r['cameraId'] == main and r['status'] == phase['status']]
                assert len(selected) == len({r['frame'] for r in selected}) == phase['frames']
                following = [by_frame[r['frame'] + 1]['updateIntervalMilliseconds'] for r in selected if r['frame'] + 1 in by_frame]
                assert len(selected) - len(following) == phase['missingFollowingUpdateIntervals']
                check_stats(following, phase['cpuFollowingUpdateIntervals'])
            warming = [f['updateIntervalMilliseconds'] for f in frames[1:] if f['stage'] == name and f['status'] == 'Warming']
            check_stats(warming, route['originalWarmupAndLoadingUpdateIntervals'])
            samples = native[i][1]
            assert len(samples) == v['nativeResults'][i]['count']
            assert v['nativeResults'][i] == sample['nativeResults'][i]
            assert all(b[0] >= a[0] for a, b in zip(samples, samples[1:]))
            check_stats((s[1] for s in samples), v['nativeResults'][i]['warmedFrameIntervals'])
        long_intervals = []
        for before, after in zip(frames, frames[1:]):
            if after['updateIntervalMilliseconds'] <= 200:
                continue
            start, end = before['seconds'], after['seconds']
            neighbors = [dict(kind=e['kind'], scene=e['scene'], frame=e['frame'], seconds=e['seconds'])
                         for e in c['events'] if start - .02 <= e['seconds'] <= end + .02]
            # The bootstrap origin does not establish exact equivalence between
            # Unity realtime and Stopwatch throughout startup. Join using real
            # frame IDs and retain the source clock, without invented precision.
            phase_neighbors = [dict(kind=e['kind'], phase=e['phase'], status=e['status'], frame=e['frame'],
                                    engineRealtimeSeconds=e['seconds'])
                               for e in v['phaseEvents'] if e['frame'] in (before['frame'], after['frame'])]
            long_intervals.append(dict(frame=after['frame'], milliseconds=after['updateIntervalMilliseconds'],
                startSeconds=start, endSeconds=end,
                before=dict(stage=before['stage'], status=before['status'], scene=before['scene']),
                after=dict(stage=after['stage'], status=after['status'], scene=after['scene']),
                sampledAllocatedDeltaBytes=after['allocatedBytes'] - before['allocatedBytes'],
                sampledReservedDeltaBytes=after['reservedBytes'] - before['reservedBytes'],
                adjacentCaptureEvents=neighbors, adjacentPhaseEvents=phase_neighbors,
                interpretation='Capture events use the observer Stopwatch; phase events are joined by the two adjacent frame IDs and retain Unity realtime. No exact cross-clock conversion or causal profiler sample.'))
        audited.append(dict(index=planned['index'], policy=planned['policy'], elapsedSeconds=c['elapsedSeconds'],
                            cpuIntervals=len(values), maximumUpdateMilliseconds=max(values),
                            observerToFirstUpdateSeconds=frames[0]['seconds'], lastUpdateToQuitCallbackSeconds=quit_gap,
                            intervalsOver200Milliseconds=long_intervals,
                            captureSha256=v['captureSha256'], nativeCsvSha256=v['nativeCsvSha256']))
    for arm in ARMS:
        samples = [s for s in audited if s['policy'] == arm]
        group = summary['byArm'][arm]
        assert len(samples) == group['processes'] == group['accepted'] == 4
        equal(median(s['elapsedSeconds'] for s in samples), group['medianElapsedSeconds'])
        equal(max(s['maximumUpdateMilliseconds'] for s in samples), group['maximumUpdateMilliseconds'])
    return dict(directory=folder.name, protocolSha256=summary['protocolSha256'], buildGuid=protocol['buildGuid'],
                allPassed=True, runs=audited, scope='Independent recomputation from existing native raw captures, CSVs and indexed receipts. No new Unity execution. No fresh rehash of full Player binaries; the runner postcheck is retained.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.folder)
    with args.output.open('x', encoding='utf-8') as f:
        json.dump(dict(verifiedUtc=datetime.now(timezone.utc).isoformat(), **result), f, indent=2)
        f.write('\n')
    print(json.dumps(dict(allPassed=True, verifiedProcesses=len(result['runs']), output=str(args.output))))
