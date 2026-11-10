"""Build pinned official V8 static SDKs for macOS/iOS/Android on macOS or Linux.
No upstream sources are edited. SDK and adapter are built outside Unity.
"""
import argparse,datetime,hashlib,json,os,platform,shutil,subprocess,sys
from pathlib import Path
HERE=Path(__file__).resolve().parent

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def source_sha(p):return hashlib.sha256(p.read_bytes().replace(b"\r\n",b"\n")).hexdigest()
def run(args,**kw):subprocess.run([str(x) for x in args],check=True,**kw)
def capture(args,**kw):return subprocess.check_output([str(x) for x in args],text=True,**kw).strip()
def main():
    ap=argparse.ArgumentParser(description=__doc__)
    for n in ['source','out','export','package']:ap.add_argument('--'+n,type=Path,required=True)
    ap.add_argument('--target',choices=['macos','ios','ios-simulator','android'],required=True)
    ap.add_argument('--cpu',choices=['arm64','x64'],required=True);ap.add_argument('--ndk',type=Path);ap.add_argument('--jobs',type=int,default=2);ap.add_argument('--configure-only',action='store_true');ap.add_argument('--adapter-only',action='store_true');a=ap.parse_args()
    if a.target=='android' and platform.system()!='Linux':raise RuntimeError('This pinned upstream V8 supports Android builds on Linux only')
    source,out,export,pkg=[getattr(a,n).resolve() for n in ['source','out','export','package']]
    if source.is_relative_to(pkg) or out.is_relative_to(pkg) or export.is_relative_to(pkg):raise RuntimeError('Build outputs must be outside the read-only package')
    lock=json.loads((pkg/'ZenTS~/v8/official-source-lock.json').read_text(encoding='utf-8'))
    env=os.environ.copy();env['PATH']='/opt/homebrew/bin:/usr/local/bin:'+env.get('PATH','');env['DEPOT_TOOLS_UPDATE']='0'
    actual=capture(['git','-c','core.fsmonitor=false','-C',source,'rev-parse','HEAD'],env=env)
    if actual!=lock['source_commit'] or sha(source/'DEPS')!=lock['deps_sha256']:raise RuntimeError('Official V8 source lock mismatch')
    origin=capture(['git','-c','core.fsmonitor=false','-C',source,'remote','get-url','origin'],env=env).rstrip('/')
    if origin not in [lock['repository'],lock['official_mirror']]:raise RuntimeError('Official V8 origin required')
    # Compare contents; cross-platform index stat caches/file modes are not source edits.
    if capture(['git','-c','core.fsmonitor=false','-c','core.fileMode=false','-C',source,'diff','HEAD','--no-ext-diff','--ignore-submodules=none'],env=env):raise RuntimeError('Upstream V8 source is modified')
    args=dict(lock['gn_args']);args.pop('target_os');args.pop('target_cpu');args.update(target_cpu=a.cpu,target_os='ios' if a.target.startswith('ios') else 'mac' if a.target=='macos' else 'android',clang_use_chrome_plugins=False,treat_warnings_as_errors=False,use_custom_libcxx_for_host=False)
    args['clang_base_path']=str(source/'third_party/llvm-build/Release+Asserts')
    if a.target.startswith('ios'):args.update(v8_jitless=True,v8_enable_turbofan=False,v8_enable_sparkplug=False,ios_enable_code_signing=False,ios_deployment_target='13.0',target_environment='simulator' if a.target=='ios-simulator' else 'device')
    if a.target=='macos':args['mac_deployment_target']='11.0'
    if a.target=='android':
        if not a.ndk:raise RuntimeError('--ndk is required for Android')
        args.update(android_ndk_root=str(a.ndk.resolve()),android_ndk_version='r23b',android64_ndk_api_level=21,use_sysroot=False,use_custom_libcxx=True,use_custom_libcxx_for_host=True,use_relative_vtables_abi=False)
    out.mkdir(parents=True,exist_ok=True)
    gn=source/'buildtools/mac/gn' if platform.system()=='Darwin' else source/'buildtools/linux64/gn'
    ninja=source/'third_party/ninja/ninja'
    if not a.adapter_only:
        (out/'args.gn').write_text('\n'.join(k+' = '+(str(v).lower() if isinstance(v,bool) else json.dumps(v)) for k,v in sorted(args.items()))+'\n')
        run([sys.executable,source/'build/util/lastchange.py','-o',source/'build/util/LASTCHANGE'],cwd=source,env=env)
        run([gn,'gen',out,'--fail-on-unused-args','--script-executable='+sys.executable],cwd=source,env=env)
        if a.configure_only:return
        targets=capture([ninja,'-C',out,'-t','targets','all'],env=env)
        headers=[line.rsplit(': ',1)[0] for line in targets.splitlines() if line.rsplit(': ',1)[0].endswith('include/v8-gn.h')]
        run([ninja,'-C',out,*headers],env=env)
        for header in headers:
            generated=out/header;shutil.copy2(generated,generated.parent.parent/'v8-gn.h')
        run([ninja,'-C',out,'-j',a.jobs,'v8_monolith'],env=env)
    dependencies=[]
    for base,dirs,files in os.walk(source):
        path=Path(base)
        if path!=source and '.git' in dirs:
            if capture(['git','-c','core.fsmonitor=false','-c','core.fileMode=false','-C',path,'diff','HEAD','--no-ext-diff','--ignore-submodules=none'],env=env):raise RuntimeError('Modified upstream dependency: '+str(path))
            dependencies.append({'path':path.relative_to(source).as_posix(),'commit':capture(['git','-c','core.fsmonitor=false','-C',path,'rev-parse','HEAD'],env=env)})
        dirs[:]=[d for d in dirs if d not in ['.git','out','llvm-build','node_modules','.cipd']]
    if capture(['git','-c','core.fsmonitor=false','-c','core.fileMode=false','-C',source,'diff','HEAD','--no-ext-diff','--ignore-submodules=none'],env=env):raise RuntimeError('V8 source changed during build')
    if a.adapter_only:
        recorded=json.loads((export/'official-sdk.json').read_text(encoding='utf-8'))
        if recorded.get('source_commit')!=lock['source_commit'] or recorded.get('target')!=a.target+'-'+a.cpu or recorded.get('upstream_patches') or not recorded.get('source_pristine') or not recorded.get('deps_pristine'):
            raise RuntimeError('Only a matching pristine official SDK can be reused')
        if recorded.get('gn_args')!=args:raise RuntimeError('Existing SDK GN configuration differs; rebuild V8 first')
        if sha(export/'lib/libv8_monolith.a')!=recorded['static_libraries_sha256']['libv8_monolith.a']:
            raise RuntimeError('Original V8 static library hash mismatch')
        for name,digest in recorded['headers_sha256'].items():
            if sha(export/'include'/name)!=digest:raise RuntimeError('Original V8 header hash mismatch: '+name)
    else:
        library=out/'obj/libv8_monolith.a'
        export.mkdir(parents=True,exist_ok=True);(export/'lib').mkdir(exist_ok=True)
        shutil.copytree(source/'include',export/'include',dirs_exist_ok=True);shutil.copy2(out/'gen/include/v8-gn.h',export/'include/v8-gn.h');shutil.copy2(library,export/'lib/libv8_monolith.a')
    clang=source/'third_party/llvm-build/Release+Asserts/bin/clang++'
    stage=out/'zents-adapter';(stage/'sdk').mkdir(parents=True,exist_ok=True)
    shutil.copytree(export/'include',stage/'sdk/include',dirs_exist_ok=True)
    for name in ['V8Engine.cpp','V8Mono.cpp']:shutil.copy2(pkg/'ZenTS~/zents-runtime/engine/v8'/name,stage/name)
    # Match the relative ../EngineApi.h include without changing adapter sources.
    shutil.copy2(pkg/'ZenTS~/zents-runtime/engine/EngineApi.h',out/'EngineApi.h')
    common=['-std=c++20','-O2','-DNDEBUG','-DV8_GN_HEADER','-DZENTS_V8_ENGINE_IMPLEMENTATION','-fvisibility=hidden','-fPIC','-I'+str(stage/'sdk/include')]
    if a.target=='android':
        triple='aarch64' if a.cpu=='arm64' else 'x86_64'
        sysroot=a.ndk/('toolchains/llvm/prebuilt/'+('darwin-x86_64' if platform.system()=='Darwin' else 'linux-x86_64')+'/sysroot')
        common+=['--target='+triple+'-linux-android21','--sysroot='+str(sysroot),'-nostdinc++','-isystem'+str(source/'third_party/libc++/src/include'),'-isystem'+str(source/'third_party/libc++abi/src/include'),'-I'+str(source/'buildtools/third_party/libc++'),'-D_LIBCPP_HARDENING_MODE=_LIBCPP_HARDENING_MODE_EXTENSIVE','-D_LIBCPP_DISABLE_VISIBILITY_ANNOTATIONS','-D_LIBCXXABI_DISABLE_VISIBILITY_ANNOTATIONS']
    else:
        sdk='macosx' if a.target=='macos' else 'iphonesimulator' if a.target=='ios-simulator' else 'iphoneos'
        sdkpath=capture(['xcrun','--sdk',sdk,'--show-sdk-path'])
        triple=('arm64' if a.cpu=='arm64' else 'x86_64')+('-apple-macos11.0' if a.target=='macos' else '-apple-ios13.0'+('-simulator' if a.target=='ios-simulator' else ''))
        common+=['-target',triple,'-isysroot',sdkpath]
    if a.target.startswith('ios'):common+=['-DZENTS_V8_JITLESS']
    obj=stage/'V8Engine.o';run([clang,*common,'-c',stage/'V8Engine.cpp','-o',obj],env=env)
    ar=source/'third_party/llvm-build/Release+Asserts/bin/llvm-ar'
    adapter_objects=[obj]
    if a.target=='android':
        run([ninja,'-C',out,'-j',a.jobs,'phony/buildtools/third_party/libc++/libc++'],env=env)
        # Unversioned std::logic_error/runtime_error are supplied by Unity's NDK libc++.
        bridge=stage/'AndroidLibcxxBridge.cpp'
        shutil.copy2(pkg/'ZenTS~/zents-runtime/engine/v8/AndroidLibcxxBridge.cpp',bridge)
        bridge_obj=stage/'AndroidLibcxxBridge.o'
        run([clang,*common,'-DZENTS_V8_ANDROID_LIBCXX_BRIDGE','-c',bridge,'-o',bridge_obj],env=env)
        adapter_objects.append(bridge_obj)
        adapter_objects+=sorted(p for p in (out/'obj/buildtools/third_party/libc++/libc++').glob('*.o') if p.name!='stdexcept.o')
        if len(adapter_objects)==1:raise RuntimeError('Missing official Android libc++ objects')
    (export/'lib/libzents_v8_engine.a').unlink(missing_ok=True)
    run([ar,'rcs',export/'lib/libzents_v8_engine.a',*adapter_objects],env=env)
    if a.target=='macos':
        mono=stage/'V8Mono.o';run([clang,*common,'-DZENTS_V8_MONO_PLUGIN','-DZENTS_ENGINE_V8','-c',stage/'V8Mono.cpp','-o',mono],env=env)
        (export/'plugin').mkdir(exist_ok=True)
        run([clang,*common,'-dynamiclib',mono,export/'lib/libv8_monolith.a','-o',export/'plugin/libzents_v8.dylib','-Wl,-exported_symbol,_ze_*','-Wl,-install_name,@rpath/libzents_v8.dylib','-framework','Foundation','-framework','CoreFoundation','-framework','Security','-pthread'],env=env)
    if a.target=='macos':
        helper=pkg/'Editor/3rds/UnityHook/Plugins/Utils.cpp'
        run([clang,*common,'-fvisibility=default','-dynamiclib',helper,'-o',export/'plugin/libMonoHookUtils_OSX.dylib','-Wl,-install_name,@rpath/libMonoHookUtils_OSX.dylib'],env=env)
    (export/'licenses').mkdir(exist_ok=True)
    for f in source.glob('LICENSE*'):
        if f.is_file():shutil.copy2(f,export/'licenses'/f.name)
    for dependency in dependencies:
        base=source/dependency['path']
        for pattern in ['LICENSE*','COPYING*','NOTICE*','COPYRIGHT*']:
            for file in base.glob(pattern):
                if file.is_file():
                    destination=export/'licenses'/dependency['path']/file.name
                    destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(file,destination)
    manifest=dict(lock);manifest.update(git_dependencies=dependencies,gn_sha256=sha(gn),ninja_sha256=sha(ninja),clang_sha256=sha(clang),target=a.target+'-'+a.cpu,cpu=a.cpu,platform=a.target,gn_args=args,jitless=a.target.startswith('ios'),source_pristine=True,deps_pristine=True,upstream_patches=[],clang_version=capture([clang,'--version']),built_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),static_libraries_sha256={p.name:sha(p) for p in (export/'lib').glob('*.a')},headers_sha256={p.relative_to(export/'include').as_posix():sha(p) for p in (export/'include').rglob('*') if p.is_file()})
    manifest['adapter_source_sha256']={name:source_sha(pkg/'ZenTS~/zents-runtime/engine'/name) for name in ['EngineApi.h','v8/V8Engine.cpp']}
    if a.target=='android':
        manifest['adapter_source_sha256']['v8/AndroidLibcxxBridge.cpp']=source_sha(pkg/'ZenTS~/zents-runtime/engine/v8/AndroidLibcxxBridge.cpp')
        manifest['android_sdk_api_level']=21
        manifest['android_standard_vtable_abi']=not args['use_relative_vtables_abi']
        manifest['android_cxx_runtime']='official DEPS libc++ in __Cr namespace; NDK exception ABI with owned string bridge'
    if a.target=='macos':
        manifest['adapter_source_sha256']['v8/V8Mono.cpp']=source_sha(pkg/'ZenTS~/zents-runtime/engine/v8/V8Mono.cpp')
        manifest['editor_plugin_sha256']=sha(export/'plugin/libzents_v8.dylib')
        manifest['monohook_plugin_sha256']=sha(export/'plugin/libMonoHookUtils_OSX.dylib')
        manifest['monohook_source_sha256']=hashlib.sha256((pkg/'Editor/3rds/UnityHook/Plugins/Utils.cpp').read_bytes().replace(b'\r\n',b'\n')).hexdigest()
    (export/'official-sdk.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    sys.path.insert(0,str(pkg/'ZenTS~/v8'))
    from native_sdk import prepare
    prepare(export,pkg)
    print('P5 static SDK complete:',a.target,a.cpu,flush=True)
if __name__=='__main__':main()
