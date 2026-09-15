"""Freeze and verify a finite native official URP 3D Sample comparison.

This is collection around the unchanged official workload, not scene content or
a general PSO benchmark. Formal timing never launches from this Python module.
"""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
from statistics import median
from pso_boat_comparison import ARMS, ORDER, ENVIRONMENT_KEYS, PROCESS_CACHE, identity_failures, index, save, verify_index
from pso_external_capture import HITCH_THRESHOLDS, load, sha
from pso_urp_capture import ROUTES, urp


def freeze(attempt, player_stage, pilot_suffix, output, pilot_validation='validation-01.json'):
    player_root=attempt/'players'/player_stage
    player=player_root/'UrpExternal.exe'
    build=load(attempt/player_stage/'build-summary.json')
    if build['result']!='Succeeded' or build['errors']:
        raise ValueError('Actual successful final IL2CPP build required')
    pilots=[]; environments=[]
    for arm in ARMS:
        path=attempt/f'urp-policy-pilot-{arm}-{pilot_suffix}'/pilot_validation
        result=load(path); actual=urp(path.parent,require_warmup=True)
        if result!=actual or not actual['accepted'] or not actual['nativePolicyValidated'] or actual['buildGuid']!=build['guid']:
            raise ValueError('Actual independent content/policy gate failed: '+str(path))
        if actual['traceEnabled'] or actual['screenshotsEnabled'] or actual.get('diagnosticOnly') or actual.get('observerOnly'):
            raise ValueError('Policy pilots must use the final non-diagnostic modes')
        pilots.append(dict(policy=arm,path=str(path),sha256=sha(path)))
        environments.append(actual['warmupReceipts'][0]['data']['environment'])
    for key in ENVIRONMENT_KEYS:
        if len({json.dumps(e[key],sort_keys=True) for e in environments})!=1:
            raise ValueError('Pilot environment drift: '+key)
    plan_path=player_root/'UrpExternal_Data/StreamingAssets/ShaderHitchPipeline/plan.json'
    plan=load(plan_path)
    if any(p['prewarmAtStartup'] for p in plan['phases']):
        raise ValueError('Disabled observation must not activate startup work')
    output.mkdir(parents=True,exist_ok=False)
    files=index(player_root); save(output/'player-files.json',files)
    save(output/'adapter-package-inputs.json',index(attempt/player_stage/'source-inputs'))
    protocol=dict(schemaVersion=1,frozenUtc=datetime.now(timezone.utc).isoformat(),
        workload='Official URP 3D Sample 17.1.5, original BenchmarkScene and four full native timelines, declared adapters',
        sourceArchiveSha256='c2a2bcbd8bac1340be683b33ca53e1fddc78c2ae80fa3a0b5406bd616df6628f',
        player=str(player.resolve()),playerStage=player_stage,buildGuid=build['guid'],
        playerFiles=len(files),playerBytes=sum(f['bytes'] for f in files),playerIndexSha256=sha(output/'player-files.json'),
        sourceInputIndexSha256=sha(output/'adapter-package-inputs.json'),
        protocolToolSha256=sha(Path(__file__)),validationToolSha256=sha(Path(__file__).with_name('pso_urp_capture.py')),
        editorSha256=load(attempt/player_stage/'command.json')['unitySha256'],environment=environments[0],
        graphics='Unity 6000.1.0f1 / Windows x64 D3D12 IL2CPP Development Player / 1920x1080 windowed / PC High',
        processCacheCondition=PROCESS_CACHE,planFileSha256=sha(plan_path),planContentSha256=plan['planSha256'],
        collections=[dict(phase=p['phase'],sha256=p['collectionSha256'],states=p['graphicsStateCount']) for p in plan['phases']],
        entry='Original automatic BenchmarkScene -> Terminal -> Garden -> Oasis -> Cockpit; all original stages Finished and native full CSV, then normal adapter Application.Quit. No input simulation or runtime batchmode.',
        routes=[dict(scene=name,folder=folder) for name,folder in ROUTES],
        routeCompletion='Positive original live-camera/timeline association and renders, original director stop and Finished state, complete native CSV. Sampled endpoint gaps are retained; a long frame is not excluded for stepping over an endpoint.',
        backend='native-async-bulk; all-at-once uses throughput admission; scheduled/observed use ready-phase admission. No selected fixed-progressive capability.',
        warmupWindow='Only the original five-second warmup with its original timeline advancing; 1000 ms estimated per-admission cap, no extra loading fence, wait, or hard latency bound.',
        observation='Common seeded plan baseline and encountered-shader retention, zero disabled activations. No screenshots, phase-switching training trace, diagnostic watchdog, or native-route modification in formal processes.',
        cache='Existing application/OS/driver caches retained after training/pilots; fresh processes are not driver-cold. No cache purge, and cache carryover continues across order rows.',
        timing='Complete CPU Update stream including first loads and original warmups; following-Update intervals for actual render submissions; original warmed full-timeline CSV separately. No verified GPU/presentation timing.',
        hitchThresholdMilliseconds=list(HITCH_THRESHOLDS),percentile='Nearest rank per process, no trimming or outlier removal.',
        coverage='Native seeded collection entry growth and observation blind interval are descriptive; opaque native identity does not prove misses or useful first-draw coverage.',
        order='Four-period balanced Williams order, four fresh processes per arm, fixed before formal runs. No selective replacement.',
        stop='Stop remaining runs on process/content/work/cleanup/identity failure, external workload, capacity threat, or 1200 second process timeout. Preserve all failures and unexecuted runs.',
        minimumFreeGiBReserve=20,earlyStopFreeGiB=25,estimatedAdditionalPeakGiB=2,maximumProcessSeconds=1200,
        pilots=pilots,runs=[dict(index=i+1,policy=ARMS[a],directory=f'r{i+1:02d}-{ARMS[a]}') for i,a in enumerate(sum(ORDER,[]))])
    for pilot in pilots:
        failures=identity_failures(load(Path(pilot['path'])),pilot,protocol)
        if failures: raise ValueError('Pilot identity gate: '+str(failures))
    save(output/'protocol.json',protocol)
    save(output/'freeze-receipt.json',dict(protocolSha256=sha(output/'protocol.json'),frozenBeforeRuns=True))
    print(json.dumps(dict(protocol=str(output/'protocol.json'),sha256=sha(output/'protocol.json'),runs=16)))


