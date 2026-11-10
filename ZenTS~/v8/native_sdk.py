"""Native SDK descriptors and project-local immutable installation. Never writes the package."""
import contextlib,hashlib,json,os,shutil,tempfile,uuid
from pathlib import Path

ABI=1
TARGETS={'windows-x64','macos-arm64','macos-x64','ios-arm64','ios-simulator-x64','ios-simulator-arm64','android-arm64','android-x64'}
def sha(path):
 h=hashlib.sha256()
 with Path(path).open('rb') as f:
  for data in iter(lambda:f.read(1024*1024),b''):h.update(data)
 return h.hexdigest()
def source_sha(path):
 return hashlib.sha256(Path(path).read_bytes().replace(b"\r\n",b"\n")).hexdigest()

def project_root(value=None):
 if value is not None:
  root=Path(value).resolve()
  if not (root/'Assets').is_dir() or not (root/'ProjectSettings').is_dir():raise ValueError('Not a Unity project: '+str(root))
  return root
 for root in [Path.cwd().resolve(),*Path.cwd().resolve().parents]:
  if (root/'Assets').is_dir() and (root/'ProjectSettings').is_dir():return root
 raise ValueError('Pass --project <Unity project root>')
def fingerprint(target,commit,files):
 value='zents-native-sdk-v1\n'+target+'\n'+commit+'\n'+''.join(x['path']+'\0'+x['sha256']+'\n' for x in files)
 return hashlib.sha256(value.encode('utf-8')).hexdigest()
def safe(root,relative):
 path=(root/relative).resolve()
 if not path.is_relative_to(root.resolve()) or path==root.resolve():raise ValueError('Unsafe SDK path: '+relative)
 return path

def validate_manifest(sdk,package):
 sdk=Path(sdk).resolve();package=Path(package).resolve()
 m=json.loads((sdk/'official-sdk.json').read_text(encoding='utf-8'))
 lock=json.loads((package/'ZenTS~/v8/official-source-lock.json').read_text(encoding='utf-8'))
 target=m.get('target')
 if target not in TARGETS:raise ValueError('Unsupported SDK target: '+str(target))
 if m.get('provider')!='official-v8-source' or m.get('source_commit')!=lock['source_commit'] or not m.get('source_pristine') or not m.get('deps_pristine') or m.get('upstream_patches'):raise ValueError('Locked pristine official V8 SDK required')
 if target.startswith('ios') and not m.get('jitless'):raise ValueError('iOS requires JITless V8')
 if target.startswith('android') and (not m.get('android_standard_vtable_abi') or m.get('gn_args',{}).get('use_relative_vtables_abi') is not False or m.get('android_sdk_api_level')!=21):raise ValueError('Android standard vtable ABI and API 21 required')
 libraries=['v8_monolith.lib','zents_v8_engine.lib'] if target=='windows-x64' else ['libv8_monolith.a','libzents_v8_engine.a']
 for name in libraries:
  if sha(sdk/'lib'/name)!=m['static_libraries_sha256'][name]:raise ValueError('Library hash mismatch: '+name)
 for name,digest in m['headers_sha256'].items():
  if sha(safe(sdk/'include',name))!=digest:raise ValueError('Header hash mismatch: '+name)
 sources=[]
 for name,digest in m.get('adapter_source_sha256',{}).items():
  relative='ZenTS~/zents-runtime/engine/'+name
  if digest not in (sha(safe(package,relative)),source_sha(safe(package,relative))):raise ValueError('Adapter must be rebuilt for this package: '+relative)
  sources.append({'path':relative,'sha256':source_sha(safe(package,relative))})
 if not sources:raise ValueError('SDK is missing adapter source identity; rebuild with the current tools')
 if target=='windows-x64' or target.startswith('macos'):
  plugin=sdk/'plugin'/('zents_v8.dll' if target=='windows-x64' else 'libzents_v8.dylib')
  if sha(plugin)!=m['editor_plugin_sha256']:raise ValueError('Editor plugin hash mismatch')
  if target.startswith('macos'):
   helper=sdk/'plugin/libMonoHookUtils_OSX.dylib'
   if sha(helper)!=m['monohook_plugin_sha256']:raise ValueError('MonoHook helper hash mismatch')
   relative='Editor/3rds/UnityHook/Plugins/Utils.cpp'
   if m['monohook_source_sha256'] not in (sha(package/relative),source_sha(package/relative)):raise ValueError('MonoHook helper source mismatch')
   sources.append({'path':relative,'sha256':source_sha(package/relative)})
 return m,sources

