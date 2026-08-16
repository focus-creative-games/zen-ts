#include "MethodBridge.h"
#include "../generated/MethodBridgeStub.h"
#include "../marshal/ObjectRegistry.h"
#include "../marshal/OpaqueValueMarshal.h"
#include "../marshal/TableMarshal.h"

#include "il2cpp-tabledefs.h"
#include "vm/Array.h"
#include "vm/Class.h"
#include "vm/Object.h"
#include "vm/Parameter.h"

#include "../utils/MetadataUtil.h"

#include <cstdlib>
#include <cstring>

namespace zts
{
namespace
{
bool IsOptionalParam(const Il2CppType* paramType)
{
    if (paramType == nullptr)
        return false;
    return (paramType->attrs & PARAM_ATTRIBUTE_OPTIONAL) != 0
        || (paramType->attrs & PARAM_ATTRIBUTE_HAS_DEFAULT) != 0;
}

bool FillDefaultParam(
    const MethodInfo* method,
    uint8_t paramIndex,
    const MarshalMetaInfo* paramMeta,
    void* storage)
{
    bool isNullDefault = false;
    Il2CppObject* boxed =
        il2cpp::vm::Parameter::GetDefaultParameterValueObject(method, (int32_t)paramIndex, &isNullDefault);
    if (isNullDefault || boxed == nullptr)
    {
        if (paramMeta->passByValue)
            *reinterpret_cast<void**>(storage) = nullptr;
        else
            std::memset(storage, 0, (size_t)paramMeta->size);
        return true;
    }

    Il2CppClass* klass = il2cpp::vm::Class::FromIl2CppType(method->parameters[paramIndex]);
    if (klass != nullptr && il2cpp::vm::Class::IsValuetype(klass))
    {
        void* raw = ObjectUnbox(boxed);
        std::memcpy(storage, raw, (size_t)paramMeta->size);
        return true;
    }

    if (paramMeta->passByValue)
    {
        *reinterpret_cast<void**>(storage) = boxed;
        return true;
    }

    std::memset(storage, 0, (size_t)paramMeta->size);
    return true;
}

/** Byref: OpaqueValue or matching ByVal/ByObj valuetype → payload address (writeback). */
bool TryBindByRefAddress(
    JSContext* ctx,
    JSValueConst jsArg,
    const Il2CppType* paramType,
    const MarshalMetaInfo* paramMeta,
    void** outAddr)
{
    if (outAddr == nullptr || paramType == nullptr || !paramType->byref || paramMeta == nullptr)
        return false;
    if (paramMeta->passByValue)
        return false;

    void* opaqueAddr = nullptr;
    if (OpaqueValueMarshal::TryGetValueAddress(ctx, jsArg, &opaqueAddr) && opaqueAddr != nullptr)
    {
        *outAddr = opaqueAddr;
        return true;
    }

    if (paramMeta->typeKlass == nullptr || !il2cpp::vm::Class::IsValuetype(paramMeta->typeKlass)
        || paramMeta->typeKlass->enumtype)
        return false;

    Il2CppObject* holder = ObjectRegistry::Get(ctx, jsArg);
    if (holder == nullptr || holder->klass == nullptr)
        return false;
    if (!il2cpp::vm::Class::IsAssignableFrom(paramMeta->typeKlass, holder->klass))
        return false;

    *outAddr = ObjectUnbox(holder);
    return true;
}
} // namespace

void MethodBridge::Initialize()
{
    MethodBridge_Initialize();
}

JSValue MethodBridge::DefaultInvoke(
    JSContext* ctx,
    void* target,
    int argc,
    JSValueConst* argv,
    const MethodInfo* method,
    const MethodMarshalCtx* mctx)
{
    const bool isExt = mctx->isExtension;
    const uint8_t clrArity = method->parameters_count;
    const uint8_t jsStart = isExt ? 1 : 0;
    const int paramsIndex = mctx->hasParamsArray ? (int)clrArity - 1 : -1;

    void** params = (void**)alloca(clrArity * sizeof(void*));

    if (isExt)
    {
        const MarshalMetaInfo* selfMeta = mctx->paramsMeta[0];
        void* tempStorage = nullptr;
        void* storage = selfMeta->passByValue ? &tempStorage : alloca((size_t)selfMeta->size);
        if (selfMeta->passByValue)
        {
            tempStorage = target;
            params[0] = tempStorage;
        }
        else
        {
            Il2CppObject* boxed = reinterpret_cast<Il2CppObject*>(target);
            void* raw = ObjectUnbox(boxed);
            std::memcpy(storage, raw, (size_t)selfMeta->size);
            params[0] = storage;
        }
    }

    int jsIndex = 0;
    for (uint8_t i = jsStart; i < clrArity; i++)
    {
        const MarshalMetaInfo* paramMeta = mctx->paramsMeta[i];
        void* tempStorage = nullptr;
        void* storage = paramMeta->passByValue ? &tempStorage : alloca((size_t)paramMeta->size);

        if ((int)i == paramsIndex)
        {
            if (jsIndex >= argc)
            {
                Il2CppClass* arrKlass = il2cpp::vm::Class::FromIl2CppType(method->parameters[i]);
                Il2CppClass* el = arrKlass != nullptr ? arrKlass->element_class : nullptr;
                tempStorage = el != nullptr ? il2cpp::vm::Array::New(el, 0) : nullptr;
            }
            else if (JS_IsUndefined(argv[jsIndex]))
            {
                return JS_ThrowTypeError(ctx, "zts: undefined is not assignable to params array");
            }
            else
            {
                paramMeta->js2csWriter(ctx, argv[jsIndex], storage, paramMeta);
            }
            jsIndex += 1;
        }
        else if (paramMeta->marshalAsKind == MarshalAsKind::UnpackedValues)
        {
            const int slots = TableMarshal::GetJsArgSlotCount(paramMeta);
            if (jsIndex + slots <= argc)
            {
                TableMarshal::Js2CsUnpacked(ctx, argv, jsIndex, argc, storage, paramMeta);
            }
            else
            {
                std::memset(storage, 0, (size_t)paramMeta->size);
            }
            jsIndex += slots;
        }
        else if (jsIndex < argc)
        {
            void* byrefAddr = nullptr;
            if (TryBindByRefAddress(ctx, argv[jsIndex], method->parameters[i], paramMeta, &byrefAddr))
            {
                params[i] = byrefAddr;
                jsIndex += 1;
                if (JS_HasException(ctx))
                    return JS_EXCEPTION;
                continue;
            }
            paramMeta->js2csWriter(ctx, argv[jsIndex], storage, paramMeta);
            jsIndex += 1;
        }
        else if (IsOptionalParam(method->parameters[i]))
        {
            FillDefaultParam(method, i, paramMeta, paramMeta->passByValue ? &tempStorage : storage);
        }
        else
        {
            if (paramMeta->passByValue)
                tempStorage = nullptr;
            else
                std::memset(storage, 0, (size_t)paramMeta->size);
        }

        if (JS_HasException(ctx))
            return JS_EXCEPTION;
        params[i] = paramMeta->passByValue ? tempStorage : storage;
    }

    void* invokeThis = isExt ? nullptr : target;
    if (!isExt && target != nullptr)
    {
        auto* obj = reinterpret_cast<Il2CppObject*>(target);
        if (obj->klass != nullptr && il2cpp::vm::Class::IsValuetype(obj->klass) && !obj->klass->enumtype)
            invokeThis = ObjectUnbox(obj);
    }

    if (mctx->retMeta != nullptr)
    {
        void* ret = alloca((size_t)mctx->retMeta->size);
        method->invoker_method(method->methodPointer, method, invokeThis, params, ret);
        return mctx->retMeta->cs2jsWriter(ctx, ret, mctx->retMeta);
    }

    method->invoker_method(method->methodPointer, method, invokeThis, params, nullptr);
    return JS_UNDEFINED;
}

FnJs2CsInvoker MethodBridge::ResolveMethodInvoker(const MethodInfo* /*method*/)
{
    return DefaultInvoke;
}
}
