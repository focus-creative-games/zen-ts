// Copyright 2026 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#include "DelegateMarshal.h"
#include "MarshalMeta.h"
#include "ObjectRegistry.h"
#include "OpaqueValueMarshal.h"
#include "../jvm/JsEnv.h"
#include "../jvm/JsGlobalRefs.h"
#include "../mt/MetaBinding.h"

#include "../utils/MetadataUtil.h"
#include "../utils/JsException.h"

#include "gc/GarbageCollector.h"
#include "vm/Class.h"
#include "vm/Method.h"
#include "vm/Object.h"
#include "vm/Runtime.h"
#include "vm/String.h"
#include "vm/Type.h"
#include "utils/Memory.h"

#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

namespace zents
{
struct TsDelegateCtorCache
{
    const MethodInfo* originalInvokeMethod;
    const MethodMarshalCtx* marshalCtx;
};

static std::unordered_map<const Il2CppClass*, const TsDelegateCtorCache*> s_ctorByClass;

struct JsDelegateCacheKey
{
    const Il2CppClass* delClass;
    void* jsFuncPtr;

    bool operator==(const JsDelegateCacheKey& o) const
    {
        return delClass == o.delClass && jsFuncPtr == o.jsFuncPtr;
    }
};

struct JsDelegateCacheKeyHash
{
    size_t operator()(const JsDelegateCacheKey& k) const
    {
        return (size_t)k.delClass ^ ((size_t)k.jsFuncPtr << 1);
    }
};

static std::unordered_map<JsDelegateCacheKey, Il2CppDelegate*, JsDelegateCacheKeyHash> s_jsFuncToDelegate;
static Il2CppDelegate** s_rootedDelegates = nullptr;
static int32_t s_rootedCap = 0;
static int32_t s_rootedCount = 0;

static void EnsureRootCapacity(int32_t minCap)
{
    if (minCap <= s_rootedCap)
        return;
    int32_t newCap = s_rootedCap == 0 ? 32 : s_rootedCap;
    while (newCap < minCap)
        newCap *= 2;
    auto* neu = (Il2CppDelegate**)il2cpp::utils::Memory::Malloc(sizeof(Il2CppDelegate*) * (size_t)newCap);
    std::memset(neu, 0, sizeof(Il2CppDelegate*) * (size_t)newCap);
    if (s_rootedDelegates != nullptr)
    {
        std::memcpy(neu, s_rootedDelegates, sizeof(Il2CppDelegate*) * (size_t)s_rootedCount);
        il2cpp::gc::GarbageCollector::UnregisterRoot((char*)s_rootedDelegates);
        il2cpp::utils::Memory::Free(s_rootedDelegates);
    }
    s_rootedDelegates = neu;
    s_rootedCap = newCap;
    il2cpp::gc::GarbageCollector::RegisterRoot(
        (char*)s_rootedDelegates, sizeof(Il2CppDelegate*) * (size_t)s_rootedCap);
}

static void RootDelegate(Il2CppDelegate* del)
{
    EnsureRootCapacity(s_rootedCount + 1);
    s_rootedDelegates[s_rootedCount++] = del;
}

void DelegateMarshal::Reset()
{
    for (int32_t i = 0; i < s_rootedCount; i++)
    {
        Il2CppDelegate* del = s_rootedDelegates[i];
        if (del == nullptr || del->target == nullptr)
            continue;
        Il2CppClass* methodClass = MetadataUtil::GetJsMethodClass();
        if (methodClass != nullptr && del->target->klass == methodClass)
            reinterpret_cast<JsMethod*>(del->target)->disposed = true;
    }
    s_jsFuncToDelegate.clear();
    if (s_rootedDelegates != nullptr)
    {
        il2cpp::gc::GarbageCollector::UnregisterRoot((char*)s_rootedDelegates);
        il2cpp::utils::Memory::Free(s_rootedDelegates);
        s_rootedDelegates = nullptr;
    }
    s_rootedCap = 0;
    s_rootedCount = 0;
}

static bool IsValueTypeParam(const Il2CppType* type)
{
    if (type->byref)
        return false;
    switch (type->type)
    {
    case IL2CPP_TYPE_BOOLEAN:
    case IL2CPP_TYPE_I1:
    case IL2CPP_TYPE_U1:
    case IL2CPP_TYPE_CHAR:
    case IL2CPP_TYPE_I2:
    case IL2CPP_TYPE_U2:
    case IL2CPP_TYPE_I4:
    case IL2CPP_TYPE_U4:
    case IL2CPP_TYPE_R4:
    case IL2CPP_TYPE_I8:
    case IL2CPP_TYPE_U8:
    case IL2CPP_TYPE_R8:
    case IL2CPP_TYPE_I:
    case IL2CPP_TYPE_U:
    case IL2CPP_TYPE_VALUETYPE:
        return true;
    case IL2CPP_TYPE_GENERICINST:
        return type->data.generic_class->type->type == IL2CPP_TYPE_VALUETYPE;
    default:
        return false;
    }
}

static JSValue PushArg(
    JSContext* ctx,
    const MethodMarshalCtx* mctx,
    uint8_t paramIndex,
    const Il2CppType* type,
    void* argSlot)
{
    if (type->byref)
        return OpaqueValueMarshal::Push(ctx, argSlot, type);

    if (mctx != nullptr && mctx->paramsMeta != nullptr && mctx->paramsMeta[paramIndex] != nullptr)
        return mctx->paramsMeta[paramIndex]->cs2jsWriter(ctx, argSlot, mctx->paramsMeta[paramIndex]);

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(type);
    if (meta != nullptr)
        return meta->cs2jsWriter(ctx, argSlot, meta);

    JsException::ThrowFormat("zents: unsupported delegate param type %d", (int)type->type);
    return JS_UNDEFINED;
}

static void PopRet(
    JSContext* ctx, const MethodMarshalCtx* mctx, const Il2CppType* type, JSValue value, void* __ret)
{
    if (type->type == IL2CPP_TYPE_VOID)
        return;

    const MarshalMetaInfo* meta =
        (mctx != nullptr && mctx->retMeta != nullptr) ? mctx->retMeta : MarshalMeta::TryCreateDefault(type);
    if (meta == nullptr)
        JsException::ThrowFormat("zents: unsupported delegate return type %d", (int)type->type);

    void* tempStorage = nullptr;
    void* dest = meta->passByValue ? &tempStorage : __ret;
    meta->js2csWriter(ctx, value, dest, meta);
    if (JS_HasException(ctx))
        JsEnv::ThrowPendingException();
    if (meta->passByValue)
        *reinterpret_cast<void**>(__ret) = tempStorage;
}

// Unity InvokerMethod convention: value-type args are pointers; ref types live in __args[i].
static void JsDelegateInvoke(Il2CppMethodPointer /*methodPtr*/, const MethodInfo* method, void* __this, void** __args, void* __ret)
{
    JsMethod* target = reinterpret_cast<JsMethod*>(__this);
    JSContext* ctx = JsEnv::GetContext();
    if (target == nullptr || target->disposed || ctx == nullptr)
    {
        il2cpp::vm::Exception::Raise(
            il2cpp::vm::Exception::GetInvalidOperationException(
                "zents: JS runtime was reset; re-bind GetFunction delegates."));
    }

    OpaqueParameterScope opaqueScope;

    JSValue func = JsGlobalRefs::Get(target->funcRef);
    const MethodMarshalCtx* mctx = reinterpret_cast<const MethodMarshalCtx*>(target->methodMarshalCtx);
    const uint8_t argc = method->parameters_count;

    /* Prefer stack for typical delegate arity; heap only for unusually large Invoke. */
    constexpr uint8_t kStackArgc = 16;
    JSValue stackArgv[kStackArgc];
    JSValue* argv = nullptr;
    bool heapArgv = false;
    if (argc > 0)
    {
        if (argc <= kStackArgc)
            argv = stackArgv;
        else
        {
            argv = (JSValue*)il2cpp::utils::Memory::Malloc(sizeof(JSValue) * argc);
            heapArgv = true;
        }
    }

    for (uint8_t i = 0; i < argc; i++)
    {
        const Il2CppType* pt = method->parameters[i];
        void* data = IsValueTypeParam(pt) ? __args[i] : &__args[i];
        if (pt->byref)
            data = &__args[i];
        argv[i] = PushArg(ctx, mctx, i, pt, data);
    }

    JSValue thisVal = JS_UNDEFINED;
    JSValue result = JS_Call(ctx, func, thisVal, argc, argv);

    for (uint8_t i = 0; i < argc; i++)
        JS_FreeValue(ctx, argv[i]);
    if (heapArgv)
        il2cpp::utils::Memory::Free(argv);

    if (JS_IsException(result))
        JsEnv::ThrowPendingException();

    PopRet(ctx, mctx, method->return_type, result, __ret);
    JS_FreeValue(ctx, result);
}

// Closed-delegate invoke_impl trampolines (Win64 ABI; ≤4-byte ints share i4 slots).
static void Trampoline_Action(Il2CppObject* target, const MethodInfo* method)
{
    JsDelegateInvoke(nullptr, method, target, nullptr, nullptr);
}

static void Trampoline_Action_i4(Il2CppObject* target, int32_t a, const MethodInfo* method)
{
    void* args[1] = { &a };
    JsDelegateInvoke(nullptr, method, target, args, nullptr);
}

static void Trampoline_Action_byref_i4(Il2CppObject* target, int32_t* a, const MethodInfo* method)
{
    void* args[1] = { a };
    JsDelegateInvoke(nullptr, method, target, args, nullptr);
}

static void Trampoline_Action_obj(Il2CppObject* target, Il2CppObject* a, const MethodInfo* method)
{
    void* args[1] = { a };
    JsDelegateInvoke(nullptr, method, target, args, nullptr);
}

static void Trampoline_Action_i4_i4(Il2CppObject* target, int32_t a, int32_t b, const MethodInfo* method)
{
    void* args[2] = { &a, &b };
    JsDelegateInvoke(nullptr, method, target, args, nullptr);
}

static int32_t Trampoline_Func_i4(Il2CppObject* target, const MethodInfo* method)
{
    int32_t ret = 0;
    JsDelegateInvoke(nullptr, method, target, nullptr, &ret);
    return ret;
}

static int64_t Trampoline_Func_i8(Il2CppObject* target, const MethodInfo* method)
{
    int64_t ret = 0;
    JsDelegateInvoke(nullptr, method, target, nullptr, &ret);
    return ret;
}

static float Trampoline_Func_r4(Il2CppObject* target, const MethodInfo* method)
{
    float ret = 0.f;
    JsDelegateInvoke(nullptr, method, target, nullptr, &ret);
    return ret;
}

static double Trampoline_Func_r8(Il2CppObject* target, const MethodInfo* method)
{
    double ret = 0.0;
    JsDelegateInvoke(nullptr, method, target, nullptr, &ret);
    return ret;
}

static Il2CppObject* Trampoline_Func_obj(Il2CppObject* target, const MethodInfo* method)
{
    Il2CppObject* ret = nullptr;
    JsDelegateInvoke(nullptr, method, target, nullptr, &ret);
    return ret;
}

static int32_t Trampoline_Func_i4_i4(Il2CppObject* target, int32_t a, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[1] = { &a };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static int64_t Trampoline_Func_i8_i8(Il2CppObject* target, int64_t a, const MethodInfo* method)
{
    int64_t ret = 0;
    void* args[1] = { &a };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static float Trampoline_Func_r4_r4(Il2CppObject* target, float v, const MethodInfo* method)
{
    float ret = 0.f;
    void* args[1] = { &v };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static double Trampoline_Func_r8_r8(Il2CppObject* target, double v, const MethodInfo* method)
{
    double ret = 0.0;
    void* args[1] = { &v };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static Il2CppObject* Trampoline_Func_obj_obj(Il2CppObject* target, Il2CppObject* v, const MethodInfo* method)
{
    Il2CppObject* ret = nullptr;
    void* args[1] = { v };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static int32_t Trampoline_Func_i4_obj(Il2CppObject* target, Il2CppObject* v, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[1] = { v };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static int32_t Trampoline_Func_i4_i4_i4(Il2CppObject* target, int32_t a, int32_t b, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[2] = { &a, &b };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static int32_t Trampoline_Func_i4_byref_i4(Il2CppObject* target, int32_t* a, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[1] = { a };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static int32_t Trampoline_Func_i4_byref_i4_i4(Il2CppObject* target, int32_t* a, int32_t b, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[2] = { a, &b };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

/* Small valuetype (≤8 bytes) closed-delegate arg — Win64 passes as integer register. */
static int32_t Trampoline_Func_i4_vt8(Il2CppObject* target, uint64_t vtBits, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[1] = { &vtBits };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return ret;
}

static bool ParamIs(const Il2CppType* t, Il2CppTypeEnum expected)
{
    return t != nullptr && !t->byref && t->type == expected;
}

static bool ParamIsByref(const Il2CppType* t, Il2CppTypeEnum expected)
{
    return t != nullptr && t->byref && t->type == expected;
}

static bool ParamIsEnum(const Il2CppType* t)
{
    if (t == nullptr || t->byref || t->type != IL2CPP_TYPE_VALUETYPE)
        return false;
    Il2CppClass* klass = il2cpp::vm::Class::FromIl2CppType(t);
    return klass != nullptr && klass->enumtype;
}

/* bool/char/i1/u1/i2/u2/i4/u4/enum — Win64 closed-delegate slot as i4. */
static bool ParamIsI4Like(const Il2CppType* t)
{
    if (t == nullptr || t->byref)
        return false;
    switch (t->type)
    {
    case IL2CPP_TYPE_BOOLEAN:
    case IL2CPP_TYPE_CHAR:
    case IL2CPP_TYPE_I1:
    case IL2CPP_TYPE_U1:
    case IL2CPP_TYPE_I2:
    case IL2CPP_TYPE_U2:
    case IL2CPP_TYPE_I4:
    case IL2CPP_TYPE_U4:
        return true;
    default:
        return ParamIsEnum(t);
    }
}

static bool ParamIsI8Like(const Il2CppType* t)
{
    if (t == nullptr || t->byref)
        return false;
    switch (t->type)
    {
    case IL2CPP_TYPE_I8:
    case IL2CPP_TYPE_U8:
    case IL2CPP_TYPE_I:
    case IL2CPP_TYPE_U:
        return true;
    default:
        return false;
    }
}

static bool ParamIsObjRef(const Il2CppType* t)
{
    if (t == nullptr || t->byref)
        return false;
    switch (t->type)
    {
    case IL2CPP_TYPE_STRING:
    case IL2CPP_TYPE_CLASS:
    case IL2CPP_TYPE_OBJECT:
    case IL2CPP_TYPE_SZARRAY:
    case IL2CPP_TYPE_ARRAY:
        return true;
    case IL2CPP_TYPE_GENERICINST:
        return !IsValueTypeParam(t);
    default:
        return false;
    }
}

static bool ParamIsSmallValueType(const Il2CppType* t, size_t maxBytes)
{
    if (t == nullptr || t->byref)
        return false;
    if (t->type != IL2CPP_TYPE_VALUETYPE && t->type != IL2CPP_TYPE_GENERICINST)
        return false;
    Il2CppClass* klass = il2cpp::vm::Class::FromIl2CppType(t);
    if (klass == nullptr || !il2cpp::vm::Class::IsValuetype(klass) || klass->enumtype)
        return false;
    il2cpp::vm::Class::Init(klass);
    size_t sz = (size_t)klass->instance_size - sizeof(Il2CppObject);
    return sz > 0 && sz <= maxBytes;
}

static Il2CppMethodPointer ResolveClosedInvokeImpl(const MethodInfo* invoke)
{
    const Il2CppType* ret = invoke->return_type;
    const uint8_t n = invoke->parameters_count;

    if (n == 0 && ParamIs(ret, IL2CPP_TYPE_VOID))
        return (Il2CppMethodPointer)Trampoline_Action;
    if (n == 1 && ParamIs(ret, IL2CPP_TYPE_VOID) && ParamIsI4Like(invoke->parameters[0]))
        return (Il2CppMethodPointer)Trampoline_Action_i4;
    if (n == 1 && ParamIs(ret, IL2CPP_TYPE_VOID) && ParamIsByref(invoke->parameters[0], IL2CPP_TYPE_I4))
        return (Il2CppMethodPointer)Trampoline_Action_byref_i4;
    if (n == 1 && ParamIs(ret, IL2CPP_TYPE_VOID) && ParamIsObjRef(invoke->parameters[0]))
        return (Il2CppMethodPointer)Trampoline_Action_obj;
    if (n == 2 && ParamIs(ret, IL2CPP_TYPE_VOID) && ParamIsI4Like(invoke->parameters[0])
        && ParamIsI4Like(invoke->parameters[1]))
        return (Il2CppMethodPointer)Trampoline_Action_i4_i4;

    if (n == 0 && ParamIsI4Like(ret))
        return (Il2CppMethodPointer)Trampoline_Func_i4;
    if (n == 0 && ParamIsI8Like(ret))
        return (Il2CppMethodPointer)Trampoline_Func_i8;
    if (n == 0 && ParamIs(ret, IL2CPP_TYPE_R4))
        return (Il2CppMethodPointer)Trampoline_Func_r4;
    if (n == 0 && ParamIs(ret, IL2CPP_TYPE_R8))
        return (Il2CppMethodPointer)Trampoline_Func_r8;
    if (n == 0 && ParamIsObjRef(ret))
        return (Il2CppMethodPointer)Trampoline_Func_obj;

    if (n == 1 && ParamIsI4Like(ret) && ParamIsI4Like(invoke->parameters[0]))
        return (Il2CppMethodPointer)Trampoline_Func_i4_i4;
    if (n == 1 && ParamIsI8Like(ret) && ParamIsI8Like(invoke->parameters[0]))
        return (Il2CppMethodPointer)Trampoline_Func_i8_i8;
    if (n == 1 && ParamIs(ret, IL2CPP_TYPE_R4) && ParamIs(invoke->parameters[0], IL2CPP_TYPE_R4))
        return (Il2CppMethodPointer)Trampoline_Func_r4_r4;
    if (n == 1 && ParamIs(ret, IL2CPP_TYPE_R8) && ParamIs(invoke->parameters[0], IL2CPP_TYPE_R8))
        return (Il2CppMethodPointer)Trampoline_Func_r8_r8;
    if (n == 1 && ParamIsObjRef(ret) && ParamIsObjRef(invoke->parameters[0]))
        return (Il2CppMethodPointer)Trampoline_Func_obj_obj;
    if (n == 1 && ParamIsI4Like(ret) && ParamIsObjRef(invoke->parameters[0]))
        return (Il2CppMethodPointer)Trampoline_Func_i4_obj;

    if (n == 1 && ParamIsI4Like(ret) && ParamIsByref(invoke->parameters[0], IL2CPP_TYPE_I4))
        return (Il2CppMethodPointer)Trampoline_Func_i4_byref_i4;
    if (n == 2 && ParamIsI4Like(ret) && ParamIsByref(invoke->parameters[0], IL2CPP_TYPE_I4)
        && ParamIsI4Like(invoke->parameters[1]))
        return (Il2CppMethodPointer)Trampoline_Func_i4_byref_i4_i4;
    if (n == 1 && ParamIsI4Like(ret) && ParamIsSmallValueType(invoke->parameters[0], 8))
        return (Il2CppMethodPointer)Trampoline_Func_i4_vt8;
    if (n == 2 && ParamIsI4Like(ret) && ParamIsI4Like(invoke->parameters[0])
        && ParamIsI4Like(invoke->parameters[1]))
        return (Il2CppMethodPointer)Trampoline_Func_i4_i4_i4;

    JsException::ThrowFormat(
        "zents: no closed-delegate trampoline for %s (argc=%u ret=%d)",
        MetadataUtil::GetTypeFullName(invoke->klass),
        (unsigned)n,
        ret != nullptr ? (int)ret->type : -1);
    return nullptr;
}

static const TsDelegateCtorCache* GetOrCreateCtor(Il2CppClass* delegateClass)
{
    auto it = s_ctorByClass.find(delegateClass);
    if (it != s_ctorByClass.end())
        return it->second;

    auto* cache = (TsDelegateCtorCache*)il2cpp::utils::Memory::Malloc(sizeof(TsDelegateCtorCache));
    std::memset(cache, 0, sizeof(TsDelegateCtorCache));
    cache->originalInvokeMethod = il2cpp::vm::Runtime::GetDelegateInvoke(delegateClass);
    if (cache->originalInvokeMethod == nullptr)
        JsException::Throw("failed to resolve delegate Invoke");
    cache->marshalCtx = MetaBinding::CreateMethodMarshalCtx(cache->originalInvokeMethod);
    s_ctorByClass.insert({ delegateClass, cache });
    return cache;
}

Il2CppDelegate* DelegateMarshal::CreateFromFuncRef(JSContext* ctx, Il2CppClass* delegateClass, int funcRef)
{
    const TsDelegateCtorCache* cache = GetOrCreateCtor(delegateClass);
    Il2CppClass* methodClass = MetadataUtil::GetJsMethodClass();
    if (methodClass == nullptr)
        JsException::Throw("JsMethod class not found (ZenTS.Il2Cpp stripped?)");

    JsMethod* target = reinterpret_cast<JsMethod*>(il2cpp::vm::Object::New(methodClass));
    target->disposed = false;
    target->ctx = ctx;
    target->funcRef = funcRef;
    target->methodMarshalCtx = cache->marshalCtx;

    Il2CppDelegate* del = reinterpret_cast<Il2CppDelegate*>(il2cpp::vm::Object::New(delegateClass));
    Il2CppMethodPointer invokeImpl = ResolveClosedInvokeImpl(cache->originalInvokeMethod);
    il2cpp::vm::Type::ConstructClosedDelegate(
        del,
        reinterpret_cast<Il2CppObject*>(target),
        invokeImpl,
        cache->originalInvokeMethod);
    RootDelegate(del);
    return del;
}

Il2CppDelegate* DelegateMarshal::GetOrCreateFromJsFunction(
    JSContext* ctx, Il2CppClass* delegateClass, JSValueConst jsFunc)
{
    if (ctx == nullptr || delegateClass == nullptr || !JS_IsFunction(ctx, jsFunc))
        return nullptr;

    JsDelegateCacheKey key{ delegateClass, JS_VALUE_GET_PTR(jsFunc) };
    auto it = s_jsFuncToDelegate.find(key);
    if (it != s_jsFuncToDelegate.end())
        return it->second;

    int funcRef = JsGlobalRefs::Store(ctx, jsFunc);
    Il2CppDelegate* del = CreateFromFuncRef(ctx, delegateClass, funcRef);
    if (del == nullptr)
    {
        JsGlobalRefs::FreeAndRelease(ctx, funcRef);
        return nullptr;
    }
    s_jsFuncToDelegate.insert({ key, del });
    return del;
}

bool DelegateMarshal::TryGetBoundJsFunction(JSContext* ctx, Il2CppDelegate* del, JSValue* outFn)
{
    if (ctx == nullptr || del == nullptr || outFn == nullptr || del->target == nullptr)
        return false;

    Il2CppClass* methodClass = MetadataUtil::GetJsMethodClass();
    if (methodClass == nullptr || del->target->klass != methodClass)
        return false;

    JsMethod* tm = reinterpret_cast<JsMethod*>(del->target);
    if (tm->disposed)
        return false;

    JSValue fn = JsGlobalRefs::Get(tm->funcRef);
    if (!JS_IsFunction(ctx, fn))
        return false;

    *outFn = JS_DupValue(ctx, fn);
    return true;
}

JSValue DelegateMarshal::PushToJs(JSContext* ctx, Il2CppDelegate* del, Il2CppClass* viewKlass)
{
    if (ctx == nullptr || del == nullptr)
        return JS_NULL;

    JSValue bound = JS_UNDEFINED;
    if (TryGetBoundJsFunction(ctx, del, &bound))
        return bound;

    Il2CppClass* view = viewKlass != nullptr ? viewKlass : del->object.klass;
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, view);
    JSValue handle = ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(del), binding);
    if (JS_IsException(handle))
        return handle;
    return MetaBinding::WrapDelegateCall(ctx, handle);
}
}
