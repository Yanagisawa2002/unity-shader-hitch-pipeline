"""Validate original Megacity content, positive simulation and ownership evidence.

This is an adapted external application acceptance, not a city traversal or a
standardized benchmark. No frame sample is removed as an outlier. SceneSystem
readiness and actual camera submission remain distinct observed facts.
"""
import argparse
from collections import defaultdict
import json
import math
from pathlib import Path
import re
from pso_external_capture import load, sha, statistics

EXPECTED_SCENES = {
    'd15a274585a786440ad97fbd9f40d43a': 'Blimps',
    '3326447b997a6814bab701262e9f6f38': 'Common',
    'ed1a49ee1f7b28b499c8cece71ee2353': 'Level',
    'f98b38b047e44c14ab1e89f0c3d96b12': 'MegacityMetroLevelBounds',
    '49a72a7c2a2c8044abfaf7829f316369': 'Player_Subscene',
    '46dffb08de5f4cf498cabf1a709740e0': 'Traffic',
}
MENU, MAIN = 'Assets/Scenes/Menu.unity', 'Assets/Scenes/Main.unity'


def engine_window(start, end):
    values = start['engineRealtimeSeconds'], end['engineRealtimeSeconds']
    if not all(math.isfinite(v) for v in values) or values[1] <= values[0] or end['frame'] <= start['frame']:
        raise ValueError('Invalid direct engine-clock observation window')
    return values


def scene_payloads(snapshot):
    """Actual same-world requests + all resolved section loads + positive bytes/entities."""
    ready = {}
    for scene in snapshot['scenes']:
        sections = scene['sections']
        if (scene['requested'] and scene['loaded'] and sections and
                all(s['requested'] and s['loaded'] and s['guid'] == scene['guid'] for s in sections) and
                sum(s['payloadEntities'] for s in sections) > 0 and
                sum(s['fileBytes'] for s in sections) > 0):
            ready[scene['guid']] = dict(name=EXPECTED_SCENES.get(scene['guid'], 'other-original-scene'),
                sections=len(sections), fileBytes=sum(s['fileBytes'] for s in sections),
                payloadEntities=sum(s['payloadEntities'] for s in sections),
                renderEntities=sum(s['renderEntities'] for s in sections))
    return ready


def movement(snapshots, kind):
    """Stable world/entity/version across time; counts or replacement entities do not suffice."""
    tracks = defaultdict(list)
    for snapshot in snapshots:
        for sample in snapshot['samples']:
            if sample['kind'] == kind:
                tracks[(snapshot['worldSequence'], sample['entity'], sample['version'])].append((snapshot, sample))
    result = []
    for (world, entity, version), track in tracks.items():
        first, last = track[0], track[-1]
        duration = last[0]['realtimeSeconds'] - first[0]['realtimeSeconds']
        positions = [[sample['position'][axis] for axis in ('x', 'y', 'z')] for _, sample in track]
        span = max((math.dist(positions[0], p) for p in positions), default=0)
        simulation_delta = last[0]['simulationSeconds'] - first[0]['simulationSeconds']
        if kind == 'traffic':
            native_state_changed = any((s['roadIndex'], s['splinePosition']) !=
                (first[1]['roadIndex'], first[1]['splinePosition']) for _, s in track)
        elif kind == 'blimp':
            native_state_changed = any(s['blimpRotation'] != first[1]['blimpRotation'] for _, s in track)
        else:
            native_state_changed = span > 0.1
        result.append(dict(worldSequence=world, entity=entity, version=version, observations=len(track),
            observedSeconds=duration, simulationSeconds=simulation_delta, maximumDisplacementFromFirst=span,
            nativeStateChanged=native_state_changed,
            positiveMovement=duration >= 20 and simulation_delta >= 20 and span > .1 and native_state_changed))
    return result


def complete_native_population(snapshot):
    return (EXPECTED_SCENES.keys() <= scene_payloads(snapshot).keys() and
        snapshot['gameLoadInfoPresent'] and snapshot['requestedSections'] > 0 and
        snapshot['requestedSections'] == snapshot['loadedSections'] and
        snapshot['singlePlayers'] > 0 and snapshot['vehicles'] > 0 and
        snapshot['blimps'] > 0 and snapshot['renderEntities'] > 0)


