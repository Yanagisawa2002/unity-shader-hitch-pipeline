"""Validate actual external Player captures; summarize CPU intervals, never GPU/presentation time.

Raw captures, native logs and native benchmark samples remain the source of truth.
No sample is removed as an outlier. The first Unity Update has no preceding
observer Update; its startup gap is reported separately rather than as zero time.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import re


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def statistics(samples):
    values = list(samples)
    if not values or any(not math.isfinite(v) or v < 0 for v in values):
        raise ValueError('Missing, negative or non-finite actual interval samples')
    ordered = sorted(values)
    return dict(count=len(values), sumMilliseconds=sum(values),
                p95Milliseconds=ordered[math.ceil(len(values)*.95)-1],
                p99Milliseconds=ordered[math.ceil(len(values)*.99)-1],
                maximumMilliseconds=ordered[-1],
                hitchCounts={str(limit): sum(v > limit for v in values) for limit in (16.67, 33.33, 50)},
                percentileDefinition='nearest rank; no trimming')


def complete_render_indices(indices, length):
    counts = Counter(indices)
    return set(counts) == set(range(length)) and all(n == 1 for n in counts.values())


def native_policy_failures(command, warmup, feedback, expected_phases):
    failures = []
    if len(warmup) != 1 or len(feedback) != 1:
        return ['One complete warmup/observation receipt and scheduler receipt required']
    run, scheduler = warmup[0]['data'], feedback[0]['data']
    if run['error'] or scheduler['hasInFlightBatch'] or scheduler['hasPendingRetirement']:
        failures.append('Failure or unfinished native ownership at exit')
    if not run['cacheMissTrace']['armed'] or run['cacheMissTrace']['error']:
        failures.append('Common seeded plan-baseline observation unavailable')
    if command['policy'] == 'disabled':
        if scheduler['activations'] or run['phases'] or not command.get('planBaselineForDisabled'):
            failures.append('Disabled observation arm must attest zero submitted work')
        return failures
    expected = Counter(expected_phases)
    if Counter(p['phase'] for p in run['phases']) != expected:
        failures.append('Missing or duplicated native phase work')
    if Counter(a['phase'] for a in scheduler['activations']) != expected:
        failures.append('Missing or duplicated scheduler activations')
    for phase in run['phases']:
        if (phase['error'] or phase['totalGraphicsStates'] <= 0 or
                phase['completedGraphicsStates'] != phase['totalGraphicsStates'] or
                not phase['backendReportedWarmedUp'] or phase['batchCount'] < 1):
            failures.append('Incomplete actual native work: '+phase['phase'])
    if not all(a['ownerReleased'] and not a['hasInFlightBatch'] and a['backendReportedWarmedUp']
               and not a['failure'] and a['state'] == 'Completed' and a['completedPermutations'] > 0
               for a in scheduler['activations']):
        failures.append('Scheduler did not complete and retire every native owner')
    return failures


def boat(stage, require_warmup=False, require_no_leaks=False):
    stage = Path(stage)
    path = stage/'capture/external-capture.json'
    capture = load(path)
    command, process, boundary = (load(stage/name) for name in ('command.json', 'process.json', 'stage.json'))
    failures = []

    def require(condition, reason):
        if not condition:
            failures.append(reason)

    require(boundary['status'] == 'completed' and boundary['mutexReleased'], 'Stage did not complete and release its mutex')
    require(process.get('exitCode') == 0 and not process.get('stopReason'), 'Player exit was not normal zero')
    require(capture['applicationQuit'], 'Missing application-quit callback')
    require(capture['graphicsApi'] == 'Direct3D12' and capture['unityVersion'] == '6000.1.0f1', 'Wrong engine/API')
    require((capture['width'], capture['height'], capture['quality']) == (1920, 1080, 'High'), 'Wrong render cell')
    require(capture['errors'] == 0 and capture['exceptions'] == 0, 'Observer recorded errors/exceptions')
    log = (stage/'player.log').read_text(encoding='utf-8-sig', errors='replace')
    # Full post-exit log supplements the in-process threaded observer.
    error_lines = [line for line in log.splitlines() if re.search(
        r'(^\w*(?:Exception|Error):|\b(?:NullReferenceException|AccessViolationException|InvalidOperationException|Assertion failed|Crash!!!)\b|\[ShaderHitchPipeline\].*Failed)', line)]
    require(not error_lines, 'Full Player log contains error/exception markers')
    frames = capture['frames']
    require(bool(frames), 'No Update samples')
    require(all(b['frame'] > a['frame'] and b['seconds'] > a['seconds'] for a, b in zip(frames, frames[1:])), 'Non-monotonic Update evidence')
    by_frame = {f['frame']: f for f in frames}
    route_results = []
    for route, scene, runs, length in (
        ('Island Flythrough', 'Assets/scenes/Testing/benchmark_island-flythrough.unity', 3, 500),
        ('Island Static', 'Assets/scenes/Testing/benchmark_island-static.unity', 5, 25),
    ):
        loaded = [e for e in capture['events'] if e['kind'] == 'scene-loaded-not-first-draw' and e['scene'] == scene]
        unloaded = [e for e in capture['events'] if e['kind'] == 'scene-unloaded' and e['scene'] == scene]
        require(len(loaded) == 1, 'Expected exactly one original scene load: '+scene)
        if not loaded:
            continue
        start, end = loaded[0]['seconds'], unloaded[0]['seconds'] if unloaded else capture['elapsedSeconds']
        renders = [r for r in capture['renders'] if start <= r['seconds'] <= end and r['route'] == route and
                   r['cameraType'] == 'Game' and not r['targetTexture'] and r['pixelWidth'] == 1920 and r['pixelHeight'] == 1080]
        require(bool(renders), 'No real main-camera rendering: '+scene)
        groups = []
        for run in range(-1, runs):
            selected = [r for r in renders if r['run'] == run and 0 <= r['routeFrame'] < length]
            counts = Counter(r['routeFrame'] for r in selected)
            require(complete_render_indices((r['routeFrame'] for r in selected), length),
                    f'{route} run {run}: incomplete/duplicate original route frames ({len(counts)}/{length})')
            # Update N+1 measures the observer interval containing rendering of N.
            # Missing trailing N+1 is explicit, never replaced with a made-up sample.
            following = [by_frame[r['frame']+1]['updateIntervalMilliseconds'] for r in selected if r['frame']+1 in by_frame]
            bounds = {axis: [min((r['position'][axis] for r in selected), default=0),
                             max((r['position'][axis] for r in selected), default=0)] for axis in 'xyz'}
            groups.append(dict(run=run, upstreamWarmup=run == -1, expectedRenderFrames=length,
                               actualRenderFrames=len(selected), uniqueRouteFrames=len(counts), positionBounds=bounds,
                               firstRenderSeconds=selected[0]['seconds'] if selected else None,
                               lastRenderSeconds=selected[-1]['seconds'] if selected else None,
                               missingFollowingUpdateIntervals=len(selected)-len(following),
                               cpuFollowingUpdateIntervals=statistics(following) if following else None))
        if groups and route == 'Island Flythrough':
            require(any(hi-lo > 10 for lo, hi in groups[0]['positionBounds'].values()), 'Flythrough camera did not traverse the original route')
        route_results.append(dict(route=route, scene=scene, sceneLoadedSeconds=start, sceneUnloadedOrQuitSeconds=end,
                                  firstMainCameraRenderSeconds=renders[0]['seconds'] if renders else None, runs=groups,
                                  transitionRendersOutsideDeclaredCounterRange=sum(r['run'] not in range(-1, runs) or not 0 <= r['routeFrame'] < length for r in renders)))
    native = []
    for file in sorted((stage/'capture/upstream-results').glob('*.json')):
        for result in load(file).get('perfStats', []):
            name = result['info']['BenchmarkName']
            expected_runs, expected_frames = {'Island Flythrough': (3, 500), 'Island Static': (5, 25)}.get(name, (0, 0))
            samples = [run['rawSamples'] for run in result['RunData']]
            require(len(samples) == expected_runs and all(len(s) == expected_frames and all(v > 0 and math.isfinite(v) for v in s) for s in samples),
                    'Incomplete/empty upstream native samples: '+name)
            native.append(dict(route=name, file=file.name, sha256=sha(file), runs=len(samples), framesPerRun=[len(s) for s in samples]))
    require(Counter(n['route'] for n in native) == Counter({'Island Flythrough': 1, 'Island Static': 1}), 'Expected exactly one complete native result per route')
    phases = [json.loads(line.split('[PSO External Phase] ', 1)[1]) for line in log.splitlines() if line.startswith('[PSO External Phase] {')]
    require(not any(e['status'] == 'Failed' for e in phases), 'Content phase lifecycle failed')
    feedback = []
    for file in sorted((stage/'capture').rglob('*scheduling*.json')):
        feedback.append(dict(file=file.relative_to(stage).as_posix(), sha256=sha(file), data=load(file)))
    warmup = [dict(file=f.relative_to(stage).as_posix(), sha256=sha(f), data=load(f)) for f in sorted((stage/'capture').rglob('*.warmup.json'))]
    policy_failures = native_policy_failures(command, warmup, feedback,
        ['boat-loading', 'boat-flythrough', 'boat-static']) if require_warmup else []
    content_accepted = not failures
    failures.extend(policy_failures)
    leaks = re.findall(r'Leak Detected : Persistent allocates (\d+) individual allocations', log)
    if require_no_leaks:
        require(not leaks, 'Persistent-allocation warning reappeared after the cleanup repair')
    result = dict(schemaVersion=1, workload='Official Boat Attack adapted native benchmark',
                  accepted=not failures, contentAccepted=content_accepted, nativePolicyValidated=require_warmup and not policy_failures,
                  failures=failures, captureSha256=sha(path), playerLogSha256=sha(stage/'player.log'),
                  buildGuid=capture['buildGuid'], engine=capture['unityVersion'], policy=command['policy'],
                  screenshotsEnabled=capture['screenshotsEnabled'], traceEnabled=command['trace'],
                  cacheCondition=command['cacheCondition'], timingScope=capture['timingScope'],
                  observerElapsedSeconds=capture['elapsedSeconds'], observerToFirstUpdateSeconds=frames[0]['seconds'] if frames else None,
                  allUpdateIntervals=statistics(f['updateIntervalMilliseconds'] for f in frames[1:]) if len(frames)>1 else None,
                  peakAllocatedBytes=max((f['allocatedBytes'] for f in frames), default=0), peakReservedBytes=max((f['reservedBytes'] for f in frames), default=0),
                  routes=route_results, nativeResults=native, phaseEvents=phases, schedulingFeedback=feedback, warmupReceipts=warmup,
                  logErrors=error_lines, persistentAllocationWarnings=leaks,
                  limitations=['CPU Update/endCameraRendering evidence is not GPU completion/presentation or driver-cold.',
                               'First main-camera submission and code-captured screenshots establish different facts; sceneLoaded may follow earlier offscreen draws.',
                               'A zero leak warning count only describes this process exit; earlier failures remain retained.',
                               'Seeded native trace entry growth is not a proven driver compilation-miss count or useful first-draw coverage.'])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--require-warmup', action='store_true')
    parser.add_argument('--require-no-leaks', action='store_true')
    args = parser.parse_args()
    result = boat(args.stage, args.require_warmup, args.require_no_leaks)
    with args.output.open('x', encoding='utf-8') as stream:
        json.dump(result, stream, indent=2)
        stream.write('\n')
    print(json.dumps({key: result[key] for key in ('accepted', 'failures', 'buildGuid', 'policy', 'observerElapsedSeconds', 'persistentAllocationWarnings')}))
    raise SystemExit(0 if result['accepted'] else 1)


if __name__ == '__main__':
    main()
