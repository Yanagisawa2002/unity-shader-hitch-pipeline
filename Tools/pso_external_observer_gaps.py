"""Post-process immutable native captures: startup/exit gaps and largest intervals.

This adds no Player workload and changes no saved validation or frozen protocol.
It correlates actual event time/frame markers; correlation does not assign cause.
"""
import argparse
from datetime import datetime
import json
from pathlib import Path
from pso_external_capture import load, sha


def inspect(stage):
    stage = Path(stage)
    capture_path, process_path = stage/'capture/external-capture.json', stage/'process.json'
    capture, process = load(capture_path), load(process_path)
    frames = capture['frames']
    if not frames: raise ValueError('Empty observer stream')
    largest = sorted(range(1,len(frames)), key=lambda i: frames[i]['updateIntervalMilliseconds'], reverse=True)[:10]
    def nearby(collection, first, last):
        return [e for e in collection if first-2 <= e.get('frame',-10000) <= last+2]
    events, timelines = capture.get('events',[]), capture.get('timelineEvents',[])
    phases=[]
    for line in (stage/'player.log').read_text(encoding='utf-8-sig',errors='replace').splitlines():
        if line.startswith('[PSO External Phase] '):
            phases.append(json.loads(line[len('[PSO External Phase] '):]))
    start, end = process['processStartedUtc'], process['finishedUtc']
    samples = []
    for i in largest:
        previous, current = frames[i-1], frames[i]
        samples.append(dict(previousUpdate=previous,followingUpdate=current,
            nearbyObserverEvents=nearby(events,previous['frame'],current['frame']),
            nearbyTimelineEvents=nearby(timelines,previous['frame'],current['frame']),
            nearbyPhaseEvents=nearby(phases,previous['frame'],current['frame'])))
    return dict(directory=stage.name,buildGuid=capture['buildGuid'],
        scope='Measured CPU Update stream and explicit unsampled gaps; launch to monitor-observed exit is not exact presentation/shutdown timing.',
        firstUpdate=frames[0],lastUpdate=frames[-1],observerElapsedSeconds=capture['elapsedSeconds'],
        observerToFirstUpdateSeconds=frames[0]['seconds'],
        lastUpdateToQuitCallbackSeconds=capture['elapsedSeconds']-frames[-1]['seconds'],
        processLaunchUtc=start,monitorObservedExitUtc=end,
        launchToMonitorObservedExitSeconds=(datetime.fromisoformat(end)-datetime.fromisoformat(start)).total_seconds(),
        consecutiveUpdateFrames=all(b['frame']==a['frame']+1 for a,b in zip(frames,frames[1:])),
        largestObservedIntervals=samples,
        rawFiles=[dict(file=str(p.relative_to(stage)).replace('\\','/'),sha256=sha(p),bytes=p.stat().st_size)
                  for p in (capture_path,process_path,stage/'player.log')])


if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--protocol',required=True,type=Path); p.add_argument('--output',required=True,type=Path)
    args=p.parse_args()
    if args.output.exists(): raise ValueError('Use a new evidence file')
    protocol=load(args.protocol)
    samples=[dict(index=run['index'],policy=run['policy'],**inspect(args.protocol.parent/run['directory'])) for run in protocol['runs']]
    args.output.write_text(json.dumps(dict(schemaVersion=1,protocolSha256=sha(args.protocol),
        toolSha256=sha(Path(__file__)),samples=samples),indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(samples=len(samples),output=str(args.output))))
