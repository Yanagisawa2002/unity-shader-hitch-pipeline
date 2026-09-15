"""Prepare the declared adapter in the retained hydrated official Megacity host.

No new checkout, cache deletion, source download or scene rewriting. Use the
serial resource stage wrapper. Existing evidence directories are never reused.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
from datetime import datetime, timezone

PIN = '07652ee74a1f322c2c3e607020f07be720175680'
TREE = '7b0bf700ebc01face91223fd7185a02f44ec376e'


def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--host',type=Path,required=True); parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    repo=Path(__file__).resolve().parents[1]
    host,output=args.host.resolve(),args.output.resolve()
    target=host/'Assets/PsoMegacityAcceptanceAdapter'
    if not host.is_relative_to(repo/'work') or output.exists() or target.exists():
        raise ValueError('Require the retained work host and new adapter/evidence directories')
    def git(*args): return subprocess.check_output(['git','-C',str(host),*args])
    if git('rev-parse','HEAD').decode().strip()!=PIN or git('rev-parse','HEAD^{tree}').decode().strip()!=TREE:
        raise ValueError('Official pin/tree mismatch')
    original_manifest=json.loads(git('show',PIN+':Packages/manifest.json'))['dependencies']
    current_manifest=json.loads((host/'Packages/manifest.json').read_text(encoding='utf-8-sig'))['dependencies']
    if len(original_manifest)!=70 or any(current_manifest.get(k)!=v for k,v in original_manifest.items()):
        raise ValueError('Original 70 requested versions changed')
    extras=set(current_manifest)-set(original_manifest)
    if extras!={'com.yanagisawa.shader-hitch-pipeline','com.yanagisawa.shader-hitch-native-scenes'}:
        raise ValueError('Unexpected manifest overlay')
    original_lock=json.loads(git('show',PIN+':Packages/packages-lock.json'))['dependencies']
    current_lock=json.loads((host/'Packages/packages-lock.json').read_text(encoding='utf-8-sig'))['dependencies']
    if len(original_lock)!=91 or any(current_lock.get(k)!=v for k,v in original_lock.items()):
        raise ValueError('Retained original 91-entry lock differs before the new declared Core backport')
    scenes=['Assets/Scenes/Menu.unity','Assets/Scenes/Main.unity']+[
        'Assets/Scenes/Main/'+s+'.unity' for s in ('Blimps','Common','Level','MegacityMetroLevelBounds','Player_Subscene','Traffic')]
    checked=[]
    for relative in scenes+[s+'.meta' for s in scenes]+[
        'ProjectSettings/EditorBuildSettings.asset','ProjectSettings/QualitySettings.asset',
        'ProjectSettings/GraphicsSettings.asset','ProjectSettings/NetCodeClientSettings.asset','ProjectSettings/ProjectVersion.txt']:
        original=git('show',PIN+':'+relative)
        actual=host/relative
        if original.startswith(b'version https://git-lfs.github.com/spec/v1\n'):
            pointer=dict(line.decode().split(' ',1) for line in original.splitlines())
            expected=pointer['oid'].removeprefix('sha256:')
            if actual.stat().st_size!=int(pointer['size']) or digest(actual)!=expected:
                raise ValueError('Hydrated original scene mismatch: '+relative)
        else:
            expected=hashlib.sha256(original).hexdigest()
            if actual.read_bytes().replace(b'\r\n',b'\n')!=original.replace(b'\r\n',b'\n'):
                raise ValueError('Original route/configuration mismatch: '+relative)
        checked.append(dict(path=relative,bytes=actual.stat().st_size,sha256=digest(actual),originalGitOrLfsSha256=expected))
    output.mkdir(parents=True)
    for name in ('manifest.json','packages-lock.json'):
        shutil.copy2(host/'Packages'/name,output/name)
    source=repo/'Integrations/MegacityMetroNative/AcceptanceAdapter'
    shutil.copytree(source,target)
    shutil.copy2(Path(str(source)+'.meta'),Path(str(target)+'.meta'))
    # Compile the shared real lifecycle bridge inside the host adapter assembly.
    # Its authoritative source remains shared with the external-scene adapters.
    shared=repo/'Integrations/ExternalScenes/Adapter'
    for name in ('PsoExternalPhaseBridge.cs','PsoExternalPhaseBridge.cs.meta'):
        shutil.copy2(shared/name,target/name)
    files=[dict(path=str(p.relative_to(target)).replace('\\','/'),bytes=p.stat().st_size,sha256=digest(p))
           for p in sorted(target.rglob('*')) if p.is_file()]
    receipt=dict(schemaVersion=1,utc=datetime.now(timezone.utc).isoformat(),upstreamCommit=PIN,upstreamTree=TREE,
        host=str(host),existingHostReused=True,fullCheckoutReaudited=False,
        scope='Targeted original route/settings/LFS payload checks before the declared source adaptation; prior full 3820-file audits remain separately identified.',
        originalManifestEntries=70,originalLockEntries=91,checkedOriginalInputs=checked,adapterFiles=files,
        entry='Shared ordinary application API after original rendered initialized Menu; explicit opt-in, no input simulation/reflection/runtime batchmode.',
        sourcePatchApplied=False,renderGraphBackportApplied=False,runtimeValidated=False)
    (output/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(checkedOriginalInputs=len(checked),adapterFiles=len(files),receipt=str(output/'receipt.json'))))


if __name__=='__main__': main()