def prepare(sdk,package):
 sdk=Path(sdk).resolve()
 if sdk.is_relative_to(Path(package).resolve()):raise ValueError('SDK descriptors must be generated outside the read-only package')
 m,sources=validate_manifest(sdk,package)
 files=[]
 for p in sorted(sdk.rglob('*')):
  if p.is_symlink():raise ValueError('SDK symlinks are not supported: '+str(p))
  if p.is_file() and p.name!='installation.json':files.append({'path':p.relative_to(sdk).as_posix(),'sha256':sha(p)})
 files.sort(key=lambda x:x['path'])
 d={'schema':1,'abi':ABI,'target':m['target'],'sourceCommit':m['source_commit'],'fingerprint':fingerprint(m['target'],m['source_commit'],files),'files':files,'sources':sources}
 (sdk/'installation.json').write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')
 return d

def validate(sdk,package):
 sdk=Path(sdk).resolve();m,_=validate_manifest(sdk,package)
 d=json.loads((sdk/'installation.json').read_text(encoding='utf-8'))
 if d.get('schema')!=1 or d.get('abi')!=ABI or d.get('target')!=m['target'] or d.get('sourceCommit')!=m['source_commit']:raise ValueError('SDK descriptor identity mismatch')
 files=d.get('files',[])
 if files!=sorted(files,key=lambda x:x['path']) or len({x['path'] for x in files})!=len(files):raise ValueError('Unsorted/duplicate SDK entries')
 if fingerprint(d['target'],d['sourceCommit'],files)!=d['fingerprint']:raise ValueError('SDK fingerprint mismatch')
 for item in files:
  if sha(safe(sdk,item['path']))!=item['sha256']:raise ValueError('SDK file hash mismatch: '+item['path'])
 for item in d['sources']:
  if item['sha256'] not in (sha(safe(Path(package),item['path'])),source_sha(safe(Path(package),item['path']))):raise ValueError('SDK adapter source mismatch: '+item['path'])
 if 'official-sdk.json' not in {x['path'] for x in files}:raise ValueError('Missing manifest in SDK descriptor')
 return d

@contextlib.contextmanager
def catalog_lock(root):
 with (root/'.catalog.lock').open('a+b') as f:
  if os.name=='nt':
   import msvcrt
   f.seek(0);f.write(b'0');f.flush();f.seek(0);msvcrt.locking(f.fileno(),msvcrt.LK_LOCK,1)
  else:
   import fcntl
   fcntl.lockf(f.fileno(),fcntl.LOCK_EX,1,0,os.SEEK_SET)
  try:yield
  finally:
   if os.name=='nt':f.seek(0);msvcrt.locking(f.fileno(),msvcrt.LK_UNLCK,1)
   else:fcntl.lockf(f.fileno(),fcntl.LOCK_UN,1,0,os.SEEK_SET)

def install(sdk,project,package):
 sdk=Path(sdk).resolve();project=project_root(project);package=Path(package).resolve()
 if project.is_relative_to(package):raise ValueError('SDK output must be outside the package')
 d=validate(sdk,package);root=project/'Library/ZenTS/NativeSDK';root.mkdir(parents=True,exist_ok=True)
 relative=d['fingerprint']+'/'+d['target'];destination=root/relative
 with catalog_lock(root):
  if destination.exists():validate(destination,package)
  else:
   with tempfile.TemporaryDirectory(prefix='.stage-',dir=root) as temp:
    stage=Path(temp)/'sdk';shutil.copytree(sdk,stage);validate(stage,package)
    destination.parent.mkdir(parents=True,exist_ok=True);stage.rename(destination)
  catalog=root/'installed.json'
  value=json.loads(catalog.read_text(encoding='utf-8')) if catalog.exists() else {'schema':1,'entries':[]}
  value['entries']=[x for x in value['entries'] if x['target']!=d['target']]+[{'target':d['target'],'fingerprint':d['fingerprint'],'relativePath':relative}]
  value['entries'].sort(key=lambda x:x['target'])
  temp=root/('.catalog-'+uuid.uuid4().hex+'.tmp');temp.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8');os.replace(temp,catalog)
 return destination
