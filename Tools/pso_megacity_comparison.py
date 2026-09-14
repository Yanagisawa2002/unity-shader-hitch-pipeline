"""Freeze and verify bounded original Megacity SinglePlayer application comparisons.

No Player launches or invented routes here. Native pilots must already prove
the original content, changing simulation, plan work and ownership independently.
"""
import argparse
from datetime import datetime, timezone
from pathlib import Path
from statistics import median
import json
from pso_boat_comparison import ARMS, ORDER, ENVIRONMENT_KEYS, PROCESS_CACHE, identity_failures, index, save, verify_index
from pso_external_capture import HITCH_THRESHOLDS, load, sha
from pso_megacity_capture import EXPECTED_SCENES, megacity

UPSTREAM = '07652ee74a1f322c2c3e607020f07be720175680'
TREE = '7b0bf700ebc01face91223fd7185a02f44ec376e'
EXECUTION_TOOLS = ('pso_megacity_comparison.py','pso_megacity_capture.py','pso_external_capture.py','pso_boat_comparison.py',
    'Invoke-PsoMegacityComparison.ps1','Invoke-PsoExternalPlayer.ps1','Invoke-PsoNativeStage.ps1',
    'Wait-PsoOwnedProcess.ps1','PsoProcessOwnership.ps1')


def native_identity_failures(result, run, protocol):
    failures = identity_failures(result, run, protocol)
    if result.get('requestedObservationSeconds') != protocol['observationSeconds']:
        failures.append('Original Main observation duration differs from protocol')
    if result.get('renderSettings') != protocol['renderSettings']:
        failures.append('Actual VSync/target frame rate differs from protocol')
    binding = result.get('installedPlanBinding') or {}
    for key in ('planFileSha256', 'planContentSha256', 'collections'):
        if binding.get(key) != protocol[key]:
            failures.append('Actual installed plan binding differs from protocol: '+key)
    for key in ENVIRONMENT_KEYS:
        if result.get('environment', {}).get(key) != protocol['environment'].get(key):
            failures.append('Actual captured environment differs from frozen cell: '+key)
    return failures


