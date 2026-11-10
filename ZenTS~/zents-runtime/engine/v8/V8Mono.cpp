#if defined(ZENTS_V8_MONO_PLUGIN)
// Copyright 2026 Code Philosophy. MIT license; see package LICENSE.
#include "V8Engine.cpp"
#if defined(_WIN64) || defined(ZENTS_ENGINE_V8)
#if defined(_WIN32)
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif
using namespace zents::engine;
EXPORT int ze_GetLiveRuntimeCount() { return liveRuntimeCount; }
#include "sdk/include/v8-inspector.h"
#include <chrono>
namespace {
using InspectorSend = void (*)(const char*, int);
using InspectorPump = void (*)(int);
// Only the isolate owner enters Inspector; transport threads never enter V8.
class EditorInspector final : public v8_inspector::V8InspectorClient {
public:
 Context* context; InspectorSend send; InspectorPump pump; std::string baseUrl;
 bool paused=false, waiting=false;
 std::unique_ptr<v8_inspector::V8Inspector> inspector;
 // A dispatch keeps its connection alive even when the frontend detaches
 // from inside Runtime.evaluate's nested pause. Stale replies are discarded.
 struct Connection final : v8_inspector::V8Inspector::Channel {
  EditorInspector* owner;
  std::unique_ptr<v8_inspector::V8InspectorSession> session;
  explicit Connection(EditorInspector* p):owner(p){}
  void sendResponse(int,std::unique_ptr<v8_inspector::StringBuffer> s) override {Send(s->string());}
  void sendNotification(std::unique_ptr<v8_inspector::StringBuffer> s) override {Send(s->string());}
  void flushProtocolNotifications() override {}
  void Send(v8_inspector::StringView s){if(owner->connection.get()==this)owner->Send(s);}
 };
 std::shared_ptr<Connection> connection;
 static v8_inspector::StringView View(const char* s) {return v8_inspector::StringView((const uint8_t*)s,std::strlen(s));}
 EditorInspector(Context* c,InspectorSend s,InspectorPump p,const char* url):context(c),send(s),pump(p),baseUrl(url?url:"") {
  inspector=v8_inspector::V8Inspector::create(c->runtime->isolate,this);
  v8_inspector::V8ContextInfo info(c->value.Get(c->runtime->isolate),1,View("ZenTS Editor"));
  info.auxData=View("{\"isDefault\":true}");inspector->contextCreated(info);
 }
 v8::Local<v8::String> String(v8_inspector::StringView s) {
  auto* i=context->runtime->isolate;
  return s.is8Bit()?v8::String::NewFromOneByte(i,s.characters8(),v8::NewStringType::kNormal,(int)s.length()).ToLocalChecked():v8::String::NewFromTwoByte(i,s.characters16(),v8::NewStringType::kNormal,(int)s.length()).ToLocalChecked();
 }
 void Connect() {
  bool w=waiting;Disconnect();waiting=w;
  auto next=std::make_shared<Connection>(this);connection=next;
  next->session=inspector->connect(1,next.get(),v8_inspector::StringView(),v8_inspector::V8Inspector::kFullyTrusted,
   waiting?v8_inspector::V8Inspector::kWaitingForDebugger:v8_inspector::V8Inspector::kNotWaitingForDebugger);
 }
 void Disconnect() {waiting=false;paused=false;auto old=std::move(connection);if(old&&old->session)old->session->resume();}
 void Send(v8_inspector::StringView s) {v8::HandleScope h(context->runtime->isolate);v8::String::Utf8Value text(context->runtime->isolate,String(s));send(*text,text.length());}
 void runMessageLoopOnPause(int) override {paused=true;while(paused&&connection)pump(10);paused=false;}
 void quitMessageLoopOnPause() override {paused=false;}
 void runIfWaitingForDebugger(int) override {waiting=false;}
 double currentTimeMS() override {return std::chrono::duration<double,std::milli>(std::chrono::system_clock::now().time_since_epoch()).count();}
 v8::Local<v8::Context> ensureDefaultContextInGroup(int) override {return context->value.Get(context->runtime->isolate);}
 std::unique_ptr<v8_inspector::StringBuffer> resourceNameToUrl(const v8_inspector::StringView& name) override {
  v8::HandleScope h(context->runtime->isolate);v8::String::Utf8Value text(context->runtime->isolate,String(name));std::string url(*text,text.length());
  if(url.find(":")!=std::string::npos||baseUrl.empty())return v8_inspector::StringBuffer::create(name);
  if(url.size()<3||(url.compare(url.size()-3,3,".js")!=0&&(url.size()<4||url.compare(url.size()-4,4,".mjs")!=0)))url+=".js";
  // Inspector's 8-bit StringView is Latin-1, not UTF-8. Encode canonical
  // path bytes as an ASCII URL; preserve caller-supplied URLs as StringView.
  std::string encoded;const char* hex="0123456789ABCDEF";
  for(unsigned char ch:url){if((ch>='a'&&ch<='z')||(ch>='A'&&ch<='Z')||(ch>='0'&&ch<='9')||ch=='/'||ch=='-'||ch=='_'||ch=='.'||ch=='~')encoded+=ch;else{encoded+='%';encoded+=hex[ch>>4];encoded+=hex[ch&15];}}
  url=baseUrl+encoded;
  return v8_inspector::StringBuffer::create(View(url.c_str()));
 }
 ~EditorInspector() override {Disconnect();inspector->contextDestroyed(context->value.Get(context->runtime->isolate));inspector.reset();}
};
std::unordered_map<Context*,std::unique_ptr<EditorInspector>> editorInspectors;
EditorInspector* FindInspector(void* handle) {for(auto& item:editorInspectors)if(item.second.get()==handle)return item.second.get();return nullptr;}
void DestroyInspector(Context* c) {auto it=editorInspectors.find(c);if(it!=editorInspectors.end()){Scope scope(c);editorInspectors.erase(it);}}
}
EXPORT int ze_InspectorCount(){return (int)editorInspectors.size();}
EXPORT void* ze_InspectorCreate(Context* c,InspectorSend send,InspectorPump pump,const char* url) {
 DestroyInspector(c);Scope scope(c);auto value=std::make_unique<EditorInspector>(c,send,pump,url);auto* ptr=value.get();editorInspectors[c]=std::move(value);return ptr;
}
EXPORT void ze_InspectorDestroy(void* handle){if(auto* p=FindInspector(handle))DestroyInspector(p->context);}
EXPORT void ze_InspectorConnect(void* handle){if(auto* p=FindInspector(handle)){Scope scope(p->context);p->Connect();}}
EXPORT void ze_InspectorDisconnect(void* handle){if(auto* p=FindInspector(handle)){Scope scope(p->context);p->Disconnect();}}
EXPORT void ze_InspectorDispatch(void* handle,const uint8_t* data,int length) {
 auto* p=FindInspector(handle);if(!p||!p->connection)return;auto active=p->connection;Scope scope(p->context);
 auto text=v8::String::NewFromUtf8(p->context->runtime->isolate,(const char*)data,v8::NewStringType::kNormal,length).ToLocalChecked();
 v8::String::Value words(p->context->runtime->isolate,text);active->session->dispatchProtocolMessage(v8_inspector::StringView(*words,words.length()));
}
// The managed lifetime lock leases this handle while posting. The interrupt
// callback resolves it again on the owner thread, so queued work cannot use a
// destroyed session. This follows V8 test/inspector/task-runner.cc's protocol-only
// interrupt loop; never run gameplay or Editor updates inside this callback.
EXPORT void ze_InspectorInterrupt(void* handle) {
 auto* p=static_cast<EditorInspector*>(handle);
 p->context->runtime->isolate->RequestInterrupt([](v8::Isolate* isolate,void* token){
  auto* current=FindInspector(token);
  if(current&&current->context->runtime->isolate==isolate)current->pump(0);
 },handle);
}
EXPORT void ze_InspectorWait(void* handle){if(auto* p=FindInspector(handle)){Scope scope(p->context);p->waiting=true;while(p->waiting)p->pump(10);}}


