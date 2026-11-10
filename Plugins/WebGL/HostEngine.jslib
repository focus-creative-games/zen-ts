// Copyright 2026 Code Philosophy. MIT License.
mergeInto(LibraryManager.library,{HostOperation:function(op,context,output,av,bv,text,extra,number){
 var H=Module['__zentsHostBridge'];
 if(!H){
 if(typeof WeakRef!=='function'||typeof FinalizationRegistry!=='function')throw Error('ZenTS HostJs requires WeakRef and FinalizationRegistry; update the WeChat base library/runtime.');
 H=Module['__zentsHostBridge']={next:1,nextContext:1,contexts:new Map(),slots:new Map(),ids:new WeakMap(),finalized:[]};
 H.finalizer=new FinalizationRegistry(function(x){H.finalized.push(x);});
 H.sourceKey=function(source){var key='';for(var i=0;i<source.length;i++)key+=('000'+source.charCodeAt(i).toString(16)).slice(-4);return key;};
 H.decode=function(bytes){
  if(typeof TextDecoder==='function')return new TextDecoder('utf-8').decode(bytes);
  var result='',i=bytes.length>=3&&bytes[0]===239&&bytes[1]===187&&bytes[2]===191?3:0;
  while(i<bytes.length){var first=bytes[i++],code=first,need=0,min=0;
   if(first<128){result+=String.fromCharCode(first);continue;}
   if(first>=194&&first<=223){code=first&31;need=1;min=128;}
   else if(first>=224&&first<=239){code=first&15;need=2;min=2048;}
   else if(first>=240&&first<=244){code=first&7;need=3;min=65536;}
   else{result+='\uFFFD';continue;}
   var valid=true;
   for(var n=0;n<need;n++){var next=bytes[i];if(i>=bytes.length||(next&192)!==128){valid=false;break;}if(n===0&&((first===224&&next<160)||(first===237&&next>=160)||(first===240&&next<144)||(first===244&&next>=144))){valid=false;break;}code=(code<<6)|(next&63);i++;}
   if(!valid||code<min||code>1114111||(code>=55296&&code<=57343)){result+='\uFFFD';continue;}
   if(code<=65535)result+=String.fromCharCode(code);else{code-=65536;result+=String.fromCharCode(55296+(code>>10),56320+(code&1023));}
  }return result;
 };
 H.read=function(p){var tag=HEAP32[(p+8)>>2],bits=HEAPU32[p>>2];switch(tag){case 0:return HEAP32[p>>2];case 1:return !!bits;case 2:return null;case 3:return undefined;case 8:return HEAPF64[p>>3];default:var s=H.slots.get(bits);if(!s)throw Error('ZenTS stale value handle');var v=s.refs?s.value:s.weak.deref();if(v===undefined)throw Error('ZenTS collected value handle');return v;}};
 H.write=function(p,v){var tag=3,id=0;if(v===null)tag=2;else if(typeof v==='boolean'){tag=1;id=+v;}else if(typeof v==='number'){tag=8;}else if(v!==undefined){var object=(typeof v==='object'||typeof v==='function');tag=object?-1:typeof v==='string'?-7:typeof v==='bigint'?-9:-8;id=object?H.ids.get(v):0;var s=id&&H.slots.get(id);if(!s){id=H.next++;s={id:id,refs:0,classId:0,opaque:0,context:H.activeContext};H.slots.set(id,s);if(object){H.ids.set(v,id);s.weak=new WeakRef(v);}}s.refs++;s.value=v;}HEAPU32[p>>2]=id;HEAP32[(p+4)>>2]=0;HEAP32[(p+8)>>2]=tag;HEAP32[(p+12)>>2]=tag<0?-1:0;if(tag===8)HEAPF64[p>>3]=v;};
 H.free=function(p){if(HEAP32[(p+8)>>2]>=0)return;var s=H.slots.get(HEAPU32[p>>2]);if(!s)return;if(--s.refs===0){s.value=undefined;if(!s.weak)H.slots.delete(s.id);}};
 H.state=function(id){var c=H.contexts.get(id);if(!c||!c.alive)throw Error('ZenTS runtime was reset');return c;};
 H.require=function(c,name){name=name.replace(/\\/g,'/').replace(/\.js$/,'');var parts=[];name.split('/').forEach(function(x){if(x==='..')parts.pop();else if(x&&x!=='.')parts.push(x);});name=parts.join('/');if(c.modules.has(name))return c.modules.get(name).exports;var factories=Module['ZenTSHostModules']||{};var f=factories[name];if(name.indexOf('csharp:')!==0){var top=stackSave(),np=stackAlloc(lengthBytesUTF8(name)+1);stringToUTF8(name,np,lengthBytesUTF8(name)+1);var sp=_zents_host_module_source(np);try{if(!sp)throw c.exception||Error('ZenTS module loader failed: '+name);var source=UTF8ToString(sp).replace(/\r\n/g,'\n');f=(Module['ZenTSHostSources']||{})[H.sourceKey(source)];}finally{if(sp)_free(sp);stackRestore(top);}}if(!f)throw Error('ZenTS module not precompiled: '+name);var m={exports:{}};c.modules.set(name,m);try{f(function(dep){if(dep[0]==='.')dep=name.substring(0,name.lastIndexOf('/')+1)+dep;return H.require(c,dep);},m,m.exports,c.global.CSharp,c.global.zents,c.global.console,c.global);return m.exports;}catch(e){c.modules.delete(name);throw e;}};
 }
 H.activeContext=context||H.activeContext;
 var c=context?H.contexts.get(context):null;var a,b,t=(text&&op!==23&&op!==31)?UTF8ToString(text):'';
 try{
 if(av&&op!==29&&op!==30)a=H.read(av);if(bv)b=H.read(bv);
 switch(op){
 case 0:var id=H.nextContext++;var g=Object.create(globalThis);g.globalThis=g;g.__ZENTS_HOST_JS__=true;g.console=undefined;H.contexts.set(id,{alive:true,global:g,modules:new Map(),exception:undefined,pending:false});return id;
 case 1:c.alive=false;for(var s of H.slots.values())if(s.context===context){if(s.classId&&s.opaque)_zents_host_finalize(context,s.classId,s.opaque,s.id);H.slots.delete(s.id);}H.contexts.delete(context);return 0;
 case 2:H.write(output,a);return 0;
 case 3:H.free(av);return 0;
 case 4:H.write(output,{});return 0;
 case 5:H.write(output,[]);return 0;
 case 6:H.write(output,extra<0?t:H.decode(HEAPU8.subarray(text,text+extra)));return 0;
 case 7:H.write(output,c.global);return 0;
 case 8:H.write(output,a[t]);return 0;
 case 9:a[t]=b;return 1;
 case 10:Object.defineProperty(a,t,{value:b,configurable:!!(extra&1),writable:!!(extra&2),enumerable:!!(extra&4)});return 1;
 case 11:Object.setPrototypeOf(a,b);return 1;
 case 12:var pair=b;Object.defineProperty(a,t,{get:pair[0],set:pair[1],configurable:!!(extra&1),enumerable:!!(extra&4)});return 1;
 case 13:return Object.prototype.hasOwnProperty.call(a,t)?1:0;
 case 14:return Array.isArray(a)?1:0;
 case 15:return typeof a==='function'?1:0;
 case 16:return a===b?1:0;
 case 17:return !!a?1:0;
 case 18:HEAPF64[output>>3]=Number(a);return 0;
 case 19:var v=String(a);var size=lengthBytesUTF8(v);var p=_malloc(size+1);stringToUTF8(v,p,size+1);HEAPU32[output>>2]=size;return p;
 case 20:H.write(output,c.exception);c.exception=undefined;c.pending=false;return 0;
 case 21:c.exception=a;c.pending=true;return 0;
 case 22:return c.pending?1:0;
 case 23:var params=[];for(var i=0;i<extra;i++)params.push(H.read(text+i*16));H.write(output,Reflect.apply(a,b,params));return 0;
 case 24:var script=(Module['ZenTSHostScripts']||{})[H.sourceKey(t.replace(/\r\n/g,'\n'))];if(!script)throw Error('ZenTS global script was not precompiled by the build pipeline');H.write(output,script(c.global));return 0;
 case 25:H.write(output,H.require(c,t));return 0;
 case 26:var cb=extra,owner=context;var captures=a;var func=function(){var current=H.state(owner);var args=Array.prototype.slice.call(arguments);var n=args.length;var top=stackSave(),ap=stackAlloc(n*16),rp=stackAlloc(16),tp=stackAlloc(16),dp=stackAlloc(captures.length*16);try{for(var i=0;i<n;i++)H.write(ap+i*16,args[i]);for(var i=0;i<captures.length;i++)H.write(dp+i*16,captures[i]);H.write(tp,this);_zents_host_callback(cb,rp,tp,n,ap,dp);if(HEAP32[(rp+8)>>2]===6){var error=current.exception;current.exception=undefined;current.pending=false;throw error;}var result=H.read(rp);H.free(rp);return result;}finally{for(var i=0;i<n;i++)H.free(ap+i*16);for(var i=0;i<captures.length;i++)H.free(dp+i*16);H.free(tp);stackRestore(top);}};H.write(output,func);var slot=H.slots.get(HEAPU32[output>>2]);slot.classId=number;slot.opaque=cb;H.finalizer.register(func,{context:context,classId:number,opaque:cb,id:slot.id},slot);return 0;
 case 27:var slot=H.slots.get(HEAPU32[av>>2]);slot.classId=extra;H.finalizer.register(a,{context:context,classId:extra,opaque:0,id:slot.id},slot);return 0;
 case 28:var slot=H.slots.get(HEAPU32[av>>2]);slot.opaque=extra;H.finalizer.unregister(slot);if(slot.classId)H.finalizer.register(a,{context:context,classId:slot.classId,opaque:extra,id:slot.id},slot);return 0;
 case 29:if(HEAP32[(av+8)>>2]!==-1)return 0;var slot=H.slots.get(HEAPU32[av>>2]);return slot&&slot.classId===extra?slot.opaque:0;
 case 30:if(HEAP32[(av+8)>>2]!==-1)return 0;var slot=H.slots.get(HEAPU32[av>>2]);return slot?slot.classId:0;
 case 31:var bytes=new Uint8Array(extra);bytes.set(HEAPU8.subarray(text,text+extra));H.write(output,bytes);return 0;
 case 32:if(!(a instanceof Uint8Array)||(typeof SharedArrayBuffer!=='undefined'&&a.buffer instanceof SharedArrayBuffer))return 0;var size=a.byteLength;var p=_malloc(size||1);HEAPU8.set(a,p);HEAPU32[output>>2]=size;return p;
 case 34:return H.finalized.length?1:0;
 case 33:var pending=H.finalized.splice(0);for(var x of pending){if(H.contexts.has(x.context))_zents_host_finalize(x.context,x.classId,x.opaque,x.id);H.slots.delete(x.id);}return 0;
 }
 throw Error('Unknown ZenTS HostJs operation '+op);
 }catch(e){if(c){c.exception=e;c.pending=true;}if(output){HEAPU32[output>>2]=0;HEAP32[(output+4)>>2]=0;HEAP32[(output+8)>>2]=6;HEAP32[(output+12)>>2]=0;}return -1;}
}});