def sustained_population(snapshots, start, end):
    """Same-world state throughout the declared window, with sampling gaps exposed."""
    worlds = defaultdict(list)
    for snapshot in snapshots:
        if start - 2 <= snapshot['realtimeSeconds'] <= end:
            worlds[snapshot['worldSequence']].append(snapshot)
    results = []
    for world, rows in worlds.items():
        before = [s for s in rows if s['realtimeSeconds'] <= start]
        rows = (before[-1:] if before else []) + [s for s in rows if s['realtimeSeconds'] > start]
        gaps = [b['realtimeSeconds']-a['realtimeSeconds'] for a,b in zip(rows,rows[1:])]
        advances = all(b['simulationSeconds'] > a['simulationSeconds'] for a,b in zip(rows,rows[1:]))
        first_gap, last_gap = rows[0]['realtimeSeconds']-start, end-rows[-1]['realtimeSeconds']
        positive = (len(rows) >= 3 and first_gap <= 2 and last_gap <= 2 and advances and
                    all(complete_native_population(s) for s in rows))
        results.append(dict(worldSequence=world, snapshots=len(rows), firstSnapshotRelativeToStartSeconds=first_gap,
            lastSnapshotToEndSeconds=last_gap, maximumObservedSnapshotGapSeconds=max(gaps,default=0),
            simulationContinuouslyAdvancing=advances, completePopulationInEverySnapshot=all(complete_native_population(s) for s in rows),
            accepted=positive))
    return results


