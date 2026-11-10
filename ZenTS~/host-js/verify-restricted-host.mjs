// Copyright 2026 Code Philosophy. MIT License.
// Exercise the shipped host bridge with dynamic code generation and TextDecoder unavailable.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const project=path.resolve(process.argv[2]||process.cwd());
const packageRoot=path.resolve(import.meta.dirname,'../..');
const library={};
const memory=new ArrayBuffer(65536);
const sandbox={Module:{},LibraryManager:{library},mergeInto:(target,source)=>Object.assign(target,source),
 HEAP32:new Int32Array(memory),HEAPU32:new Uint32Array(memory),HEAPF64:new Float64Array(memory),HEAPU8:new Uint8Array(memory),
 TextDecoder:undefined,WeakRef,FinalizationRegistry,
 UTF8ToString:p=>{let end=p;while(sandbox.HEAPU8[end])end++;return new TextDecoder().decode(sandbox.HEAPU8.subarray(p,end));}};
sandbox.out=()=>{};sandbox.err=()=>{};
const context=vm.createContext(sandbox,{codeGeneration:{strings:false,wasm:false}});
vm.runInContext(fs.readFileSync(process.argv[3]||path.join(project,'Assets/ZenTSHostGenerated/ZenTSModules.jslib'),'utf8'),context);
// Official WeChat conversion rewrites these tokens even inside string literals.
vm.runInContext(library.$ZenTSModules__postset.replace(/console\.log/g,'out').replace(/console\.error/g,'err'),context);
vm.runInContext(fs.readFileSync(path.join(packageRoot,'Plugins/WebGL/HostEngine.jslib'),'utf8'),context);
const op=library.HostOperation,id=op(0,0,0,0,0,0,0,0),H=sandbox.Module.__zentsHostBridge;
const source=fs.readFileSync(path.join(packageRoot,'ZenTS~/jslib/zentslib.js'),'utf8');
sandbox.HEAPU8.set(new TextEncoder().encode(source+'\0'),2048);
assert.equal(op(24,id,16,0,0,2048,0,0),0,String(H.contexts.get(id).exception));
assert.equal(typeof H.contexts.get(id).global.zents.typeof,'function');
const c=H.contexts.get(id);
const suiteSource=fs.readFileSync(path.join(project,'Assets/StreamingAssets/Tests/Js/jstest/suite.js'),'utf8');
sandbox.HEAPU8.set(new TextEncoder().encode(suiteSource+'\0'),8192);
sandbox.stackSave=()=>0;sandbox.stackRestore=()=>{};sandbox.stackAlloc=()=>512;
sandbox.lengthBytesUTF8=s=>new TextEncoder().encode(s).length;
sandbox.stringToUTF8=(s,p)=>sandbox.HEAPU8.set(new TextEncoder().encode(s+'\0'),p);
sandbox._zents_host_module_source=()=>8192;sandbox._free=()=>{};
c.global.console={info(){},error(){},log(){}};
const suite=H.require(c,'jstest/suite');
suite.defineSuite('conversion_regression',{test_source_identity(){}})();
assert.equal(JSON.parse(suite.report()).passed,1);
assert.throws(suite.defineSuite('conversion_failure',{test_throws(){throw Error('expected');}}),/expected/);
assert.equal(JSON.parse(suite.report()).failed,1);

for(const bytes of [new TextEncoder().encode('A\0中😀尾'),[0xef,0xbb,0xbf,65,0,66],[65,0xef,0xbb,0xbf],[0xf0,0x9f],[0xed,0xa0,0x80],[0xe2,0x28,0xa1],[0xff,0x80],[0xe0,0x80,0x80],[0xf4,0x90,0x80,0x80]]){
 const data=new Uint8Array(bytes);assert.equal(H.decode(data),new TextDecoder().decode(data));
 sandbox.HEAPU8.set(data,1024);assert.equal(op(6,id,16,0,0,1024,data.length,0),0);assert.equal(H.read(16),new TextDecoder().decode(data));op(3,id,0,16,0,0,0,0);
}
assert.throws(()=>vm.runInContext('new Function("return 1")()',context));
assert.equal(op(1,id,0,0,0,0,0,0),0);assert.equal(H.contexts.size,0);
console.log('Restricted HostJs PASS: no eval/Function; precompiled facade/module identity after WeChat console rewrite; UTF8/NUL/invalid-byte fallback; Reset');
