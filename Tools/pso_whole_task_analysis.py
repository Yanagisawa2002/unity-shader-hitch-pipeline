"""Audit complete URP discovery processes without promoting correlation to causation.

Stdlib-only offline analysis. No engine launch, cache mutation or invented GPU
times. Invalid content and unavailable diagnostic metrics remain visible.
"""
import argparse
from datetime import datetime, timezone
import json
import math
from pathlib import Path

from pso_external_capture import load, sha
from pso_urp_capture import urp
from pso_whole_task_evidence import CELLS, cell_failures, checkpoint_summary, native_progressive_summary

THRESHOLD = 33.333


def interval_summary(frames, elapsed):
    if len(frames) < 2:
        raise ValueError('At least two real Update observations are required')
    gaps = []
    long_frames = []
    intervals = []
    for before, after in zip(frames, frames[1:]):
        value = (after['seconds'] - before['seconds']) * 1000
        if not math.isfinite(value) or value <= 0:
            raise ValueError('Nonmonotonic or nonfinite observer clock')
        if not math.isclose(value, after['updateIntervalMilliseconds'], rel_tol=1e-7, abs_tol=1e-5):
            raise ValueError('Stored interval differs from raw observer clock')
        if after['frame'] != before['frame'] + 1:
            gaps.append([before['frame'], after['frame']])
        intervals.append(value)
        if value > THRESHOLD:
            long_frames.append(dict(beforeFrame=before['frame'], afterFrame=after['frame'],
                milliseconds=value, beforeScene=before['scene'], afterScene=after['scene'],
                beforeStage=before.get('stage'), afterStage=after.get('stage'),
                beforeStatus=before.get('status'), afterStatus=after.get('status')))
    startup = frames[0]['seconds']
    tail = elapsed - frames[-1]['seconds']
    if startup < 0 or tail < 0 or not all(math.isfinite(x) for x in (startup, tail, elapsed)):
        raise ValueError('Invalid whole-observer boundaries')
    if not math.isclose(startup + sum(intervals) / 1000 + tail, elapsed, abs_tol=1e-7):
        raise ValueError('Full interval partition does not conserve observer time')
    return dict(intervalCount=len(intervals), frameGaps=gaps, maximumMilliseconds=max(intervals),
        excessAbove33333Milliseconds=sum(max(0, value - THRESHOLD) for value in intervals),
        countsAboveMilliseconds={str(t): sum(v > t for v in intervals) for t in (16.67, THRESHOLD, 50, 200, 500)},
        observerToFirstUpdateSeconds=startup, lastUpdateToQuitCallbackSeconds=tail,
        observerSeconds=elapsed, longIntervals=long_frames,
        scope='All CPU Update intervals, no trimming. First Update gap and tail are separate. Not GPU or presentation.')


def recorder_summary(document):
    if document.get('diagnosticOnly') is not True or document.get('normalQuit') is not True:
        raise ValueError('Incomplete or incorrectly labeled diagnostic')
    result = []
    count = len(document['metrics'])
    for sample in document['samples']:
        if len(sample['values']) != count or len(sample['counts']) != count:
            raise ValueError('Recorder metric/sample cardinality differs')
    for index, metric in enumerate(document['metrics']):
        valid = metric.get('available') is True and metric.get('unit') == 'TimeNanoseconds'
        values = [(s, s['values'][index], s['counts'][index]) for s in document['samples']]
        present = [(s, v, n) for s, v, n in values if valid and v >= 0 and n >= 0]
        positive = [dict(observedAtUnityFrame=s['observedAtUnityFrame'],
                         workMilliseconds=v / 1e6, markerSampleCount=n)
                    for s, v, n in present if v > 0 or n > 0]
        result.append(dict(name=metric['name'], registeredAvailable=metric.get('available'),
            usableNanosecondMetric=valid, unit=metric.get('unit'), error=metric.get('error'),
            observedSamples=len(present), unavailableSamples=len(values) - len(present),
            maximumRecordedWorkMilliseconds=max((v / 1e6 for _, v, _ in present), default=None),
            positiveSamples=positive))
    return dict(metrics=result, observedFrames=len(document['samples']),
        scope='Previous completed profiler-frame aggregates sampled at Update, not an exact cross-thread critical-path join. Positive samples retained individually. Missing units/recorders remain unavailable. Use binary metadata and stacks for attribution.')


