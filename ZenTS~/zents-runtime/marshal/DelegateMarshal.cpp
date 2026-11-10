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
#include "../utils/XmlBindingUtil.h"

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

static js::Value PushArg(
    js::Context* ctx,
    const MethodMarshalCtx* mctx,
    uint8_t paramIndex,
    const Il2CppType* type,
    void* argSlot)
{
    if (type->byref)
        return OpaqueValueMarshal::Push(ctx, argSlot, type, mctx != nullptr ? mctx->paramsMeta[paramIndex] : nullptr);

    if (mctx != nullptr && mctx->paramsMeta != nullptr && mctx->paramsMeta[paramIndex] != nullptr)
        return mctx->paramsMeta[paramIndex]->cs2jsWriter(ctx, argSlot, mctx->paramsMeta[paramIndex]);

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(type);
    if (meta != nullptr)
        return meta->cs2jsWriter(ctx, argSlot, meta);

    JsException::ThrowFormat("zents: unsupported delegate param type %d", (int)type->type);
    return js::Undefined();
}

static void PopRet(
    js::Context* ctx, const MethodMarshalCtx* mctx, const Il2CppType* type, js::Value value, void* __ret)
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
    if (js::HasException(ctx))
        JsEnv::ThrowPendingException();
    if (meta->passByValue)
        *reinterpret_cast<void**>(__ret) = tempStorage;
}

