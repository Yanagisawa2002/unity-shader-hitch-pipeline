"""Backport the official RenderGraph cache disposal fix into a NEW host-local UPM copy.

Does not edit or delete the registry cache. Keeps rendering/caching enabled and
retains all original package files and notices. The old 17.1 cache pool must keep
its object capacity when public Cleanup is followed by reuse.
"""
import argparse
import difflib
import json
from pathlib import Path
import shutil
from prepare_boatattack import ROOT, save, sha

FIX = '5b9c68a3755eefa71ee7af08b3fc5aac473c2985'
PACKAGE = 'com.unity.render-pipelines.core'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('host', type=Path)
    parser.add_argument('receipt', type=Path)
    args = parser.parse_args()
    host, receipt = args.host.resolve(), args.receipt.resolve()
    if not host.is_relative_to(ROOT/'work') or not receipt.is_relative_to(ROOT/'work') or receipt.exists():
        raise ValueError('Use an ignored host and a fresh independent receipt')
    if receipt.is_relative_to(host) or host.is_relative_to(receipt):
        raise ValueError('Receipt must be separate from the host')
    sources = list((host/'Library/PackageCache').glob(PACKAGE+'@*'))
    if len(sources) != 1 or json.loads((sources[0]/'package.json').read_text())['version'] != '17.1.0':
        raise ValueError('Requires exactly the resolved official Core RP 17.1.0')
    source = sources[0]
    destination = host/'Packages'/PACKAGE
    if destination.exists():
        raise ValueError('Never overwrite an existing embedded package')
    graph_path = 'Runtime/RenderGraph/RenderGraph.cs'
    cache_path = 'Runtime/RenderGraph/RenderGraphCompilationCache.cs'
    originals = {p: (source/p).read_bytes() for p in (graph_path, cache_path)}
    expected = {graph_path: '24e446e831897af8f15ea0a1a53c85038f08bbbe83f9269faae231ca079b05a6',
                cache_path: '2b1f8e32009b453ffe1c491155990307d51c8898a95fe2d5bb3e0307896d925b'}
    if any(sha(source/p) != digest for p, digest in expected.items()):
        raise ValueError('The resolved source bytes differ from the reviewed 17.1.0 implementation')
    graph, cache = (originals[p].decode('utf-8').replace('\r\n', '\n') for p in (graph_path, cache_path))
    if graph.count('m_CompilationCache?.Clear();') != 1 or 'public void Cleanup()' in cache:
        raise ValueError('Unexpected RenderGraph cache cleanup implementation')
    if cache.count('class RenderGraphCompilationCache\n{') != 1 or cache.count('    public void Clear()\n') != 1:
        raise ValueError('Unexpected compilation cache source')
    graph = graph.replace('m_CompilationCache?.Clear();', 'm_CompilationCache?.Cleanup();')
    addition = '''    // Backport of Unity Graphics 5b9c68a3755eefa71ee7af08b3fc5aac473c2985.
    // Return active entries first: the 17.1 cache must retain all pool objects
    // across Cleanup/reuse, while disposing their native contents explicitly.
    public void Cleanup()
    {
        Clear();
        foreach (var compiledGraph in m_CompiledGraphPool)
            compiledGraph.Clear();
        foreach (var compiledGraph in m_NativeCompiledGraphPool)
            compiledGraph.Dispose();
    }

'''
    cache = cache.replace('    public void Clear()\n', addition+'    public void Clear()\n')
    entries = [dict(path=p.relative_to(source).as_posix(), bytes=p.stat().st_size, sha256=sha(p))
               for p in sorted(source.rglob('*')) if p.is_file()]
    receipt.mkdir(parents=True)
    save(receipt/'original-package-files.json', entries)
    shutil.copytree(source, destination)
    diff = []
    for relative, modified in ((graph_path, graph), (cache_path, cache)):
        original_file = receipt/'originals'/relative
        original_file.parent.mkdir(parents=True, exist_ok=True)
        original_file.write_bytes(originals[relative])
        (destination/relative).write_text(modified, encoding='utf-8')
        diff.extend(difflib.unified_diff(originals[relative].decode().replace('\r\n','\n').splitlines(True),
                                       modified.splitlines(True), fromfile='official-17.1.0/'+relative, tofile='adapted-17.1.0/'+relative))
    (receipt/'rendergraph-backport.diff').write_text(''.join(diff), encoding='utf-8')
    save(receipt/'repair.json', dict(package=PACKAGE, version='17.1.0 + declared cleanup backport', upstreamFixCommit=FIX,
         upstreamFixUrl='https://github.com/Unity-Technologies/Graphics/commit/'+FIX,
         sourcePath=str(source), preparedPackagePath=str(destination), originalFiles=len(entries),
         originalBytes=sum(e['bytes'] for e in entries), originalIndexSha256=sha(receipt/'original-package-files.json'),
         diffSha256=sha(receipt/'rendergraph-backport.diff'),
         changedFiles=[dict(path=p, originalSha256=sha(source/p), adaptedSha256=sha(destination/p)) for p in originals],
         cacheModified=False, graphicsFeaturesChanged=False, nativeValidation='Pending, source adaptation alone is not a leak fix result'))
    print(json.dumps(load_summary(receipt)))


def load_summary(receipt):
    d=json.loads((receipt/'repair.json').read_text())
    return {k:d[k] for k in ('preparedPackagePath','originalFiles','originalBytes','diffSha256','nativeValidation')}


if __name__ == '__main__':
    main()
