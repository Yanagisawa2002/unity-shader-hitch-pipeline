"""Freeze or summarize a finite, matched official Boat Attack native comparison."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
from statistics import median
from pso_external_capture import HITCH_THRESHOLDS, boat, load, sha

ARMS = ['disabled', 'all-at-once', 'scheduled', 'observed-budget']
ORDER = [[0, 1, 3, 2], [1, 2, 0, 3], [2, 3, 1, 0], [3, 0, 2, 1]]
ENVIRONMENT_KEYS = ('unityVersion','buildGuid','graphicsDeviceType','graphicsDeviceName','driverIdentity',
                    'driverVersion','qualityLevelName','renderingThreadingMode','processorCount','identity')
PROCESS_CACHE = 'New process, existing application/OS/driver caches retained. Not driver-cold.'


def save(path, data):
    with path.open('x', encoding='utf-8') as f:
        json.dump(data, f, indent=2)
        f.write('\n')


def index(root):
    return [dict(path=f.relative_to(root).as_posix(), bytes=f.stat().st_size, sha256=sha(f))
            for f in sorted(root.rglob('*')) if f.is_file()]


def freeze(attempt, player_stage, pilot_suffix, output):
    player_root = attempt/'players'/player_stage
    player = player_root/'BoatAttack.exe'
    summary = load(attempt/player_stage/'build-summary.json')
    if summary['result'] != 'Succeeded' or summary['errors']:
        raise ValueError('A successful actual final build is required')
    pilots = []
    environments = []
    for arm in ARMS:
        path = attempt/f'boat-policy-pilot-{arm}-{pilot_suffix}'/'validation-01.json'
        result = load(path)
        if not result['accepted'] or not result['nativePolicyValidated'] or result['persistentAllocationWarnings'] or result['buildGuid'] != summary['guid']:
            raise ValueError('Final native content/policy/cleanup gate failed: '+str(path))
        pilots.append(dict(policy=arm, path=str(path), sha256=sha(path)))
        environments.append(result['warmupReceipts'][0]['data']['environment'])
    for key in ENVIRONMENT_KEYS:
        if len({json.dumps(e[key], sort_keys=True) for e in environments}) != 1:
            raise ValueError('Pilot environment drift: '+key)
    plan_path = player_root/'BoatAttack_Data/StreamingAssets/ShaderHitchPipeline/plan.json'
    plan = load(plan_path)
    if any(p['prewarmAtStartup'] for p in plan['phases']):
        raise ValueError('Disabled baseline observation must never automatically activate work')
    output.mkdir(parents=True, exist_ok=False)
    files = index(player_root)
    save(output/'player-files.json', files)
    inputs = index(attempt/player_stage/'source-inputs')
    save(output/'adapter-package-inputs.json', inputs)
    protocol = dict(schemaVersion=1, frozenUtc=datetime.now(timezone.utc).isoformat(),
        workload='Official Boat Attack, declared source/configuration repairs, original complete benchmark routes',
        upstreamCommit='6d51b73619199c6dc8266045ea5c494355acf6b5',
        upstreamTree='3dea6169e8c4799e781e64af9c91043670a5df52',
        player=str(player.resolve()), playerStage=player_stage, buildGuid=summary['guid'],
        playerFiles=len(files), playerBytes=sum(f['bytes'] for f in files), playerIndexSha256=sha(output/'player-files.json'),
        sourceInputIndexSha256=sha(output/'adapter-package-inputs.json'),
        protocolToolSha256=sha(Path(__file__)), validationToolSha256=sha(Path(__file__).with_name('pso_external_capture.py')),
        processCacheCondition=PROCESS_CACHE,
        editorSha256=load(attempt/player_stage/'command.json')['unitySha256'],
        environment=environments[0], graphics='Windows x64 / D3D12 / IL2CPP / Development Player / 1920x1080 / High / visible normal window',
        planFileSha256=sha(plan_path), planContentSha256=plan['planSha256'],
        collections=[dict(phase=p['phase'],sha256=p['collectionSha256'],states=p['graphicsStateCount']) for p in plan['phases']],
        backend='Unity 6000.1 native-async-bulk; throughput submits all ready phase work; scheduled/observed use admission. Not fixed-progressive.',
        entry='Original loader -> original Island Flythrough (warmup + 3x500) -> original Island Static (warmup + 5x25) -> original Exit',
        warmupWindow='Original upstream warmup traversals only, 1000 ms estimated per-admission cap; no added wait, camera/route change or hard latency guarantee.',
        observation='Same seeded plan baseline and encountered-shader retention in all arms; disabled has zero activations. No screenshots, phase-switching training trace or diagnostic watchdog in formal runs.',
        cache='Existing application/OS/driver caches retained after recorded training/pilots; new processes. No driver-cold claim, no cache purge. Artifact hashing also reads local files before this sequence.',
        timing='All CPU Update intervals, first warmup-route pass, native warmed samples, startup-to-quit callback, loads and total route; not GPU completion or presentation.',
        hitchThresholdMilliseconds=list(HITCH_THRESHOLDS), percentile='nearest rank per process, no discarded intervals/outliers',
        coverage='Native seeded GSC entry growth is reported with its observation blind interval. The uninterrupted diagnostic retained 94 states/public fields through offline round-trip but differed from the segmented union. Opaque native state identity and useful first-draw coverage remain unresolved; no coverage percentage.',
        order='Four-period balanced Williams order; four new processes per arm. Caches continue across row boundaries; no selective replacement.',
        stop='Stop remaining runs on native/process errors, external workload, reserve threat, any missing route/work/cleanup/identity gate, or 150 second timeout. Retain all failures and unexecuted arms.',
        minimumFreeGiBReserve=20, earlyStopFreeGiB=25, estimatedAdditionalPeakGiB=2, maximumProcessSeconds=150,
        pilots=pilots, runs=[dict(index=i+1, policy=ARMS[arm], directory=f'r{i+1:02d}-{ARMS[arm]}') for i,arm in enumerate(sum(ORDER, []))])
    save(output/'protocol.json', protocol)
    save(output/'freeze-receipt.json', dict(protocolSha256=sha(output/'protocol.json'), frozenBeforeRuns=True))
    print(json.dumps(dict(protocol=str(output/'protocol.json'), sha256=sha(output/'protocol.json'), playerIndexSha256=protocol['playerIndexSha256'], runs=16)))


def identity_failures(result, run, protocol):
    failures = []
    for key in ('accepted','contentAccepted','nativePolicyValidated'):
        if result.get(key) is not True: failures.append('Missing native gate: '+key)
    if result.get('policy') != run['policy']: failures.append('Run policy differs from frozen order')
    if result.get('buildGuid') != protocol['buildGuid']: failures.append('Build GUID differs from frozen Player')
    if result.get('screenshotsEnabled') is not False or result.get('traceEnabled') is not False:
        failures.append('Formal diagnostic/screenshot mode differs from protocol')
    if result.get('diagnosticOnly') or result.get('observerOnly'):
        failures.append('Whole-task discovery/profiler mode is not a formal policy comparison arm')
    if result.get('cacheCondition') != protocol.get('processCacheCondition',PROCESS_CACHE):
        failures.append('Cache condition differs from protocol')
    if result.get('persistentAllocationWarnings'): failures.append('Native shutdown allocation warnings')
    receipts = result.get('warmupReceipts',[])
    if len(receipts) != 1:
        return failures+['Missing unique warmup/observation receipt']
    receipt = receipts[0]['data']
    if receipt.get('planSha256') != protocol['planFileSha256']: failures.append('Plan file identity differs from protocol')
    environment = receipt.get('environment',{})
    for key in ENVIRONMENT_KEYS:
        if key not in environment or environment[key] != protocol['environment'].get(key):
            failures.append('Environment differs from protocol: '+key)
    return failures


def verify_index(directory, path, expected_hash):
    if sha(path) != expected_hash: return ['Frozen file index changed: '+path.name]
    expected = load(path)
    # Exact files, not merely matching the subset which happened to be listed.
    return [] if index(directory) == expected else ['Actual artifact bytes/files differ: '+path.name]


def raw_validation_failures(saved, actual, thresholds):
    # Compare every field subsequently reported by summarize, including totals
    # and memory: matching frame percentiles alone cannot attest those metrics.
    keys=('captureSha256','playerLogSha256','allUpdateIntervals','routes','nativeResults','schedulingFeedback','warmupReceipts',
          'observerElapsedSeconds','observerToFirstUpdateSeconds','peakAllocatedBytes','peakReservedBytes','phaseEvents')
    def declared_thresholds(value):
        if isinstance(value,dict):
            return {k:declared_thresholds(v) for k,v in value.items()
                    if k not in ('200','500') or k in {str(t) for t in thresholds}}
        if isinstance(value,list): return [declared_thresholds(v) for v in value]
        return value
    return ['Saved validation differs from actual raw evidence: '+key for key in keys
            if declared_thresholds(saved[key])!=declared_thresholds(actual[key])]


def summarize(root, output=None):
    protocol = load(root/'protocol.json')
    samples, missing, verification_failures = [], [], []
    if sha(root/'protocol.json') != load(root/'freeze-receipt.json')['protocolSha256']:
        verification_failures.append('Protocol changed after freeze')
    player_root = Path(protocol['player']).parent
    for directory,path,digest in (
        (player_root,root/'player-files.json',protocol['playerIndexSha256']),
        (player_root.parent.parent/protocol['playerStage']/'source-inputs',root/'adapter-package-inputs.json',protocol['sourceInputIndexSha256'])):
        try: verification_failures.extend(verify_index(directory,path,digest))
        except (OSError,ValueError) as error: verification_failures.append('Artifact index unavailable: '+str(error))
    if not (root/'player-after.json').exists(): verification_failures.append('Missing post-run Player verification')
    else:
        after = load(root/'player-after.json')
        if after.get('playerUnchanged') is not True or after.get('files') != protocol['playerFiles']:
            verification_failures.append('Post-run Player verification differs from protocol')
    for run in protocol['runs']:
        path = root/run['directory']/'validation.json'
        if not path.exists():
            missing.append(run)
            continue
        d = load(path)
        gates = identity_failures(d,run,protocol)
        try:
            actual = boat(path.parent,require_warmup=True,require_no_leaks=True)
            gates.extend(identity_failures(actual,run,protocol))
            gates.extend(raw_validation_failures(d,actual,protocol['hitchThresholdMilliseconds']))
        except (OSError,ValueError,KeyError) as error:
            gates.append('Raw native evidence unavailable/invalid: '+str(error))
        first = d['routes'][0]['runs'][0]['cpuFollowingUpdateIntervals']
        static = d['routes'][1]['runs'][0]['cpuFollowingUpdateIntervals']
        receipt = d['warmupReceipts'][0]['data'] if d['warmupReceipts'] else None
        samples.append(dict(index=run['index'], policy=run['policy'], accepted=d['accepted'] and not gates, validationSha256=sha(path),
            elapsedSeconds=d['observerElapsedSeconds'], firstPass=first, firstStaticPass=static, allUpdates=d['allUpdateIntervals'],
            peakAllocatedBytes=d['peakAllocatedBytes'], peakReservedBytes=d['peakReservedBytes'], failures=d['failures']+gates,
            nativeTraceEntryGrowth=receipt['cacheMissTrace']['cacheMissGraphicsStates'] if receipt else None,
            warmupPhases=[dict(phase=p['phase'], states=p['completedGraphicsStates'], total=p['totalGraphicsStates'],
                              milliseconds=p['elapsedMilliseconds'], batchDurationsMilliseconds=p['batchDurationsMilliseconds']) for p in receipt['phases']] if receipt else []))
    by_arm = {}
    for arm in ARMS:
        items = [s for s in samples if s['policy'] == arm]
        by_arm[arm] = dict(processes=len(items), accepted=sum(s['accepted'] for s in items),
            medianElapsedSeconds=median(s['elapsedSeconds'] for s in items) if items else None,
            medianFirstPassP95=median(s['firstPass']['p95Milliseconds'] for s in items) if items else None,
            medianFirstPassP99=median(s['firstPass']['p99Milliseconds'] for s in items) if items else None,
            maximumUpdateMilliseconds=max((s['allUpdates']['maximumMilliseconds'] for s in items), default=None),
            totalHitches={str(limit): sum(s['allUpdates']['hitchCounts'][str(limit)] for s in items) for limit in protocol['hitchThresholdMilliseconds']})
    result = dict(protocolSha256=sha(root/'protocol.json'), completed=not verification_failures and len(samples)==16 and all(s['accepted'] for s in samples),
        verificationFailures=verification_failures, verifiedUtc=datetime.now(timezone.utc).isoformat(),
        summaryToolSha256=sha(Path(__file__)), validationToolSha256=sha(Path(__file__).with_name('pso_external_capture.py')),
        allSamples=samples, unexecuted=missing, byArm=by_arm,
        scope='Observed native external-workload CPU intervals on existing caches; four processes/arm, no population significance, driver-cold or coverage-gain claim.')
    save(output or root/'comparison-summary.json',result)
    print(json.dumps(dict(completed=result['completed'],byArm=by_arm,unexecuted=len(missing))))
    return result


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['freeze','summarize'])
    parser.add_argument('path',type=Path)
    parser.add_argument('--player-stage')
    parser.add_argument('--pilot-suffix')
    parser.add_argument('--output',type=Path)
    args=parser.parse_args()
    if args.action=='freeze': freeze(args.path.resolve(),args.player_stage,args.pilot_suffix,args.output.resolve())
    else: raise SystemExit(0 if summarize(args.path.resolve(),args.output)['completed'] else 1)
