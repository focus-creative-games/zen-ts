// Copyright 2026 Code Philosophy. MIT License.
// Host JS backend: no interpreter or V8 SDK is compiled into wasm.
#if defined(__EMSCRIPTEN__)
#include "../EngineApi.h"
#include <emscripten.h>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>
namespace zents { namespace engine {
struct Runtime { int id; ExternalExceptionHandler* exceptionHandler=nullptr; std::unordered_map<ClassID,ClassFinalizer*> classes; };
struct Context {Runtime* runtime;};
struct Module {};
struct Callback {Context* context;CFunction* simple;CFunctionData* data;int magic;};
static std::unordered_map<int,Runtime*> runtimes;
extern "C" int HostOperation(int op,int context,void* output,const Value* av,const Value* bv,const char* text,int extra,double number);
static int Id(Context*c){return c?c->runtime->id:0;}
static Value Imm(uint64_t x,int64_t tag){return Value{{x,(uint64_t)tag}};}
static Value Op(Context*c,int op,Value a=Imm(0,3),Value b=Imm(0,3),const char*t=nullptr,int n=0,double d=0){Value result=Imm(0,3);HostOperation(op,Id(c),&result,&a,&b,t,n,d);return result;}
Backend GetBackend(){return Backend::HostJs;}
Value Undefined(){return Imm(0,3);} Value Null(){return Imm(0,2);} Value Exception(){return Imm(0,6);}
void* GetIdentity(ValueConst v){return (void*)(uintptr_t)v.opaque[0];} Module* GetCompiledModule(ValueConst){return nullptr;}
void SetExternalExceptionHandler(Runtime*r,ExternalExceptionHandler*h){r->exceptionHandler=h;}
extern "C" void zents_host_modules();
Runtime* NewRuntime(){zents_host_modules();auto*r=new Runtime();r->id=HostOperation(0,0,nullptr,nullptr,nullptr,nullptr,0,0);runtimes[r->id]=r;return r;}
Context* NewContext(Runtime*r){auto*c=new Context();c->runtime=r;return c;} Runtime* GetRuntime(Context*c){return c->runtime;}
void FreeContext(Context*c){delete c;} void FreeRuntime(Runtime*r){HostOperation(1,r->id,nullptr,nullptr,nullptr,nullptr,0,0);runtimes.erase(r->id);delete r;}
void RunGC(Runtime*r){HostOperation(33,r->id,nullptr,nullptr,nullptr,nullptr,0,0);}
Value DupValue(Context*c,ValueConst v){return Op(c,2,v);} void FreeValue(Context*c,Value v){HostOperation(3,Id(c),nullptr,&v,nullptr,nullptr,0,0);}
Value NewObject(Context*c){return Op(c,4);} Value NewArray(Context*c){return Op(c,5);}
Value NewObjectClass(Context*c,int id){Value v=NewObject(c);HostOperation(27,Id(c),nullptr,&v,nullptr,nullptr,id,0);return v;}
Value NewBool(Context*,int v){return Imm(v!=0,1);} Value NewInt32(Context*,int32_t v){return Imm((uint32_t)v,0);} Value NewUint32(Context*c,uint32_t v){return NewFloat64(c,v);} Value NewInt64(Context*c,int64_t v){return NewFloat64(c,(double)v);} Value NewFloat64(Context*,double d){uint64_t b;std::memcpy(&b,&d,8);return Imm(b,8);}
Value NewString(Context*c,const char*s){return Op(c,6,Undefined(),Undefined(),s?s:"",-1);} Value NewStringLen(Context*c,const char*s,size_t n){return Op(c,6,Undefined(),Undefined(),s,(int)n);}
int ToBool(Context*c,ValueConst v){return HostOperation(17,Id(c),nullptr,&v,nullptr,nullptr,0,0);}
int ToFloat64(Context*c,double*out,ValueConst v){return HostOperation(18,Id(c),out,&v,nullptr,nullptr,0,0);} int ToInt32(Context*c,int32_t*out,ValueConst v){double d=0;int r=ToFloat64(c,&d,v);*out=(int32_t)d;return r;} int ToInt64(Context*c,int64_t*out,ValueConst v){double d=0;int r=ToFloat64(c,&d,v);*out=(int64_t)d;return r;}
const char* ToCStringLen(Context*c,size_t*size,ValueConst v){return (const char*)(uintptr_t)HostOperation(19,Id(c),size,&v,nullptr,nullptr,0,0);} const char* ToCString(Context*c,ValueConst v){size_t n;return ToCStringLen(c,&n,v);} void FreeCString(Context*,const char*p){std::free((void*)p);}
Value GetGlobalObject(Context*c){return Op(c,7);} Value GetException(Context*c){return Op(c,20);} Value Throw(Context*c,Value v){HostOperation(21,Id(c),nullptr,&v,nullptr,nullptr,0,0);FreeValue(c,v);return Exception();} int HasException(Context*c){return HostOperation(22,Id(c),nullptr,nullptr,nullptr,nullptr,0,0);}
Value GetPropertyStr(Context*c,ValueConst v,const char*k){return Op(c,8,v,Undefined(),k);} Value GetPropertyUint32(Context*c,ValueConst v,uint32_t k){return GetPropertyStr(c,v,std::to_string(k).c_str());}
int SetPropertyStr(Context*c,ValueConst a,const char*k,Value b){int r=HostOperation(9,Id(c),nullptr,&a,&b,k,0,0);FreeValue(c,b);return r;} int SetPropertyUint32(Context*c,ValueConst a,uint32_t k,Value b){return SetPropertyStr(c,a,std::to_string(k).c_str(),b);}
int DefinePropertyValueStr(Context*c,ValueConst a,const char*k,Value b,int flags){int r=HostOperation(10,Id(c),nullptr,&a,&b,k,flags,0);FreeValue(c,b);return r;}
int HasOwnProperty(Context*c,ValueConst a,const char*k){return HostOperation(13,Id(c),nullptr,&a,nullptr,k,0,0);} int SetPrototype(Context*c,ValueConst a,ValueConst b){return HostOperation(11,Id(c),nullptr,&a,&b,nullptr,0,0);} int SetConstructorBit(Context*,ValueConst,int){return 0;}
static std::unordered_map<Atom,std::string> atoms;static Atom nextAtom=1; Atom NewAtom(Context*,const char*k){Atom n=nextAtom++;atoms[n]=k;return n;}void FreeAtom(Context*,Atom n){atoms.erase(n);}
int DefinePropertyGetSet(Context*c,ValueConst a,Atom k,Value get,Value set,int flags){Value pair=NewArray(c);SetPropertyUint32(c,pair,0,get);SetPropertyUint32(c,pair,1,set);int r=HostOperation(12,Id(c),nullptr,&a,&pair,atoms[k].c_str(),flags,0);FreeValue(c,pair);return r;}
ClassID NewClassID(ClassID*id){static ClassID next=1;return *id=next++;}int NewClass(Runtime*r,ClassID id,const ClassDef*d){r->classes[id]=d->finalizer;return 0;}
ClassID GetClassID(Value v){return HostOperation(30,0,nullptr,&v,nullptr,nullptr,0,0);} void SetOpaque(Value v,void*p){HostOperation(28,0,nullptr,&v,nullptr,nullptr,(int)(uintptr_t)p,0);} void* GetOpaque(ValueConst v,ClassID id){return (void*)(uintptr_t)HostOperation(29,0,nullptr,&v,nullptr,nullptr,id,0);}
int IsArray(Context*c,ValueConst v){return HostOperation(14,Id(c),nullptr,&v,nullptr,nullptr,0,0);}int IsFunction(Context*c,ValueConst v){return HostOperation(15,Id(c),nullptr,&v,nullptr,nullptr,0,0);}int IsBigInt(Context*,ValueConst v){return (int64_t)v.opaque[1]==-9;}int StrictEq(Context*c,ValueConst a,ValueConst b){return HostOperation(16,Id(c),nullptr,&a,&b,nullptr,0,0);}
int IsNumber(ValueConst v){return v.opaque[1]==0||v.opaque[1]==8;}int IsBool(ValueConst v){return v.opaque[1]==1;}int IsNull(ValueConst v){return v.opaque[1]==2;}int IsUndefined(ValueConst v){return v.opaque[1]==3;}int IsException(ValueConst v){return v.opaque[1]==6;}int IsObject(ValueConst v){return (int64_t)v.opaque[1]==-1;}int IsString(ValueConst v){return (int64_t)v.opaque[1]==-7;}
Value Call(Context*c,ValueConst f,ValueConst recv,int argc,ValueConst*argv){return Op(c,23,f,recv,(const char*)argv,argc);}
Value Eval(Context*c,const char*src,size_t len,const char*,int flags){if(flags&EvalModule)return ThrowTypeError(c,"HostJs modules must be precompiled by the build pipeline");std::string s(src,len);return Op(c,24,Undefined(),Undefined(),s.c_str());}
Value LoadModule(Context*c,const char*,const char*name){return Op(c,25,Undefined(),Undefined(),name);} int PromiseState(Context*,Value){return 1;}Value PromiseResult(Context*c,Value v){return DupValue(c,v);}int IsJobPending(Runtime*r){return HostOperation(34,r->id,nullptr,nullptr,nullptr,nullptr,0,0);}int ExecutePendingJob(Runtime*r,Context**){RunGC(r);return 0;}
void StdInitHandlers(Runtime*){}void StdFreeHandlers(Runtime*){}void StdAddHelpers(Context*,int,char**){}void SetModuleLoaderFunc(Runtime*,ModuleNormalize*,ModuleLoader*,void*){}
static ClassID callbackClass=0;
static void FreeCallback(Runtime*,Value v){delete (Callback*)GetOpaque(v,callbackClass);}
static Value CallbackValue(Context*c,Callback*cb,int count,ValueConst*data){if(!callbackClass)NewClassID(&callbackClass);ClassDef def={"ZenTS.Callback",FreeCallback};NewClass(c->runtime,callbackClass,&def);Value captures=NewArray(c);for(int i=0;i<count;i++)SetPropertyUint32(c,captures,i,DupValue(c,data[i]));Value f=Op(c,26,captures,Undefined(),nullptr,(int)(uintptr_t)cb,callbackClass);FreeValue(c,captures);return f;}
Value NewCFunction(Context*c,CFunction*f,const char*,int){auto*cb=new Callback{c,f,nullptr,0};return CallbackValue(c,cb,0,nullptr);}Value NewCFunctionData(Context*c,CFunctionData*f,int,int magic,int n,ValueConst*data){auto*cb=new Callback{c,nullptr,f,magic};return CallbackValue(c,cb,n,data);}
Value NewByteArrayCopy(Context*c,const uint8_t*d,size_t n){return Op(c,31,Undefined(),Undefined(),(const char*)d,(int)n);} const uint8_t* GetByteArrayData(Context*c,ValueConst v,size_t*n){static std::vector<uint8_t> bytes;auto*p=(uint8_t*)(uintptr_t)HostOperation(32,Id(c),n,&v,nullptr,nullptr,0,0);if(!p)return nullptr;bytes.assign(p,p+*n);std::free(p);static uint8_t empty;return bytes.empty()?&empty:bytes.data();}
static Value Error(Context*c,const char*kind,const char*format,va_list args){char text[4096];std::vsnprintf(text,sizeof(text),format,args);Value g=GetGlobalObject(c),ctor=GetPropertyStr(c,g,kind),msg=NewString(c,text),err=Call(c,ctor,Undefined(),1,&msg);FreeValue(c,msg);FreeValue(c,ctor);FreeValue(c,g);return Throw(c,err);}
#define ERROR_FN(name,kind) Value name(Context*c,const char*format,...){va_list args;va_start(args,format);Value v=Error(c,kind,format,args);va_end(args);return v;}
ERROR_FN(ThrowTypeError,"TypeError") ERROR_FN(ThrowRangeError,"RangeError") ERROR_FN(ThrowReferenceError,"ReferenceError") ERROR_FN(ThrowInternalError,"Error")
extern "C" EMSCRIPTEN_KEEPALIVE void zents_host_callback(Callback*cb,Value*out,Value*receiver,int argc,Value*argv,Value*data){try{*out=cb->simple?cb->simple(cb->context,*receiver,argc,argv):cb->data(cb->context,*receiver,argc,argv,cb->magic,data);}catch(...){auto*r=cb->context->runtime;*out=r->exceptionHandler?r->exceptionHandler(cb->context,std::current_exception()):ThrowInternalError(cb->context,"native callback failed");}}
extern "C" EMSCRIPTEN_KEEPALIVE void zents_host_finalize(int context,int classId,void*opaque,int id){auto it=runtimes.find(context);if(it==runtimes.end())return;auto f=it->second->classes.find(classId);if(f==it->second->classes.end()||!f->second)return;Value v=Imm(id,-1);f->second(it->second,v);}
} }
#endif