def freeze(attempt, player_stage, pilot_suffix, output, validation_name):
    player_root = attempt/'players'/player_stage
    player = player_root/'Megacity.exe'
    build = load(attempt/player_stage/'build-summary.json')
    stage = load(attempt/player_stage/'stage.json')
    if build['result'] != 'Succeeded' or build['errors'] or stage['status'] != 'completed' or not stage['mutexReleased']:
        raise ValueError('Actual complete successful final IL2CPP Player required')
    pilots, results = [], []
    for arm in ARMS:
        path = attempt/f'megacity-policy-pilot-{arm}-{pilot_suffix}'/validation_name
        saved = load(path)
        actual = megacity(path.parent, require_warmup=True)
        if saved != actual or not actual['accepted'] or not actual['nativePolicyValidated'] or actual['buildGuid'] != build['guid']:
            raise ValueError('Actual native content/policy pilot failed: '+str(path))
        if actual['traceEnabled'] or actual['screenshotsEnabled']:
            raise ValueError('Formal pilots must use the final non-diagnostic modes')
        pilots.append(dict(policy=arm, path=str(path), sha256=sha(path)))
        results.append(actual)
    for key in ENVIRONMENT_KEYS:
        if len({json.dumps(r['environment'][key], sort_keys=True) for r in results}) != 1:
            raise ValueError('Actual pilot environment drift: '+key)
    if len({r['requestedObservationSeconds'] for r in results}) != 1:
        raise ValueError('Pilot observation windows differ')
    if any(r['renderSettings'] != results[0]['renderSettings'] for r in results):
        raise ValueError('Pilot actual VSync/target frame rate differs')
    binding = results[0]['installedPlanBinding']
    if any(r['installedPlanBinding'] != binding for r in results):
        raise ValueError('Pilots did not execute the same installed plan')
    output.mkdir(parents=True, exist_ok=False)
    files = index(player_root)
    save(output/'player-files.json', files)
    save(output/'adapter-package-inputs.json', index(attempt/player_stage/'source-inputs'))
    protocol = dict(schemaVersion=1, frozenUtc=datetime.now(timezone.utc).isoformat(),
        workload='Official Megacity Metro with declared shared-API entry and lifetime repairs; bounded original SinglePlayer application acceptance, not a standardized PSO benchmark.',
        upstreamCommit=UPSTREAM, upstreamTree=TREE, expectedSubScenes=EXPECTED_SCENES,
        player=str(player.resolve()), playerStage=player_stage, buildGuid=build['guid'],
        actualBuildCommandCommit=load(attempt/player_stage/'command.json')['integrationCommit'],
        buildInputIdentity=load(attempt/player_stage/'PsoBuildIdentity.json'),
        adaptedHostDiffSha256=sha(attempt/player_stage/'host.diff'),
        resolvedManifestSha256=sha(attempt/player_stage/'manifest-after.json'),
        resolvedPackageLockSha256=sha(attempt/player_stage/'packages-lock-after.json'),
        playerFiles=len(files), playerBytes=sum(f['bytes'] for f in files),
        playerIndexSha256=sha(output/'player-files.json'), sourceInputIndexSha256=sha(output/'adapter-package-inputs.json'),
        protocolToolSha256=sha(Path(__file__)), validationToolSha256=sha(Path(__file__).with_name('pso_megacity_capture.py')),
        executionTools=[dict(file=name,sha256=sha(Path(__file__).with_name(name))) for name in EXECUTION_TOOLS],
        editorSha256=load(attempt/player_stage/'command.json')['unitySha256'], environment=results[0]['environment'],
        graphics='Unity 6000.1.0f1 / Windows x64 D3D12 IL2CPP Development Player / NetCode Client / 1920x1080 windowed / High',
        processCacheCondition=PROCESS_CACHE, **binding,
        entry='Original initialized rendered Menu -> shared public SinglePlayer action -> original async Main/LoadingScreen. After original loading completes, shared tutorial dismissal. Original stationary HybridCamera and evolving native simulation; no input simulation or runtime batchmode.',
        observationSeconds=results[0]['requestedObservationSeconds'], renderSettings=results[0]['renderSettings'],
        contentGate='All six original SubScene requests, every resolved section loaded, positive payloads and current native populations in one advancing world throughout the window; actual stable moving traffic/blimps and original visible camera submissions; normal original QuitSystem exit.',
        backend='native-async-bulk, one process-wide ready collection. All-at-once uses throughput admission; scheduled and observed-budget retain their actual policies. No selected fixed-progressive capability.',
        warmupWindow='Only the independently observed original loading screen, 1000 ms estimated per-admission cap; no added loading fence, wait, scene delay or hard latency guarantee. Readiness can follow first draw.',
        observation='Common plan-baseline tracing and encountered-shader retention in every arm. Disabled has zero native activations. Formal runs have no screenshots, training trace or diagnostic flags.',
        cache='Application/OS/driver caches retained after training and pilots; fresh processes are not driver-cold. No purge. Identity checks read the same declared artifacts before processes; cache carryover persists across order blocks.',
        timing='Full CPU Update stream including first load and post-readiness Main window, observer startup-to-quit callback and external process span separately. End-camera callbacks are submissions, not GPU completion or presentation. Clock domains remain explicit.',
        coverage='One uninterrupted process trace is not six isolated SubScene coverages. Native seeded entry growth is descriptive, not proven driver misses or useful first-draw coverage.',
        hitchThresholdMilliseconds=list(HITCH_THRESHOLDS), percentile='Nearest rank per process, no trimming or outlier replacement.',
        order='Four-period Williams order, four independent process samples per arm, predeclared and retained in full.',
        stop='Stop remaining runs on content, plan/work/ownership, process, identity or leak failure, external workload, reserve threat or 900-second process timeout. Keep failures and unexecuted runs; no selective replacements.',
        minimumFreeGiBReserve=20, earlyStopFreeGiB=25, estimatedAdditionalPeakGiB=2, maximumProcessSeconds=900,
        pilots=pilots, runs=[dict(index=i+1, policy=ARMS[a], directory=f'r{i+1:02d}-{ARMS[a]}')
                            for i, a in enumerate(sum(ORDER, []))])
    for pilot, result in zip(pilots, results):
        failures = native_identity_failures(result, pilot, protocol)
        if failures: raise ValueError('Pilot frozen identity gate failed: '+str(failures))
    save(output/'protocol.json', protocol)
    save(output/'freeze-receipt.json', dict(protocolSha256=sha(output/'protocol.json'), frozenBeforeRuns=True))
    print(json.dumps(dict(protocol=str(output/'protocol.json'), sha256=sha(output/'protocol.json'), runs=16)))


