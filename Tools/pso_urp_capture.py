"""Validate the real official URP template route and its native CSV, including first load.

No source scene or native timing values are synthesized. FrameTimingManager
columns are retained in raw CSV, but are not promoted to verified GPU timings.
"""
import argparse
from collections import Counter
import csv
import io
import json
import math
from pathlib import Path
import re
from pso_external_capture import load, sha, statistics, native_policy_failures

ROUTES = [('TerminalScene','Terminal'),('GardenScene','Garden'),('OasisScene','Oasis'),('CockpitScene','Cockpit')]
PHASES = ['urp-loading','urp-terminal','urp-garden','urp-oasis','urp-cockpit']


def completed_timeline(events, name, asset, duration):
    """Natural stop + original stage completion, independent of sampled endpoints.

    A time-based route can jump past a last rendered pose during a real hitch.
    It must not be excluded merely for doing so. The unchanged native loop ends
    on its director pausing; cancellation/empty data are checked separately.
    """
    selected=[e for e in events if e['asset']==asset]
    starts=[e for e in selected if e['kind']=='stage-'+name+'-Running']
    ends=[e for e in selected if e['kind']=='stage-'+name+'-Finished']
    if len(starts)!=1 or len(ends)!=1: return False
    start,end=starts[0],ends[0]
    stops=[e for e in selected if e['kind']=='stopped' and e['wrapMode']=='None' and e['status']=='Running' and e['state']=='Paused'
           and start['seconds']<=e['seconds']<=end['seconds'] and e['duration']==duration]
    return len(stops)==1 and end['seconds']>start['seconds']


def main_route_renders(rows):
    """Identify the persistent native benchmark camera from its Running pass.

    The original LoadAndInit yields a frame before disabling scene cameras. Its
    extra first-load submissions are real workload, retained separately and in
    the full CPU stream, not duplicate captures of the benchmark camera.
    """
    ids={r['cameraId'] for r in rows if r['status']=='Running'}
    if len(ids)!=1: raise ValueError('Missing/ambiguous native Running camera')
    camera=next(iter(ids))
    return [r for r in rows if r['cameraId']==camera],[r for r in rows if r['cameraId']!=camera]


def parse_native_csv(text):
    rows=list(csv.reader(io.StringIO(text)))
    if not rows or rows[0]!=['URP Template Performance Test']:
        raise ValueError('Missing original full native CSV banner')
    current=None; results=[]; i=1
    while i<len(rows):
        row=rows[i]
        if row and row[0]=='Scene':
            current=row[1]
        if row and row[0]=='Captured frames':
            count=int(row[1]); samples=[]; i+=2 # original column header follows count
            for _ in range(count):
                values=[float(v) for v in rows[i]]
                if len(values)!=6 or not all(math.isfinite(v) for v in values) or values[0]<0 or values[1]<=0:
                    raise ValueError('Invalid/empty native sample: '+str(current))
                samples.append(dict(timelineSeconds=values[0],frameMilliseconds=values[1]))
                i+=1
            if count<2: raise ValueError('No meaningful native full-timeline samples: '+str(current))
            results.append(dict(scene=current,count=count,samples=samples))
            continue
        i+=1
    if [r['scene'] for r in results]!=[r[0] for r in ROUTES]:
        raise ValueError('Missing, duplicate or reordered native route result')
    return results


