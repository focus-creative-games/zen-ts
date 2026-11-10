"""Verify WeChat host-VM execution evidence and native/browser regressions without modifying the package."""
import argparse,datetime,hashlib,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--project',type=Path,required=True);parser.add_argument('--output',type=Path);args=parser.parse_args()
 project=args.project.resolve();output=(args.output or project/'Library/ZenTS/Verification/p6-verification.json').resolve()
 if output.is_relative_to(ROOT):raise ValueError('Verification report must be outside the source package')
 logs={'wechat':'wechat-player.log','editor':'wechat-editor-regression.log','win64':'p5-player.log','webgl':'p4-webgl-browser.log'};evidence={}
 for name,file in logs.items():
  p=project/file;text=p.read_text(encoding='utf-8-sig',errors='replace');smoke=8 if name=='editor' else 10
  for marker in ['summary: total=56 passed=56 failed=0',f'Test summary: total={smoke} passed={smoke} failed=0','[ZenTS.TsDemo] ALL OK']:
   if marker not in text:raise RuntimeError(name+' missing runtime evidence: '+marker)
  for marker in ['[matrix] FAIL','[ZenTS] FAIL','TestBootstrap FAILED','TsDemoRunner FAILED']:
   if marker in text:raise RuntimeError(name+' recorded failure: '+marker)
  evidence[name]={'path':file,'sha256':sha(p),'js':'56/56','smoke':f'{smoke}/{smoke}','ts':'3/3'}
 wx=(project/logs['wechat']).read_text(encoding='utf-8')
 for marker in ['Player engine=HostJs (platform VM)','[ZenTS][WeChat] runtime capabilities','HostJs bridge verified {"hosts":true,"callback":"function"']:
  if marker not in wx:raise RuntimeError('Missing WeChat host evidence: '+marker)
 browser=(project/logs['webgl']).read_text(encoding='utf-8')
 for marker in ['Browser GC PASS','Browser exception/UTF8 boundary PASS','Browser HostJs full acceptance PASS']:
  if marker not in browser:raise RuntimeError('Missing WebGL boundary evidence: '+marker)
 subprocess.run(['node',str(ROOT/'ZenTS~/host-js/verify-restricted-host.mjs'),str(project)],check=True)
 build=project/'wechat-build-release-check.log'
 if '[ZenTS][WeChat] Build PASS' not in build.read_text(encoding='utf-8',errors='replace'):raise RuntimeError('Final WeChat conversion did not pass')
 dependency=json.loads((project/'Packages/manifest.json').read_text(encoding='utf-8-sig'))['dependencies']['com.qq.weixin.minigame']
 if not dependency.endswith('#915d4bf4f60ef0cd1c29316a8d67257185d609a7'):raise RuntimeError('Official SDK pin differs from verified version')
 active=Path(json.loads((project/'Library/ZenTS/WeChatAcceptance/active-project.json').read_text())['project'])
 artifacts={}
 for folder,label in [(active,'executed_acceptance_copy'),(project/'Build-WeChat/minigame','final_export')]:
  artifacts[label]={p.relative_to(folder).as_posix():sha(p) for p in folder.rglob('*') if p.is_file() and p.name!='project.private.config.json'}
 report={'verified_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'WeChat platform host JS VM; Unity 2022.3.62f3; applicable full suites',
  'supported_engine_policy':'Unity 2022.3 LTS and later LTS; Tuanjie based on 2022.3 LTS',
  'official_sdk':{'git':dependency,'sdk_version':'202610200817'},'wechat_devtools':'2.01.2510260','wechat_base_library':'3.17.6',
  'backend':'HostJs; no embedded V8/QuickJS in wasm','code_generation':'OptimizeSize; fully shared generic delegates and GTAP invokers',
  'logs':evidence,'final_build_log':{'path':build.name,'sha256':sha(build)},'artifacts':artifacts,
  'restricted_host':'PASS: strings code generation disabled, no TextDecoder; facade, UTF8/NUL/BOM/invalid bytes, Reset',
  'skipped_subcases':['Existing managed worker-thread test is not applicable to WebGL/WeixinMiniGame'],
  'limitations':['Windows WeChat Developer Tools execution only; no phone runtime, upload or publishing','Tuanjie target routing implemented but Tuanjie Editor not available for this run','macOS/iOS/Android native suites were not rerun in this WeChat-focused change'],
  'source_sha256':{p.relative_to(ROOT).as_posix():sha(p) for base in ['Editor','Runtime','Plugins','ZenTS~/wechat','ZenTS~/host-js','ZenTS~/zents-runtime'] for p in (ROOT/base).rglob('*') if p.is_file() and p.suffix in ['.cs','.cpp','.h','.js','.jslib','.mjs']}}
 output.parent.mkdir(parents=True,exist_ok=True);output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print('P6 WeChat acceptance PASS; report: '+str(output))
if __name__=='__main__':main()