def verify_player(root):
    protocol = load(root/'protocol.json')
    if sha(root/'protocol.json') != load(root/'freeze-receipt.json')['protocolSha256']:
        raise ValueError('Frozen protocol changed')
    failures = verify_index(Path(protocol['player']).parent, root/'player-files.json', protocol['playerIndexSha256'])
    if failures: raise ValueError(str(failures))
    save(root/'player-after.json', dict(verifiedUtc=datetime.now(timezone.utc).isoformat(), files=protocol['playerFiles'],
        playerUnchanged=True, playerIndexSha256=protocol['playerIndexSha256']))


def aggregate_samples(samples, thresholds):
    """Retain failed runs; unavailable metrics are explicit, never zero-time wins."""
    by_arm = {}
    for arm in ARMS:
        rows = [s for s in samples if s['policy'] == arm]
        elapsed = [r['elapsedSeconds'] for r in rows if r.get('elapsedSeconds') is not None]
        updates = [r['allUpdates'] for r in rows if r.get('allUpdates') is not None]
        windows = {}
        for window in ('firstLoad', 'mainObservation'):
            available = [r[window] for r in rows if r.get(window) is not None]
            windows[window] = dict(availableProcesses=len(available),
                **{p:median(r[p+'Milliseconds'] for r in available) if available else None for p in ('p95','p99')})
        by_arm[arm] = dict(processes=len(rows), accepted=sum(r['accepted'] for r in rows),
            elapsedAvailableProcesses=len(elapsed), medianElapsedSeconds=median(elapsed) if elapsed else None,
            updateAvailableProcesses=len(updates),
            maximumUpdateMilliseconds=max((r['maximumMilliseconds'] for r in updates), default=None),
            totalHitches={str(t):sum(r['hitchCounts'][str(t)] for r in updates) if updates else None for t in thresholds},
            windows=windows)
    return by_arm


