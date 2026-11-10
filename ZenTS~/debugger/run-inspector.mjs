// Copyright 2026 Code Philosophy. MIT License.
// Real Unity Editor + V8 Inspector protocol, with no mocked engine responses.
import fs from 'node:fs';
import path from 'node:path';
import {createRequire,SourceMap} from 'node:module';
import {spawn} from 'node:child_process';
import assert from 'node:assert/strict';
const project=path.resolve(process.argv[2]||process.cwd());
const reloadOnly=process.argv.includes('--reload-only'),protocolOnly=process.argv.includes('--protocol-only');
if(reloadOnly&&protocolOnly)throw Error('Choose only one acceptance mode');
const unity=process.argv[3]||'D:/UnityHubs/2022.3.62f3/Editor/Unity.exe';
const require=createRequire(path.join(project,'TsProject/package.json'));
const {buildSync}=require('esbuild');
const fixture=path.join(project,'TsProject/generated/debugger-acceptance.ts');
const generated=path.join(project,'TsProject/out/debugger-acceptance.js');
for(const file of [fixture,generated,generated+'.map'])if(fs.existsSync(file))throw Error('Acceptance fixture already exists: '+file);
fs.mkdirSync(path.dirname(fixture),{recursive:true});fs.mkdirSync(path.dirname(generated),{recursive:true});
const ts=`export function compute(input: number): number {
  const local = input + 1;
  const result = local * 2; // TS_BREAKPOINT
  return result;
}
export function nested(): number {
  const F = globalThis.CSharp['ZenTS.Tests']['ZenTS.Tests.Fixtures.P3AlignmentFixture'];
  return F.CallNested(() => {
    const inside = 11;
    debugger;
    return inside + 1;
  });
}
export function thrower(): void { throw new Error('debugger-exception-marker'); }
export function spin(): number { let iterations=0; while(!globalThis.__zentsStop){iterations++;} return iterations>0?42:0; }
`;
fs.writeFileSync(fixture,ts);
buildSync({entryPoints:[fixture],outfile:generated,format:'esm',target:'es2020',sourcemap:true,sourcesContent:true});
const compiled=fs.readFileSync(generated,'utf8'), mapPayload=JSON.parse(fs.readFileSync(generated+'.map','utf8')), map=new SourceMap(mapPayload);
const wanted=ts.split('\n').findIndex(x=>x.includes('TS_BREAKPOINT'));
let breakLine=-1,breakColumn=0;
for(const [i,line] of compiled.split('\n').entries()) {
 const column=Math.max(0,line.search(/\S/));
 if(map.findEntry(i,column).originalLine===wanted){breakLine=i;breakColumn=column;break;}
}
assert.ok(breakLine>=0,'No TS mapping for breakpoint');
const base='http://127.0.0.1:9231',url=base+'/source/out/debugger-acceptance.js';
let child,ws,fault,sequence=0,phase=0;const evidence=[];
const note=x=>{evidence.push(x);console.log(x);};
const delay=ms=>new Promise(r=>setTimeout(r,ms));
async function timed(promise,ms,message){let timer;try{return await Promise.race([promise,new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error(message)),ms);})]);}finally{clearTimeout(timer);}}
function launch(method,log){
 child=spawn(unity,['-batchmode','-nographics','-job-worker-count','1','-projectPath',project,'-buildTarget',process.platform==='darwin'?'OSXUniversal':'Win64','-executeMethod',method,'-logFile',path.join(project,log)],{windowsHide:true,stdio:'ignore',env:{...process.env,ALLUSERSPROFILE:process.env.ALLUSERSPROFILE||'C:/ProgramData',LOCALAPPDATA:process.env.LOCALAPPDATA||'C:/Users/MSN/AppData/Local'}});
 return new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',code=>resolve(code));});
}
async function discover(previous){
 for(let i=0;i<900;i++){
  if(fault)throw fault;
  try{const response=await fetch(base+'/json/list',{signal:AbortSignal.timeout(1000)});const targets=await response.json();if(targets[0]?.id!==previous)return targets[0];}catch{}
  await delay(100);
 }
 throw Error('Inspector discovery timed out');
}
async function connect(target,round){
 ws=new WebSocket(target.webSocketDebuggerUrl);const socket=ws,requests=new Map();let unicodeScriptUrl;
 await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
 const send=(method,params={})=>new Promise((resolve,reject)=>{
  const id=++sequence,timer=setTimeout(()=>{requests.delete(id);reject(Error('CDP timed out: '+method));},15000);
  requests.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
 });
 const watch=async(callFrameId,expression)=>{
  const result=await send('Debugger.evaluateOnCallFrame',{callFrameId,expression,returnByValue:true});assert.ok(!result.exceptionDetails,JSON.stringify(result));return result.result.value;
 };
 socket.onmessage=async event=>{
  try{
   const message=JSON.parse(event.data);
   if(message.id){const request=requests.get(message.id);if(request){clearTimeout(request.timer);requests.delete(message.id);message.error?request.reject(Error(JSON.stringify(message.error))):request.resolve(message.result);}return;}
   if(message.method==='Debugger.scriptParsed'&&message.params.url.includes('unicode-'))unicodeScriptUrl=message.params.url;
   if(message.method==='Debugger.scriptParsed'&&message.params.url===url){assert.equal(message.params.sourceMapURL,'debugger-acceptance.js.map');note('scriptParsed URL/sourceMapURL PASS');}
   if(message.method!=='Debugger.paused')return;
   const p=message.params,frame=p.callFrames[0];
   if(round>=2){assert.equal(await watch(frame.callFrameId,round===2?'inside':'inspectionLocal'),round===2?11:23);note(round===2?'Reset target reattach + disconnect while CLR callback paused':'Disconnect during paused Inspector Runtime.evaluate PASS');socket.close();return;}
   phase++;
   if(phase===1){
    assert.equal(frame.location.lineNumber,breakLine);assert.equal(map.findEntry(frame.location.lineNumber,frame.location.columnNumber).originalLine,wanted);
    assert.equal(await watch(frame.callFrameId,'local'),21);
    assert.equal(await watch(frame.callFrameId,JSON.stringify('中😀\u0000tail')),'中😀\u0000tail');
    const bad=await send('Debugger.evaluateOnCallFrame',{callFrameId:frame.callFrameId,expression:'(()=>{throw Error("watch-error")})()'});assert.ok(bad.exceptionDetails);
    assert.equal(await watch(frame.callFrameId,"CSharp['ZenTS.Tests']['ZenTS.Tests.Fixtures.P3AlignmentFixture'].Sum(20,22)"),42);
    const badManaged=await send('Debugger.evaluateOnCallFrame',{callFrameId:frame.callFrameId,expression:"CSharp['ZenTS.Tests']['ZenTS.Tests.Fixtures.P3AlignmentFixture'].Sum('invalid')"});assert.ok(badManaged.exceptionDetails);
    assert.equal(await watch(frame.callFrameId,"CSharp['ZenTS.Tests']['ZenTS.Tests.Fixtures.P3AlignmentFixture'].Sum(1,2)"),3);
    assert.equal(await watch(frame.callFrameId,JSON.stringify('café')),'café');
    note('TS-mapped breakpoint + locals + Unicode watch + watch exception PASS');
    await send('Debugger.stepOver');
   }else if(phase===2){assert.equal(await watch(frame.callFrameId,'result'),42);note('stepOver PASS');await send('Debugger.resume');}
   else if(phase===3){assert.equal(await watch(frame.callFrameId,'inside'),11);await send('Debugger.setPauseOnExceptions',{state:'all'});note('Pause inside nested CLR callback PASS');await send('Debugger.resume');}
   else if(phase===4){assert.equal(p.reason,'exception');assert.ok(p.data.description.includes('debugger-exception-marker'));await send('Debugger.setPauseOnExceptions',{state:'none'});note('Pause on exception PASS');await send('Debugger.resume');}
   else if(phase===5){assert.equal(await watch(frame.callFrameId,'local'),3);note('Execution after exception PASS');await send('Debugger.resume');setTimeout(()=>send('Debugger.pause').catch(e=>{fault=e;socket.close();}),100);}
   else if(phase===6){assert.equal(frame.functionName,'spin');assert.ok(await watch(frame.callFrameId,'iterations')>0);await watch(frame.callFrameId,'globalThis.__zentsStop=true');note('Pause interrupts CPU-bound JavaScript on owner thread PASS');await send('Debugger.resume');}
   else throw Error('Unexpected pause '+phase);
  }catch(e){fault=e;socket.close();}
 };
 socket.onclose=()=>{for(const r of requests.values()){clearTimeout(r.timer);r.reject(Error('Inspector connection closed'));}requests.clear();};
 await send('Runtime.enable');await send('Debugger.enable');
 if(round===1){
  const special='unicode-中 space';
  const unicode=await send('Runtime.compileScript',{expression:'1+1',sourceURL:special,persistScript:true});assert.ok(unicode.scriptId);
  assert.equal(unicodeScriptUrl,base+'/source/out/'+encodeURIComponent(special)+'.js');note('Unicode/space module URL encoding PASS');
  const result=await send('Debugger.setBreakpointByUrl',{url,lineNumber:breakLine,columnNumber:breakColumn});assert.ok(result.breakpointId);
  const sourceMapResponse=await fetch(base+'/source/out/debugger-acceptance.js.map');assert.equal(sourceMapResponse.status,200);assert.deepEqual(await sourceMapResponse.json(),mapPayload);
  assert.equal((await fetch(base+'/source/generated/debugger-acceptance.ts').then(r=>r.text())),ts);
  assert.equal((await fetch(base+'/source/out/../../../../ProjectSettings/ProjectSettings.asset')).status,404);
  note('HTTP discovery + JS/TS source map routes PASS');
 }
 const closed=new Promise(resolve=>socket.addEventListener('close',resolve,{once:true}));
 if(round===3)send('Runtime.evaluate',{expression:'(()=>{const inspectionLocal=23;debugger;return inspectionLocal;})()'}).catch(e=>{if(socket.readyState!==WebSocket.CLOSED&&socket.readyState!==WebSocket.CLOSING){fault=e;socket.close();}});
 else await send('Runtime.runIfWaitingForDebugger');
 return closed;
}
try{
 if(!reloadOnly){
 const exited=launch('ZenTS.Editor.Diagnostics.V8DebuggerAcceptance.Run','debugger-editor.log');
 const first=await discover();
 await connect(first,1);if(fault)throw fault;assert.equal(phase,6);
 const second=await discover(first.id);assert.notEqual(second.id,first.id);await connect(second,2);if(fault)throw fault;
 const third=await discover(second.id);assert.notEqual(third.id,second.id);await connect(third,3);if(fault)throw fault;
 const code=await timed(exited,30000,'Editor did not finish');assert.equal(code,0);
 const log=fs.readFileSync(path.join(project,'debugger-editor.log'),'utf8');assert.ok(log.includes('[ZenTS][debugger-test] ALL OK'));note('Editor breakpoint + transport + Reset full acceptance PASS');
 }
 if(!protocolOnly){
 const reload=await timed(launch('ZenTS.Editor.Diagnostics.V8DebuggerAcceptance.RunDomainReload','debugger-reload.log'),120000,'Domain Reload timed out');assert.equal(reload,0);
 assert.ok(fs.readFileSync(path.join(project,'debugger-reload.log'),'utf8').includes('Domain Reload PASS'));note('Actual Editor Domain Reload PASS');
 }
 fs.writeFileSync(path.join(project,'debugger-protocol.log'),evidence.join('\n')+'\n');
 }catch(e){
 console.error(e);process.exitCode=1;
 if(process.platform==='darwin'&&e.message==='Domain Reload timed out'&&fs.readFileSync(path.join(project,'debugger-reload.log'),'utf8').includes('Domain Reload PASS')){
  note('Domain Reload assertions PASS; Unity shutdown timed out (not a clean process exit)');
  await new Promise(resolve=>{const sample=spawn('/usr/bin/sample',[String(child.pid),'2','-file',path.join(project,'debugger-mac-shutdown-sample.txt')],{stdio:'ignore'});sample.once('exit',resolve);sample.once('error',resolve);});
 }
}
finally{fs.writeFileSync(path.join(project,'debugger-protocol.log'),evidence.join('\n')+'\n');if(ws)ws.close();if(child&&child.exitCode===null)child.kill('SIGKILL');for(const file of [fixture,generated,generated+'.map'])fs.rmSync(file,{force:true});}
