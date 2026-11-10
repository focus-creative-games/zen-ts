"""Verify P5 platform runtime evidence and imported static SDKs."""
from pathlib import Path
import argparse
import datetime
import hashlib
import importlib.util
import json
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PROJECT = None

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()

def require(log, *markers):
    for marker in markers:
        if marker not in log:
            raise RuntimeError('Missing runtime evidence: ' + marker)
    if '[matrix] FAIL' in log or 'TestBootstrap FAILED' in log:
        raise RuntimeError('Runtime failure recorded')

def main():
    global PROJECT
    sys.path.insert(0,str(ROOT/'ZenTS~/v8'))
    import native_sdk
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project',type=Path)
    parser.add_argument('--output',type=Path)
    options=parser.parse_args()
    PROJECT=native_sdk.project_root(options.project)
    output=(options.output or PROJECT/'Library/ZenTS/Verification/p5-verification.json').resolve()
    if output.is_relative_to(ROOT):raise ValueError('Verification reports must be outside the read-only package')
    names = ['p5-editor.log', 'p5-domain-reload.log', 'p5-player.log',
             'p5-macos-editor.log', 'p5-macos-domain-reload.log',
             'p5-macos-player.log', 'p5-macos-x64-player.log',
             'p5-ios-simulator-player.log', 'p5-ios-device-build.log',
             'p5-android-x64-player.log', 'p5-android-arm64-player.log',
             'p5-webgl-build.log', 'p5-webgl-browser.log']
    logs = {name: (PROJECT / name).read_text(encoding='utf-8-sig', errors='replace') for name in names}
    for name in ['p5-editor.log', 'p5-macos-editor.log']:
        require(logs[name], 'Actual Editor engine=V8:13.6.233.17',
                'summary: total=56 passed=56 failed=0',
                'Test summary: total=8 passed=8 failed=0',
                '[ZenTS.TsDemo] ALL OK', 'Editor V8 acceptance ALL OK')
    for name in ['p5-domain-reload.log', 'p5-macos-domain-reload.log']:
        require(logs[name], 'Domain Reload PASS: native runtime count 1 -> 0 -> 1')
    native_players = ['p5-player.log', 'p5-macos-player.log', 'p5-macos-x64-player.log',
                      'p5-ios-simulator-player.log', 'p5-android-x64-player.log',
                      'p5-android-arm64-player.log']
    for name in native_players:
        require(logs[name], 'summary: total=56 passed=56 failed=0',
                'Test summary: total=10 passed=10 failed=0',
                'Il2CppSmokeRunner ALL OK', '[ZenTS.TsDemo] ALL OK')
        if '[SKIP]' in logs[name]:
            raise RuntimeError('Unexpected native skipped case in ' + name)
    require(logs['p5-ios-device-build.log'], '** BUILD SUCCEEDED **')
    require(logs['p5-webgl-build.log'], '[ZenTS][P4] WebGL build PASS')
    require(logs['p5-webgl-browser.log'], 'Player engine=HostJs (platform VM)',
            'summary: total=56 passed=56 failed=0', 'Test summary: total=10 passed=10 failed=0',
            '[ZenTS.TsDemo] ALL OK', 'Browser GC PASS', 'Browser exception/UTF8 boundary PASS')
    sdk_root=PROJECT/'Library/ZenTS/NativeSDK'
    catalog=json.loads((sdk_root/'installed.json').read_text(encoding='utf-8'))
    entries={x['target']:x for x in catalog['entries']}
    sdks = {}
    for target in ['macos-arm64', 'macos-x64', 'ios-arm64', 'ios-simulator-x64',
                   'android-x64', 'android-arm64']:
        entry=entries[target];sdk=native_sdk.safe(sdk_root,entry['relativePath'])
        descriptor=native_sdk.validate(sdk,ROOT)
        if descriptor['target']!=target or descriptor['fingerprint']!=entry['fingerprint']:raise RuntimeError('Catalog SDK identity mismatch')
        manifest=json.loads((sdk/'official-sdk.json').read_text(encoding='utf-8'))
        sdks[target] = {'manifest_sha256': sha(sdk / 'official-sdk.json'),
                        'static_libraries_sha256': manifest['static_libraries_sha256'],
                        'jitless': manifest['jitless'], 'source_commit': manifest['source_commit']}
    for script in ['verify-boundaries.py', 'verify-contract.py']:
        subprocess.run([sys.executable, str(Path(__file__).with_name(script))], check=True)
    apk_artifacts = {}
    for abi, folder, machine in [('x64','x86_64',62),('arm64','arm64-v8a',183)]:
        apk=PROJECT/('Build-Android/ZenTSTest-'+abi+'.apk')
        with zipfile.ZipFile(apk) as archive:
            with archive.open('lib/'+folder+'/libil2cpp.so') as native:
                header=native.read(20)
            if header[:4]!=b'\x7fELF' or int.from_bytes(header[18:20],'little')!=machine:
                raise RuntimeError('APK native architecture mismatch: '+abi)
        apk_artifacts[abi]={'sha256':sha(apk),'native_abi':folder,'elf_machine':machine}
    source_hashes={}
    for folder in ['Runtime','Editor','Plugins/WebGL','ZenTS~/zents-runtime','ZenTS~/v8']:
        for path in (ROOT/folder).rglob('*'):
            relative=path.relative_to(ROOT)
            if path.is_file() and path.suffix in ['.cs','.cpp','.h','.jslib','.mjs','.py'] and not set(relative.parts)&{'sdk','sdks','generated','out'}:
                source_hashes[relative.as_posix()]=sha(path)
    report = {'verified_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'scope': 'P5 native V8 SDKs, Unity integration and applicable runtime tests',
              'editor': {'windows_x64': '8/8 smoke; 56/56 JS; 3/3 TS; Domain Reload',
                         'macos_arm64': '8/8 smoke; 56/56 JS; 3/3 TS; Domain Reload'},
              'players': {name: '10/10 smoke; 56/56 JS; 3/3 TS' for name in native_players},
              'ios_device': {'unsigned_build': 'passed', 'physical_device_runtime': 'not verified'},
              'android_test_device': 'MuMu localhost:7555, Android API 32; arm64 uses emulator translation',
              'webgl': 'host VM; 10/10 smoke; 56/56 JS; 3/3 TS; GC; exception/UTF8',
              'sdks': sdks, 'logs': {name: {'path': str(PROJECT / name), 'sha256': sha(PROJECT / name)} for name in names},
              'apk_artifacts':apk_artifacts,'source_hashes':source_hashes,
              'macos_x64_execution':'Rosetta on Apple M1',
              'macos_node_cli':'TypeScript check exited 0 with PATH=/usr/bin:/bin:/usr/sbin:/sbin',
              'limitations': ['Remote Unity 2022.3.56f1c1 on macOS 26 can hang in Curl Job shutdown after Domain Reload PASS; CI process terminated after assertions; see p5-macos-editor-quit-sample.txt','Physical iOS/Android devices not tested', 'Intel macOS Editor not installed',
                              'Multi-ABI Android APK and macOS universal SDK not included', 'Tuanjie not tested in this environment']}
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('P5 acceptance PASS; physical iOS device execution remains outside verified scope.')

if __name__ == '__main__':
    main()