def summarize(root, output=None):
    protocol = load(root/'protocol.json')
    failures, samples, missing, failed_attempts = [], [], [], []
    if sha(root/'protocol.json') != load(root/'freeze-receipt.json')['protocolSha256']:
        failures.append('Protocol changed after freeze')
    player_root = Path(protocol['player']).parent
    for directory, path, digest in (
        (player_root, root/'player-files.json', protocol['playerIndexSha256']),
        (player_root.parent.parent/protocol['playerStage']/'source-inputs', root/'adapter-package-inputs.json', protocol['sourceInputIndexSha256'])):
        try: failures.extend(verify_index(directory, path, digest))
        except (OSError, ValueError) as error: failures.append('Artifact evidence unavailable: '+str(error))
    if not (root/'player-after.json').exists(): failures.append('Missing post-run Player verification')
    else:
        after = load(root/'player-after.json')
        if after.get('playerUnchanged') is not True or after.get('files') != protocol['playerFiles']:
            failures.append('Post-run Player verification differs')
    for run in protocol['runs']:
        path = root/run['directory']/'validation.json'
        if not path.exists():
            if path.parent.exists():
                stage_path, process_path = path.parent/'stage.json', path.parent/'process.json'
                failed_attempts.append(dict(**run, nativeProcessRecorded=process_path.exists(),
                    failure='Attempt has no completed native validation; retain process/stage evidence, not a replacement sample.',
                    stage=load(stage_path) if stage_path.exists() else None,
                    process=load(process_path) if process_path.exists() else None,
                    rawFiles=[dict(file=p.relative_to(root).as_posix(),sha256=sha(p)) for p in
                              (stage_path,process_path,path.parent/'command.json',path.parent/'player.log') if p.exists()]))
            else: missing.append(run)
            continue
        saved = load(path)
        actual, gates = None, native_identity_failures(saved, run, protocol)
        try:
            actual = megacity(path.parent, require_warmup=True)
            gates.extend(native_identity_failures(actual, run, protocol))
            if actual != saved: gates.append('Saved validation differs from actual raw evidence')
        except (OSError, ValueError, KeyError) as error: gates.append('Raw validation unavailable: '+str(error))
        value = actual or saved
        warmup = value['warmupReceipts'][0]['data'] if value['warmupReceipts'] else None
        samples.append(dict(index=run['index'], policy=run['policy'], accepted=value['accepted'] and not gates,
            validationSha256=sha(path), captureSha256=value['captureSha256'], playerLogSha256=value['playerLogSha256'],
            failures=value['failures']+gates, elapsedSeconds=value['observerElapsedSeconds'],
            allUpdates=value['allObservedUpdateIntervals'], firstLoad=value['firstLoadThroughReadinessUpdateIntervals'],
            mainObservation=value['declaredMainObservationUpdateIntervals'],
            observerToFirstUpdateSeconds=value['observerToFirstUpdateSeconds'],
            lastUpdateToQuitCallbackSeconds=value['lastUpdateToQuitCallbackSeconds'],
            processLaunchUtc=value['processLaunchUtc'], monitorObservedExitUtc=value['monitorObservedExitUtc'],
            peakAllocatedBytes=value['peakObservedAllocatedBytes'], peakReservedBytes=value['peakObservedReservedBytes'],
            focusSamples=value['focusSamples'], renderSettings=value['renderSettings'],
            readinessEvents=value['readinessEvents'], quitEvents=value['quitEvents'],
            sustainedPopulation=value['sustainedPopulation'], dynamicEntityEvidence=value['dynamicEntityEvidence'],
            nativeTraceEntryGrowth=warmup['cacheMissTrace']['cacheMissGraphicsStates'] if warmup else None,
            warmupPhases=warmup['phases'] if warmup else []))
    by_arm = aggregate_samples(samples, protocol['hitchThresholdMilliseconds'])
    result = dict(schemaVersion=1, protocolSha256=sha(root/'protocol.json'),
        completed=not failures and not failed_attempts and len(samples)==16 and all(s['accepted'] for s in samples),
        verifiedUtc=datetime.now(timezone.utc).isoformat(), verificationFailures=failures,
        summaryToolSha256=sha(Path(__file__)), validationToolSha256=sha(Path(__file__).with_name('pso_megacity_capture.py')),
        allSamples=samples, failedAttempts=failed_attempts, unexecuted=missing, byArm=by_arm,
        scope='Bounded adapted original external application, stationary original camera/evolving city, retained caches, CPU observation intervals; no standardized score, driver-cold or useful-coverage claim.')
    save(output or root/'comparison-summary.json', result)
    print(json.dumps(dict(completed=result['completed'], byArm=by_arm, unexecuted=len(missing))))
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['freeze','verify-player','summarize'])
    parser.add_argument('path', type=Path); parser.add_argument('--output', type=Path)
    parser.add_argument('--player-stage'); parser.add_argument('--pilot-suffix')
    parser.add_argument('--pilot-validation', default='validation-01.json')
    args = parser.parse_args()
    if args.action == 'freeze': freeze(args.path.resolve(), args.player_stage, args.pilot_suffix, args.output.resolve(), args.pilot_validation)
    elif args.action == 'verify-player': verify_player(args.path.resolve())
    else: raise SystemExit(0 if summarize(args.path.resolve(), args.output)['completed'] else 1)