namespace
{
using ManagedFn = void (*)(Context *, Value *, int, Value *, int, Value *);
struct ManagedCallback
{
    ManagedFn fn;
    int magic;
};
ClassID managedCallbackClass = 0;
void FreeManaged(Runtime *, Value v)
{
    delete static_cast<ManagedCallback *>(GetOpaque(v, managedCallbackClass));
}
Value InvokeManaged(Context *c, Value receiver, int argc, Value *argv, int, Value *data)
{
    auto *cb = static_cast<ManagedCallback *>(GetOpaque(data[0], managedCallbackClass));
    Value result = Undefined();
    cb->fn(c, &receiver, argc, argv, cb->magic, &result);
    return static_cast<int64_t>(result.opaque[1]) == static_cast<int64_t>(static_cast<int32_t>(0xFFFF5A12))
               ? Exception()
               : result;
}
Value NewManaged(Context *c, void *fn, int magic, int length)
{
    if (!managedCallbackClass)
        NewClassID(&managedCallbackClass);
    ClassDef def{"ZenTS.ManagedCallback", FreeManaged};
    NewClass(GetRuntime(c), managedCallbackClass, &def);
    Value holder = NewObjectClass(c, managedCallbackClass);
    SetOpaque(holder, new ManagedCallback{reinterpret_cast<ManagedFn>(fn), magic});
    Value result = NewCFunctionData(c, InvokeManaged, length, 0, 1, &holder);
    FreeValue(c, holder);
    return result;
}
} // namespace
EXPORT void ze_NewManaged(Context *c, void *fn, int magic, int length, const char *name, Value *out)
{
    *out = NewManaged(c, fn, magic, length);
    Scope scope(c);
    Local(c, *out).As<v8::Function>()->SetName(Text(c->runtime->isolate, name));
}
EXPORT void ze_Dup(Value *v, Value *out)
{
    *out = DupValue(nullptr, *v);
}
EXPORT void ze_Free(Context *c, Value *v)
{
    FreeValue(c, *v);
}

