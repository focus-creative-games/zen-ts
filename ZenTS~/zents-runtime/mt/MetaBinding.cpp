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

#include "MetaBinding.h"
#include "TypeRegistry.h"
#include "ArrayBinding.h"

#include "../bridge/MethodBridge.h"
#include "../marshal/MarshalMeta.h"
#include "../marshal/BytesMarshal.h"
#include "../marshal/TableMarshal.h"
#include "../marshal/OpaqueValueMarshal.h"
#include "../marshal/MethodOverloadResolver.h"
#include "../marshal/ObjectRegistry.h"
#include "../utils/MetadataUtil.h"
#include "../utils/JsException.h"
#include "AliasXmlTable.h"
#include "ExtensionXmlTable.h"

#include "il2cpp-tabledefs.h"
#include "gc/GarbageCollector.h"
#include "utils/Memory.h"
#include "vm/Class.h"
#include "vm/Field.h"
#include "vm/Object.h"
#include "vm/Parameter.h"
#include "vm/MetadataCache.h"
#include "vm/Reflection.h"
#include "vm/Runtime.h"

#include <cstring>
#include <list>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#if defined(_MSC_VER)
#include <malloc.h>
#else
#include <alloca.h>
#endif

namespace zents
{
static std::unordered_map<Il2CppClass*, TypeBinding*> s_bindings;
static std::unordered_map<const MethodInfo*, MethodMarshalCtx*> s_methodPlans;
static void CollectExtensionMethods(Il2CppClass* klass, std::vector<const MethodInfo*>& outMethods);
static js::ClassID s_methodCtxClassId = 0;
static js::Runtime* s_methodCtxRuntime = nullptr;

static void EnsureMethodCtxClass(js::Runtime* rt)
{
    if (rt == nullptr)
        return;
    if (s_methodCtxRuntime == rt)
        return;
    js::NewClassID(&s_methodCtxClassId);
    js::ClassDef def = {};
    def.class_name = "ZentsMethodCtx";
    js::NewClass(rt, s_methodCtxClassId, &def);
    s_methodCtxRuntime = rt;
}

static js::Value InvokeStaticMethod(
    js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv, int /*magic*/, js::Value* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (mctx == nullptr)
        return js::ThrowInternalError(ctx, "zents: missing method context");
    if (mctx->method != nullptr && mctx->method->is_generic)
    {
        int prefix=0;auto* closed=MetaBinding::ResolveGtap(ctx,{mctx},argc,argv,&prefix);
        if(!closed)return js::HasException(ctx)?js::Exception():js::ThrowTypeError(ctx,"zents: open generic requires System.Type GTAP prefix or zents.inflate");
        mctx=closed;argc-=prefix;argv+=prefix;
    }
    return MethodBridge::InvokeJs2Cs(ctx, nullptr, argc, argv, mctx);
}

static js::Value InvokeInstanceMethod(
    js::Context* ctx, js::ValueConst this_val, int argc, js::ValueConst* argv, int magic, js::Value* func_data)
{
    if(magic!=0 && ObjectRegistry::IsByVal(ctx,this_val)!=(magic==1))return js::ThrowTypeError(ctx,"zents: incompatible ByVal/ByObj receiver");

    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (mctx == nullptr)
        return js::ThrowInternalError(ctx, "zents: missing method context");
    if (mctx->method != nullptr && mctx->method->is_generic)
    {
        int prefix=0;auto* closed=MetaBinding::ResolveGtap(ctx,{mctx},argc,argv,&prefix);
        if(!closed)return js::HasException(ctx)?js::Exception():js::ThrowTypeError(ctx,"zents: open generic requires System.Type GTAP prefix or zents.inflate");
        mctx=closed;argc-=prefix;argv+=prefix;
    }
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (target == nullptr)
        return js::ThrowTypeError(ctx, "zents: instance method requires a CLR object receiver");
    return MethodBridge::InvokeJs2Cs(ctx, target, argc, argv, mctx);
}

static js::Value FieldGetter(
    js::Context* ctx, js::ValueConst this_val, int /*argc*/, js::ValueConst* /*argv*/, int magic, js::Value* func_data)
{
    if(magic!=0 && ObjectRegistry::IsByVal(ctx,this_val)!=(magic==1))return js::ThrowTypeError(ctx,"zents: incompatible ByVal/ByObj receiver");

    FieldMarshalCtx* fctx =
        reinterpret_cast<FieldMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (fctx == nullptr || target == nullptr)
        return js::ThrowTypeError(ctx, "zents: invalid field getter receiver");

    if(!il2cpp::vm::Class::IsAssignableFrom(fctx->field->parent,target->klass))return js::ThrowTypeError(ctx,"zents: incompatible field receiver");
    void* tempStorage = nullptr;
    void* storage = fctx->meta->passByValue ? &tempStorage : alloca((size_t)fctx->meta->size);
    il2cpp::vm::Field::GetValue(target, fctx->field, storage);
    return fctx->meta->cs2jsWriter(ctx, storage, fctx->meta);
}

static js::Value FieldSetter(
    js::Context* ctx, js::ValueConst this_val, int argc, js::ValueConst* argv, int magic, js::Value* func_data)
{
    if(magic!=0 && ObjectRegistry::IsByVal(ctx,this_val)!=(magic==1))return js::ThrowTypeError(ctx,"zents: incompatible ByVal/ByObj receiver");

    FieldMarshalCtx* fctx =
        reinterpret_cast<FieldMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (fctx == nullptr || target == nullptr)
        return js::ThrowTypeError(ctx, "zents: invalid field setter receiver");
    if(il2cpp::vm::Field::GetFlags(fctx->field)&(FIELD_ATTRIBUTE_INIT_ONLY|FIELD_ATTRIBUTE_LITERAL))return js::ThrowTypeError(ctx,"zents: field is read-only");
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: field setter requires a value");

    if(!il2cpp::vm::Class::IsAssignableFrom(fctx->field->parent,target->klass))return js::ThrowTypeError(ctx,"zents: incompatible field receiver");
    void* tempStorage = nullptr;
    void* storage = fctx->meta->passByValue ? &tempStorage : alloca((size_t)fctx->meta->size);
    fctx->meta->js2csWriter(ctx, argv[0], storage, fctx->meta);
    if (js::HasException(ctx))
        return js::Exception();
    /* Field::SetValue uses deref_pointer=false for refs ??pass object pointer, not &slot. */
    void* setArg = fctx->meta->passByValue ? tempStorage : storage;
    il2cpp::vm::Field::SetValue(target, fctx->field, setArg);
    return js::Undefined();
}

static js::Value StaticFieldGetter(
    js::Context* ctx, js::ValueConst /*this_val*/, int /*argc*/, js::ValueConst* /*argv*/, int /*magic*/, js::Value* func_data)
{
    FieldMarshalCtx* fctx =
        reinterpret_cast<FieldMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (fctx == nullptr)
        return js::ThrowTypeError(ctx, "zents: invalid static field getter");

    void* tempStorage = nullptr;
    void* storage = fctx->meta->passByValue ? &tempStorage : alloca((size_t)fctx->meta->size);
    il2cpp::vm::Field::StaticGetValue(fctx->field, storage);
    return fctx->meta->cs2jsWriter(ctx, storage, fctx->meta);
}

static js::Value StaticFieldSetter(
    js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv, int /*magic*/, js::Value* func_data)
{
    FieldMarshalCtx* fctx =
        reinterpret_cast<FieldMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (fctx == nullptr)
        return js::ThrowTypeError(ctx, "zents: invalid static field setter");
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: field setter requires a value");

    void* tempStorage = nullptr;
    void* storage = fctx->meta->passByValue ? &tempStorage : alloca((size_t)fctx->meta->size);
    fctx->meta->js2csWriter(ctx, argv[0], storage, fctx->meta);
    if (js::HasException(ctx))
        return js::Exception();
    void* setArg = fctx->meta->passByValue ? tempStorage : storage;
    il2cpp::vm::Field::StaticSetValue(fctx->field, setArg);
    return js::Undefined();
}

static js::Value PropertyGetter(
    js::Context* ctx, js::ValueConst this_val, int /*argc*/, js::ValueConst* /*argv*/, int magic, js::Value* func_data)
{
    if(magic!=0 && ObjectRegistry::IsByVal(ctx,this_val)!=(magic==1))return js::ThrowTypeError(ctx,"zents: incompatible ByVal/ByObj receiver");

    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (mctx == nullptr || target == nullptr)
        return js::ThrowTypeError(ctx, "zents: invalid property getter receiver");
    return MethodBridge::InvokeJs2Cs(ctx, target, 0, nullptr, mctx);
}

static js::Value PropertySetter(
    js::Context* ctx, js::ValueConst this_val, int argc, js::ValueConst* argv, int magic, js::Value* func_data)
{
    if(magic!=0 && ObjectRegistry::IsByVal(ctx,this_val)!=(magic==1))return js::ThrowTypeError(ctx,"zents: incompatible ByVal/ByObj receiver");

    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (mctx == nullptr || target == nullptr)
        return js::ThrowTypeError(ctx, "zents: invalid property setter receiver");
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: property setter requires a value");
    return MethodBridge::InvokeJs2Cs(ctx, target, 1, argv, mctx);
}

static js::Value StaticPropertyGetter(
    js::Context* ctx, js::ValueConst /*this_val*/, int /*argc*/, js::ValueConst* /*argv*/, int /*magic*/, js::Value* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (mctx == nullptr)
        return js::ThrowTypeError(ctx, "zents: invalid static property getter");
    return MethodBridge::InvokeJs2Cs(ctx, nullptr, 0, nullptr, mctx);
}

static js::Value StaticPropertySetter(
    js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv, int /*magic*/, js::Value* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (mctx == nullptr)
        return js::ThrowTypeError(ctx, "zents: invalid static property setter");
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: property setter requires a value");
    return MethodBridge::InvokeJs2Cs(ctx, nullptr, 1, argv, mctx);
}

static MethodMarshalCtx* FindCtor(TypeBinding* binding, js::Context* ctx, int argc, js::ValueConst* argv)
{
    OverloadGroup group;
    group.candidates = binding->ctors;
    return MethodOverloadResolver::Resolve(ctx, &group, argc, argv);
}

static js::Value TypeConstruct(
    js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv, int /*magic*/, js::Value* func_data)
{
    TypeBinding* binding =
        reinterpret_cast<TypeBinding*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (binding == nullptr)
        return js::ThrowInternalError(ctx, "zents: missing type binding");

    MethodMarshalCtx* ctor = FindCtor(binding, ctx, argc, argv);
    if (ctor == nullptr)
    {
        return js::ThrowTypeError(
            ctx,
            "zents: no matching constructor for %s with %d argument(s)",
            MetadataUtil::GetTypeFullName(binding->klass),
            argc);
    }

    Il2CppObject* obj = il2cpp::vm::Object::New(binding->klass);
    js::Value invokeRet = MethodBridge::InvokeJs2Cs(ctx, obj, argc, argv, ctor);
    if (js::IsException(invokeRet))
        return invokeRet;
    js::FreeValue(ctx, invokeRet);
    if (il2cpp::vm::Class::IsValuetype(binding->klass) && !binding->klass->enumtype)
        return ObjectRegistry::PushByVal(ctx, obj, binding);
    return ObjectRegistry::Push(ctx, obj, binding);
}

static js::Value StructDefaultInvoke(
    js::Context* ctx, js::ValueConst /*this_val*/, int /*argc*/, js::ValueConst* /*argv*/, int /*magic*/, js::Value* func_data)
{
    TypeBinding* binding =
        reinterpret_cast<TypeBinding*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (binding == nullptr || binding->klass == nullptr)
        return js::ThrowInternalError(ctx, "zents: missing type binding for _default");

    Il2CppClass* klass = binding->klass;
    il2cpp::vm::Class::Init(klass);
    /* Object::New for ptr-free valuetypes uses GC_MALLOC_ATOMIC (uninitialized). */
    Il2CppObject* boxed = il2cpp::vm::Object::New(klass);
    size_t size = (size_t)il2cpp::vm::Class::GetInstanceSize(klass);
    if (size > sizeof(Il2CppObject))
        std::memset(ObjectUnbox(boxed), 0, size - sizeof(Il2CppObject));
    return ObjectRegistry::PushByVal(ctx, boxed, binding);
}

static void AttachStructDefault(js::Context* ctx, js::Value typeObj, TypeBinding* binding)
{
    Il2CppClass* klass = binding->klass;
    if (!il2cpp::vm::Class::IsValuetype(klass) || klass->enumtype)
        return;

    binding->memberKeys.insert("_default");
    js::Value holder = js::NewObjectClass(ctx, s_methodCtxClassId);
    js::SetOpaque(holder, binding);
    js::Value fn = js::NewCFunctionData(ctx, StructDefaultInvoke, 0, 0, 1, &holder);
    js::FreeValue(ctx, holder);
    js::SetPropertyStr(ctx, typeObj, "_default", fn);
}

static js::Value Cs2JsOpaqueValue(js::Context* ctx, void* address, const MarshalMetaInfo* meta)
{
    return OpaqueValueMarshal::Push(ctx, address, meta->type);
}

static void Js2CsOpaqueValue(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta)
{
    if (OpaqueValueMarshal::IsOpaqueHandle(ctx, value))
    {
        void* src = nullptr;
        if (OpaqueValueMarshal::TryGetValueAddress(ctx, value, &src) && src != nullptr)
        {
            if (meta->passByValue)
                *reinterpret_cast<void**>(address) = *reinterpret_cast<void**>(src);
            else
                std::memcpy(address, src, (size_t)meta->size);
            return;
        }
    }
    const MarshalMetaInfo* base = MarshalMeta::TryCreateDefault(meta->type);
    if (base != nullptr)
        base->js2csWriter(ctx, value, address, base);
}

static bool TryMaterializeDefaultParam(
    const MethodInfo* method,
    int paramIndex,
    const MarshalMetaInfo* meta,
    void** outValueSlot,
    Il2CppObject** outObjectSlot)
{
    *outValueSlot = nullptr;
    *outObjectSlot = nullptr;
    if (meta == nullptr)
        return false;
    if ((method->parameters[paramIndex]->attrs & PARAM_ATTRIBUTE_HAS_DEFAULT) == 0
        && (method->parameters[paramIndex]->attrs & PARAM_ATTRIBUTE_OPTIONAL) == 0)
        return false;

    bool isExplicitNull = false;
    Il2CppObject* obj =
        il2cpp::vm::Parameter::GetDefaultParameterValueObject(method, paramIndex, &isExplicitNull);
    if (obj == nullptr && !isExplicitNull
        && (method->parameters[paramIndex]->attrs & PARAM_ATTRIBUTE_HAS_DEFAULT) == 0)
    {
        if (meta->passByValue)
        {
            *outObjectSlot = nullptr;
            return true;
        }
        void* buf = il2cpp::utils::Memory::Malloc((size_t)meta->size);
        std::memset(buf, 0, (size_t)meta->size);
        *outValueSlot = buf;
        return true;
    }

    if (meta->passByValue)
    {
        *outObjectSlot = obj;
        return true;
    }

    void* buf = il2cpp::utils::Memory::Malloc((size_t)meta->size);
    std::memset(buf, 0, (size_t)meta->size);
    if (obj != nullptr)
    {
        void* unboxed = ObjectUnbox(obj);
        std::memcpy(buf, unboxed, (size_t)meta->size);
    }
    *outValueSlot = buf;
    return true;
}

static MethodDefaultArgs* TryBuildDefaultArgs(
    const MethodInfo* method, const MarshalMetaInfo** paramsMeta, bool isExtension)
{
    if (method == nullptr || paramsMeta == nullptr || method->parameters_count == 0)
        return nullptr;

    const int paramStart = isExtension ? 1 : 0;
    void** tempValues = (void**)alloca(method->parameters_count * sizeof(void*));
    Il2CppObject** tempObjects = (Il2CppObject**)alloca(method->parameters_count * sizeof(Il2CppObject*));
    std::memset(tempValues, 0, method->parameters_count * sizeof(void*));
    std::memset(tempObjects, 0, method->parameters_count * sizeof(Il2CppObject*));

    int firstDefault = -1;
    bool anyObjectDefault = false;
    for (int i = (int)method->parameters_count - 1; i >= paramStart; --i)
    {
        void* valueSlot = nullptr;
        Il2CppObject* objectSlot = nullptr;
        if (!TryMaterializeDefaultParam(method, i, paramsMeta[i], &valueSlot, &objectSlot))
            break;
        firstDefault = i;
        tempValues[i] = valueSlot;
        tempObjects[i] = objectSlot;
        if (paramsMeta[i]->passByValue)
            anyObjectDefault = true;
    }
    if (firstDefault < 0)
        return nullptr;

    const uint8_t defaultCount = (uint8_t)(method->parameters_count - firstDefault);
    void** valueSlots = (void**)il2cpp::utils::Memory::Malloc(defaultCount * sizeof(void*));
    std::memset(valueSlots, 0, defaultCount * sizeof(void*));
    Il2CppObject** objectSlots = nullptr;
    if (anyObjectDefault)
        objectSlots = (Il2CppObject**)il2cpp::gc::GarbageCollector::AllocateFixed(
            defaultCount * sizeof(Il2CppObject*), nullptr);

    for (uint8_t di = 0; di < defaultCount; ++di)
    {
        const int paramIndex = firstDefault + di;
        valueSlots[di] = tempValues[paramIndex];
        if (objectSlots != nullptr)
            objectSlots[di] = tempObjects[paramIndex];
    }

    auto* defaults = new MethodDefaultArgs();
    defaults->firstDefaultParamIndex = (uint8_t)firstDefault;
    defaults->defaultParamCount = defaultCount;
    defaults->defaultValueSlots = valueSlots;
    defaults->defaultObjectSlots = objectSlots;
    return defaults;
}

static bool TryBuildMarshalCtx(const MethodInfo* method, MethodMarshalCtx** outCtx)
{
    if (method == nullptr)
        return false;
    auto cached=s_methodPlans.find(method);if(cached!=s_methodPlans.end()){*outCtx=cached->second;return true;}

    if (method->is_generic)
    {
    auto* ctx = new MethodMarshalCtx();
    ctx->method = method;
    ctx->js2CsInvoker = nullptr; /* open generic: invoke rejects */
    ctx->paramsMeta = nullptr;
    ctx->retMeta = nullptr;
    ctx->paramsElementMeta = nullptr;
    ctx->defaults = nullptr;
    ctx->arity = method->parameters_count;
    ctx->minArity = method->parameters_count;
    ctx->sealed = true;
    ctx->isExtension = false;
    ctx->hasParamsArray = false;
    s_methodPlans[method]=ctx;
    *outCtx = ctx;
    return true;
    }

    const bool isExtension = MetadataUtil::IsExtensionMethod(method);
    const MarshalMetaInfo** paramsMeta = nullptr;
    if (method->parameters_count > 0)
    {
        paramsMeta = new const MarshalMetaInfo*[method->parameters_count];
        for (uint8_t i = 0; i < method->parameters_count; i++)
        {
            const MarshalMetaInfo* meta = nullptr;
            int32_t marshalKind = 0;
            std::vector<std::string> members;
            MetadataUtil::TryReadJsMarshalAs(method, (int)i, &marshalKind, &members);
            if (marshalKind == 2 /* Bytes */)
            {
                Il2CppClass* arrKlass = il2cpp::vm::Class::FromIl2CppType(method->parameters[i]);
                const bool validByteArray = method->parameters[i]->type == IL2CPP_TYPE_SZARRAY
                    && arrKlass != nullptr
                    && arrKlass->element_class == il2cpp_defaults.byte_class;
                if (validByteArray)
                {
                    auto* bytesMeta = new MarshalMetaInfo();
                    bytesMeta->js2csWriter = BytesMarshal::Js2CsBytes;
                    bytesMeta->cs2jsWriter = BytesMarshal::Cs2JsBytes;
                    bytesMeta->type = method->parameters[i];
                    bytesMeta->typeKlass = arrKlass;
                    bytesMeta->size = sizeof(Il2CppArray*);
                    bytesMeta->passByValue = true;
                    bytesMeta->jsArgSlots = 1;
                    bytesMeta->marshalAsKind = MarshalAsKind::None;
                    meta = bytesMeta;
                }
                else
                {
                    marshalKind=0;
                    meta=MarshalMeta::TryCreateDefault(method->parameters[i]);
                }
            }
            else if (marshalKind == 3 /* OpaqueValue */)
            {
                const MarshalMetaInfo* base = MarshalMeta::TryCreateDefault(method->parameters[i]);
                if (base != nullptr)
                {
                    auto* om = new MarshalMetaInfo();
                    *om = *base;
                    om->marshalAsKind = MarshalAsKind::OpaqueValue;
                om->js2csWriter = Js2CsOpaqueValue;
                    om->cs2jsWriter = Cs2JsOpaqueValue;
                    meta = om;
                }
            }
            else if ((marshalKind == 5 /* Table */ || marshalKind == 4 /* UnpackedValues */)
                     && !members.empty())
            {
                Il2CppClass* klass = il2cpp::vm::Class::FromIl2CppType(method->parameters[i]);
                if (klass != nullptr && il2cpp::vm::Class::IsValuetype(klass) && !klass->enumtype)
                {
                    MarshalAsKind kind =
                        marshalKind == 4 ? MarshalAsKind::UnpackedValues : MarshalAsKind::Table;
                    meta = TableMarshal::Create(method->parameters[i], klass, members, kind);
                }
            }
            if (meta == nullptr)
                meta = MarshalMeta::TryCreateDefault(method->parameters[i]);
            if (meta == nullptr)
            {
                delete[] paramsMeta;
                return false;
            }
            if (marshalKind == 7 || marshalKind == 1) meta = MarshalMeta::WithStrategy(method->parameters[i], marshalKind);
            if (marshalKind == 2) const_cast<MarshalMetaInfo*>(meta)->marshalAsKind = MarshalAsKind::Bytes;
            paramsMeta[i] = meta;
        }
    }

    const MarshalMetaInfo* retMeta = nullptr;
    if (!MetadataUtil::IsVoidType(method->return_type))
    {
        int32_t retMarshalKind = 0;
        std::vector<std::string> retMembers;
        MetadataUtil::TryReadJsMarshalAs(method, -1, &retMarshalKind, &retMembers);
        if (retMarshalKind == 2 /* Bytes */)
        {
            Il2CppClass* arrKlass = il2cpp::vm::Class::FromIl2CppType(method->return_type);
            const bool validByteArray = method->return_type->type == IL2CPP_TYPE_SZARRAY
                && arrKlass != nullptr
                && arrKlass->element_class == il2cpp_defaults.byte_class;
            if (validByteArray)
            {
                auto* bytesMeta = new MarshalMetaInfo();
                bytesMeta->js2csWriter = BytesMarshal::Js2CsBytes;
                bytesMeta->cs2jsWriter = BytesMarshal::Cs2JsBytes;
                bytesMeta->type = method->return_type;
                bytesMeta->typeKlass = arrKlass;
                bytesMeta->size = sizeof(Il2CppArray*);
                bytesMeta->passByValue = true;
                bytesMeta->jsArgSlots = 1;
                bytesMeta->marshalAsKind = MarshalAsKind::None;
                    retMeta = bytesMeta;
            }
            else
            {
                retMeta=MarshalMeta::TryCreateDefault(method->return_type);
            }
        }
        else if (retMarshalKind == 3 /* OpaqueValue */)
        {
            const MarshalMetaInfo* base = MarshalMeta::TryCreateDefault(method->return_type);
            if (base != nullptr)
            {
                auto* om = new MarshalMetaInfo();
                *om = *base;
                om->marshalAsKind = MarshalAsKind::OpaqueValue;
                om->js2csWriter = Js2CsOpaqueValue;
                om->cs2jsWriter = Cs2JsOpaqueValue;
                retMeta = om;
            }
        }
        else if ((retMarshalKind == 5 || retMarshalKind == 4) && !retMembers.empty())
        {
            Il2CppClass* klass = il2cpp::vm::Class::FromIl2CppType(method->return_type);
            if (klass != nullptr && il2cpp::vm::Class::IsValuetype(klass) && !klass->enumtype)
            {
                MarshalAsKind kind =
                    retMarshalKind == 4 ? MarshalAsKind::UnpackedValues : MarshalAsKind::Table;
                retMeta = TableMarshal::Create(method->return_type, klass, retMembers, kind);
            }
        }
        if (retMeta == nullptr)
            retMeta = MarshalMeta::TryCreateDefault(method->return_type);
        if (retMeta == nullptr)
        {
            delete[] paramsMeta;
            return false;
        }
    }

    auto* ctx = new MethodMarshalCtx();
    ctx->method = method;
    ctx->js2CsInvoker = MethodBridge::ResolveMethodInvoker(method);
    ctx->paramsMeta = paramsMeta;
    int32_t returnKind = 0; MetadataUtil::TryReadJsMarshalAs(method, -1, &returnKind, nullptr);
    if (returnKind == 6 || returnKind == 7 || returnKind == 1) retMeta = MarshalMeta::WithStrategy(method->return_type, returnKind);
    ctx->retMeta = retMeta;
    ctx->paramsElementMeta = nullptr;
    ctx->isExtension = isExtension;
    ctx->hasParamsArray = false;
    {
        int32_t jsArity = 0;
        int32_t jsMin = 0;
        const uint8_t start = isExtension ? 1 : 0;
        const int paramsIndex =
            method->parameters_count > 0 && MetadataUtil::IsParamsParameter(method, method->parameters_count - 1)
                ? (int)method->parameters_count - 1
                : -1;
        if (paramsIndex >= (int)start)
        {
            ctx->hasParamsArray = true;
            Il2CppClass* arrayClass = paramsMeta[paramsIndex]->typeKlass;
            if (arrayClass && arrayClass->element_class) ctx->paramsElementMeta = MarshalMeta::TryCreateDefault(&arrayClass->element_class->byval_arg);
        }

        for (uint8_t i = start; i < method->parameters_count; ++i)
        {
            if ((method->parameters[i]->attrs & PARAM_ATTRIBUTE_OUT) && !(method->parameters[i]->attrs & PARAM_ATTRIBUTE_IN)) continue;
            int32_t slots = TableMarshal::GetJsArgSlotCount(paramsMeta[i]);
            if ((int)i == paramsIndex)
            {
                jsArity += 1; /* params overflow is handled by the element plan */
                continue;
            }
            jsArity += slots;
            const uint32_t attrs = method->parameters[i]->attrs;
            const bool optional =
                (attrs & PARAM_ATTRIBUTE_OPTIONAL) != 0 || (attrs & PARAM_ATTRIBUTE_HAS_DEFAULT) != 0;
            if (!optional)
                jsMin += slots;
        }
        ctx->arity = jsArity;
        ctx->minArity = jsMin;
    }
    ctx->defaults = TryBuildDefaultArgs(method, paramsMeta, isExtension);
    ctx->sealed = MetadataUtil::IsMethodSealed(method, false);
    s_methodPlans[method]=ctx;
    *outCtx = ctx;
    return true;
}

MethodMarshalCtx* MetaBinding::ResolveGtap(js::Context* ctx,const std::vector<MethodMarshalCtx*>& methods,int argc,js::ValueConst* argv,int* prefix)
{
    *prefix=0;std::vector<MethodMarshalCtx*> closed;int commonPrefix=-1;
    for(auto* mc:methods)
    {
        const MethodInfo* open=mc->method;if(!open->is_generic)continue;
        auto container=open->genericContainerHandle;if(!container&&open->methodMetadataHandle)container=il2cpp::vm::MetadataCache::GetGenericContainerFromMethod(open->methodMetadataHandle);
        if(!container)continue;uint32_t n=il2cpp::vm::MetadataCache::GetGenericContainerCount(container);if(argc<(int)n)continue;
        std::vector<const Il2CppType*> types;bool valid=true;
        for(uint32_t i=0;i<n;++i)
        {
            Il2CppObject* obj=ObjectRegistry::Get(ctx,argv[i]);
            if(!obj || !il2cpp::vm::Class::IsAssignableFrom(il2cpp_defaults.systemtype_class,obj->klass)){valid=false;break;}
            types.push_back(reinterpret_cast<Il2CppReflectionType*>(obj)->type);
        }
        if(!valid)continue;
        if(!MetadataUtil::MethodGenericTypeArgsSatisfyConstraints(open,types.data(),n))continue;
        const MethodInfo* method=il2cpp::vm::MetadataCache::GetGenericInstanceMethod(open,types.data(),n);
        // Fully shared methods can lack a direct-call stub while their invoker redirects
        // to a valid raw AOT body. This bridge calls the invoker, never the direct stub.
        if(!method || !method->invoker_method || method->invoker_method==il2cpp::vm::Runtime::GetMissingMethodInvoker())
        {js::ThrowTypeError(ctx,"zents: generic AOT code is unavailable for %s",open->name);return nullptr;}
        MethodMarshalCtx* cm=CreateMethodMarshalCtx(method);
        if(!cm || !MethodOverloadResolver::IsApplicable(ctx,cm,argc-(int)n,argv+n))continue;
        if(commonPrefix>=0&&commonPrefix!=(int)n){js::ThrowTypeError(ctx,"zents: ambiguous GTAP overload");return nullptr;}
        commonPrefix=(int)n;closed.push_back(cm);
    }
    if(closed.empty())return nullptr;
    OverloadGroup group;group.candidates=closed;auto* winner=MethodOverloadResolver::Resolve(ctx,&group,argc-commonPrefix,argv+commonPrefix);
    if(!winner){js::ThrowTypeError(ctx,"zents: ambiguous GTAP overload");return nullptr;}*prefix=commonPrefix;return winner;
}

const MethodInfo* MetaBinding::FindPublicMethod(Il2CppClass* owner,const char* key,int staticDomain,int genericArity)
{
    std::vector<const MethodInfo*> methods;
    for(Il2CppClass* walk=owner;walk;walk=walk->parent){il2cpp::vm::Class::SetupMethods(walk);for(uint16_t i=0;i<walk->method_count;++i)methods.push_back(walk->methods[i]);}
    if(staticDomain!=1)CollectExtensionMethods(owner,methods);
    const MethodInfo* found=nullptr;
    for(const MethodInfo* m:methods)
    {
        bool extension=MetadataUtil::IsExtensionMethod(m);
        if(!MetadataUtil::IsPublicMethod(m)||m->name[0]=='.')continue;
        if((m->flags&METHOD_ATTRIBUTE_SPECIAL_NAME)&&std::strncmp(m->name,"add_",4)!=0&&std::strncmp(m->name,"remove_",7)!=0)continue;
        bool stat=MetadataUtil::IsStaticMethod(m)&&!extension;
        if(staticDomain>=0&&stat!=(staticDomain!=0))continue;
        if(genericArity>=0){if(!m->is_generic)continue;auto c=m->genericContainerHandle;if(!c||(int)il2cpp::vm::MetadataCache::GetGenericContainerCount(c)!=genericArity)continue;}
        std::string name=m->name,alias;
        if(MetadataUtil::TryReadJsAlias(m,alias)||AliasXmlTable::TryGetAlias(m,alias))name=alias;
        bool match=std::strchr(key,'(')?MethodOverloadResolver::BuildSignatureKey(m)==key:name==key;
        if(!match)continue;
        if(!found)found=m;
        else if(MethodOverloadResolver::BuildSignatureKey(found)!=MethodOverloadResolver::BuildSignatureKey(m))return nullptr;
    }
    return found;
}

MethodMarshalCtx* MetaBinding::CreateMethodMarshalCtx(const MethodInfo* method)
{
    // CLR method metadata and AOT XML are immutable for the process lifetime.
    auto found=s_methodPlans.find(method);if(found!=s_methodPlans.end())return found->second;
    MethodMarshalCtx* ctx = nullptr;
    if (!TryBuildMarshalCtx(method, &ctx))
        return nullptr;
    s_methodPlans[method]=ctx;
    return ctx;
}

static js::Value CreateMethodFunction(js::Context* ctx, void* opaque, js::CFunctionData* invoker, int length, bool markDirect, int mode = 0)
{
    EnsureMethodCtxClass(js::GetRuntime(ctx));
    js::Value holder = js::NewObjectClass(ctx, s_methodCtxClassId);
    js::SetOpaque(holder, opaque);
    js::Value fn = js::NewCFunctionData(ctx, invoker, length, mode, 1, &holder);
    if (markDirect)
    {
        js::SetPropertyStr(ctx, fn, "__zents_mctx", js::DupValue(ctx, holder));
        js::SetPropertyStr(ctx, fn, "__zents_direct", js::NewBool(ctx, 1));
    }
    js::FreeValue(ctx, holder);
    return fn;
}

js::Value MetaBinding::CreateInstanceMethodFunction(js::Context* ctx, MethodMarshalCtx* mctx, int mode)
{
    return CreateMethodFunction(ctx, mctx, InvokeInstanceMethod, mctx->minArity, true, mode);
}

js::Value MetaBinding::CreateStaticMethodFunction(js::Context* ctx, MethodMarshalCtx* mctx)
{
    return CreateMethodFunction(ctx, mctx, InvokeStaticMethod, mctx->minArity, true);
}

MethodMarshalCtx* MetaBinding::TryGetDirectMethodCtx(js::Context* ctx, js::ValueConst fn)
{
    if (!js::IsFunction(ctx, fn))
        return nullptr;
    js::Value direct = js::GetPropertyStr(ctx, fn, "__zents_direct");
    bool ok = js::IsBool(direct) && js::ToBool(ctx, direct);
    js::FreeValue(ctx, direct);
    if (!ok)
        return nullptr;
    js::Value holder = js::GetPropertyStr(ctx, fn, "__zents_mctx");
    if (!js::IsObject(holder))
    {
        js::FreeValue(ctx, holder);
        return nullptr;
    }
    EnsureMethodCtxClass(js::GetRuntime(ctx));
    MethodMarshalCtx* mctx = reinterpret_cast<MethodMarshalCtx*>(js::GetOpaque(holder, s_methodCtxClassId));
    js::FreeValue(ctx, holder);
    return mctx;
}

static bool IsPublicField(FieldInfo* field)
{
    return (il2cpp::vm::Field::GetFlags(field) & FIELD_ATTRIBUTE_FIELD_ACCESS_MASK) == FIELD_ATTRIBUTE_PUBLIC;
}

static bool IsPublicZeroParamProperty(const PropertyInfo* property)
{
    if (property == nullptr)
        return false;
    if (property->get != nullptr)
    {
        if (!MetadataUtil::IsPublicMethod(property->get) || property->get->parameters_count != 0)
            return false;
    }
    if (property->set != nullptr)
    {
        if (!MetadataUtil::IsPublicMethod(property->set) || property->set->parameters_count != 1)
            return false;
    }
    return property->get != nullptr || property->set != nullptr;
}

static bool IsIndexerProperty(const PropertyInfo* property)
{
    if (property == nullptr)
        return false;
    if (property->get != nullptr && property->get->parameters_count > 0)
        return true;
    if (property->set != nullptr && property->set->parameters_count > 1)
        return true;
    return false;
}

static void RegisterIndexerAccessors(Il2CppClass* klass, TypeBinding* binding, NameMetaMap& instanceMap)
{
    void* iter = nullptr;
    while (const PropertyInfo* property = il2cpp::vm::Class::GetProperties(klass, &iter))
    {
        if (!IsIndexerProperty(property))
            continue;

        if (property->get != nullptr && MetadataUtil::IsPublicMethod(property->get)
            && !MetadataUtil::IsStaticMethod(property->get)
            && instanceMap.find("get_Item") == instanceMap.end())
        {
            MethodMarshalCtx* getter = nullptr;
            if (TryBuildMarshalCtx(property->get, &getter))
            {
                MetaInfo info = {};
                info.kind = MetaKind::Method;
                info.methodCtx = getter;
                instanceMap["get_Item"] = info;
                binding->memberKeys.insert("get_Item");
            }
        }

        if (property->set != nullptr && MetadataUtil::IsPublicMethod(property->set)
            && !MetadataUtil::IsStaticMethod(property->set)
            && instanceMap.find("set_Item") == instanceMap.end())
        {
            MethodMarshalCtx* setter = nullptr;
            if (TryBuildMarshalCtx(property->set, &setter))
            {
                MetaInfo info = {};
                info.kind = MetaKind::Method;
                info.methodCtx = setter;
                instanceMap["set_Item"] = info;
                binding->memberKeys.insert("set_Item");
            }
        }
    }
}

static void RegisterFields(Il2CppClass* klass, TypeBinding* binding, NameMetaMap& instanceMap)
{
    il2cpp::vm::Class::SetupFields(klass);
    for (uint16_t i = 0; i < klass->field_count; ++i)
    {
        FieldInfo* field = &klass->fields[i];
        if (!IsPublicField(field))
            continue;
        if (!il2cpp::vm::Field::IsInstance(field))
            continue;
        if (instanceMap.find(field->name) != instanceMap.end())
            continue;

        int32_t fieldKind=0;std::vector<std::string> fieldMembers;
        MetadataUtil::TryReadFieldMarshalAs(field,&fieldKind,&fieldMembers);
        const MarshalMetaInfo* meta = MarshalMeta::WithStrategy(field->type,fieldKind);
        if((fieldKind==4||fieldKind==5)&&!fieldMembers.empty())meta=TableMarshal::Create(field->type,il2cpp::vm::Class::FromIl2CppType(field->type),fieldMembers,(MarshalAsKind)fieldKind);
        if (meta == nullptr)
            continue;

        auto* fctx = new FieldMarshalCtx();
        fctx->field = field;
        fctx->meta = meta;

        MetaInfo info = {};
        info.kind = MetaKind::Field;
        info.fieldCtx = fctx;
        instanceMap[field->name] = info;
        binding->memberKeys.insert(field->name);
    }
}

static void AttachStaticLiteralFields(js::Context* ctx, js::Value typeObj, Il2CppClass* klass)
{
    il2cpp::vm::Class::SetupFields(klass);
    for (uint16_t i = 0; i < klass->field_count; ++i)
    {
        FieldInfo* field = &klass->fields[i];
        if (!IsPublicField(field))
            continue;
        if (il2cpp::vm::Field::IsInstance(field))
            continue;
        /* Skip backing / special non-literal statics for now except enums & literals. */
        if ((il2cpp::vm::Field::GetFlags(field) & FIELD_ATTRIBUTE_LITERAL) == 0 && !klass->enumtype)
            continue;

        int32_t fieldKind=0;std::vector<std::string> fieldMembers;
        MetadataUtil::TryReadFieldMarshalAs(field,&fieldKind,&fieldMembers);
        const MarshalMetaInfo* meta = MarshalMeta::WithStrategy(field->type,fieldKind);
        if((fieldKind==4||fieldKind==5)&&!fieldMembers.empty())meta=TableMarshal::Create(field->type,il2cpp::vm::Class::FromIl2CppType(field->type),fieldMembers,(MarshalAsKind)fieldKind);
        if (meta == nullptr)
            continue;

        void* storage = alloca((size_t)meta->size);
        il2cpp::vm::Field::StaticGetValue(field, storage);
        js::Value js = meta->cs2jsWriter(ctx, storage, meta);
        js::DefinePropertyValueStr(ctx, typeObj, field->name, js, 4);
    }
}

static void AttachStaticFields(js::Context* ctx, js::Value typeObj, TypeBinding* binding, Il2CppClass* klass)
{
    il2cpp::vm::Class::SetupFields(klass);
    for (uint16_t i = 0; i < klass->field_count; ++i)
    {
        FieldInfo* field = &klass->fields[i];
        if (!IsPublicField(field))
            continue;
        if (il2cpp::vm::Field::IsInstance(field))
            continue;
        if ((il2cpp::vm::Field::GetFlags(field) & FIELD_ATTRIBUTE_LITERAL) != 0)
            continue;
        if (klass->enumtype)
            continue;
        if (binding->memberKeys.find(field->name) != binding->memberKeys.end())
            continue;

        int32_t fieldKind=0;std::vector<std::string> fieldMembers;
        MetadataUtil::TryReadFieldMarshalAs(field,&fieldKind,&fieldMembers);
        const MarshalMetaInfo* meta = MarshalMeta::WithStrategy(field->type,fieldKind);
        if((fieldKind==4||fieldKind==5)&&!fieldMembers.empty())meta=TableMarshal::Create(field->type,il2cpp::vm::Class::FromIl2CppType(field->type),fieldMembers,(MarshalAsKind)fieldKind);
        if (meta == nullptr)
            continue;

        auto* fctx = new FieldMarshalCtx();
        fctx->field = field;
        fctx->meta = meta;

        MetaInfo info = {};
        info.kind = MetaKind::Field;
        info.fieldCtx = fctx;
        binding->staticMap[field->name] = info;
        binding->memberKeys.insert(field->name);

        js::Value getter = CreateMethodFunction(ctx, fctx, StaticFieldGetter, 0, false);
        js::Value setter = CreateMethodFunction(ctx, fctx, StaticFieldSetter, 1, false);
        js::Atom atom = js::NewAtom(ctx, field->name);
        js::DefinePropertyGetSet(ctx, typeObj, atom, getter, setter, js::PropertyConfigurableWritableEnumerable);
        js::FreeAtom(ctx, atom);
    }
}

static void RegisterProperties(Il2CppClass* klass, TypeBinding* binding, NameMetaMap& instanceMap)
{
    void* iter = nullptr;
    while (const PropertyInfo* property = il2cpp::vm::Class::GetProperties(klass, &iter))
    {
        if (!IsPublicZeroParamProperty(property))
            continue;
        if (property->get != nullptr && MetadataUtil::IsStaticMethod(property->get))
            continue;
        if (property->set != nullptr && MetadataUtil::IsStaticMethod(property->set))
            continue;
        if (instanceMap.find(property->name) != instanceMap.end())
            continue;

        MethodMarshalCtx* getter = nullptr;
        MethodMarshalCtx* setter = nullptr;
        if (property->get != nullptr && !TryBuildMarshalCtx(property->get, &getter))
            continue;
        if (property->set != nullptr && !TryBuildMarshalCtx(property->set, &setter))
        {
            /* CLR plans are process-owned and may be shared by another binding. */
            continue;
        }

        MetaInfo info = {};
        info.kind = MetaKind::Property;
        info.methodCtx = getter;
        info.setterCtx = setter;
        instanceMap[property->name] = info;
        binding->memberKeys.insert(property->name);
    }
}

static js::Value ThrowReadOnlyProperty(js::Context* ctx, js::ValueConst /*this_val*/, int /*argc*/, js::ValueConst* /*argv*/)
{
    return js::ThrowTypeError(ctx, "zents: property is read-only");
}

static void AttachStaticProperties(js::Context* ctx, js::Value typeObj, TypeBinding* binding, Il2CppClass* klass)
{
    void* iter = nullptr;
    while (const PropertyInfo* property = il2cpp::vm::Class::GetProperties(klass, &iter))
    {
        if (!IsPublicZeroParamProperty(property))
            continue;
        const bool isStatic =
            (property->get != nullptr && MetadataUtil::IsStaticMethod(property->get))
            || (property->set != nullptr && MetadataUtil::IsStaticMethod(property->set));
        if (!isStatic)
            continue;
        if (binding->memberKeys.find(property->name) != binding->memberKeys.end())
            continue;

        MethodMarshalCtx* getter = nullptr;
        MethodMarshalCtx* setter = nullptr;
        if (property->get != nullptr && !TryBuildMarshalCtx(property->get, &getter))
            continue;
        if (property->set != nullptr && !TryBuildMarshalCtx(property->set, &setter))
        {
            /* CLR plans are process-owned and may be shared by another binding. */
            continue;
        }

        MetaInfo info = {};
        info.kind = MetaKind::Property;
        info.methodCtx = getter;
        info.setterCtx = setter;
        binding->staticMap[property->name] = info;
        binding->memberKeys.insert(property->name);

        js::Value jsGetter = js::Undefined();
        js::Value jsSetter = js::Undefined();
        if (getter != nullptr)
            jsGetter = CreateMethodFunction(ctx, getter, StaticPropertyGetter, 0, false);
        if (setter != nullptr)
            jsSetter = CreateMethodFunction(ctx, setter, StaticPropertySetter, 1, false);
        else if (getter != nullptr)
            jsSetter = js::NewCFunction(ctx, ThrowReadOnlyProperty, "set", 1);
        js::Atom atom = js::NewAtom(ctx, property->name);
        js::DefinePropertyGetSet(ctx, typeObj, atom, jsGetter, jsSetter, js::PropertyConfigurableWritableEnumerable);
        js::FreeAtom(ctx, atom);
    }
}

static void CollectExtensionMethods(Il2CppClass* klass, std::vector<const MethodInfo*>& outMethods)
{
    std::vector<Il2CppClass*> extensionClasses;
    std::unordered_set<Il2CppClass*> seen;

    for (Il2CppClass* walk = klass; walk != nullptr; walk = walk->parent)
    {
        if (walk == il2cpp_defaults.object_class || walk == il2cpp_defaults.value_type_class
            || walk == il2cpp_defaults.enum_class)
            break;

        std::vector<Il2CppClass*> attrTypes;
        if (MetadataUtil::TryReadJsExtensionTypes(walk, attrTypes))
        {
            for (Il2CppClass* ext : attrTypes)
            {
                if (ext != nullptr && seen.insert(ext).second)
                    extensionClasses.push_back(ext);
            }
        }

        std::vector<Il2CppClass*> xmlTypes;
        if (ExtensionXmlTable::TryGetExtensionClasses(walk, xmlTypes))
        {
            for (Il2CppClass* ext : xmlTypes)
            {
                if (ext != nullptr && seen.insert(ext).second)
                    extensionClasses.push_back(ext);
            }
        }
    }

    for (Il2CppClass* extKlass : extensionClasses)
    {
        MetadataUtil::EnsureMethods(extKlass);
        for (uint16_t i = 0; i < extKlass->method_count; ++i)
        {
            const MethodInfo* method = extKlass->methods[i];
            if (!MetadataUtil::IsPublicMethod(method) || !MetadataUtil::IsStaticMethod(method))
                continue;
            if (MetadataUtil::IsCtorOrCCtor(method))
                continue;
            if (!MetadataUtil::IsExtensionMethod(method))
                continue;
            if (method->parameters_count < 1)
                continue;

            Il2CppClass* p0Klass = il2cpp::vm::Class::FromIl2CppType(method->parameters[0]);
            if (p0Klass == nullptr || !il2cpp::vm::Class::IsAssignableFrom(p0Klass, klass))
                continue;
            outMethods.push_back(method);
        }
    }
}

static void CollectMethods(
    Il2CppClass* klass,
    std::vector<const MethodInfo*>& ctors,
    std::vector<const MethodInfo*>& staticMethods,
    std::vector<const MethodInfo*>& instanceMethods)
{
    MetadataUtil::EnsureMethods(klass);
    for (uint16_t i = 0; i < klass->method_count; ++i)
    {
        const MethodInfo* method = klass->methods[i];
        if (!MetadataUtil::IsPublicMethod(method))
            continue;
        if (MetadataUtil::IsCCtor(method))
            continue;
        if (MetadataUtil::IsCtor(method))
        {
            ctors.push_back(method);
            continue;
        }

        /* Skip property accessors; keep event add_/remove_. */
        if ((method->flags & METHOD_ATTRIBUTE_SPECIAL_NAME) != 0)
        {
            const char* name = method->name;
            const bool keepEvent =
                (std::strncmp(name, "add_", 4) == 0) || (std::strncmp(name, "remove_", 7) == 0);
            if (!keepEvent)
                continue;
        }

        if (MetadataUtil::IsStaticMethod(method))
            staticMethods.push_back(method);
        else
            instanceMethods.push_back(method);
    }
}

static js::Value InvokeStaticDispatch(
    js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv, int /*magic*/, js::Value* func_data)
{
    OverloadGroup* group = reinterpret_cast<OverloadGroup*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (group == nullptr)
        return js::ThrowInternalError(ctx, "zents: missing overload group");
    int prefix=0;MethodMarshalCtx* mctx=MetaBinding::ResolveGtap(ctx,group->candidates,argc,argv,&prefix);
    if(js::HasException(ctx))return js::Exception();
    if(mctx){argc-=prefix;argv+=prefix;}else mctx=MethodOverloadResolver::Resolve(ctx,group,argc,argv);
    if (mctx == nullptr)
        return js::HasException(ctx)?js::Exception():js::ThrowTypeError(ctx, "zents: no matching overload");
    return MethodBridge::InvokeJs2Cs(ctx, nullptr, argc, argv, mctx);
}

static js::Value InvokeInstanceDispatch(
    js::Context* ctx, js::ValueConst this_val, int argc, js::ValueConst* argv, int magic, js::Value* func_data)
{
    if(magic!=0 && ObjectRegistry::IsByVal(ctx,this_val)!=(magic==1))return js::ThrowTypeError(ctx,"zents: incompatible ByVal/ByObj receiver");

    OverloadGroup* group = reinterpret_cast<OverloadGroup*>(js::GetOpaque(func_data[0], s_methodCtxClassId));
    if (group == nullptr)
        return js::ThrowInternalError(ctx, "zents: missing overload group");
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (target == nullptr)
        return js::ThrowTypeError(ctx, "zents: instance method requires a CLR object receiver");
    int prefix=0;MethodMarshalCtx* mctx=MetaBinding::ResolveGtap(ctx,group->candidates,argc,argv,&prefix);
    if(js::HasException(ctx))return js::Exception();
    if(mctx){argc-=prefix;argv+=prefix;}else mctx=MethodOverloadResolver::Resolve(ctx,group,argc,argv);
    if (mctx == nullptr)
        return js::HasException(ctx)?js::Exception():js::ThrowTypeError(ctx, "zents: no matching overload");
    return MethodBridge::InvokeJs2Cs(ctx, target, argc, argv, mctx);
}

static void RegisterUniqueMethods(
    js::Context* ctx,
    TypeBinding* binding,
    NameMetaMap& map,
    js::Value targetObj,
    const std::vector<const MethodInfo*>& methods,
    js::CFunctionData* directInvoker,
    js::CFunctionData* dispatchInvoker,
    bool attachToObject)
{
    std::unordered_map<std::string, std::vector<const MethodInfo*>> groups;
    std::unordered_set<std::string> signatures;
    for (const MethodInfo* method : methods)
    {
        std::string signature=MethodOverloadResolver::BuildSignatureKey(method);
        if(MetadataUtil::IsExtensionMethod(method))signature+="@"+MetadataUtil::BuildTypeFullName(method->klass);
        if(!signatures.insert(signature).second)continue;
        std::string key = method->name;
        std::string alias;
        if (MetadataUtil::TryReadJsAlias(method, alias))
            key = alias;
        else if (AliasXmlTable::TryGetAlias(method, alias) && !alias.empty())
            key = alias;
        groups[key].push_back(method);
    }

    for (const auto& kv : groups)
    {
        std::vector<MethodMarshalCtx*> built;
        std::vector<const MethodInfo*> builtMethods;
        for (const MethodInfo* method : kv.second)
        {
            MethodMarshalCtx* mctx = nullptr;
            if (TryBuildMarshalCtx(method, &mctx))
            {
                built.push_back(mctx);
                builtMethods.push_back(method);
            }
        }
        if (built.empty())
            continue;

        /* Keep display name string alive for map keys. */
        binding->ownedNames.push_back(kv.first);
        const char* methodName = binding->ownedNames.back().c_str();
        if (built.size() == 1)
        {
            MetaInfo info = {};
            info.kind = MetaKind::Method;
            info.methodCtx = built[0];
            map[methodName] = info;
            if (attachToObject)
            {
                js::Value fn = CreateMethodFunction(ctx, built[0], directInvoker, built[0]->minArity, true);
                js::SetPropertyStr(ctx, targetObj, methodName, fn);
            }
            binding->memberKeys.insert(methodName);
            continue;
        }

        auto* group = new OverloadGroup();
        group->candidates = built;
        binding->ownedGroups.push_back(group);

        MetaInfo info = {};
        info.kind = MetaKind::MethodDispatch;
        info.overloadGroup = group;
        map[methodName] = info;
        binding->memberKeys.insert(methodName);

        if (attachToObject)
        {
            js::Value fn = CreateMethodFunction(ctx, group, dispatchInvoker, 0, false);
            js::SetPropertyStr(ctx, targetObj, methodName, fn);
        }


    }
}

void MetaBinding::AttachInstanceMembers(js::Context* ctx, js::ValueConst jsObj, TypeBinding* binding)
{
    int mode=il2cpp::vm::Class::IsValuetype(binding->klass)&&!binding->klass->enumtype?(js::StrictEq(ctx,jsObj,binding->byvalInstanceProto)?1:2):0;
    for (const auto& kv : binding->instanceMap)
    {
        const MetaInfo& info = kv.second;
        if (info.kind == MetaKind::Method && info.methodCtx != nullptr)
        {
            js::Value fn = CreateInstanceMethodFunction(ctx, info.methodCtx, mode);
            js::SetPropertyStr(ctx, jsObj, kv.first, fn);
            continue;
        }

        if (info.kind == MetaKind::MethodDispatch && info.overloadGroup != nullptr)
        {
            js::Value fn = CreateMethodFunction(ctx, info.overloadGroup, InvokeInstanceDispatch, 0, false, mode);
            js::SetPropertyStr(ctx, jsObj, kv.first, fn);
            continue;
        }

        if (info.kind == MetaKind::Field && info.fieldCtx != nullptr)
        {
            js::Value getter = CreateMethodFunction(ctx, info.fieldCtx, FieldGetter, 0, false, mode);
            js::Value setter = CreateMethodFunction(ctx, info.fieldCtx, FieldSetter, 1, false, mode);
            js::Atom atom = js::NewAtom(ctx, kv.first);
            js::DefinePropertyGetSet(ctx, jsObj, atom, getter, setter, js::PropertyConfigurableWritableEnumerable);
            js::FreeAtom(ctx, atom);
            continue;
        }

        if (info.kind == MetaKind::Property)
        {
            js::Value getter = js::Undefined();
            js::Value setter = js::Undefined();
            if (info.methodCtx != nullptr)
                getter = CreateMethodFunction(ctx, info.methodCtx, PropertyGetter, 0, false, mode);
            if (info.setterCtx != nullptr)
                setter = CreateMethodFunction(ctx, info.setterCtx, PropertySetter, 1, false, mode);
            else if (info.methodCtx != nullptr)
                setter = js::NewCFunction(ctx, ThrowReadOnlyProperty, "set", 1);
            js::Atom atom = js::NewAtom(ctx, kv.first);
            js::DefinePropertyGetSet(ctx, jsObj, atom, getter, setter, js::PropertyConfigurableWritableEnumerable);
            js::FreeAtom(ctx, atom);
        }
    }
}

js::Value MetaBinding::WrapStrictMiss(js::Context* ctx, js::Value obj)
{
    js::Value global = js::GetGlobalObject(ctx);
    js::Value wrapFn = js::GetPropertyStr(ctx, global, "__zents_wrap_miss");
    js::FreeValue(ctx, global);
    if (!js::IsFunction(ctx, wrapFn))
    {
        js::FreeValue(ctx, wrapFn);
        return obj;
    }
    js::Value argv[1] = { obj };
    js::Value wrapped = js::Call(ctx, wrapFn, js::Undefined(), 1, argv);
    js::FreeValue(ctx, wrapFn);
    if (js::IsException(wrapped))
        return wrapped;
    js::FreeValue(ctx, obj);
    return wrapped;
}

js::Value MetaBinding::WrapDelegateCall(js::Context* ctx, js::Value obj)
{
    js::Value global = js::GetGlobalObject(ctx);
    js::Value wrapFn = js::GetPropertyStr(ctx, global, "__zents_wrap_delegate_call");
    js::FreeValue(ctx, global);
    if (!js::IsFunction(ctx, wrapFn))
    {
        js::FreeValue(ctx, wrapFn);
        return obj;
    }
    js::Value argv[1] = { obj };
    js::Value wrapped = js::Call(ctx, wrapFn, js::Undefined(), 1, argv);
    js::FreeValue(ctx, wrapFn);
    if (js::IsException(wrapped))
        return wrapped;
    js::FreeValue(ctx, obj);
    return wrapped;
}

static void BuildBinding(js::Context* ctx, TypeBinding* binding)
{
    Il2CppClass* klass = binding->klass;
    il2cpp::vm::Class::Init(klass);
    ObjectRegistry::Initialize(js::GetRuntime(ctx));
    EnsureMethodCtxClass(js::GetRuntime(ctx));

    std::vector<const MethodInfo*> ctors;
    std::vector<const MethodInfo*> staticMethods;
    std::vector<const MethodInfo*> instanceMethods;

    CollectMethods(klass, ctors, staticMethods, instanceMethods);
    CollectExtensionMethods(klass, instanceMethods);
    RegisterFields(klass, binding, binding->instanceMap);
    RegisterProperties(klass, binding, binding->instanceMap);
    RegisterIndexerAccessors(klass, binding, binding->instanceMap);

    for (Il2CppClass* current = klass->parent; current != nullptr; current = current->parent)
    {
        if (current == il2cpp_defaults.object_class)
            break;
        std::vector<const MethodInfo*> ignoredCtors;
        CollectMethods(current, ignoredCtors, staticMethods, instanceMethods);
        RegisterFields(current, binding, binding->instanceMap);
        RegisterProperties(current, binding, binding->instanceMap);
        RegisterIndexerAccessors(current, binding, binding->instanceMap);
    }

    for (const MethodInfo* ctor : ctors)
    {
        MethodMarshalCtx* mctx = nullptr;
        if (TryBuildMarshalCtx(ctor, &mctx))
            binding->ctors.push_back(mctx);
    }

    js::Value holder = js::NewObjectClass(ctx, s_methodCtxClassId);
    js::SetOpaque(holder, binding);
    js::Value typeFn = js::NewCFunctionData(ctx, TypeConstruct, 1, 0, 1, &holder);
    js::FreeValue(ctx, holder);
    js::SetConstructorBit(ctx, typeFn, 1);

    RegisterUniqueMethods(
        ctx,
        binding,
        binding->staticMap,
        typeFn,
        staticMethods,
        InvokeStaticMethod,
        InvokeStaticDispatch,
        true);
    RegisterUniqueMethods(
        ctx,
        binding,
        binding->instanceMap,
        typeFn,
        instanceMethods,
        InvokeInstanceMethod,
        InvokeInstanceDispatch,
        false);

    binding->instanceProto = js::NewObject(ctx);
    MetaBinding::AttachInstanceMembers(ctx, binding->instanceProto, binding);
    if (klass->rank > 0)
        ArrayBinding::AttachMembers(ctx, binding->instanceProto, binding, klass);
    binding->byobjInstanceProto = js::Undefined();
    binding->byvalInstanceProto = js::Undefined();

    AttachStaticLiteralFields(ctx, typeFn, klass);
    AttachStaticFields(ctx, typeFn, binding, klass);
    AttachStaticProperties(ctx, typeFn, binding, klass);

    if (il2cpp::vm::Class::IsValuetype(klass) && !klass->enumtype)
    {
        /* ByObj IEO aliases instanceProto; ByVal IEO is a distinct proto with the same member set. */
        binding->byobjInstanceProto = binding->instanceProto;
        binding->byvalInstanceProto = js::NewObject(ctx);
        MetaBinding::AttachInstanceMembers(ctx, binding->byvalInstanceProto, binding);

        js::SetPropertyStr(ctx, typeFn, "__struct", js::NewBool(ctx, 1));
        js::SetPropertyStr(ctx, typeFn, "__byvalInstanceProto", js::DupValue(ctx, binding->byvalInstanceProto));
        js::SetPropertyStr(ctx, typeFn, "__byobjInstanceProto", js::DupValue(ctx, binding->byobjInstanceProto));
        AttachStructDefault(ctx, typeFn, binding);
    }
    if (klass->nullabletype)
        js::SetPropertyStr(ctx, typeFn, "__nullable", js::NewBool(ctx, 1));

    std::string fullName = MetadataUtil::BuildTypeFullName(klass);
    js::SetPropertyStr(ctx, typeFn, "__zents_type_name", js::NewString(ctx, fullName.c_str()));

    binding->typeObjectRaw = js::DupValue(ctx, typeFn);
    binding->typeObject = MetaBinding::WrapStrictMiss(ctx, typeFn);
    binding->hasJsObject = true;
}

TypeBinding* MetaBinding::EnsureBinding(js::Context* ctx, Il2CppClass* klass)
{
    auto it = s_bindings.find(klass);
    if (it != s_bindings.end())
        return it->second;

    TypeBinding* binding = new TypeBinding();
    binding->klass = klass;
    binding->typeObject = js::Undefined();
    binding->typeObjectRaw = js::Undefined();
    binding->instanceProto = js::Undefined();
    binding->byvalInstanceProto = js::Undefined();
    binding->byobjInstanceProto = js::Undefined();
    binding->hasJsObject = false;
    BuildBinding(ctx, binding);
    s_bindings[klass] = binding;
    return binding;
}

Il2CppClass* MetaBinding::TryGetKlassFromTypeValue(js::Context* ctx, js::ValueConst typeVal)
{
    if (ctx == nullptr || !js::IsObject(typeVal))
        return nullptr;
    for (const auto& kv : s_bindings)
    {
        TypeBinding* binding = kv.second;
        if (!binding->hasJsObject)
            continue;
        if (!js::IsUndefined(binding->typeObject) && js::StrictEq(ctx, typeVal, binding->typeObject))
            return binding->klass;
        if (!js::IsUndefined(binding->typeObjectRaw) && js::StrictEq(ctx, typeVal, binding->typeObjectRaw))
            return binding->klass;
    }

    /* Fallback: match STO __zents_type_name (Proxy StrictEq can miss across Dup edges). */
    js::Value nameVal = js::GetPropertyStr(ctx, typeVal, "__zents_type_name");
    if (js::IsException(nameVal) || !js::IsString(nameVal))
    {
        js::FreeValue(ctx, nameVal);
        return nullptr;
    }
    const char* cstr = js::ToCString(ctx, nameVal);
    js::FreeValue(ctx, nameVal);
    if (cstr == nullptr)
        return nullptr;
    std::string fullName = cstr;
    js::FreeCString(ctx, cstr);
    for (const auto& kv : s_bindings)
    {
        TypeBinding* binding = kv.second;
        if (!binding->hasJsObject || binding->klass == nullptr)
            continue;
        if (MetadataUtil::BuildTypeFullName(binding->klass) == fullName)
            return binding->klass;
    }
    return nullptr;
}

void MetaBinding::Reset(js::Context* ctx)
{
    for (auto& kv : s_bindings)
    {
        TypeBinding* binding = kv.second;
        if (binding->hasJsObject && ctx != nullptr)
        {
            if (!js::IsUndefined(binding->typeObject))
            {
                js::FreeValue(ctx, binding->typeObject);
                binding->typeObject = js::Undefined();
            }
            if (!js::IsUndefined(binding->typeObjectRaw))
            {
                js::FreeValue(ctx, binding->typeObjectRaw);
                binding->typeObjectRaw = js::Undefined();
            }
            if (!js::IsUndefined(binding->byvalInstanceProto))
            {
                js::FreeValue(ctx, binding->byvalInstanceProto);
                binding->byvalInstanceProto = js::Undefined();
            }
            /* byobjInstanceProto aliases instanceProto for structs — free once via instanceProto. */
            binding->byobjInstanceProto = js::Undefined();
            if (!js::IsUndefined(binding->instanceProto))
            {
                js::FreeValue(ctx, binding->instanceProto);
                binding->instanceProto = js::Undefined();
            }
            binding->hasJsObject = false;
        }
        for (OverloadGroup* g : binding->ownedGroups)
            delete g;
        delete binding;
    }
    s_bindings.clear();
    s_methodCtxRuntime = nullptr;
    ObjectRegistry::Reset();
}
}