// Unity InvokerMethod convention: value-type args are pointers; ref types live in __args[i].
static void JsDelegateInvoke(Il2CppMethodPointer /*methodPtr*/, const MethodInfo* method, void* __this, void** __args, void* __ret)
{
    JsMethod* target = reinterpret_cast<JsMethod*>(__this);
    js::Context* ctx = JsEnv::GetContext();
    if (target == nullptr || target->disposed || ctx == nullptr)
    {
        il2cpp::vm::Exception::Raise(
            il2cpp::vm::Exception::GetInvalidOperationException(
                "zents: JS runtime was reset; re-bind GetFunction delegates."));
    }

    if(method->parameters_count && MetadataUtil::IsParamsParameter(method,method->parameters_count-1))
        JsException::ThrowFormat("zents: delegate/GetFunction bridge does not support params expansion");
    OpaqueParameterScope opaqueScope;

    js::Value func = JsGlobalRefs::Get(target->funcRef);
    const MethodMarshalCtx* mctx = reinterpret_cast<const MethodMarshalCtx*>(target->methodMarshalCtx);
    std::vector<js::Value> argv;
    for(uint8_t i=0;i<method->parameters_count;++i)
    {
        const Il2CppType* type=method->parameters[i];
        void* data=IsValueTypeParam(type)?__args[i]:&__args[i];if(type->byref)data=&__args[i];
        const MarshalMetaInfo* meta=mctx?mctx->paramsMeta[i]:nullptr;
        js::Value value=PushArg(ctx,mctx,i,type,data);
        if(!type->byref&&meta&&meta->marshalAsKind==MarshalAsKind::UnpackedValues)
        {
            for(int k=0;k<meta->jsArgSlots;++k)argv.push_back(js::GetPropertyUint32(ctx,value,(uint32_t)k));
            js::FreeValue(ctx,value);
        }
        else argv.push_back(value);
        if(js::HasException(ctx)){for(auto arg:argv)js::FreeValue(ctx,arg);JsEnv::ThrowPendingException();}
    }
    js::Value result=js::Call(ctx,func,js::Undefined(),(int)argv.size(),argv.empty()?nullptr:argv.data());
    for(auto arg:argv)js::FreeValue(ctx,arg);

    if (js::IsException(result))
        JsEnv::ThrowPendingException();

    PopRet(ctx, mctx, method->return_type, result, __ret);
    js::FreeValue(ctx, result);
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

// Mixed integer/floating-point slots must retain their native ABI classes;
// this includes arguments that spill to the stack after the register slots.
static void Trampoline_Action_i4_i4_r4_r8_i8(Il2CppObject* target,
    int32_t a, int32_t b, float c, double d, int64_t e, const MethodInfo* method)
{
    void* args[5] = { &a, &b, &c, &d, &e };
    JsDelegateInvoke(nullptr, method, target, args, nullptr);
}

// A wasm i32 result must already carry the CLR small integer sign/zero extension.
static int32_t NormalizeI4Return(int32_t value,const Il2CppType* type)
{
    if(type->type==IL2CPP_TYPE_VALUETYPE)type=il2cpp::vm::Type::GetUnderlyingType(type);
    switch(type->type){case IL2CPP_TYPE_I1:return (int8_t)value;case IL2CPP_TYPE_U1:return (uint8_t)value;
    case IL2CPP_TYPE_I2:return (int16_t)value;case IL2CPP_TYPE_CHAR:case IL2CPP_TYPE_U2:return (uint16_t)value;
    case IL2CPP_TYPE_BOOLEAN:return value!=0;default:return value;}
}

static int32_t Trampoline_Func_i4(Il2CppObject* target, const MethodInfo* method)
{
    int32_t ret = 0;
    JsDelegateInvoke(nullptr, method, target, nullptr, &ret);
    return NormalizeI4Return(ret,method->return_type);
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
    return NormalizeI4Return(ret,method->return_type);
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
    return NormalizeI4Return(ret,method->return_type);
}

static int32_t Trampoline_Func_i4_i4_i4(Il2CppObject* target, int32_t a, int32_t b, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[2] = { &a, &b };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return NormalizeI4Return(ret,method->return_type);
}

static int32_t Trampoline_Func_i4_byref_i4(Il2CppObject* target, int32_t* a, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[1] = { a };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return NormalizeI4Return(ret,method->return_type);
}

static int32_t Trampoline_Func_i4_byref_i4_i4(Il2CppObject* target, int32_t* a, int32_t b, const MethodInfo* method)
{
    int32_t ret = 0;
    void* args[2] = { a, &b };
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return NormalizeI4Return(ret,method->return_type);
}

/* Small valuetype (≤8 bytes) closed-delegate arg — Win64 passes as integer register. */
#if defined(__EMSCRIPTEN__)
// wasm C ABI passes an aggregate by an address, not a Win64 integer register.
static int32_t Trampoline_Func_i4_vt8(Il2CppObject* target, void* vtAddress, const MethodInfo* method)
#else
static int32_t Trampoline_Func_i4_vt8(Il2CppObject* target, uint64_t vtBits, const MethodInfo* method)
#endif
{
    int32_t ret = 0;
#if defined(__EMSCRIPTEN__)
    void* args[1] = { vtAddress };
#else
    void* args[1] = { &vtBits };
#endif
    JsDelegateInvoke(nullptr, method, target, args, &ret);
    return NormalizeI4Return(ret,method->return_type);
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
    if(t&&!t->byref&&(t->type==IL2CPP_TYPE_I||t->type==IL2CPP_TYPE_U))return sizeof(intptr_t)==4;
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
    if(t&&!t->byref&&(t->type==IL2CPP_TYPE_I||t->type==IL2CPP_TYPE_U))return sizeof(intptr_t)==8;
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

#if defined(__EMSCRIPTEN__) && ZENTS_UNITY_VERSION >= 20220000
// OptimizeSize lowers open generic delegate slots to pointers, including primitive T.
// The delegate's Invoke body calls invoke_impl with that shared ABI, not its inflated CLR signature.
static bool SharedSlot(const Il2CppType* type)
{
    return !type->byref&&(type->type==IL2CPP_TYPE_VAR||type->type==IL2CPP_TYPE_MVAR||
        (type->type==IL2CPP_TYPE_GENERICINST&&IsValueTypeParam(type)));
}
static void SharedDelegateInvoke(Il2CppObject* target,const MethodInfo* method,void** words,void* result)
{
    const MethodInfo* definition=XmlBindingUtil::NormalizeMethodForToken(method);
    void* args[16]={};uint32_t values[16]={};
    for(uint8_t i=0;i<method->parameters_count;++i)
    {
        const Il2CppType* actual=method->parameters[i];
        const Il2CppType* declared=definition->parameters[i];
        if(actual->byref)args[i]=words[i];
        else if(SharedSlot(declared))args[i]=words[i];
        else if(IsValueTypeParam(actual)){values[i]=(uint32_t)(uintptr_t)words[i];args[i]=&values[i];}
        else args[i]=words[i];
    }
    JsDelegateInvoke(nullptr,method,target,args,result);
}
template<size_t... I> struct SharedIndices {};
template<size_t N,size_t... I> struct MakeSharedIndices:MakeSharedIndices<N-1,N-1,I...>{};
template<size_t... I> struct MakeSharedIndices<0,I...>{typedef SharedIndices<I...> type;};
template<size_t> struct SharedWord{typedef void* type;};
template<typename Sequence> struct SharedDelegate;
template<size_t... I> struct SharedDelegate<SharedIndices<I...>>
{
    static void Indirect(Il2CppObject* target,typename SharedWord<I>::type... values,void* result,const MethodInfo* method)
    {void* words[sizeof...(I)+1]={values...,nullptr};SharedDelegateInvoke(target,method,words,result);}
    static void Void(Il2CppObject* target,typename SharedWord<I>::type... values,const MethodInfo* method)
    {void* words[sizeof...(I)+1]={values...,nullptr};SharedDelegateInvoke(target,method,words,nullptr);}
    template<typename R> static R Direct(Il2CppObject* target,typename SharedWord<I>::type... values,const MethodInfo* method)
    {void* words[sizeof...(I)+1]={values...,nullptr};R result{};SharedDelegateInvoke(target,method,words,&result);return result;}
    static int32_t I4(Il2CppObject* target,typename SharedWord<I>::type... values,const MethodInfo* method)
    {return NormalizeI4Return(Direct<int32_t>(target,values...,method),method->return_type);}
};
template<size_t N> static Il2CppMethodPointer SelectSharedDelegate(const MethodInfo* invoke)
{
    typedef SharedDelegate<typename MakeSharedIndices<N>::type> T;
    const Il2CppType* declared=XmlBindingUtil::NormalizeMethodForToken(invoke)->return_type;
    const Il2CppType* actual=invoke->return_type;
    if(SharedSlot(declared))return (Il2CppMethodPointer)T::Indirect;
    if(ParamIs(actual,IL2CPP_TYPE_VOID))return (Il2CppMethodPointer)T::Void;
    if(ParamIsI4Like(actual))return (Il2CppMethodPointer)T::I4;
    if(ParamIsI8Like(actual))return (Il2CppMethodPointer)T::template Direct<int64_t>;
    if(ParamIs(actual,IL2CPP_TYPE_R4))return (Il2CppMethodPointer)T::template Direct<float>;
    if(ParamIs(actual,IL2CPP_TYPE_R8))return (Il2CppMethodPointer)T::template Direct<double>;
    if(ParamIsObjRef(actual))return (Il2CppMethodPointer)T::template Direct<Il2CppObject*>;
    JsException::Throw("zents: unsupported non-generic return ABI in fully shared delegate");return nullptr;
}
static Il2CppMethodPointer ResolveSharedDelegate(const MethodInfo* invoke)
{
    const MethodInfo* definition=XmlBindingUtil::NormalizeMethodForToken(invoke);
    for(uint8_t i=0;i<invoke->parameters_count;++i)
    {
        const Il2CppType* declared=definition->parameters[i];
        if(!declared->byref&&!SharedSlot(declared)&&!ParamIsI4Like(declared)&&
            !ParamIsObjRef(declared)&&declared->type!=IL2CPP_TYPE_I&&declared->type!=IL2CPP_TYPE_U)
            JsException::Throw("zents: fully shared delegate has a non-generic parameter requiring a generated ABI stub");
    }
    switch(invoke->parameters_count)
    {
#define SHARED_CASE(N) case N:return SelectSharedDelegate<N>(invoke);
        SHARED_CASE(0) SHARED_CASE(1) SHARED_CASE(2) SHARED_CASE(3) SHARED_CASE(4)
        SHARED_CASE(5) SHARED_CASE(6) SHARED_CASE(7) SHARED_CASE(8) SHARED_CASE(9)
        SHARED_CASE(10) SHARED_CASE(11) SHARED_CASE(12) SHARED_CASE(13) SHARED_CASE(14)
        SHARED_CASE(15) SHARED_CASE(16)
#undef SHARED_CASE
    }
    JsException::Throw("zents: fully shared delegate exceeds 16 parameters");return nullptr;
}
#endif

static Il2CppMethodPointer ResolveClosedInvokeImpl(const MethodInfo* invoke)
{
#if defined(__EMSCRIPTEN__) && ZENTS_UNITY_VERSION >= 20220000
    if(il2cpp::vm::Method::HasFullGenericSharingSignature(invoke))return ResolveSharedDelegate(invoke);
#endif
    const Il2CppType* ret = invoke->return_type;
    const uint8_t n = invoke->parameters_count;

    if (n == 5 && ParamIs(ret, IL2CPP_TYPE_VOID)
        && ParamIsI4Like(invoke->parameters[0]) && ParamIsI4Like(invoke->parameters[1])
        && ParamIs(invoke->parameters[2], IL2CPP_TYPE_R4) && ParamIs(invoke->parameters[3], IL2CPP_TYPE_R8)
        && ParamIsI8Like(invoke->parameters[4]))
        return (Il2CppMethodPointer)Trampoline_Action_i4_i4_r4_r8_i8;
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

Il2CppDelegate* DelegateMarshal::CreateFromFuncRef(js::Context* ctx, Il2CppClass* delegateClass, int funcRef)
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
    js::Context* ctx, Il2CppClass* delegateClass, js::ValueConst jsFunc)
{
    if (ctx == nullptr || delegateClass == nullptr || !js::IsFunction(ctx, jsFunc))
        return nullptr;

    JsDelegateCacheKey key{ delegateClass, js::GetIdentity(jsFunc) };
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

bool DelegateMarshal::TryGetBoundJsFunction(js::Context* ctx, Il2CppDelegate* del, js::Value* outFn)
{
    if (ctx == nullptr || del == nullptr || outFn == nullptr || del->target == nullptr)
        return false;

    Il2CppClass* methodClass = MetadataUtil::GetJsMethodClass();
    if (methodClass == nullptr || del->target->klass != methodClass)
        return false;

    JsMethod* tm = reinterpret_cast<JsMethod*>(del->target);
    if (tm->disposed)
        return false;

    js::Value fn = JsGlobalRefs::Get(tm->funcRef);
    if (!js::IsFunction(ctx, fn))
        return false;

    *outFn = js::DupValue(ctx, fn);
    return true;
}

js::Value DelegateMarshal::PushToJs(js::Context* ctx, Il2CppDelegate* del, Il2CppClass* viewKlass)
{
    if (ctx == nullptr || del == nullptr)
        return js::Null();

    js::Value bound = js::Undefined();
    if (TryGetBoundJsFunction(ctx, del, &bound))
        return bound;

    Il2CppClass* view = viewKlass != nullptr ? viewKlass : del->object.klass;
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, view);
    js::Value handle = ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(del), binding);
    if (js::IsException(handle))
        return handle;
    return MetaBinding::WrapDelegateCall(ctx, handle);
}
}
