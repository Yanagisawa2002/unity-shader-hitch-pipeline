"""Verify/extract the complete fixed official template and add our declared adapter.

Archive/member hashes precede every overlay. Never overwrites a host or receipt,
downloads dependencies, removes cache data, or launches Unity.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import tarfile
from prepare_boatattack import ROOT, save, sha

ARCHIVE_SHA256 = 'c2a2bcbd8bac1340be683b33ca53e1fddc78c2ae80fa3a0b5406bd616df6628f'
ARCHIVE_SHA1 = 'cbf518aa0f1d9f0d4eb93d94c33a6cb4bf4fe43b'
URL = 'https://download.packages.unity.com/com.unity.template.urp-sample/-/com.unity.template.urp-sample-17.1.5.tgz'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('host', type=Path)
    parser.add_argument('receipt', type=Path)
    parser.add_argument('--editor-version', default='6000.1.0f1', choices=['6000.1.0f1', '6000.5.9f1'])
    parser.add_argument('--build-cell', default='windows-d3d12-v1', choices=['windows-d3d12-v1', 'linux-vulkan-v1'])
    args = parser.parse_args()
    if args.build_cell == 'linux-vulkan-v1' and args.editor_version != '6000.5.9f1':
        raise ValueError('Linux diagnostic preparation requires reviewed Unity 6000.5.9f1')
    archive, host, receipt = (p.resolve() for p in (args.archive,args.host,args.receipt))
    if any(not p.is_relative_to(ROOT/'work') or p.exists() for p in (host,receipt)):
        raise ValueError('Require two new directories inside the repository ignored work directory')
    if host.is_relative_to(receipt) or receipt.is_relative_to(host):
        raise ValueError('Host and evidence directories must be independent')
    adapter = ROOT/'Integrations/Urp3DSample/Adapter'
    if not adapter.is_dir(): raise ValueError('Missing authored adapter')
    if archive.stat().st_size != 924710979 or sha(archive) != ARCHIVE_SHA256:
        raise ValueError('Wrong official template archive')
    with archive.open('rb') as f:
        if hashlib.file_digest(f,'sha1').hexdigest() != ARCHIVE_SHA1: raise ValueError('Registry SHA-1 mismatch')
    host.mkdir(parents=True); receipt.mkdir(parents=True)
    entries, project = [], []
    with tarfile.open(archive, 'r:gz') as tar:
        for member in tar:
            path = PurePosixPath(member.name)
            if path.is_absolute() or '..' in path.parts or ':' in member.name or '\\' in member.name:
                raise ValueError('Unsafe archive path')
            if member.isdir(): continue
            if not member.isfile(): raise ValueError('Unsupported archive link/device')
            prefix = 'package/ProjectData~/'
            relative = member.name[len(prefix):] if member.name.startswith(prefix) else None
            destination = host/relative if relative else receipt/'template-notices'/member.name
            destination.parent.mkdir(parents=True,exist_ok=True)
            h = hashlib.sha256(); size = 0
            with tar.extractfile(member) as source, destination.open('xb') as out:
                while data := source.read(1024*1024): out.write(data); h.update(data); size += len(data)
            if size != member.size: raise ValueError('Truncated archive member')
            item = dict(path=member.name,bytes=size,sha256=h.hexdigest());entries.append(item)
            if relative: project.append(dict(item,path=relative))
    if len(entries) != 3532 or sum(e['bytes'] for e in entries) != 1012172703:
        raise ValueError('Full archive inventory differs from reviewed source')
    save(receipt/'archive-files.json',entries); save(receipt/'original-files.json',project)
    shutil.copytree(host/'ProjectSettings',receipt/'original-settings')
    manifest_path = host/'Packages/manifest.json'
    shutil.copy2(manifest_path,receipt/'manifest-original.json')
    manifest=json.loads(manifest_path.read_text())
    if manifest['dependencies']['com.unity.splines'] != '2.8.0': raise ValueError('Unexpected original Splines dependency')
    manifest['dependencies']['com.unity.splines']='2.8.1' # Official SPLB-345 fix, declared across external hosts.
    linux_packages = {}
    if args.build_cell == 'linux-vulkan-v1':
        # These names/versions come from the selected 6000.5.9f1 Editor's own
        # PackageManager manifest; older *-linux-x86_64 package names are deprecated there.
        linux_packages = {name: '1.1.0' for name in ('com.unity.toolchain.win-x86_64-linux',
                          'com.unity.sysroot.base', 'com.unity.sdk.linux-x86_64')}
        manifest['dependencies'].update(linux_packages)
    package=ROOT/'Packages/com.yanagisawa.shader-hitch-pipeline'
    manifest['dependencies'][package.name]='file:'+Path(os.path.relpath(package,host/'Packages')).as_posix()
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    destination=host/'Assets/PsoUrpSampleAdapter'
    shutil.copytree(adapter,destination)
    for p in (ROOT/'Integrations/ExternalScenes/Adapter').iterdir():
        if p.is_file(): shutil.copy2(p,destination/p.name)
    version=host/'ProjectSettings/ProjectVersion.txt'
    if version.exists(): raise ValueError('Template now includes an unexpected ProjectVersion; review it')
    revision = {'6000.1.0f1': '9ea152932a88', '6000.5.9f1': 'b57deb96f08d'}[args.editor_version]
    selected_editor = f'{args.editor_version} ({revision})'
    version.write_text(f'm_EditorVersion: {args.editor_version}\nm_EditorVersionWithRevision: {selected_editor}\n',encoding='utf-8')
    shutil.copy2(receipt/'template-notices/package/LICENSE.md',host/'UPSTREAM-LICENSE.md')
    save(receipt/'source-overlay.json',dict(utc=datetime.now(timezone.utc).isoformat(),url=URL,
        archiveSha256=ARCHIVE_SHA256,archiveSha1=ARCHIVE_SHA1,archiveBytes=archive.stat().st_size,
        archiveFiles=len(entries),projectFiles=len(project),projectBytes=sum(e['bytes'] for e in project),
        originalProjectIndexSha256=sha(receipt/'original-files.json'),verifiedActualExtractedBytes=True,
        declaredTemplateEngine='6000.0',originalProjectVersionPresent=False,selectedEditor=selected_editor,
        buildCell=args.build_cell,linuxCrossCompilePackages=linux_packages,
        manifestBeforeSha256=sha(receipt/'manifest-original.json'),manifestAfterSha256=sha(manifest_path),
        host=str(host),adapted=True,declaredChanges=['Local package and additive adapter','Official Splines 2.8.1 patch','Explicit installed Editor version; compatibility pending import'],
        adapterFiles=[dict(path=p.relative_to(host).as_posix(),bytes=p.stat().st_size,sha256=sha(p)) for p in sorted(destination.rglob('*')) if p.is_file()]))
    print(json.dumps(dict(host=str(host),projectFiles=len(project),archiveVerified=True,importExecuted=False)))


if __name__=='__main__': main()