def summarize(root,output=None):
    protocol=load(root/'protocol.json'); failures=[]; samples=[]; missing=[]
    if sha(root/'protocol.json')!=load(root/'freeze-receipt.json')['protocolSha256']:
        failures.append('Protocol changed after freeze')
    player_root=Path(protocol['player']).parent
    for directory,path,digest in (
        (player_root,root/'player-files.json',protocol['playerIndexSha256']),
        (player_root.parent.parent/protocol['playerStage']/'source-inputs',root/'adapter-package-inputs.json',protocol['sourceInputIndexSha256'])):
        try: failures.extend(verify_index(directory,path,digest))
        except (OSError,ValueError) as error: failures.append('Missing artifact evidence: '+str(error))
    if not (root/'player-after.json').exists(): failures.append('Missing post-run Player verification')
    else:
        after=load(root/'player-after.json')
        if after.get('playerUnchanged') is not True or after.get('files')!=protocol['playerFiles']:
            failures.append('Post-run Player verification differs')
    for run in protocol['runs']:
        path=root/run['directory']/'validation.json'
        if not path.exists(): missing.append(run); continue
        saved=load(path); gates=identity_failures(saved,run,protocol)
        actual=None
        try:
            actual=urp(path.parent,require_warmup=True)
            gates.extend(identity_failures(actual,run,protocol))
            # Every published metric and evidence hash comes from rederived raw
            # input. Do not trust stale total-time or memory fields in a receipt.
            if saved!=actual: gates.append('Saved validation differs from rederived raw evidence')
        except (OSError,ValueError,KeyError) as error: gates.append('Raw evidence unavailable: '+str(error))
        d=actual or saved
        receipt=d['warmupReceipts'][0]['data'] if d['warmupReceipts'] else None
        samples.append(dict(index=run['index'],policy=run['policy'],accepted=not gates and d['accepted'],
            validationSha256=sha(path),captureSha256=d['captureSha256'],playerLogSha256=d['playerLogSha256'],nativeCsvSha256=d['nativeCsvSha256'],
            elapsedSeconds=d['observerElapsedSeconds'],allUpdates=d['allUpdateIntervals'],routes=d['routes'],
            observerToFirstUpdateSeconds=d['observerToFirstUpdateSeconds'],
            nativeResults=d['nativeResults'],peakAllocatedBytes=d['peakAllocatedBytes'],peakReservedBytes=d['peakReservedBytes'],
            failures=d['failures']+gates,nativeTraceEntryGrowth=receipt['cacheMissTrace']['cacheMissGraphicsStates'] if receipt else None,
            warmupPhases=[dict(phase=p['phase'],states=p['completedGraphicsStates'],total=p['totalGraphicsStates'],
                milliseconds=p['elapsedMilliseconds'],batchDurationsMilliseconds=p['batchDurationsMilliseconds']) for p in receipt['phases']] if receipt else []))
    by_arm={}
    for arm in ARMS:
        items=[s for s in samples if s['policy']==arm]
        by_arm[arm]=dict(processes=len(items),accepted=sum(s['accepted'] for s in items),
            medianElapsedSeconds=median(s['elapsedSeconds'] for s in items) if items else None,
            maximumUpdateMilliseconds=max((s['allUpdates']['maximumMilliseconds'] for s in items),default=None),
            totalHitches={str(t):sum(s['allUpdates']['hitchCounts'][str(t)] for s in items) for t in protocol['hitchThresholdMilliseconds']},
            routes=[dict(scene=name,
                medianFirstWarmupP95=median(s['routes'][i]['passes'][0]['cpuFollowingUpdateIntervals']['p95Milliseconds'] for s in items) if items else None,
                medianFirstWarmupP99=median(s['routes'][i]['passes'][0]['cpuFollowingUpdateIntervals']['p99Milliseconds'] for s in items) if items else None,
                medianWarmedFullTimelineP99=median(s['nativeResults'][i]['warmedFrameIntervals']['p99Milliseconds'] for s in items) if items else None)
                for i,(name,_) in enumerate(ROUTES)])
    result=dict(protocolSha256=sha(root/'protocol.json'),completed=not failures and len(samples)==16 and all(s['accepted'] for s in samples),
        verificationFailures=failures,verifiedUtc=datetime.now(timezone.utc).isoformat(),summaryToolSha256=sha(Path(__file__)),
        validationToolSha256=sha(Path(__file__).with_name('pso_urp_capture.py')),allSamples=samples,unexecuted=missing,byArm=by_arm,
        scope='Official external scenes, existing caches, CPU observation intervals; four processes per arm are descriptive repetitions. No general PSO score, driver-cold or useful-coverage claim.')
    save(output or root/'comparison-summary.json',result)
    print(json.dumps(dict(completed=result['completed'],byArm=by_arm,unexecuted=len(missing))))
    return result


def verify_player(root):
    protocol=load(root/'protocol.json')
    if sha(root/'protocol.json')!=load(root/'freeze-receipt.json')['protocolSha256']:
        raise ValueError('Frozen protocol changed')
    failures=verify_index(Path(protocol['player']).parent,root/'player-files.json',protocol['playerIndexSha256'])
    if failures: raise ValueError(str(failures))
    save(root/'player-after.json',dict(verifiedUtc=datetime.now(timezone.utc).isoformat(),
        files=protocol['playerFiles'],playerUnchanged=True,playerIndexSha256=protocol['playerIndexSha256']))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['freeze','summarize','verify-player']);parser.add_argument('path',type=Path)
    parser.add_argument('--player-stage');parser.add_argument('--pilot-suffix');parser.add_argument('--output',type=Path)
    parser.add_argument('--pilot-validation',default='validation-01.json')
    args=parser.parse_args()
    if args.action=='freeze': freeze(args.path.resolve(),args.player_stage,args.pilot_suffix,args.output.resolve(),args.pilot_validation)
    elif args.action=='verify-player': verify_player(args.path.resolve())
    else: raise SystemExit(0 if summarize(args.path.resolve(),args.output)['completed'] else 1)
