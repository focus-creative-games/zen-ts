"""Build unmodified official V8 and export a reproducible static SDK, outside Unity."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request

HERE = Path(__file__).resolve().parent

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()

def git(path, *args):
    return subprocess.check_output(['git', '-c', 'core.fsmonitor=false', '-C', str(path), *args], text=True).strip()

def clean_checkout(path, revision):
    if git(path, 'rev-parse', 'HEAD') != revision:
        raise RuntimeError('Wrong official checkout revision: ' + str(path))
    if git(path, 'status', '--porcelain', '--untracked-files=no'):
        raise RuntimeError('Modified upstream source is not allowed: ' + str(path))

def build_environment(source, depot, out):
    env = os.environ.copy()
    # SDK CRT selection belongs to GN, not to an inherited Unity build override.
    for name in ['CL', '_CL_']:
        env.pop(name, None)
    env.update(DEPOT_TOOLS_WIN_TOOLCHAIN='0', DEPOT_TOOLS_UPDATE='0')
    vswhere = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    vs = subprocess.check_output([str(vswhere), '-latest', '-products', '*', '-property', 'installationPath'], text=True).strip()
    env['vs2022_install'] = vs
    env['GYP_MSVS_OVERRIDE_PATH'] = vs
    env['GYP_MSVS_VERSION'] = '2022'
    for key, value in urllib.request.getproxies().items():
        if key in ['http', 'https']:
            env.setdefault(key.upper() + '_PROXY', value)
    python_bin = out.parent / 'python-bin'
    python_bin.mkdir(parents=True, exist_ok=True)
    (python_bin / 'python3.bat').write_text('@echo off\n"' + sys.executable + '" %*\n', encoding='utf-8')
    env['PATH'] = str(python_bin) + os.pathsep + str(depot) + os.pathsep + env['PATH']
    return env

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--depot-tools', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--export', required=True, type=Path)
    parser.add_argument('--jobs', type=int, default=2)
    parser.add_argument('--configure-only', action='store_true')
    opts = parser.parse_args()
    source, depot, out, export = [x.resolve() for x in [opts.source, opts.depot_tools, opts.out, opts.export]]
    package=Path(__file__).resolve().parents[2]
    if any(p.is_relative_to(package) for p in [source,out,export]):raise RuntimeError('V8 checkout and outputs must be outside the read-only package')
    lock = json.loads((HERE / 'official-source-lock.json').read_text(encoding='utf-8'))
    clean_checkout(source, lock['source_commit'])
    clean_checkout(depot, lock['depot_tools_commit'])
    origin = git(source, 'remote', 'get-url', 'origin').removesuffix('/')
    if origin not in [lock['repository'], lock['official_mirror']]:
        raise RuntimeError('V8 source must come from the official repository/mirror')
    if sha(source / 'DEPS') != lock['deps_sha256'] or lock['upstream_patches']:
        raise RuntimeError('Original official DEPS/source policy mismatch')
    if out == source or out in source.parents or export == source or export in source.parents:
        raise RuntimeError('Output/export cannot replace a source checkout')
    out.mkdir(parents=True, exist_ok=True)
    args = lock['gn_args']
    (out / 'args.gn').write_text('\n'.join(k + ' = ' + (str(v).lower() if isinstance(v, bool) else json.dumps(v))
        for k, v in sorted(args.items())) + '\n', encoding='utf-8')
    env = build_environment(source, depot, out)
    subprocess.run([sys.executable, str(source / 'build/util/lastchange.py'), '-o', str(source / 'build/util/LASTCHANGE')], cwd=source, env=env, check=True)
    gn = source / 'buildtools/win/gn.exe'
    ninja = source / 'third_party/ninja/ninja.exe'
    if not ninja.exists():
        ninja = Path(shutil.which('ninja', path=env['PATH']) or '')
    if not gn.is_file() or not ninja.is_file():
        raise RuntimeError('Official DEPS GN/Ninja dependencies must be synced first')
    commands = [[str(gn), 'gen', str(out), '--fail-on-unused-args', '--script-executable=' + sys.executable],
                [str(ninja), '-C', str(out), '-j', str(opts.jobs), 'v8_monolith']]
    subprocess.run(commands[0], cwd=source, env=env, check=True)
    if opts.configure_only:
        return
    # V8 13.6 emits the optional public config header under gen/include while
    # its internal compile paths include gen. Stage this GENERATED output in
    # that search directory, without editing any upstream source/build files.
    subprocess.run([str(ninja), '-C', str(out), 'gen_v8_gn'], cwd=source, env=env, check=True)
    shutil.copy2(out / 'gen/include/v8-gn.h', out / 'gen/v8-gn.h')
    subprocess.run(commands[1], cwd=source, env=env, check=True)
    clean_checkout(source, lock['source_commit'])
    clean_checkout(depot, lock['depot_tools_commit'])
    library = out / 'obj/v8_monolith.lib'
    generated = out / 'gen/include/v8-gn.h'
    if not library.exists() or not generated.exists():
        raise RuntimeError('Official SDK build output incomplete')
    # Export to a fresh directory, never overlay stale third-party headers/libraries.
    if export.exists() and any(export.iterdir()):
        raise RuntimeError('SDK export directory must be new/empty')
    (export / 'lib').mkdir(parents=True, exist_ok=True)
    shutil.copytree(source / 'include', export / 'include')
    shutil.copy2(generated, export / 'include/v8-gn.h')
    shutil.copy2(library, export / 'lib/v8_monolith.lib')
    (export / 'licenses').mkdir()
    for p in source.glob('LICENSE*'):
        if p.is_file(): shutil.copy2(p, export / 'licenses' / p.name)
    dependencies = []
    for current, dirs, files in os.walk(source):
        base = Path(current)
        if base != source and '.git' in dirs:
            revision = git(base, 'rev-parse', 'HEAD')
            clean_checkout(base, revision)
            dependencies.append({'path': base.relative_to(source).as_posix(), 'commit': revision,
                                 'repository': git(base, 'remote', 'get-url', 'origin')})
        dirs[:] = [d for d in dirs if d not in ['.git', 'out', '.cipd', 'node_modules']]
    clang = source / 'third_party/llvm-build/Release+Asserts/bin/clang-cl.exe'
    manifest = dict(lock)
    manifest.update(built_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), source_pristine=True,
        deps_pristine=True, build_commands=commands, git_dependencies=dependencies,
        clang_version=subprocess.check_output([str(clang), '--version'], text=True).strip(),
        clang_sha256=sha(clang), gn_sha256=sha(gn), ninja_sha256=sha(ninja),
        static_libraries_sha256={'v8_monolith.lib': sha(export / 'lib/v8_monolith.lib')},
        headers_sha256={p.relative_to(export / 'include').as_posix(): sha(p) for p in sorted((export / 'include').rglob('*')) if p.is_file()})
    (export / 'official-sdk.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print('Official V8 SDK built/exported; source and dependencies pristine; no upstream patches.', flush=True)

if __name__ == '__main__':
    main()
