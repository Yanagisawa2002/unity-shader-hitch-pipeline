"""Offline gates for explicitly versioned diagnostic cells. Never launches a Player.

Runtime observations do not authorize reuse of a Linux calibrated cost cache.
Image completeness, visual acceptance, attribution and performance are separate.
"""
import math
from pathlib import Path
import struct
import zlib

from pso_external_capture import load, sha

CELLS = {'windows-d3d12-v1': ('WindowsPlayer', 'Direct3D12'),
         'linux-vulkan-v1': ('LinuxPlayer', 'Vulkan')}
NATIVE_BACKEND = 'unity-native-progressive-control-v1'
ORIGINAL_BACKEND = 'original-route-no-package-warmup-v1'
NATIVE_API = 'UnityEngine.Rendering.GraphicsStateCollection.WarmUpProgressively(int,JobHandle,bool=false)'
PHASES = {'urp-loading', 'urp-terminal', 'urp-garden', 'urp-oasis', 'urp-cockpit'}
SCENES = ('TerminalScene', 'GardenScene', 'OasisScene', 'CockpitScene')
CHECKPOINTS = {f'{scene}-{point}' for scene in SCENES for point in
               ('warmup-1s', 'running-10', 'running-50', 'running-90')}
STABLE_CELL_FIELDS = ('version', 'contract', 'cell', 'unityVersion', 'runtimePlatform',
    'graphicsApi', 'graphicsDeviceName', 'graphicsDeviceVersion', 'graphicsDeviceId',
    'graphicsDeviceVendorId', 'operatingSystem', 'processorType', 'processorCount',
    'renderingThreadingMode', 'backend', 'nativeApiContract', 'buildGuid', 'buildInputSha256',
    'shaderSha256', 'contentSha256', 'linuxCgroup', 'cpuMax', 'cpusetEffective', 'memoryMax', 'nvidiaKernelVersion')


def digest(value):
    return isinstance(value, str) and len(value) == 64 and value != '0' * 64 and all(c in '0123456789abcdefABCDEF' for c in value)


def cell_failures(start, end, expected_cell, expected_gpu=None):
    failures = []
    if expected_cell not in CELLS:
        raise ValueError('Unknown declared diagnostic cell')
    if not isinstance(start, dict) or not isinstance(end, dict):
        return ['Versioned whole-task startup/quit observations missing']
    platform, api = CELLS[expected_cell]
    if (start.get('version'), start.get('contract'), start.get('cell'), start.get('unityVersion'),
        start.get('runtimePlatform'), start.get('graphicsApi')) != (
            1, 'external-runtime-observation-v1', expected_cell, '6000.5.9f1', platform, api):
        failures.append('Whole-task observation version/platform/API/engine differs')
    if expected_gpu is not None and start.get('graphicsDeviceName') != expected_gpu:
        failures.append('Whole-task observed GPU differs from frozen device')
    for field in STABLE_CELL_FIELDS:
        if start.get(field) != end.get(field):
            failures.append('Whole-task environment changed during process: ' + field)
    for field in ('buildInputSha256', 'shaderSha256', 'contentSha256'):
        if not digest(start.get(field)):
            failures.append('Whole-task build/content identity unavailable: ' + field)
    for field in ('graphicsDeviceId', 'graphicsDeviceVendorId', 'processorCount'):
        if not isinstance(start.get(field), int) or start[field] <= 0:
            failures.append('Whole-task numeric identity unavailable: ' + field)
    for field in ('buildGuid', 'graphicsDeviceVersion', 'processorType', 'operatingSystem', 'renderingThreadingMode'):
        if not start.get(field):
            failures.append('Whole-task identity unavailable: ' + field)
    if start.get('backend') not in (NATIVE_BACKEND, ORIGINAL_BACKEND):
        failures.append('Unreviewed diagnostic backend')
    expected_api = NATIVE_API if start.get('backend') == NATIVE_BACKEND else 'none'
    if start.get('nativeApiContract') != expected_api:
        failures.append('Backend and native API contract disagree')
    if platform == 'LinuxPlayer':
        for observed in (start, end):
            if observed.get('observationError'):
                failures.append('Linux runtime observation incomplete: ' + observed['observationError'])
            if observed.get('linuxCgroup') != '0::/':
                failures.append('Effective nested cgroup scope has not been implemented')
            for field in ('cpuMax', 'cpusetEffective', 'memoryMax', 'nvidiaKernelVersion', 'cpuStat'):
                if not observed.get(field): failures.append('Linux limit/driver metadata unavailable: ' + field)
            if observed.get('driverBytesAttested') is not False or observed.get('calibratedLinuxCostReuseSupported') is not False:
                failures.append('Unvalidated Linux observation was promoted to byte identity/cost reuse')
    return failures


