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
    FreeRuntime(reinterpret_cast<Runtime *>(rt));
}
EXPORT void *ze_NewContext(void *rt)
{
    return NewContext(reinterpret_cast<Runtime *>(rt));
}
EXPORT void ze_FreeContext(void *ctx)
{
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
