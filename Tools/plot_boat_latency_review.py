"""Plot completed official Boat Attack runs; never launch or generate a benchmark.

Usage: python plot_boat_latency_review.py --before COMPARISON_JSON
       [--after COMPARISON_JSON] --output NEW_DIRECTORY
       python plot_boat_latency_review.py --points SOURCE_POINTS_JSON --output NEW_DIRECTORY
Requires Matplotlib. Inputs remain immutable; every displayed point is tied to a
verified validation receipt. Four processes per arm are descriptive repetitions,
not a population confidence interval or a pooled-frame significance claim.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import statistics

ARMS = ['disabled', 'all-at-once', 'scheduled', 'observed-budget']
LABELS = ['Disabled', 'All-at-once', 'Scheduled', 'Observed budget']


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def load_series(path, label):
    path = path.resolve(strict=True)
    data, protocol = read(path), read(path.parent / 'protocol.json')
    assert data['completed'] and not data['unexecuted']
    assert data['protocolSha256'] == sha(path.parent / 'protocol.json')
    freeze = read(path.parent / 'freeze-receipt.json')
    assert freeze['frozenBeforeRuns'] and freeze['protocolSha256'] == data['protocolSha256']
    assert read(path.parent / 'player-after.json')['playerUnchanged']
    runs = data['allSamples']
    assert len(runs) == 16 and {r['index'] for r in runs} == set(range(1, 17))
    directory = {r['index']: r['directory'] for r in protocol['runs']}
    points = []
    for run in runs:
        assert run['accepted'] and run['policy'] in ARMS
        folder = (path.parent / directory[run['index']]).resolve(strict=True)
        assert folder.is_relative_to(path.parent)
        validations = list(folder.glob('*validation*.json'))
        assert len(validations) == 1 and sha(validations[0]) == run['validationSha256']
        validation = read(validations[0])
        assert validation['accepted'] and validation['nativePolicyValidated']
        assert not validation['screenshotsEnabled']
        assert not validation['traceEnabled'] and not validation['persistentAllocationWarnings']
        assert validation['policy'] == run['policy']
        assert validation['buildGuid'] == protocol['buildGuid']
        first = validation['routes'][0]['runs'][0]
        assert first['upstreamWarmup'] and first['expectedRenderFrames'] == 500
        source_p99 = first['cpuFollowingUpdateIntervals']['p99Milliseconds']
        source_max = validation['allUpdateIntervals']['maximumMilliseconds']
        assert math.isclose(source_p99, run['firstPass']['p99Milliseconds'], abs_tol=1e-9)
        assert math.isclose(source_max, run['allUpdates']['maximumMilliseconds'], abs_tol=1e-9)
        assert all(math.isfinite(v) and v >= 0 for v in (source_p99, source_max))
        points.append(dict(revision=label, index=run['index'], policy=run['policy'],
                           firstFlythroughP99Milliseconds=source_p99,
                           wholeRunMaximumMilliseconds=source_max,
                           validationSha256=run['validationSha256']))
    for arm in ARMS:
        group = [p for p in points if p['policy'] == arm]
        assert len(group) == 4
        assert math.isclose(statistics.median(p['firstFlythroughP99Milliseconds'] for p in group),
                            data['byArm'][arm]['medianFirstPassP99'], abs_tol=1e-9)
    return points, protocol, dict(summary=path.parent.name + '/' + path.name,
                                  summarySha256=sha(path), protocolSha256=data['protocolSha256'],
                                  buildGuid=protocol['buildGuid'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    inputs = parser.add_mutually_exclusive_group(required=True)
    inputs.add_argument('--before', type=Path)
    inputs.add_argument('--points', type=Path)
    parser.add_argument('--after', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    labels = ['Before attestation repair', 'After attestation repair']
    series = []
    sources = []
    if args.points:
        assert not args.after, '--after requires --before'
        public = read(args.points)
        assert public['schemaVersion'] == 1 and 1 <= len(public['sources']) <= 2
        sources = public['sources']
        for source in sources:
            for key in ('summarySha256', 'protocolSha256'):
                assert len(source[key]) == 64 and all(c in '0123456789abcdef' for c in source[key])
        assert len(public['points']) == 16 * len(sources)
        for label, color, marker in zip(labels, ['#2563A6', '#A96912'], ['o', 'D']):
            rows = [p for p in public['points'] if p['revision'] == label]
            if not rows and len(sources) == 1 and label == labels[1]:
                continue
            assert len(rows) == 16 and {p['index'] for p in rows} == set(range(1, 17))
            for arm in ARMS:
                assert sum(p['policy'] == arm for p in rows) == 4
            for row in rows:
                assert len(row['validationSha256']) == 64 and all(c in '0123456789abcdef' for c in row['validationSha256'])
                assert all(math.isfinite(row[key]) and row[key] >= 0 for key in ('firstFlythroughP99Milliseconds', 'wholeRunMaximumMilliseconds'))
            series.append((rows, color, marker, label))
    else:
        before, first_protocol, first_source = load_series(args.before, labels[0])
        series = [(before, '#2563A6', 'o', labels[0])]
        sources = [first_source]
    if args.after:
        after, second_protocol, second_source = load_series(args.after, labels[1])
        for key in ('upstreamCommit', 'upstreamTree', 'backend', 'graphics'):
            assert first_protocol[key] == second_protocol[key], 'Unmatched workload: ' + key
        for key in ('unityVersion', 'graphicsDeviceType', 'graphicsDeviceName',
                    'driverIdentity', 'driverVersion', 'qualityLevelName',
                    'renderingThreadingMode', 'processorCount'):
            assert first_protocol['environment'][key] == second_protocol['environment'][key], 'Unmatched environment: ' + key
        series.append((after, '#A96912', 'D', labels[1]))
        sources.append(second_source)
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib.lines import Line2D
    plt.rcParams.update({'font.family': 'DejaVu Sans', 'font.size': 12,
                         'svg.hashsalt': 'boat-external-latency-review-v1'})
    figure, axes = plt.subplots(1, 2, figsize=(13.5, 6.2))
    metrics = [('firstFlythroughP99Milliseconds', 'First flythrough pass: P99', 50),
               ('wholeRunMaximumMilliseconds', 'Entire run: maximum', 100)]
    for axis, (metric, title, floor) in zip(axes, metrics):
        largest = max(p[metric] for rows, *_ in series for p in rows)
        upper = max(floor, math.ceil(largest / 50) * 50)
        for series_index, (rows, color, marker, _) in enumerate(series):
            shift = (series_index - (len(series) - 1) / 2) * .32
            for index, arm in enumerate(ARMS):
                values = [p[metric] for p in rows if p['policy'] == arm]
                x = [index + shift + offset for offset in (-.07, -.023, .023, .07)]
                axis.scatter(x, values, s=42, marker=marker, c=color,
                             edgecolors='white', linewidths=.65, zorder=3)
                median = statistics.median(values)
                axis.hlines(median, index + shift - .11, index + shift + .11,
                            color=color, linewidth=2.2, zorder=4)
        axis.set(title=title, ylabel='CPU Update interval (ms)', ylim=(0, upper), xlim=(-.55, 3.55))
        axis.set_xticks(range(4), LABELS)
        axis.tick_params(axis='x', labelsize=10, pad=8)
        axis.grid(axis='y', color='#E2E5E9', linewidth=.8)
        axis.set_axisbelow(True)
        for side in ('top', 'right'):
            axis.spines[side].set_visible(False)
        for side in ('bottom', 'left'):
            axis.spines[side].set_color('#89929D')
    figure.suptitle('Boat Attack: driver-attestation repair', x=.06, ha='left', fontsize=19, y=.975)
    figure.text(.06, .91, 'Four fresh processes per arm and revision. Dots are runs; short lines are medians.', fontsize=11)
    handles = [Line2D([], [], color=color, marker=marker, linestyle='', markersize=7, label=label)
               for _, color, marker, label in series]
    figure.legend(handles=handles, loc='upper left', bbox_to_anchor=(.054, .88), frameon=False,
                  ncol=len(series), fontsize=11)
    figure.subplots_adjust(left=.075, right=.985, bottom=.23, top=.73, wspace=.25)
    figure.text(.06, .139, 'Left: original first 500-frame flythrough warmup. Right: all Update intervals, including both scene entries.', fontsize=10)
    figure.text(.06, .101, 'Sequential revision cohorts; descriptive observations, without a general policy-speedup conclusion.', fontsize=10)
    figure.text(.06, .063, 'Application / OS / driver caches retained. No trimming. CPU intervals are not GPU completion or presentation time.', fontsize=10)
    figure.text(.06, .025, 'Source protocols: ' + ' / '.join(s['protocolSha256'][:12] for s in sources), fontsize=9, color='#4B5563')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    for suffix in ('png', 'svg'):
        metadata = {'Date': None} if suffix == 'svg' else {}
        figure.savefig(output / ('boat-attack-cpu-intervals.' + suffix), dpi=170, facecolor='white', metadata=metadata)
    plt.close(figure)
    (output / 'source-points.json').write_text(json.dumps(dict(
        schemaVersion=1, sources=sources, points=[p for rows, *_ in series for p in rows],
        matplotlibVersion=matplotlib.__version__,
        scope='Descriptive process-level observations; no significance, driver-cold or general speedup claim.'
    ), indent=2) + '\n', encoding='utf-8')
    print(output)


if __name__ == '__main__':
    main()