def native_progressive_summary(receipt, expected_count):
    failures = []
    if (receipt.get('version'), receipt.get('backend'), receipt.get('apiContract')) != (1, NATIVE_BACKEND, NATIVE_API):
        failures.append('Native progressive receipt has wrong version/backend/API')
    if receipt.get('fixedCountPerSubmission') != expected_count or not 1 <= expected_count <= 65536:
        failures.append('Native progressive count differs from frozen input')
    if not receipt.get('normalQuit') or receipt.get('error') or not receipt.get('allLoadedCollectionsWarmed'):
        failures.append('Native progressive control incomplete/failed')
    phases = receipt.get('phases', [])
    if len(phases) != 5 or {p['phase'] for p in phases} != PHASES:
        failures.append('Native control did not retain the loading and all four scene collections')
    for phase in phases:
        if not phase.get('loaded') or not phase.get('warmed') or phase.get('expectedStates') != phase.get('nativeStates') or not digest(phase.get('collectionSha256')):
            failures.append('Invalid native collection evidence: ' + phase['phase'])
    calls = receipt.get('calls', [])
    if not calls:
        failures.append('Direct WarmUpProgressively API was not exercised; no native-path acceptance')
    if len({c['frame'] for c in calls}) != len(calls):
        failures.append('More than one native submission in one Update')
    previous_end = -1
    for call in calls:
        if call.get('api') != 'WarmUpProgressively' or call.get('state') != 'completed' or call.get('count') != expected_count or call.get('phase') not in PHASES:
            failures.append('Native call API/count/completion differs')
        if call.get('originalWarmupWindow') is not True or call.get('traceCacheMisses') is not False:
            failures.append('Native control changed warmup window or enabled miss tracing')
        begin, end = call['startedSeconds'], call['completedSeconds']
        if not all(math.isfinite(v) for v in (begin, end)) or end < begin or begin < previous_end:
            failures.append('Native call clocks invalid or jobs overlapped')
        if call['completedAfter'] < call['completedBefore']:
            failures.append('Native completed count went backwards')
        previous_end = end
    return dict(accepted=not failures, failures=failures, actualApiCalls=len(calls),
        shutdownFenceCalls=sum(c.get('shutdownFence') is True for c in calls),
        phases=phases, calls=calls, performanceClaimEligible=False)


