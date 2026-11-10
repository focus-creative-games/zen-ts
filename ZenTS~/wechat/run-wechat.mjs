import {verifyCaseSummary} from '../migration/test-inventory.mjs';
// Copyright 2026 Code Philosophy. MIT License.
// Run a copied acceptance project; shipped conversion output and test expectations stay unchanged.
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import {randomUUID} from 'node:crypto';
import {spawn} from 'node:child_process';
const project=path.resolve(process.argv[2]||process.cwd());
const cli=process.argv[3];
if(!cli)throw Error('Usage: node run-wechat.mjs <Unity project> <WeChat cli.bat>');
const source=path.join(project,'Build-WeChat/minigame');
const base=path.join(project,'Library/ZenTS/WeChatAcceptance');
const target=path.join(base,'project-'+Date.now());
fs.mkdirSync(base,{recursive:true});
const active=path.join(base,'active-project.json');
const runCli=async(command,folder)=>{
 const args=command==='auto'?' --trust-project --auto-port 9421':'';
 const child=spawn('cmd.exe',['/d','/s','/c','""'+path.resolve(cli)+'" '+command+' --project "'+folder+'"'+args+'"'],{windowsHide:true,windowsVerbatimArguments:true,stdio:'inherit'});
 return await new Promise((r,j)=>{child.once('error',j);child.once('exit',r);});
};
if(fs.existsSync(active)){
 const previous=JSON.parse(fs.readFileSync(active,'utf8')).project;
 if(!path.resolve(previous).startsWith(base+path.sep))throw Error('Unsafe previous acceptance project path');
 const exit=await runCli('close',previous);if(exit)throw Error('Close previous WeChat acceptance window before retrying');
}

if(!target.startsWith(path.join(project,'Library/ZenTS')+path.sep))throw Error('Unsafe acceptance path');
fs.cpSync(source,target,{recursive:true});

const entry=path.join(target,'game.js');
fs.writeFileSync(entry,"import './zents-acceptance-logger';\n"+fs.readFileSync(entry,'utf8')+"\nimport './zents-acceptance-module';\n");
const output=path.join(project,'wechat-player.log');
const log=fs.createWriteStream(output);let complete=false,failed=false;
const lines=[],route='/log/'+randomUUID();
const server=http.createServer((req,res)=>{
 if(req.method!=='POST'||req.url!==route){res.writeHead(404).end();return;}
 let body='';req.on('data',chunk=>body+=chunk);req.on('end',()=>{
  try{for(const item of JSON.parse(body)){const text=item.text;lines.push(text);log.write(text+'\n');
   if(text.includes('[ZenTS] FAIL')||text.includes('[matrix] FAIL')||text.includes('TestBootstrap FAILED')||text.includes('TsDemoRunner FAILED')||item.type==='uncaught')failed=true;
   if(text.includes('[ZenTS.TsDemo] ALL OK'))complete=true;}}
  catch(e){failed=true;log.write('Collector error: '+e+'\n');}
  res.setHeader('Access-Control-Allow-Origin','*');res.end(complete||failed?'stop':'ok');
 });
});
await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(8786,'127.0.0.1',resolve);});
const logger=`// Acceptance observability only. No engine/test behavior changes.
const pending=[];let sending=false,stopped=false,timer;
function stop(){stopped=true;pending.length=0;if(timer)clearInterval(timer);}
function record(type,args){if(stopped)return;pending.push({type,text:args.map(x=>{try{return typeof x==='string'?x:JSON.stringify(x);}catch{return String(x);}}).join(' ')});}
for(const type of ['log','info','warn','error','debug']){const original=console[type];console[type]=function(...args){record(type,args);return original.apply(console,args);};}
GameGlobal.__zentsAcceptanceRecord=record;
wx.onError(error=>record('uncaught',[error.message||error]));
function flush(){if(stopped||sending||!pending.length)return;const batch=pending.splice(0,100);sending=true;wx.request({url:'http://127.0.0.1:8786${route}',method:'POST',data:batch,success(response){sending=false;if(response.statusCode!==200||response.data==='stop'){stop();return;}flush();},fail(){sending=false;stop();}});}
timer=setInterval(flush,300);
record('info',['[ZenTS][WeChat] runtime capabilities '+JSON.stringify({WeakRef:typeof WeakRef,FinalizationRegistry:typeof FinalizationRegistry,TextDecoder:typeof TextDecoder,Proxy:typeof Proxy,BigInt:typeof BigInt})]);
`;
fs.writeFileSync(path.join(target,'zents-acceptance-logger.js'),logger);
const moduleLogger=`// Observe the SDK's documented UnityModule factory, leaving engine and test code intact.
const ns=GameGlobal.unityNamespace;
const factory=ns.UnityModule;
if(typeof factory!=='function')throw Error('Official UnityModule factory missing');
ns.UnityModule=function(config){
 const record=GameGlobal.__zentsAcceptanceRecord;
 for(const [key,level] of [['print','info'],['printErr','error']]){const original=config[key];config[key]=function(...args){record(level,args);if(original)return original.apply(this,args);};}
 return factory.call(this,config).then(module=>{
  const timer=setInterval(()=>{const H=module.__zentsHostBridge;
   if(H&&H.contexts.size){const hosts=Array.from(H.contexts.values()).every(c=>c.global.__ZENTS_HOST_JS__===true);record('info',['[ZenTS][WeChat] HostJs bridge verified '+JSON.stringify({hosts,callback:typeof module._zents_host_callback,contexts:H.contexts.size})]);clearInterval(timer);}
  },50);
  return module;
 });
};
`;
fs.writeFileSync(path.join(target,'zents-acceptance-module.js'),moduleLogger);

let result=1;
try{
 const exit=await runCli('auto',target);if(exit)throw Error('WeChat CLI exit '+exit);
 fs.writeFileSync(active,JSON.stringify({project:target}));
 const deadline=Date.now()+240000;
 while(Date.now()<deadline&&!complete&&!failed)await new Promise(r=>setTimeout(r,500));
 if(failed)throw Error('WeChat runtime reported failure; see '+output);
 await new Promise(r=>setTimeout(r,600));
 if(!lines.some(x=>x.includes('Player engine=HostJs (platform VM)')||x.includes('HostJs bridge verified {"hosts":true,"callback":"function"')))throw Error('HostJs backend not observed; see '+output);
 for(const marker of ['[ZenTS][WeChat] runtime capabilities','summary: total=56 passed=56 failed=0','Test summary: total=10 passed=10 failed=0','[ZenTS.TsDemo] ALL OK'])
  if(!lines.some(x=>x.includes(marker)))throw Error('Missing WeChat runtime evidence: '+marker+'; see '+output);
 if(failed)throw Error('WeChat runtime reported failure; see '+output);
 console.log('Named case acceptance '+JSON.stringify(verifyCaseSummary(lines,project)));
 console.log('WeChat host VM PASS: JS 56/56, Smoke 10/10, TS 3/3');result=0;
}catch(e){console.error(e);}
finally{await new Promise(r=>setTimeout(r,500));log.end();server.closeAllConnections();await new Promise(r=>server.close(r));}
process.exitCode=result;