def audit(stage, expected_unity, profile_export=None, expected_gpu=None, expected_cell=None, native_count=None, visual_review=None):
    stage = Path(stage)
    content = urp(stage, expected_unity=expected_unity, expected_cell=expected_cell)
    capture_path = stage / 'capture/external-capture.json'
    capture = load(capture_path)
    command = load(stage / 'command.json')
    process = load(stage / 'process.json')
    failures = list(content['failures'])
    if expected_gpu is not None and capture['gpu'] != expected_gpu:
        failures.append('Observed graphics device differs from the declared hardware cell')
    if expected_cell is not None:
        failures.extend(cell_failures(capture.get('wholeTaskCell'), capture.get('wholeTaskCellAtQuit'), expected_cell, expected_gpu))
    native = None
    if '-pso-native-progressive-control' in command['arguments']:
        if native_count is None:
            failures.append('Native progressive comparison requires an independently declared count')
        else:
            native = native_progressive_summary(load(stage/'capture/native-progressive.json'), native_count)
            failures.extend(native['failures'])
    elif native_count is not None:
        failures.append('Expected native progressive control did not run')
    checkpoints = None
    if '-pso-fixed-checkpoints' in command['arguments']:
        checkpoints = checkpoint_summary(stage/'capture', capture, visual_review)
        failures.extend(checkpoints['failures'])
    elif visual_review is not None:
        failures.append('Visual review was supplied without fixed checkpoint capture')
    intervals = interval_summary(capture['frames'], capture['elapsedSeconds'])
    if intervals['frameGaps']:
        failures.append('Update frame coverage has gaps; do not infer absence within gaps')
    diagnostic_path = stage / 'capture/whole-task-profiler.json'
    diagnostic = recorder_summary(load(diagnostic_path)) if diagnostic_path.exists() else None
    binary = stage / 'capture/whole-task.raw'
    profile_requested = '-pso-whole-task-profile' in command['arguments']
    if expected_cell is not None and capture.get('profilerEnabledDuringUpdates') is not profile_requested:
        failures.append('Actual Unity Profiler enabled state disagrees with diagnostic off/on assignment')
    if profile_requested and (diagnostic is None or not binary.exists() or binary.stat().st_size == 0):
        failures.append('Requested profiler lacks complete recorder/binary evidence')
    if not profile_requested and (diagnostic is not None or binary.exists()):
        failures.append('Unprofiled control contains unexpected diagnostic instrumentation')
    export = None
    if profile_export:
        profile_export = Path(profile_export)
        export = load(profile_export / 'export.json')
        if not binary.exists() or export['inputSha256'] != sha(binary):
            failures.append('Offline profiler export does not match this process raw data')
        mapped = set()
        for line in (profile_export / 'samples.jsonl').open(encoding='utf-8-sig'):
            row = json.loads(line)
            if row.get('kind') == 'frame' and row['unityFrame'] >= 0:
                mapped.add(row['unityFrame'])
        captured = {f['frame'] for f in capture['frames']}
        export = dict(export, metadataMappedFrames=len(mapped),
            updateFramesWithProfileMetadata=len(captured & mapped),
            updateFramesWithoutProfileMetadata=sorted(captured - mapped),
            samplesSha256=sha(profile_export / 'samples.jsonl'))
    launch = datetime.fromisoformat(process['processStartedUtc'].replace('Z', '+00:00'))
    end = datetime.fromisoformat(process['finishedUtc'].replace('Z', '+00:00'))
    return dict(schemaVersion=1, auditedUtc=datetime.now(timezone.utc).isoformat(),
        workload='Official URP 17.1.5 original complete four-scene Timeline route',
        stage=str(stage), contentAccepted=content['contentAccepted'], failures=failures,
        newPerformanceClaimEligible=False, optimizationOpportunity='requires manual causal profiler review',
        unityVersion=capture['unityVersion'], buildGuid=capture['buildGuid'], gpu=capture['gpu'],expectedGpu=expected_gpu,
        wholeTaskCell=capture.get('wholeTaskCell'),nativeProgressive=native,renderCheckpoints=checkpoints,
        command=command, contentValidation=content, allCpuIntervals=intervals,
        processLaunchToMonitorCompletionSeconds=(end - launch).total_seconds(),
        processSpanScope='External monitor completion, not exact presentation or engine shutdown',
        recorder=diagnostic, nativeProfilerExport=export,
        nativeProfilerBinary=dict(bytes=binary.stat().st_size,sha256=sha(binary)) if binary.exists() else None,
        rawIdentities={str(p.relative_to(stage)): sha(p) for p in
                       (capture_path, stage/'player.log', stage/'command.json', stage/'process.json', stage/'stage.json')},
        limitations=['Cache state unknown and retained; repeated processes are not independent cold-cache replicates.',
            'Profiler instrumentation changes the measured application; diagnostic timings are not formal arm samples.',
            'Shader/PSO marker work on a worker does not prove that the main/render critical path waited for it.',
            'First-to-last Update does not cover the entire process startup/shutdown; unsampled boundaries are retained.'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', type=Path)
    parser.add_argument('--expected-unity', required=True, choices=['6000.1.0f1', '6000.5.9f1'])
    parser.add_argument('--profile-export', type=Path)
    parser.add_argument('--expected-gpu', required=True)
    parser.add_argument('--expected-cell', choices=sorted(CELLS))
    parser.add_argument('--native-count', type=int)
    parser.add_argument('--visual-review', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.stage, args.expected_unity, args.profile_export, args.expected_gpu, args.expected_cell, args.native_count, args.visual_review)
    with args.output.open('x', encoding='utf-8') as stream:
        json.dump(result, stream, indent=2, allow_nan=False)
        stream.write('\n')
    print(json.dumps(dict(contentAccepted=result['contentAccepted'], failures=result['failures'],
                         newPerformanceClaimEligible=False, output=str(args.output))))
    raise SystemExit(1 if result['failures'] else 0)