def png_dimensions(path):
    """Validate Unity RGB/RGBA PNG chunks and decoded row lengths; scene content needs review."""
    with Path(path).open('rb') as stream:
        if stream.read(8) != b'\x89PNG\r\n\x1a\n': raise ValueError('Not a PNG')
        dimensions = None; compressed = bytearray(); channels = None
        while True:
            header = stream.read(8)
            if len(header) != 8: raise ValueError('Truncated PNG chunk')
            length, kind = struct.unpack('>I4s', header)
            if length > 64 * 1024 * 1024: raise ValueError('Unexpected oversized PNG chunk')
            data, crc = stream.read(length), stream.read(4)
            if len(data) != length or len(crc) != 4 or struct.unpack('>I', crc)[0] != zlib.crc32(kind + data):
                raise ValueError('PNG chunk missing or corrupt')
            if dimensions is None:
                if kind != b'IHDR' or length != 13: raise ValueError('PNG header missing')
                dimensions = struct.unpack('>II', data[:8])
                if min(dimensions) < 1 or max(dimensions) > 4096: raise ValueError('Empty/oversized PNG')
                depth, color, compression, filtering, interlace = data[8:]
                if depth != 8 or color not in (2, 6) or (compression, filtering, interlace) != (0, 0, 0):
                    raise ValueError('Unsupported Unity screenshot PNG format')
                channels = 3 if color == 2 else 4
            if kind == b'IDAT':
                compressed.extend(data)
                if len(compressed) > 64 * 1024 * 1024: raise ValueError('Oversized compressed screenshot')
            if kind == b'IEND':
                if length or stream.read(1) or not compressed: raise ValueError('Incomplete/trailing PNG data')
                stride = dimensions[0] * channels + 1
                expected = stride * dimensions[1]
                decoder = zlib.decompressobj()
                try: pixels = decoder.decompress(compressed, expected + 1)
                except zlib.error as error: raise ValueError('PNG pixels failed decompression') from error
                if len(pixels) != expected or not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:
                    raise ValueError('PNG pixels do not match declared dimensions')
                if any(pixels[offset] > 4 for offset in range(0, expected, stride)):
                    raise ValueError('Invalid PNG row filter')
                return dimensions


def checkpoint_summary(capture_directory, capture, review_path=None):
    root = Path(capture_directory)
    receipt = load(root / 'render-checkpoints.json')
    failures = []
    if (receipt.get('version'), receipt.get('contract'), receipt.get('correctnessOnly'), receipt.get('normalQuit')) != (1, 'original-route-checkpoints-v1', True, True):
        failures.append('Checkpoint receipt incomplete or incorrectly labeled')
    checkpoints = receipt.get('checkpoints', [])
    if len(checkpoints) != 16 or {c['key'] for c in checkpoints} != CHECKPOINTS or set(receipt.get('expectedKeys', [])) != CHECKPOINTS:
        failures.append('Missing/duplicate fixed original-route checkpoint')
    images = {}
    for point in checkpoints:
        key = point['key']
        try:
            if key not in CHECKPOINTS or point.get('file') != key + '.png': raise ValueError('Unexpected checkpoint path')
            path = root / point['file']
            if not point.get('written') or point.get('error') or sha(path) != point.get('sha256'): raise ValueError('Image write/hash failed')
            if png_dimensions(path) != (1920, 1080) or (point['width'], point['height']) != (1920, 1080): raise ValueError('Wrong screenshot dimensions')
            if point['capturedFrame'] != point['requestedFrame']: raise ValueError('Readback not from requested Unity frame')
            delta = point['actualTime'] - point['targetTime']
            if point['timelineDuration'] <= 0 or not math.isfinite(point['timelineDuration']): raise ValueError('Invalid checkpoint Timeline duration')
            target = 1 if key.endswith('warmup-1s') else point['timelineDuration'] * int(key.rsplit('-', 1)[1]) / 100
            status = 'Warming' if key.endswith('warmup-1s') else 'Running'
            if not math.isclose(point['targetTime'], target, abs_tol=1e-8) or point['status'] != status:
                raise ValueError('Checkpoint target/status differs from fixed contract')
            if point.get('maximumLatenessSeconds') != .25 or not math.isfinite(delta) or not 0 <= delta <= .25 or not point.get('withinTimeTolerance'):
                raise ValueError('Original route skipped fixed checkpoint tolerance')
            matching = [r for r in capture['renders'] if r['frame'] == point['requestedFrame'] and r['stage'] == point['scene']
                        and r['status'] == point['status'] and r['camera'] == point['camera'] and r['timelineAsset'] == point['timelineAsset']
                        and r['timelineTime'] == point['actualTime'] and r['timelineDuration'] == point['timelineDuration']
                        and r['position'] == point['position'] and r['rotation'] == point['rotation']]
            if len(matching) != 1: raise ValueError('Checkpoint not joined to exactly one original rendered pose')
            images[key] = point['sha256']
        except (OSError, ValueError, KeyError) as error:
            failures.append(key + ': ' + str(error))
    visual = False
    if review_path is not None:
        review = load(review_path)
        visual = (review.get('version') == 1 and bool(review.get('reviewer')) and bool(review.get('method'))
                  and review.get('accepted') is True and review.get('imageSha256ByKey') == images
                  and set(images) == CHECKPOINTS and review.get('captureSha256') == sha(root / 'external-capture.json'))
        if not visual: failures.append('Independent visual review is absent, stale or rejected')
    return dict(captureStructurallyAccepted=not failures, visualAccepted=visual and not failures,
        failures=failures, imageSha256ByKey=images, performanceClaimEligible=False,
        limitation='PNG integrity and exact pose joins do not prove visual equivalence; independent image review is required.')