def urp(stage,require_warmup=False):
    stage=Path(stage); capture=load(stage/'capture/external-capture.json')
    command,process,boundary=(load(stage/p) for p in ('command.json','process.json','stage.json'))
    failures=[]
    def require(condition,reason):
        if not condition: failures.append(reason)
    require(boundary['status']=='completed' and boundary['mutexReleased'],'Stage/mutex did not complete')
    require(process.get('exitCode')==0 and not process.get('stopReason'),'Abnormal Player exit')
    require(capture['applicationQuit'] and capture['originalBenchmarkFinished'],'Missing original benchmark completion and normal quit')
    require(capture['csvPublications']==1,'Missing/duplicate native aggregate publication')
    require((capture['unityVersion'],capture['graphicsApi'],capture['width'],capture['height'],capture['quality'])==
            ('6000.1.0f1','Direct3D12',1920,1080,'PC High'),'Wrong rendering cell')
    require(capture['errors']==0 and capture['exceptions']==0,'Observer recorded errors/exceptions')
    log=(stage/'player.log').read_text(encoding='utf-8-sig',errors='replace')
    errors=[line for line in log.splitlines() if re.search(r'\b\w*Exception:|\bAssertion failed\b|Crash!!!|\[ShaderHitchPipeline\].*Failed',line)]
    leaks=re.findall(r'Leak Detected : Persistent allocates (\d+) individual allocations',log)
    require(not errors,'Full post-exit log contains errors');require(not leaks,'Native allocation leak warning')
    frames=capture['frames'];require(len(frames)>1,'No complete Update capture')
    require(all(b['frame']>a['frame'] and b['seconds']>a['seconds'] for a,b in zip(frames,frames[1:])),'Non-monotonic Update capture')
    by_frame={f['frame']:f for f in frames}
    native=[]
    try: native=parse_native_csv((stage/'capture/upstream-results.csv').read_text(encoding='utf-8-sig'))
    except (OSError,ValueError,IndexError) as error: failures.append('Native CSV invalid: '+str(error))
    routes=[]
    expected_paths=[f'Assets/Scenes/{folder}/{name}.unity' for name,folder in ROUTES]
    loaded=[e['scene'] for e in capture['events'] if e['kind']=='scene-loaded-not-first-draw' and e['scene'] in expected_paths]
    require(loaded==expected_paths,'Missing/duplicate/reordered original scene loads')
    for name,folder in ROUTES:
        scene=f'Assets/Scenes/{folder}/{name}.unity'
        renders=[r for r in capture['renders'] if r['scene']==scene and r['stage']==name and r['cameraType']=='Game'
                 and not r['targetTexture'] and (r['pixelWidth'],r['pixelHeight'])==(1920,1080) and r['timelineBoundToRenderingCamera']]
        other_cameras=[]
        try: renders,other_cameras=main_route_renders(renders)
        except ValueError as error: failures.append(name+': '+str(error))
        groups=[]
        for status in ('Warming','Running'):
            selected=[r for r in renders if r['status']==status]
            require(bool(selected),'No original timeline-bound render evidence: '+name+'/'+status)
            durations={r['timelineDuration'] for r in selected}
            require(len(durations)==1 and all(math.isfinite(d) and d>0 for d in durations),'Missing/unstable positive original timeline duration: '+name)
            require(len({(r['director'],r['timelineAsset']) for r in selected})==1,'Ambiguous original camera timeline: '+name)
            require(len({r['frame'] for r in selected})==len(selected),'Duplicate main-route render submissions: '+name)
            following=[by_frame[r['frame']+1]['updateIntervalMilliseconds'] for r in selected if r['frame']+1 in by_frame]
            bounds={a:[min((r['position'][a] for r in selected),default=0),max((r['position'][a] for r in selected),default=0)] for a in 'xyz'}
            first=min((r['timelineTime'] for r in selected),default=0);last=max((r['timelineTime'] for r in selected),default=0)
            duration=next(iter(durations)) if len(durations)==1 else 0
            if status=='Running':
                asset=selected[0]['timelineAsset'] if selected else None
                require(completed_timeline(capture.get('timelineEvents',[]),name,asset,duration),
                        'Missing original director stop and full-timeline stage completion: '+name)
                require(any(hi-lo>1 for lo,hi in bounds.values()),'No original camera progression: '+name)
                values=next((x for x in native if x['scene']==name),None)
                if values:
                    require(abs(values['count']-len(selected))<=3,'Native samples and rendered-frame count diverge: '+name)
                    times=[s['timelineSeconds'] for s in values['samples']]
                    require(all(b>=a for a,b in zip(times,times[1:])), 'Non-monotonic native timeline CSV: '+name)
            groups.append(dict(status=status,frames=len(selected),timelineDuration=duration,firstTimelineSeconds=first,lastTimelineSeconds=last,
                director=selected[0]['director'] if selected else None,timelineAsset=selected[0]['timelineAsset'] if selected else None,
                cameraAssociations=sorted({r.get('cameraAssociation','') for r in selected}),
                activeVirtualCameras=sorted({r.get('activeVirtualCamera','') for r in selected}),
                animatedTargets=sorted({r.get('animatedTarget','') for r in selected}),
                sampledEndpointGapSeconds=dict(first=first,last=max(0,duration-last)),
                firstRenderSeconds=selected[0]['seconds'] if selected else None,lastRenderSeconds=selected[-1]['seconds'] if selected else None,
                positionBounds=bounds,cpuFollowingUpdateIntervals=statistics(following) if following else None,
                missingFollowingUpdateIntervals=len(selected)-len(following)))
        initial=[f['updateIntervalMilliseconds'] for f in frames[1:] if f['stage']==name and f['status']=='Warming']
        routes.append(dict(scene=scene,stage=name,passes=groups,
            otherOriginalCameraSubmissions=[dict(frame=r['frame'],camera=r['camera'],cameraId=r['cameraId'],status=r['status']) for r in other_cameras],
            originalWarmupAndLoadingUpdateIntervals=statistics(initial) if initial else None))
    phases=[json.loads(line.split('[PSO External Phase] ',1)[1]) for line in log.splitlines() if '[PSO External Phase] ' in line]
    require(not any(e['status']=='Failed' for e in phases),'Content lifecycle failed')
    feedback=[dict(file=p.relative_to(stage).as_posix(),sha256=sha(p),data=load(p)) for p in sorted((stage/'capture').rglob('*scheduling*.json'))]
    warmup=[dict(file=p.relative_to(stage).as_posix(),sha256=sha(p),data=load(p)) for p in sorted((stage/'capture').rglob('*.warmup.json'))]
    content_accepted=not failures
    policy_failures=native_policy_failures(command,warmup,feedback,PHASES) if require_warmup else []
    failures.extend(policy_failures)
    return dict(schemaVersion=1,workload='Official URP 3D Sample 17.1.5 native BenchmarkScene, declared adapters',
        accepted=not failures,contentAccepted=content_accepted,nativePolicyValidated=require_warmup and not policy_failures,failures=failures,
        captureSha256=sha(stage/'capture/external-capture.json'),playerLogSha256=sha(stage/'player.log'),
        nativeCsvSha256=sha(stage/'capture/upstream-results.csv') if (stage/'capture/upstream-results.csv').exists() else None,
        buildGuid=capture['buildGuid'],policy=command['policy'],screenshotsEnabled=capture['screenshotsEnabled'],traceEnabled=command['trace'],
        cacheCondition=command['cacheCondition'],observerElapsedSeconds=capture['elapsedSeconds'],
        observerToFirstUpdateSeconds=frames[0]['seconds'] if frames else None,
        allUpdateIntervals=statistics(f['updateIntervalMilliseconds'] for f in frames[1:]) if len(frames)>1 else None,
        peakAllocatedBytes=max((f['allocatedBytes'] for f in frames),default=0),peakReservedBytes=max((f['reservedBytes'] for f in frames),default=0),
        routes=routes,nativeResults=[dict(scene=r['scene'],count=r['count'],warmedFrameIntervals=statistics(s['frameMilliseconds'] for s in r['samples'])) for r in native],
        timelineEvents=capture.get('timelineEvents',[]),bindings=capture.get('bindings',[]),
        phaseEvents=phases,schedulingFeedback=feedback,warmupReceipts=warmup,logErrors=errors,persistentAllocationWarnings=leaks,
        limitations=['Update intervals/render submissions are not presentation or GPU timings. Native FrameTimingManager CSV columns are retained without claiming their validity.',
                    'Scene-loaded and observed native-stage transitions do not prove first draw; the full process stream retains earlier intervals.',
                    'Native seeded collection entry growth is not proven driver misses or useful first-draw coverage. Existing driver caches remain.'])


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('stage',type=Path)
    parser.add_argument('--output',type=Path,required=True);parser.add_argument('--require-warmup',action='store_true')
    args=parser.parse_args();result=urp(args.stage,args.require_warmup)
    with args.output.open('x',encoding='utf-8') as f: json.dump(result,f,indent=2);f.write('\n')
    print(json.dumps({k:result[k] for k in ('accepted','failures','buildGuid','policy','observerElapsedSeconds','persistentAllocationWarnings')}))
    raise SystemExit(0 if result['accepted'] else 1)
