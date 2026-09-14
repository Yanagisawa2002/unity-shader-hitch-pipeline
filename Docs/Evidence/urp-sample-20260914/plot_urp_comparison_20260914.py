"""Render all 16 completed URP processes from audited public point data.

This renderer consumes existing results; it never runs Unity or a benchmark.
Local inputs must pass the independent raw audit before points are exported.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from statistics import median

ARMS = ['disabled', 'all-at-once', 'scheduled', 'observed-budget']
SCENES = ['TerminalScene', 'GardenScene', 'OasisScene', 'CockpitScene']


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def export_points(folder):
    from audit_urp_formal_results_20260914 import audit
    audited = audit(folder)
    summary, protocol = read(folder / 'comparison-summary.json'), read(folder / 'protocol.json')
    points = []
    for run in summary['allSamples']:
        verified = next(p for p in audited['runs'] if p['index'] == run['index'])
        points.append(dict(index=run['index'], policy=run['policy'], elapsedSeconds=run['elapsedSeconds'],
                           maximumUpdateMilliseconds=run['allUpdates']['maximumMilliseconds'],
                           observerToFirstUpdateSeconds=verified['observerToFirstUpdateSeconds'],
                           lastUpdateToQuitCallbackSeconds=verified['lastUpdateToQuitCallbackSeconds'],
                           originalWarmupP99Milliseconds={r['stage']: r['passes'][0]['cpuFollowingUpdateIntervals']['p99Milliseconds'] for r in run['routes']},
                           validationSha256=run['validationSha256'], captureSha256=run['captureSha256'],
                           nativeCsvSha256=run['nativeCsvSha256'], playerLogSha256=run['playerLogSha256']))
    data = dict(schema='urp-native-process-points.v1', exportedUtc=datetime.now(timezone.utc).isoformat(),
                protocolSha256=summary['protocolSha256'], summarySha256=hashlib.sha256((folder / 'comparison-summary.json').read_bytes()).hexdigest(),
                buildGuid=protocol['buildGuid'], playerIndexSha256=protocol['playerIndexSha256'],
                sourceInputIndexSha256=protocol['sourceInputIndexSha256'], sourceArchiveSha256=protocol['sourceArchiveSha256'],
                environment={k: protocol['environment'][k] for k in ['unityVersion', 'graphicsDeviceType', 'graphicsDeviceName', 'driverVersion', 'processorCount']},
                processCacheCondition=protocol['processCacheCondition'], points=points,
                scope='Original four full timelines, four fresh processes per arm. CPU observation only; descriptive repetitions with retained caches. No trimming, GPU/presentation claim, driver-cold or useful-coverage claim.')
    return data, dict(verifiedUtc=data['exportedUtc'], **audited)


def validate(points):
    import math
    assert points['schema'] == 'urp-native-process-points.v1'
    rows = points['points']
    assert len(rows) == 16 and {p['index'] for p in rows} == set(range(1, 17))
    assert {p['policy'] for p in rows} == set(ARMS)
    for arm in ARMS:
        assert sum(p['policy'] == arm for p in rows) == 4
    for p in rows:
        assert set(p['originalWarmupP99Milliseconds']) == set(SCENES)
        values = [p['elapsedSeconds'], p['maximumUpdateMilliseconds'], *p['originalWarmupP99Milliseconds'].values()]
        assert all(math.isfinite(v) and v > 0 for v in values)
        for key in ['validationSha256', 'captureSha256', 'nativeCsvSha256', 'playerLogSha256']:
            assert len(p[key]) == 64 and all(c in '0123456789abcdef' for c in p[key])


def render(data, output):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib.ticker import MaxNLocator
    matplotlib.rcParams.update({'font.family': 'DejaVu Sans', 'font.size': 10,
                                 'axes.titlesize': 12, 'axes.labelsize': 10,
                                 'svg.hashsalt': 'urp-official-20260914'})
    rows = data['points']
    fig, axes = plt.subplots(2, 3, figsize=(16, 9))
    fig.patch.set_facecolor('#f8fafc')
    colors = ['#526173', '#2563a6', '#d47b1e', '#287d65']
    offsets = [-0.12, -0.04, 0.04, 0.12]
    labels = ['Disabled', 'All at once', 'Scheduled', 'Observed\nbudget']
    metrics = [(scene.removesuffix('Scene') + ' | original warmup P99',
                'Following-Update interval (ms)', lambda p, s=scene: p['originalWarmupP99Milliseconds'][s]) for scene in SCENES]
    metrics += [('Maximum sampled Update interval', 'Update interval (ms)', lambda p: p['maximumUpdateMilliseconds']),
                ('Observer elapsed time', 'Seconds; axis starts above zero', lambda p: p['elapsedSeconds'])]
    warmup_top = max(p['originalWarmupP99Milliseconds'][s] for p in rows for s in SCENES) * 1.16
    for i, (ax, (title, ylabel, metric)) in enumerate(zip(axes.flat, metrics)):
        ax.set_facecolor('#ffffff')
        for x, arm in enumerate(ARMS):
            group = sorted((p for p in rows if p['policy'] == arm), key=lambda p: p['index'])
            values = [metric(p) for p in group]
            ax.scatter([x + o for o in offsets], values, s=40, color=colors[x], edgecolors='white', linewidths=.7, zorder=3)
            ax.hlines(median(values), x - .23, x + .23, color='#16283d', linewidth=1.8, zorder=4)
        ax.set_title(title, loc='left', pad=12, weight='bold')
        ax.set_ylabel(ylabel)
        ax.set_xticks(range(4), labels)
        ax.set_xlim(-.5, 3.5)
        ax.yaxis.set_major_locator(MaxNLocator(5))
        ax.grid(axis='y', color='#dbe3eb', linewidth=.7, zorder=0)
        for side in ['top', 'right']:
            ax.spines[side].set_visible(False)
        for side in ['bottom', 'left']:
            ax.spines[side].set_color('#c5d0dc')
        if i < 4:
            ax.set_ylim(0, warmup_top)
        elif i == 4:
            ax.set_ylim(0, max(metric(p) for p in rows) * 1.16)
        else:
            values = [metric(p) for p in rows]
            margin = max(.15, (max(values) - min(values)) * .2)
            ax.set_ylim(min(values) - margin, max(values) + margin)
    fig.suptitle('Official URP 3D Sample | all 16 native processes', x=.055, y=.973, ha='left', fontsize=22, weight='bold', color='#16283d')
    fig.text(.055, .929, 'Four original scenes and full timelines · same Player · four processes per policy · each dot is one process, dark line is the median', fontsize=11, color='#44576a')
    fig.text(.055, .044, 'Warmup panels use the original five-second traversal; camera submissions map to the following CPU Update. No sample is removed.', fontsize=10, color='#44576a')
    fig.text(.055, .023, 'Retained application/OS/driver caches. Maxima exclude startup before the first Update and work after the quit callback; observer time includes the startup gap.', fontsize=9, color='#44576a')
    fig.subplots_adjust(left=.055, right=.98, top=.85, bottom=.13, hspace=.48, wspace=.3)
    fig.savefig(output / 'urp-native-processes.png', dpi=180, facecolor=fig.get_facecolor(), metadata={'Software': 'Matplotlib'})
    fig.savefig(output / 'urp-native-processes.svg', facecolor=fig.get_facecolor(), metadata={'Date': None})
    plt.close(fig)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--folder', type=Path)
    source.add_argument('--points', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.folder:
        data, audited = export_points(args.folder)
    else:
        data, audited = read(args.points), None
    validate(data)
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / 'source-points.json').write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
    if audited is not None:
        (args.output / 'independent-audit.json').write_text(json.dumps(audited, indent=2) + '\n', encoding='utf-8')
    render(data, args.output)
    print(json.dumps(dict(output=str(args.output), points=len(data['points']), newNativeRuns=0)))