def normalized_profile_arguments(arguments):
    result = []; i = 0
    evidence = {'-pso-output', '-pso-warmup-receipt', '-logFile'}
    while i < len(arguments):
        value = arguments[i]
        if value in evidence:
            if i + 1 >= len(arguments) or arguments[i + 1].startswith('-'):
                raise ValueError('Missing evidence destination')
            i += 2; continue
        if value != '-pso-whole-task-profile': result.append(value)
        i += 1
    return result


def profiler_pair(off, on):
    """Descriptive matched-process differences, not a causal or low-overhead certification."""
    failures = []
    for item in (off, on):
        if not item.get('contentAccepted') or item.get('failures'):
            failures.append('Incomplete original route or diagnostic audit')
        if item.get('contentValidation', {}).get('screenshotsEnabled'):
            failures.append('Image readback cannot enter a profiler perturbation pair')
    off_args, on_args = off['command']['arguments'], on['command']['arguments']
    if '-pso-whole-task-profile' in off_args or '-pso-whole-task-profile' not in on_args or off.get('recorder') is not None or on.get('recorder') is None:
        failures.append('Profiler off/on assignment differs from actual receipt')
    if normalized_profile_arguments(off_args) != normalized_profile_arguments(on_args):
        failures.append('Workload arguments changed beyond profiler/output destinations')
    for field in ('unityVersion', 'buildGuid', 'gpu'):
        if off.get(field) != on.get(field): failures.append('Profiler pair identity changed: ' + field)
    if not digest(off['command'].get('sha256')) or off['command'].get('sha256') != on['command'].get('sha256'):
        failures.append('Profiler pair Player executable hash missing or changed')
    if off['command'].get('cacheCondition') != on['command'].get('cacheCondition'):
        failures.append('Profiler pair declared cache condition changed')
    off_cell, on_cell = off.get('wholeTaskCell'), on.get('wholeTaskCell')
    if not isinstance(off_cell, dict) or not isinstance(on_cell, dict):
        failures.append('Profiler pair requires explicit versioned runtime observations')
    else:
        for cell in (off_cell, on_cell):
            if cell.get('cell') not in CELLS:
                failures.append('Profiler pair runtime observation cell missing')
            else:
                failures.extend(cell_failures(cell, cell, cell['cell']))
        for field in STABLE_CELL_FIELDS:
            if off_cell.get(field) != on_cell.get(field): failures.append('Profiler pair environment changed: ' + field)
    metrics = ('maximumMilliseconds', 'excessAbove33333Milliseconds', 'observerToFirstUpdateSeconds',
               'lastUpdateToQuitCallbackSeconds', 'observerSeconds')
    differences = {field: on['allCpuIntervals'][field] - off['allCpuIntervals'][field] for field in metrics}
    return dict(pairComparable=not failures, failures=failures, profiledMinusUnprofiled=differences,
        offRawIdentities=off['rawIdentities'], onRawIdentities=on['rawIdentities'],
        lowOverheadCertified=False, performanceClaimEligible=False,
        limitation='Retained-cache process pairs are descriptive. Keep both off/on order directions, all startup/long frames and every failure; no independence or causal overhead claim.')
