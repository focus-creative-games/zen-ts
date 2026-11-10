"""Build ZenTS adapters from its locally built, pristine official V8 static SDK."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
ADAPTER = ROOT / 'ZenTS~/zents-runtime/engine/v8'

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()

def environment():
    env = os.environ.copy()
    vswhere = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    vs = subprocess.check_output([str(vswhere), '-latest', '-products', '*', '-property', 'installationPath'], text=True).strip()
    setup = Path(vs) / 'VC/Auxiliary/Build/vcvars64.bat'
    result = subprocess.run('cmd.exe /d /s /c ""' + str(setup) + '" >nul && set"', capture_output=True, text=True, check=True)
    for line in result.stdout.splitlines():
        if '=' in line:
            key, value = line.split('=', 1)
            if key.upper() in {'PATH', 'INCLUDE', 'LIB', 'LIBPATH', 'VCTOOLSINSTALLDIR'}:
                env[key] = value
    for key in ['CL', '_CL_']:
        env.pop(key, None)
    return env

def validate_sdk(folder):
    manifest = json.loads((folder / 'official-sdk.json').read_text(encoding='utf-8'))
    policy = json.loads((HERE / 'official-source-lock.json').read_text(encoding='utf-8'))
    if manifest.get('provider') != 'official-v8-source' or manifest.get('source_commit') != policy['source_commit']:
        raise RuntimeError('Only the locked official-source SDK is accepted; third-party SDKs are not supported')
    if manifest.get('upstream_patches') or not manifest.get('source_pristine') or not manifest.get('deps_pristine'):
        raise RuntimeError('Upstream source and dependencies must be pristine')
    for name, digest in manifest['static_libraries_sha256'].items():
        if sha(folder / 'lib' / name) != digest:
            raise RuntimeError('Official static library SHA256 mismatch: ' + name)
    for name, digest in manifest['headers_sha256'].items():
        if sha(folder / 'include' / name) != digest:
            raise RuntimeError('Official header SHA256 mismatch: ' + name)
    return manifest

def main():
    from native_sdk import project_root,prepare
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk',type=Path,help='Pristine official V8 export; read-only input')
    parser.add_argument('--project',type=Path)
    parser.add_argument('--out',type=Path)
    parser.add_argument('--export',type=Path)
    args=parser.parse_args()
    project=project_root(args.project)
    out=(args.out or project/'Library/ZenTS/NativeBuild/windows-x64').resolve()
    export=(args.export or out/'sdk').resolve()
    for path in [out,export]:
        if path.is_relative_to(ROOT) or path==project or path in ROOT.parents:
            raise ValueError('Build outputs must be outside the read-only package and not replace project/source roots')
    source=(args.sdk or export).resolve();lock=validate_sdk(source)
    out.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='build-',dir=out) as temp:
        work=Path(temp);stage=work/'sdk';shutil.copytree(source,stage)
        # Strip prior descriptors; a fresh descriptor binds the rebuilt adapter/plugin.
        (stage/'installation.json').unlink(missing_ok=True)
        engine_source=work/'engine/v8';(engine_source/'sdk').mkdir(parents=True)
        shutil.copytree(stage/'include',engine_source/'sdk/include')
        for name in ['V8Engine.cpp','V8Mono.cpp']:shutil.copy2(ADAPTER/name,engine_source/name)
        shutil.copy2(ADAPTER.parent/'EngineApi.h',engine_source.parent/'EngineApi.h')
        env=environment();tools=Path(env['VCToolsInstallDir'])/'bin/Hostx64/x64'
        common=[str(tools/'cl.exe'),'/nologo','/c','/std:c++20','/Zc:__cplusplus','/EHsc','/MT','/O2','/DNDEBUG','/D_ITERATOR_DEBUG_LEVEL=0','/DV8_GN_HEADER','/DZENTS_V8_ENGINE_IMPLEMENTATION','/I'+str(stage/'include')]
        subprocess.run(common+[str(engine_source/'V8Engine.cpp'),'/Fo'+str(work/'V8Engine.obj')],env=env,check=True,cwd=work)
        engine=stage/'lib/zents_v8_engine.lib'
        subprocess.run([str(tools/'lib.exe'),'/nologo','/OUT:'+str(engine),str(work/'V8Engine.obj')],env=env,check=True,cwd=work)
        subprocess.run(common+['/DZENTS_V8_MONO_PLUGIN',str(engine_source/'V8Mono.cpp'),'/Fo'+str(work/'V8Mono.obj')],env=env,check=True,cwd=work)
        plugin=stage/'plugin/zents_v8.dll';plugin.parent.mkdir(exist_ok=True)
        subprocess.run([str(tools/'link.exe'),'/nologo','/DLL','/OUT:'+str(plugin),'/IMPLIB:'+str(work/'zents_v8.lib'),str(work/'V8Mono.obj'),str(stage/'lib/v8_monolith.lib'),'winmm.lib','dbghelp.lib','shlwapi.lib','advapi32.lib'],env=env,check=True,cwd=work)
        lock.update(target='windows-x64',platform='windows',cpu='x64',jitless=False,
                    static_libraries_sha256={f.name:sha(f) for f in (stage/'lib').glob('*.lib')},
                    editor_plugin_sha256=sha(plugin),
                    adapter_source_sha256={name:hashlib.sha256((ADAPTER.parent/name).read_bytes().replace(b'\r\n',b'\n')).hexdigest() for name in ['EngineApi.h','v8/V8Engine.cpp','v8/V8Mono.cpp']})
        (stage/'official-sdk.json').write_text(json.dumps(lock,indent=2)+'\n',encoding='utf-8')
        prepare(stage,ROOT)
        # Replace only this generated SDK export, never files in the package.
        export.parent.mkdir(parents=True,exist_ok=True)
        retired=work/'previous-sdk'
        if export.exists():export.rename(retired)
        try:stage.rename(export)
        except Exception:
            if retired.exists():retired.rename(export)
            raise
    print('Windows native SDK exported: '+str(export),flush=True)

if __name__=='__main__':main()
