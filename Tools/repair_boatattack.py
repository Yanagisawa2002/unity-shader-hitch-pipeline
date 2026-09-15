"""Apply exact, declared repairs to the pinned Boat Attack host, retaining originals."""
import argparse
import difflib
import hashlib
import json
from pathlib import Path
from prepare_boatattack import COMMIT, ROOT, git, save, sha


def repaired_texts(originals):
    edits = {
        'Assets/Scripts/System/PerfomanceStats.cs': (
            'if (mode == PerfMode.DisplayOnly)\n',
            '// The suite owns its render-frame counter; display-only UI must not advance it.\n'
            '        if (mode == PerfMode.DisplayOnly && Benchmark.Current == null)\n'),
        'Assets/scenes/Testing/benchmark_island-static.unity': (
            '  cameras:\n  - type: 0\n    camera: {fileID: 0}\n',
            '  cameras:\n  - type: 0\n    camera: {fileID: 1725543954}\n'),
        'Assets/Shaders/UI/UIPanelFade.shader': (
            '    Properties\n    {\n',
            '    Properties\n    {\n'
            '        // Required by the original uGUI Image; the procedural fragment is unchanged.\n'
            '        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}\n'),
    }
    result = {}
    for path, (before, after) in edits.items():
        text = originals[path].decode('utf-8').replace('\r\n', '\n')
        if text.count(before) != 1:
            raise ValueError('Repair context must match exactly once: ' + path)
        result[path] = text.replace(before, after).encode('utf-8')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('host', type=Path)
    parser.add_argument('receipt', type=Path)
    args = parser.parse_args()
    host, output = args.host.resolve(), args.receipt.resolve()
    if not host.is_relative_to(ROOT/'work') or not output.is_relative_to(ROOT/'work') or output.exists():
        raise ValueError('Use the ignored work host and a new receipt directory')
    if git(host, 'rev-parse', 'HEAD').decode().strip() != COMMIT:
        raise ValueError('Unexpected upstream commit')
    paths = ['Assets/Scripts/System/PerfomanceStats.cs',
             'Assets/scenes/Testing/benchmark_island-static.unity', 'Assets/Shaders/UI/UIPanelFade.shader']
    originals = {path: (host/path).read_bytes() for path in paths}
    for path, content in originals.items():
        if content.replace(b'\r\n', b'\n') != git(host, 'show', COMMIT+':'+path).replace(b'\r\n', b'\n'):
            raise ValueError('Refuse to overwrite existing host edits: '+path)
    edits = repaired_texts(originals)
    manifest_path = host/'Packages/manifest.json'
    original_manifest = manifest_path.read_bytes()
    manifest = json.loads(original_manifest)
    if manifest['dependencies'].get('com.unity.splines', '2.8.0') != '2.8.0':
        raise ValueError('Unexpected spline dependency')
    manifest['dependencies']['com.unity.splines'] = '2.8.1'
    output.mkdir(parents=True)
    receipt = dict(upstreamCommit=COMMIT, changes=[], dependencyRepair=dict(
        package='com.unity.splines', before='2.8.0 (resolved transitive)', after='2.8.1 (explicit registry pin)',
        reason='Official SPLB-345 IL2CPP finalizer fix; all subsequent arms share this dependency cell',
        sourceArchiveSha256='508a56084fbaec3390f27d9903b84af07abf32d824317a261e6eb22e6ec7f248'))
    for path, content in edits.items():
        original = output/'originals'/path
        original.parent.mkdir(parents=True, exist_ok=True)
        original.write_bytes(originals[path])
        (host/path).write_bytes(content)
        receipt['changes'].append(dict(path=path, beforeSha256=sha(original), afterSha256=sha(host/path)))
    (output/'manifest-before.json').write_bytes(original_manifest)
    manifest_path.write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    (output/'source-repairs.diff').write_text(''.join(''.join(difflib.unified_diff(
        originals[path].decode().splitlines(True), content.decode().splitlines(True),
        fromfile='upstream/'+path, tofile='adapted/'+path)) for path, content in edits.items()), encoding='utf-8')
    save(output/'repairs.json', receipt)
    print(json.dumps(receipt, indent=2))


if __name__ == '__main__':
    main()