EXPORT int ze_HasOwnProperty(Context* c, const Value* value, const char* name) { return HasOwnProperty(c,*value,name); }
EXPORT void ze_NewByteArrayCopy(Context *c, const uint8_t *data, size_t length, Value *out)
{ *out = NewByteArrayCopy(c, data, length); }
EXPORT const uint8_t *ze_GetByteArrayData(Context *c, const Value *value, size_t *length)
{ return GetByteArrayData(c, *value, length); }
EXPORT void *ze_NewRuntime()
{
    return NewRuntime();
}
EXPORT void ze_FreeRuntime(void *rt)
{
    for (auto* c : reinterpret_cast<Runtime *>(rt)->contexts) DestroyInspector(c);
    FreeRuntime(reinterpret_cast<Runtime *>(rt));
}
EXPORT void *ze_NewContext(void *rt)
{
    return NewContext(reinterpret_cast<Runtime *>(rt));
}
EXPORT void ze_FreeContext(void *ctx)
{
    DestroyInspector(reinterpret_cast<Context *>(ctx));
    FreeContext(reinterpret_cast<Context *>(ctx));
}
EXPORT void *ze_GetRuntime(void *ctx)
{
    return GetRuntime(reinterpret_cast<Context *>(ctx));
}
EXPORT void ze_SetContextOpaque(void *ctx, void *opaque)
{
    reinterpret_cast<Context *>(ctx)->opaque = opaque;
}
EXPORT void *ze_GetContextOpaque(void *ctx)
{
    return reinterpret_cast<Context *>(ctx)->opaque;
}
EXPORT void ze_FreeCString(void *ctx, void *ptr)
{
    FreeCString(reinterpret_cast<Context *>(ctx), static_cast<const char *>(ptr));
}
EXPORT void ze_SetModuleLoaderFunc(void *rt, void *moduleNormalize, void *moduleLoader, void *opaque)
{
    SetModuleLoaderFunc(reinterpret_cast<Runtime *>(rt), nullptr, reinterpret_cast<ModuleLoader *>(moduleLoader),
                        opaque);
}
EXPORT void ze_StdInitHandlers(void *rt)
{
    StdInitHandlers(reinterpret_cast<Runtime *>(rt));
}
EXPORT void ze_StdFreeHandlers(void *rt)
{
    StdFreeHandlers(reinterpret_cast<Runtime *>(rt));
}
EXPORT void ze_StdAddHelpers(void *ctx, int argc, void *argv)
{
    StdAddHelpers(reinterpret_cast<Context *>(ctx), argc, static_cast<char **>(argv));
}
EXPORT int ze_IsJobPending(void *rt)
{
    return IsJobPending(reinterpret_cast<Runtime *>(rt));
}
EXPORT int ze_ExecutePendingJob(void *rt, void **pctx)
{
    return ExecutePendingJob(reinterpret_cast<Runtime *>(rt), reinterpret_cast<Context **>(pctx));
}
EXPORT void ze_Eval(void *ctx, const char *input, size_t inputLen, const char *filename, int evalFlags, Value *result)
{
    *result = Eval(reinterpret_cast<Context *>(ctx), input, inputLen, filename, evalFlags);
}
EXPORT void ze_Call(void *ctx, Value *funcObj, Value *thisObj, int argc, void *argv, Value *result)
{
    *result = Call(reinterpret_cast<Context *>(ctx), *funcObj, *thisObj, argc, static_cast<Value *>(argv));
}
EXPORT void ze_GetException(void *ctx, Value *result)
{
    *result = GetException(reinterpret_cast<Context *>(ctx));
}
EXPORT void ze_Throw(void *ctx, Value *obj, Value *result)
{
    *result = Throw(reinterpret_cast<Context *>(ctx), *obj);
}
EXPORT void ze_NewObject(void *ctx, Value *result)
{
    *result = NewObject(reinterpret_cast<Context *>(ctx));
}
EXPORT void ze_GetGlobalObject(void *ctx, Value *result)
{
    *result = GetGlobalObject(reinterpret_cast<Context *>(ctx));
}
EXPORT int ze_SetPropertyStr(void *ctx, Value *thisObj, const char *prop, Value *val)
{
    return SetPropertyStr(reinterpret_cast<Context *>(ctx), *thisObj, prop, *val);
}
EXPORT void ze_GetPropertyStr(void *ctx, Value *thisObj, const char *prop, Value *result)
{
    *result = GetPropertyStr(reinterpret_cast<Context *>(ctx), *thisObj, prop);
}
EXPORT int ze_ToInt32(void *ctx, int *pres, Value *val)
{
    return ToInt32(reinterpret_cast<Context *>(ctx), pres, *val);
}
EXPORT int ze_ToFloat64(void *ctx, double *pres, Value *val)
{
    return ToFloat64(reinterpret_cast<Context *>(ctx), pres, *val);
}
EXPORT int ze_ToBool(void *ctx, Value *val)
{
    return ToBool(reinterpret_cast<Context *>(ctx), *val);
}
EXPORT void *ze_ToCStringLen2(void *ctx, size_t *plen, Value *val, int cesu8)
{
    return const_cast<char *>(ToCStringLen(reinterpret_cast<Context *>(ctx), plen, *val));
}
EXPORT void ze_NewStringLen(void *ctx, const char *str, size_t len, Value *result)
{
    *result = NewStringLen(reinterpret_cast<Context *>(ctx), str, len);
}
EXPORT int ze_IsFunction(void *ctx, Value *val)
{
    return IsFunction(reinterpret_cast<Context *>(ctx), *val);
}
EXPORT void ze_EvalFunction(void *ctx, Value *funObj, Value *result)
{
    *result = LoadModule(reinterpret_cast<Context *>(ctx), "", GetCompiledModule(*funObj)->name.c_str());
}
EXPORT int ze_ResolveModule(void *ctx, Value *funObj)
{
    return 0;
}
EXPORT void ze_LoadModule(void *ctx, const char *basename, const char *filename, Value *result)
{
    *result = LoadModule(reinterpret_cast<Context *>(ctx), basename, filename);
}
EXPORT void ze_GetModuleNamespace(void *ctx, void *moduleDef, Value *result)
{
    auto *c = reinterpret_cast<Context *>(ctx);
    Scope scope(c);
    *result = Wrap(c, static_cast<Module *>(moduleDef)->value.Get(c->runtime->isolate)->GetModuleNamespace());
}
EXPORT int ze_PromiseState(void *ctx, Value *promise)
{
    return PromiseState(reinterpret_cast<Context *>(ctx), *promise);
}
EXPORT void ze_PromiseResult(void *ctx, Value *promise, Value *result)
{
    *result = PromiseResult(reinterpret_cast<Context *>(ctx), *promise);
}
EXPORT void ze_NewError(void *ctx, Value *result)
{
    auto *c = reinterpret_cast<Context *>(ctx);
    Scope scope(c);
    *result = Wrap(c, v8::Exception::Error(Text(c->runtime->isolate, "")));
}
EXPORT void ze_NewObjectProto(void *ctx, Value *proto, Value *result)
{
    auto *c = reinterpret_cast<Context *>(ctx);
    *result = NewObject(c);
    SetPrototype(c, *result, *proto);
}
EXPORT int ze_SetPrototype(void *ctx, Value *obj, Value *proto)
{
    return SetPrototype(reinterpret_cast<Context *>(ctx), *obj, *proto);
}
EXPORT int ze_SetConstructor(void *ctx, Value *funcObj, Value *proto)
{
    auto *c = reinterpret_cast<Context *>(ctx);
    SetPropertyStr(c, *funcObj, "prototype", DupValue(c, *proto));
    return SetPropertyStr(c, *proto, "constructor", DupValue(c, *funcObj));
}
EXPORT int ze_SetConstructorBit(void *ctx, Value *funcObj, int val)
{
    return SetConstructorBit(reinterpret_cast<Context *>(ctx), *funcObj, val);
}
EXPORT int ze_DefinePropertyGetSet(void *ctx, Value *thisObj, uint32_t prop, Value *getter, Value *setter, int flags)
{
    return DefinePropertyGetSet(reinterpret_cast<Context *>(ctx), *thisObj, prop, *getter, *setter, flags);
}
EXPORT uint32_t ze_NewAtom(void *ctx, const char *str)
{
    return NewAtom(reinterpret_cast<Context *>(ctx), str);
}
EXPORT void ze_FreeAtom(void *ctx, uint32_t atom)
{
    FreeAtom(reinterpret_cast<Context *>(ctx), atom);
}
EXPORT int ze_DefinePropertyValueStr(void *ctx, Value *thisObj, const char *prop, Value *val, int flags)
{
    return DefinePropertyValueStr(reinterpret_cast<Context*>(ctx),*thisObj,prop,*val,flags);
}
EXPORT int ze_IsArray(void *ctx, Value *val)
{
    return IsArray(reinterpret_cast<Context *>(ctx), *val);
}
EXPORT void ze_NewArray(void *ctx, Value *result)
{
    *result = NewArray(reinterpret_cast<Context *>(ctx));
}
EXPORT int ze_SetPropertyUint32(void *ctx, Value *thisObj, uint32_t idx, Value *val)
{
    return SetPropertyUint32(reinterpret_cast<Context *>(ctx), *thisObj, idx, *val);
}
EXPORT void ze_GetPropertyUint32(void *ctx, Value *thisObj, uint32_t idx, Value *result)
{
    *result = GetPropertyUint32(reinterpret_cast<Context *>(ctx), *thisObj, idx);
}
#endif

#endif