def megacity(stage):
    stage = Path(stage)
    capture = load(stage / 'capture/external-capture.json')
    command, process, boundary = (load(stage / p) for p in ('command.json', 'process.json', 'stage.json'))
    failures = []
    def require(condition, message):
        if not condition: failures.append(message)
    require(boundary['status'] == 'completed' and boundary['mutexReleased'], 'Stage incomplete/unreleased')
    require(process.get('exitCode') == 0 and not process.get('stopReason'), 'No normal zero Player exit')
    require(capture['applicationQuit'] and capture['originalQuitRequested'], 'Original quit path not observed')
    require(capture['singlePlayerEntryAccepted'] and capture['contentReadinessObserved'], 'Native application content gate absent')
    require(capture['unityVersion'] == '6000.1.0f1' and capture['graphicsApi'] == 'Direct3D12', 'Wrong Editor/API')
    require((capture['width'], capture['height'], capture['quality']) == (1920, 1080, 'High'), 'Wrong render cell')
    require(capture['errors'] == 0 and capture['exceptions'] == 0, 'Observer recorded errors/exceptions')
    require('-pso-megacity-single-player' in command['arguments'] and '-batchmode' not in command['arguments'], 'Wrong application entry')
    log = (stage / 'player.log').read_text(encoding='utf-8-sig', errors='replace')
    errors = [line for line in log.splitlines() if re.search(
        r'(^\w*(?:Exception|Error):|\b(?:NullReferenceException|AccessViolationException|InvalidOperationException|Assertion failed|Crash!!!)\b|\[ShaderHitchPipeline\].*Failed)', line)]
    leaks = [line for line in log.splitlines() if re.search(r'Persistent.*allocations?|Leak Detected|leaked.*allocation', line, re.I)]
    require(not errors and not leaks, 'Full post-exit Player log contains errors/leak markers')
    frames, renders, events = (capture[k] for k in ('frames', 'renders', 'events'))
    require(len(frames) > 2, 'No meaningful CPU sample stream')
    require(all(b['frame'] == a['frame'] + 1 and b['seconds'] > a['seconds'] for a, b in zip(frames, frames[1:])), 'Missing/non-monotonic Update intervals')
    kinds = defaultdict(list)
    for event in events: kinds[event['kind']].append(event)
    entry = kinds['shared-api-single-player-entry']
    ready = kinds['six-payloads-original-camera-single-player-ready']
    quit_event = kinds['original-quit-system-requested']
    require(len(entry) == len(ready) == len(quit_event) == 1, 'Missing/duplicated entry/readiness/quit events')
    require(not kinds['acceptance-timeout'] and not kinds['original-quit-system-timeout'], 'Timed out native gate/quit')
    menu_renders = [r for r in renders if r['scene'] == MENU and r['cameraType'] == 'Game' and not r['targetTexture']]
    native_renders = [r for r in renders if r['scene'] == MAIN and r['originalHybridCamera'] and
        r['hybridInitialized'] and r['cameraType'] == 'Game' and not r['targetTexture'] and
        r['pixelWidth'] == 1920 and r['pixelHeight'] == 1080]
    visible_renders = [r for r in native_renders if not r['loadingVisible'] and not r['tutorialVisible']]
    require(menu_renders and entry and min(r['seconds'] for r in menu_renders) < entry[0]['seconds'], 'Entry preceded actual original Menu rendering')
    require(len(visible_renders) >= 120, 'No sustained visible original Main camera submissions')
    require(len({r['cameraId'] for r in visible_renders}) == 1, 'Original live camera identity changed')
    if entry and ready and quit_event:
        require(entry[0]['seconds'] < ready[0]['seconds'] < quit_event[0]['seconds'], 'Native route events out of order')
        require(quit_event[0]['seconds'] - ready[0]['seconds'] >= capture['requestedObservationSeconds'], 'Declared observation interval truncated')
    snapshots = capture['worldSnapshots']
    accepted_snapshots = [(s, scene_payloads(s)) for s in snapshots]
    accepted_snapshots = [(s, p) for s, p in accepted_snapshots if complete_native_population(s)]
    require(bool(accepted_snapshots), 'Six original payloads + actual native player/traffic/blimp/render population not proven in one world')
    sustained = []
    if ready and quit_event:
        # Native snapshots and these event timestamps use the same engine clock.
        # A single BeforeSplash offset is not exact cross-clock attestation.
        sustained = sustained_population(snapshots, *engine_window(ready[0], quit_event[0]))
        require(any(s['accepted'] for s in sustained), 'Same-world six-payload/native population or advancing simulation did not persist across the declared observation')
        during = [r for r in visible_renders if ready[0]['seconds'] <= r['seconds'] <= quit_event[0]['seconds']]
        require(during and during[0]['seconds']-ready[0]['seconds'] <= 2 and
            quit_event[0]['seconds']-during[-1]['seconds'] <= 2, 'Original visible camera did not span the declared post-readiness interval')
    accepted_worlds = {s['worldSequence'] for s in sustained if s['accepted']}
    motion_snapshots = [s for s in snapshots if s['worldSequence'] in accepted_worlds and ready and quit_event and
        ready[0]['engineRealtimeSeconds']-2 <= s['realtimeSeconds'] <= quit_event[0]['engineRealtimeSeconds']]
    dynamic = {kind: movement(motion_snapshots, kind) for kind in ('traffic', 'blimp')}
    for kind, tracks in dynamic.items():
        require(any(t['positiveMovement'] for t in tracks), 'No stable actual moving '+kind+' entity over 20 seconds')
    pictures = [{'file': p.name, 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in sorted((stage / 'capture').glob('main-original-camera-*.png'))]
    if command['screenshots']:
        require(len(pictures) == 3 and all(p['bytes'] > 10000 for p in pictures), 'Missing actual rendered screenshot outputs; visual review still required')
    intervals = [f['updateIntervalMilliseconds'] for f in frames[1:]]
    first_main = min((r['seconds'] for r in native_renders), default=None)
    main_frames = [f['updateIntervalMilliseconds'] for f in frames[1:] if f['scene'] == MAIN]
    return dict(schemaVersion=1, accepted=not failures, failures=failures, buildGuid=capture['buildGuid'],
        policy=command['policy'], nativePolicyValidated=False,
        scope='External adapted original SinglePlayer application content acceptance; stationary original camera, evolving simulation; no whole-city coverage or performance gain claim.',
        environment=capture['environment'], observerElapsedSeconds=capture['elapsedSeconds'],
        observerToFirstUpdateSeconds=frames[0]['seconds'] if frames else None,
        lastUpdateToQuitCallbackSeconds=capture['elapsedSeconds']-frames[-1]['seconds'] if frames else None,
        processLaunchUtc=process.get('processStartedUtc'), monitorObservedExitUtc=process.get('finishedUtc'),
        allObservedUpdateIntervals=statistics(intervals) if intervals else None,
        mainObservedUpdateIntervals=statistics(main_frames) if main_frames else None,
        mainFirstNativeCameraSubmissionSeconds=first_main, visibleNativeCameraSubmissions=len(visible_renders),
        firstSixPayloadSnapshot=accepted_snapshots[0][1] if accepted_snapshots else None,
        dynamicEntityEvidence=dynamic, sustainedPopulation=sustained, entryEvents=entry, readinessEvents=ready, quitEvents=quit_event,
        screenshots=pictures, persistentAllocationWarnings=leaks, errorMarkers=errors,
        peakObservedAllocatedBytes=max((f['allocatedBytes'] for f in frames), default=0),
        peakObservedReservedBytes=max((f['reservedBytes'] for f in frames), default=0),
        rawFiles=[dict(file=str(p.relative_to(stage)).replace('\\','/'),sha256=sha(p),bytes=p.stat().st_size)
                  for p in [stage/'capture/external-capture.json',stage/'player.log',stage/'command.json',stage/'process.json',stage/'stage.json']])


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--stage', required=True, type=Path); p.add_argument('--output', required=True, type=Path)
    args = p.parse_args()
    if args.output.exists(): raise ValueError('Retain old validation; choose a new output file')
    result = megacity(args.stage)
    args.output.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: result[k] for k in ('accepted','failures','buildGuid','observerElapsedSeconds')}))
    raise SystemExit(0 if result['accepted'] else 1)


if __name__ == '__main__': main()
