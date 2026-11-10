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

#include "MethodBridge.h"
#include "../generated/MethodBridgeStub.h"
#include "../marshal/ObjectRegistry.h"
#include "../marshal/OpaqueValueMarshal.h"
#include "../marshal/TableMarshal.h"
#include "../marshal/MarshalMeta.h"
#include "gc/GarbageCollector.h"

#include "il2cpp-tabledefs.h"
#include "vm/Array.h"
#include "vm/Class.h"
#include "vm/Object.h"
#include "vm/Parameter.h"

#include "../utils/MetadataUtil.h"

#include <cstdlib>
#include <cstring>

namespace zents
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
    void* storage,
    const MethodMarshalCtx* mctx)
{
    if (mctx != nullptr && mctx->defaults != nullptr
        && paramIndex >= mctx->defaults->firstDefaultParamIndex
        && paramIndex < (uint8_t)(mctx->defaults->firstDefaultParamIndex + mctx->defaults->defaultParamCount))
    {
        const MethodDefaultArgs* defaults = mctx->defaults;
        const uint8_t slot = (uint8_t)(paramIndex - defaults->firstDefaultParamIndex);
        if (paramMeta->passByValue)
        {
            Il2CppObject* obj =
                defaults->defaultObjectSlots != nullptr ? defaults->defaultObjectSlots[slot] : nullptr;
            *reinterpret_cast<void**>(storage) = obj;
            return true;
        }
        if (defaults->defaultValueSlots != nullptr && defaults->defaultValueSlots[slot] != nullptr)
        {
            std::memcpy(storage, defaults->defaultValueSlots[slot], (size_t)paramMeta->size);
            return true;
        }
    }

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
    js::Context* ctx,
    js::ValueConst jsArg,
    const Il2CppType* paramType,
    const MarshalMetaInfo* paramMeta,
    void** outAddr)
{
    if (outAddr == nullptr || paramType == nullptr || !paramType->byref || paramMeta == nullptr)
        return false;
    if (paramMeta->passByValue)
        return false;

    void* opaqueAddr = nullptr;
    if (OpaqueValueMarshal::TryGetValueAddress(ctx, jsArg, &opaqueAddr, paramType) && opaqueAddr != nullptr)
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

js::Value MethodBridge::DefaultInvoke(
    js::Context* ctx,
    void* target,
    int argc,
    js::ValueConst* argv,
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
    std::vector<void*> storageSlots(clrArity, nullptr);
    for (uint8_t i = jsStart; i < clrArity; i++)
    {
        const MarshalMetaInfo* meta=mctx->paramsMeta[i]; const Il2CppType* type=method->parameters[i];
        void* storage=alloca((size_t)meta->size < sizeof(void*) ? sizeof(void*) : (size_t)meta->size);
        std::memset(storage,0,(size_t)meta->size); storageSlots[i]=storage;
        bool isOut=(type->attrs&PARAM_ATTRIBUTE_OUT)&&!(type->attrs&PARAM_ATTRIBUTE_IN);
        if(isOut){params[i]=storage;continue;}
        if((int)i==paramsIndex)
        {
            Il2CppArray* array=nullptr; Il2CppObject* whole=argc-jsIndex==1?ObjectRegistry::Get(ctx,argv[jsIndex]):nullptr;
            if(whole&&il2cpp::vm::Class::IsAssignableFrom(meta->typeKlass,whole->klass))array=reinterpret_cast<Il2CppArray*>(whole);
            else
            {
                int count=argc-jsIndex;Il2CppClass* element=meta->typeKlass->element_class;
                array=il2cpp::vm::Array::New(element,count);int size=il2cpp::vm::Class::GetArrayElementSize(element);
                const MarshalMetaInfo* em=mctx->paramsElementMeta;
                for(int k=0;k<count;++k)
                {
                    void* slot=il2cpp_array_addr_with_size(array,size,k);em->js2csWriter(ctx,argv[jsIndex+k],slot,em);
                    if(js::HasException(ctx))return js::Exception();
                    if(array->klass->has_references)il2cpp::gc::GarbageCollector::SetWriteBarrier(reinterpret_cast<void**>(slot),(size_t)size);
                }
            }
            *reinterpret_cast<Il2CppArray**>(storage)=array;jsIndex=argc;
        }
        else if(IsOptionalParam(type)&&(jsIndex==argc || js::IsUndefined(argv[jsIndex])))
        {FillDefaultParam(method,i,meta,storage,mctx);if(jsIndex<argc)jsIndex++;}
        else if(meta->marshalAsKind==MarshalAsKind::UnpackedValues)
        {TableMarshal::Js2CsUnpacked(ctx,argv,jsIndex,argc,storage,meta);jsIndex+=TableMarshal::GetJsArgSlotCount(meta);}
        else
        {
            void* byref=nullptr;
            if(TryBindByRefAddress(ctx,argv[jsIndex],type,meta,&byref)){params[i]=byref;jsIndex++;continue;}
            meta->js2csWriter(ctx,argv[jsIndex++],storage,meta);
        }
        if(js::HasException(ctx))return js::Exception();
        params[i]=type->byref?storage:meta->passByValue?*reinterpret_cast<void**>(storage):storage;
    }

    void* invokeThis = isExt ? nullptr : target;
    if (!isExt && target != nullptr)
    {
        auto* obj = reinterpret_cast<Il2CppObject*>(target);
        if (obj->klass != nullptr && il2cpp::vm::Class::IsValuetype(obj->klass) && !obj->klass->enumtype)
            invokeThis = ObjectUnbox(obj);
    }

    // A ref return writes a managed address to the invoker result slot,
    // not an element-sized value (int& needs pointer-size storage on Win64).
    const bool refReturn = method->return_type->byref;
    void* ret=mctx->retMeta?alloca(refReturn ? sizeof(void*) : (size_t)mctx->retMeta->size):nullptr;
    method->invoker_method(method->methodPointer,method,invokeThis,params,ret);
    if (refReturn && ret != nullptr)
    {
        ret = *reinterpret_cast<void**>(ret);
        if (ret == nullptr) return js::ThrowTypeError(ctx, "zents: null managed reference return");
    }
    bool hasOut=false;
    for(uint8_t i=jsStart;i<clrArity;++i)if((method->parameters[i]->attrs&PARAM_ATTRIBUTE_OUT)&&!(method->parameters[i]->attrs&PARAM_ATTRIBUTE_IN)){hasOut=true;break;}
    if(!hasOut)return mctx->retMeta?mctx->retMeta->cs2jsWriter(ctx,ret,mctx->retMeta):js::Undefined();
    js::Value tuple=js::NewArray(ctx);uint32_t index=0;
    if(mctx->retMeta){js::Value value=mctx->retMeta->cs2jsWriter(ctx,ret,mctx->retMeta);if(js::IsException(value)){js::FreeValue(ctx,tuple);return value;}js::SetPropertyUint32(ctx,tuple,index++,value);}
    for(uint8_t i=jsStart;i<clrArity;++i)
    {
        if(!(method->parameters[i]->attrs&PARAM_ATTRIBUTE_OUT)||(method->parameters[i]->attrs&PARAM_ATTRIBUTE_IN))continue;
        const MarshalMetaInfo* meta=mctx->paramsMeta[i];js::Value value=meta->cs2jsWriter(ctx,storageSlots[i],meta);
        if(js::IsException(value)){js::FreeValue(ctx,tuple);return value;}js::SetPropertyUint32(ctx,tuple,index++,value);
    }
    return tuple;

}

FnJs2CsInvoker MethodBridge::ResolveMethodInvoker(const MethodInfo* /*method*/)
{
    return DefaultInvoke;
}
}
