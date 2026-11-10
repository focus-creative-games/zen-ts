import {verifyCaseSummary} from '../migration/test-inventory.mjs';
// Copyright 2026 Code Philosophy. MIT License.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
const project=path.resolve(process.argv[2]||'../..');
const root=path.join(project,'Build-WebGL');
const log=fs.createWriteStream(path.join(project,'p4-webgl-browser.log'));
const events=[];let success=false,failed=false,done=false;
const record=s=>{events.push(s);log.write(s+'\n');if(s.includes('RESULT=FAIL')||s.includes('[matrix] FAIL')||s.includes('TestBootstrap FAILED')||s.includes('TsDemoRunner FAILED'))failed=true;};
const server=http.createServer((req,res)=>{const file=path.resolve(root,'.'+decodeURIComponent(req.url.split('?')[0]==='/'?'/index.html':req.url.split('?')[0]));if(!file.startsWith(root+path.sep)){res.writeHead(403).end();return;}try{res.setHeader('Content-Type',file.endsWith('.wasm')?'application/wasm':file.endsWith('.js')?'application/javascript':file.endsWith('.html')?'text/html':'application/octet-stream');res.setHeader('Cross-Origin-Opener-Policy','same-origin');res.setHeader('Cross-Origin-Embedder-Policy','require-corp');var content=fs.readFileSync(file);if(file.endsWith('index.html'))content=Buffer.from(content.toString().replace('.then((unityInstance) => {','.then((unityInstance) => {window.__zentsTestModule=unityInstance.Module;'));res.end(content);}catch{res.writeHead(404).end();}});
await new Promise(r=>server.listen(8784,'127.0.0.1',r));
const chrome=spawn('C:/Program Files/Google/Chrome/Application/chrome.exe',['--headless=new','--no-first-run','--disable-extensions','--disable-dev-shm-usage','--enable-unsafe-swiftshader','--use-angle=swiftshader','--remote-debugging-port=9224','--user-data-dir='+path.join(project,'Library/ZenTS/ChromeP4'),'about:blank'],{windowsHide:true,stdio:'ignore'});
let ws;
try{
 let targets;for(let i=0;i<100;i++){try{targets=await(await fetch('http://127.0.0.1:9224/json/list')).json();break;}catch{await new Promise(r=>setTimeout(r,200));}}
 if(!targets)throw Error('Chrome CDP did not start');
 ws=new WebSocket(targets.find(x=>x.type==='page').webSocketDebuggerUrl);await new Promise((r,j)=>{ws.onopen=r;ws.onerror=j;});let next=1;const requests=new Map();
 ws.onmessage=msg=>{const e=JSON.parse(msg.data);if(e.id){const request=requests.get(e.id);if(request){requests.delete(e.id);e.error?request.reject(e.error):request.resolve(e.result);}return;}
 if(e.method==='Runtime.consoleAPICalled'){const p=e.params;const text=p.args.map(x=>x.value??x.description??'').join(' ');record(text);if(text.includes('[ZenTS.TsDemo] ALL OK'))success=true;}
 if(e.method==='Runtime.exceptionThrown'){record('BROWSER EXCEPTION '+JSON.stringify(e.params.exceptionDetails));failed=true;}
 };
 const send=(method,params={})=>new Promise((resolve,reject)=>{const id=next++;requests.set(id,{resolve,reject});ws.send(JSON.stringify({id,method,params}));});
 await send('Runtime.enable');await send('Page.enable');await send('Page.addScriptToEvaluateOnNewDocument',{source:"window.alert=function(message){console.error('UNITY ALERT '+message);};"});
 await send('Page.navigate',{url:'http://127.0.0.1:8784/'});
 for(let i=0;i<600&&!success&&!failed;i++)await new Promise(r=>setTimeout(r,500));
 if(!success||failed)throw Error('WebGL browser acceptance failed; see p4-webgl-browser.log');
 // TS demos run after smoke; require the actual complete matrix, not only the final marker.
 for(const marker of ['summary: total=56 passed=56 failed=0','Test summary: total=10 passed=10 failed=0','Player engine=HostJs (platform VM)'])if(!events.some(x=>x.includes(marker)))throw Error('Missing acceptance '+marker);
 const evaluate=async expression=>{const result=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));return result.result.value;};
 for(let i=0;i<100&&!await evaluate('!!window.__zentsTestModule');i++)await new Promise(r=>setTimeout(r,20));
 await evaluate("(function(){var H=window.__zentsTestModule.__zentsHostBridge;var c=Array.from(H.contexts.values())[0];new c.global.CSharp['ZenTS.Tests']['ZenTS.Tests.Fixtures.P3AlignmentFixture']();return true;})()");
 const boundary=await evaluate("(function(){var H=window.__zentsTestModule.__zentsHostBridge;var c=Array.from(H.contexts.values())[0];var F=c.global.CSharp['ZenTS.Tests']['ZenTS.Tests.Fixtures.P3AlignmentFixture'];var S=c.global.CSharp['mscorlib']['System.String'];var value='A'+String.fromCharCode(0)+'中😀';if(F.Echo(c.global.zents.typeof(S),value)!==value)throw Error('UTF8/NUL roundtrip failed');var caught=false;try{F.CallNested(function(){throw undefined;});}catch(e){caught=true;}if(!caught||F.CallNested(function(){return 42;})!==42||c.pending)throw Error('throw undefined leaked pending exception');return true;})()");
 record('[ZenTS][P4] Browser exception/UTF8 boundary PASS');
 await send('HeapProfiler.collectGarbage');await new Promise(r=>setTimeout(r,100));
 const rootCount="Array.from(window.__zentsTestModule.__zentsHostBridge.slots.values()).filter(s=>s.classId&&s.opaque).length";
 const baseline=await evaluate(rootCount);
 await evaluate("(function(){var H=window.__zentsTestModule.__zentsHostBridge;var c=Array.from(H.contexts.values())[0];var T=c.global.CSharp['ZenTS.Tests']['ZenTS.Tests.Fixtures.P3AlignmentFixture'];for(var i=0;i<128;i++){var object=new T();if(i===127)window.__zentsGcWeak=new WeakRef(object);}return true;})()");
 const allocated=await evaluate(rootCount);if(allocated<baseline+128)throw Error('GC stress did not create rooted CLR wrappers');
 let collected=false,remaining=allocated;
 for(let i=0;i<15;i++){await send('HeapProfiler.collectGarbage');await new Promise(r=>setTimeout(r,80));remaining=await evaluate(rootCount);collected=await evaluate('window.__zentsGcWeak.deref()===undefined');if(collected&&remaining<=baseline)break;}
 if(!collected||remaining>baseline)throw Error('HostJs CLR wrapper GC did not drain: '+JSON.stringify({baseline,allocated,remaining,collected}));
 const lifecycle=await evaluate("(function(){var H=window.__zentsTestModule.__zentsHostBridge;var ids=Array.from(H.contexts.keys());return {contexts:ids.length,staleSlots:Array.from(H.slots.values()).filter(s=>ids.indexOf(s.context)<0).length,pending:H.finalized.length};})()");
 if(lifecycle.contexts!==1||lifecycle.staleSlots!==0||lifecycle.pending!==0)throw Error('Reset/GC lifecycle mismatch '+JSON.stringify(lifecycle));
 record('[ZenTS][P4] Browser GC PASS '+JSON.stringify({baseline,allocated,remaining,collected,...lifecycle}));
 console.log('Named case acceptance '+JSON.stringify(verifyCaseSummary(events,project)));
 record('[ZenTS][P4] Browser HostJs full acceptance PASS');console.log('WebGL HostJs PASS: JS 56/56, Smoke 10/10, TS 3/3');done=true;
}catch(e){console.error(e);process.exitCode=1;}finally{if(ws)ws.close();chrome.kill();await new Promise(r=>server.close(r));log.end();}
