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

namespace zts
{
static std::unordered_map<Il2CppClass*, TypeBinding*> s_bindings;
static JSClassID s_methodCtxClassId = 0;
static JSRuntime* s_methodCtxRuntime = nullptr;

static void EnsureMethodCtxClass(JSRuntime* rt)
{
    if (rt == nullptr)
        return;
    if (s_methodCtxRuntime == rt)
        return;
    JS_NewClassID(&s_methodCtxClassId);
    JSClassDef def = {};
    def.class_name = "ZtsMethodCtx";
    JS_NewClass(rt, s_methodCtxClassId, &def);
    s_methodCtxRuntime = rt;
}

static JSValue InvokeStaticMethod(
    JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (mctx == nullptr)
        return JS_ThrowInternalError(ctx, "zts: missing method context");
    if (mctx->method != nullptr && mctx->method->is_generic)
        return JS_ThrowTypeError(ctx, "zts: open generic method; use zts.make_generic_method");
    return MethodBridge::InvokeJs2Cs(ctx, nullptr, argc, argv, mctx);
}

static JSValue InvokeInstanceMethod(
    JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (mctx == nullptr)
        return JS_ThrowInternalError(ctx, "zts: missing method context");
    if (mctx->method != nullptr && mctx->method->is_generic)
        return JS_ThrowTypeError(ctx, "zts: open generic method; use zts.make_generic_method");
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (target == nullptr)
        return JS_ThrowTypeError(ctx, "zts: instance method requires a CLR object receiver");
    return MethodBridge::InvokeJs2Cs(ctx, target, argc, argv, mctx);
}

static JSValue FieldGetter(
    JSContext* ctx, JSValueConst this_val, int /*argc*/, JSValueConst* /*argv*/, int /*magic*/, JSValue* func_data)
{
    FieldMarshalCtx* fctx =
        reinterpret_cast<FieldMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (fctx == nullptr || target == nullptr)
        return JS_ThrowTypeError(ctx, "zts: invalid field getter receiver");

    void* tempStorage = nullptr;
    void* storage = fctx->meta->passByValue ? &tempStorage : alloca((size_t)fctx->meta->size);
    il2cpp::vm::Field::GetValue(target, fctx->field, storage);
    return fctx->meta->cs2jsWriter(ctx, storage, fctx->meta);
}

static JSValue FieldSetter(
    JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    FieldMarshalCtx* fctx =
        reinterpret_cast<FieldMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (fctx == nullptr || target == nullptr)
        return JS_ThrowTypeError(ctx, "zts: invalid field setter receiver");
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zts: field setter requires a value");

    void* tempStorage = nullptr;
    void* storage = fctx->meta->passByValue ? &tempStorage : alloca((size_t)fctx->meta->size);
    fctx->meta->js2csWriter(ctx, argv[0], storage, fctx->meta);
    if (JS_HasException(ctx))
        return JS_EXCEPTION;
    /* Field::SetValue uses deref_pointer=false for refs ??pass object pointer, not &slot. */
    void* setArg = fctx->meta->passByValue ? tempStorage : storage;
    il2cpp::vm::Field::SetValue(target, fctx->field, setArg);
    return JS_UNDEFINED;
}

static JSValue StaticFieldGetter(
    JSContext* ctx, JSValueConst /*this_val*/, int /*argc*/, JSValueConst* /*argv*/, int /*magic*/, JSValue* func_data)
{
    FieldMarshalCtx* fctx =
        reinterpret_cast<FieldMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (fctx == nullptr)
        return JS_ThrowTypeError(ctx, "zts: invalid static field getter");

    void* tempStorage = nullptr;
    void* storage = fctx->meta->passByValue ? &tempStorage : alloca((size_t)fctx->meta->size);
    il2cpp::vm::Field::StaticGetValue(fctx->field, storage);
    return fctx->meta->cs2jsWriter(ctx, storage, fctx->meta);
}

static JSValue StaticFieldSetter(
    JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    FieldMarshalCtx* fctx =
        reinterpret_cast<FieldMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (fctx == nullptr)
        return JS_ThrowTypeError(ctx, "zts: invalid static field setter");
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zts: field setter requires a value");

    void* tempStorage = nullptr;
    void* storage = fctx->meta->passByValue ? &tempStorage : alloca((size_t)fctx->meta->size);
    fctx->meta->js2csWriter(ctx, argv[0], storage, fctx->meta);
    if (JS_HasException(ctx))
        return JS_EXCEPTION;
    void* setArg = fctx->meta->passByValue ? tempStorage : storage;
    il2cpp::vm::Field::StaticSetValue(fctx->field, setArg);
    return JS_UNDEFINED;
}

static JSValue PropertyGetter(
    JSContext* ctx, JSValueConst this_val, int /*argc*/, JSValueConst* /*argv*/, int /*magic*/, JSValue* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (mctx == nullptr || target == nullptr)
        return JS_ThrowTypeError(ctx, "zts: invalid property getter receiver");
    return MethodBridge::InvokeJs2Cs(ctx, target, 0, nullptr, mctx);
}

static JSValue PropertySetter(
    JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (mctx == nullptr || target == nullptr)
        return JS_ThrowTypeError(ctx, "zts: invalid property setter receiver");
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zts: property setter requires a value");
    return MethodBridge::InvokeJs2Cs(ctx, target, 1, argv, mctx);
}

static JSValue StaticPropertyGetter(
    JSContext* ctx, JSValueConst /*this_val*/, int /*argc*/, JSValueConst* /*argv*/, int /*magic*/, JSValue* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (mctx == nullptr)
        return JS_ThrowTypeError(ctx, "zts: invalid static property getter");
    return MethodBridge::InvokeJs2Cs(ctx, nullptr, 0, nullptr, mctx);
}

static JSValue StaticPropertySetter(
    JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    MethodMarshalCtx* mctx =
        reinterpret_cast<MethodMarshalCtx*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (mctx == nullptr)
        return JS_ThrowTypeError(ctx, "zts: invalid static property setter");
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zts: property setter requires a value");
    return MethodBridge::InvokeJs2Cs(ctx, nullptr, 1, argv, mctx);
}

static MethodMarshalCtx* FindCtor(TypeBinding* binding, JSContext* ctx, int argc, JSValueConst* argv)
{
    OverloadGroup group;
    group.candidates = binding->ctors;
    return MethodOverloadResolver::Resolve(ctx, &group, argc, argv);
}

static JSValue TypeConstruct(
    JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    TypeBinding* binding =
        reinterpret_cast<TypeBinding*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (binding == nullptr)
        return JS_ThrowInternalError(ctx, "zts: missing type binding");

    MethodMarshalCtx* ctor = FindCtor(binding, ctx, argc, argv);
    if (ctor == nullptr)
    {
        return JS_ThrowTypeError(
            ctx,
            "zts: no matching constructor for %s with %d argument(s)",
            MetadataUtil::GetTypeFullName(binding->klass),
            argc);
    }

    Il2CppObject* obj = il2cpp::vm::Object::New(binding->klass);
    JSValue invokeRet = MethodBridge::InvokeJs2Cs(ctx, obj, argc, argv, ctor);
    if (JS_IsException(invokeRet))
        return invokeRet;
    JS_FreeValue(ctx, invokeRet);
    if (il2cpp::vm::Class::IsValuetype(binding->klass) && !binding->klass->enumtype)
        return ObjectRegistry::PushByVal(ctx, obj, binding);
    return ObjectRegistry::Push(ctx, obj, binding);
}

static JSValue StructDefaultInvoke(
    JSContext* ctx, JSValueConst /*this_val*/, int /*argc*/, JSValueConst* /*argv*/, int /*magic*/, JSValue* func_data)
{
    TypeBinding* binding =
        reinterpret_cast<TypeBinding*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (binding == nullptr || binding->klass == nullptr)
        return JS_ThrowInternalError(ctx, "zts: missing type binding for _default");

    Il2CppClass* klass = binding->klass;
    il2cpp::vm::Class::Init(klass);
    /* Object::New for ptr-free valuetypes uses GC_MALLOC_ATOMIC (uninitialized). */
    Il2CppObject* boxed = il2cpp::vm::Object::New(klass);
    size_t size = (size_t)il2cpp::vm::Class::GetInstanceSize(klass);
    if (size > sizeof(Il2CppObject))
        std::memset(ObjectUnbox(boxed), 0, size - sizeof(Il2CppObject));
    return ObjectRegistry::PushByVal(ctx, boxed, binding);
}

static void AttachStructDefault(JSContext* ctx, JSValue typeObj, TypeBinding* binding)
{
    Il2CppClass* klass = binding->klass;
    if (!il2cpp::vm::Class::IsValuetype(klass) || klass->enumtype)
        return;

    binding->memberKeys.insert("_default");
    JSValue holder = JS_NewObjectClass(ctx, s_methodCtxClassId);
    JS_SetOpaque(holder, binding);
    JSValue fn = JS_NewCFunctionData(ctx, StructDefaultInvoke, 0, 0, 1, &holder);
    JS_FreeValue(ctx, holder);
    JS_SetPropertyStr(ctx, typeObj, "_default", fn);
}

static JSValue Cs2JsOpaqueValue(JSContext* ctx, void* address, const MarshalMetaInfo* meta)
{
    return OpaqueValueMarshal::Push(ctx, address, meta->type);
}

static void Js2CsOpaqueValue(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta)
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

    if (method->is_generic)
    {
    auto* ctx = new MethodMarshalCtx();
    ctx->method = method;
    ctx->js2CsInvoker = nullptr; /* open generic: invoke rejects */
    ctx->paramsMeta = nullptr;
    ctx->retMeta = nullptr;
    ctx->defaults = nullptr;
    ctx->arity = method->parameters_count;
    ctx->minArity = method->parameters_count;
    ctx->sealed = true;
    ctx->isExtension = false;
    ctx->hasParamsArray = false;
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
                    const MarshalMetaInfo* base = MarshalMeta::TryCreateDefault(method->parameters[i]);
                    if (base != nullptr)
                    {
                        auto* bad = new MarshalMetaInfo();
                        *bad = *base;
                        bad->js2csWriter = BytesMarshal::Js2CsBytesTypeMismatch;
                        bad->cs2jsWriter = BytesMarshal::Cs2JsBytesTypeMismatch;
                        meta = bad;
                    }
                }
            }
            else if (marshalKind == 3 /* OpaqueValue */)
            {
                const MarshalMetaInfo* base = MarshalMeta::TryCreateDefault(method->parameters[i]);
                if (base != nullptr)
                {
                    auto* om = new MarshalMetaInfo();
                    *om = *base;
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
                const MarshalMetaInfo* base = MarshalMeta::TryCreateDefault(method->return_type);
                if (base != nullptr)
                {
                    auto* bad = new MarshalMetaInfo();
                    *bad = *base;
                    bad->js2csWriter = BytesMarshal::Js2CsBytesTypeMismatch;
                    bad->cs2jsWriter = BytesMarshal::Cs2JsBytesTypeMismatch;
                    retMeta = bad;
                }
            }
        }
        else if (retMarshalKind == 3 /* OpaqueValue */)
        {
            const MarshalMetaInfo* base = MarshalMeta::TryCreateDefault(method->return_type);
            if (base != nullptr)
            {
                auto* om = new MarshalMetaInfo();
                *om = *base;
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
    ctx->retMeta = retMeta;
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
            ctx->hasParamsArray = true;

        for (uint8_t i = start; i < method->parameters_count; ++i)
        {
            int32_t slots = TableMarshal::GetJsArgSlotCount(paramsMeta[i]);
            if ((int)i == paramsIndex)
            {
                jsArity += 1; /* params is a single JS slot when provided */
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
    *outCtx = ctx;
    return true;
}

MethodMarshalCtx* MetaBinding::CreateMethodMarshalCtx(const MethodInfo* method)
{
    MethodMarshalCtx* ctx = nullptr;
    if (!TryBuildMarshalCtx(method, &ctx))
        return nullptr;
    return ctx;
}

static JSValue CreateMethodFunction(JSContext* ctx, void* opaque, JSCFunctionData* invoker, int length, bool markDirect)
{
    EnsureMethodCtxClass(JS_GetRuntime(ctx));
    JSValue holder = JS_NewObjectClass(ctx, s_methodCtxClassId);
    JS_SetOpaque(holder, opaque);
    JSValue fn = JS_NewCFunctionData(ctx, invoker, length, 0, 1, &holder);
    if (markDirect)
    {
        JS_SetPropertyStr(ctx, fn, "__zts_mctx", JS_DupValue(ctx, holder));
        JS_SetPropertyStr(ctx, fn, "__zts_direct", JS_NewBool(ctx, 1));
    }
    JS_FreeValue(ctx, holder);
    return fn;
}

JSValue MetaBinding::CreateInstanceMethodFunction(JSContext* ctx, MethodMarshalCtx* mctx)
{
    return CreateMethodFunction(ctx, mctx, InvokeInstanceMethod, mctx->minArity, true);
}

JSValue MetaBinding::CreateStaticMethodFunction(JSContext* ctx, MethodMarshalCtx* mctx)
{
    return CreateMethodFunction(ctx, mctx, InvokeStaticMethod, mctx->minArity, true);
}

MethodMarshalCtx* MetaBinding::TryGetDirectMethodCtx(JSContext* ctx, JSValueConst fn)
{
    if (!JS_IsFunction(ctx, fn))
        return nullptr;
    JSValue direct = JS_GetPropertyStr(ctx, fn, "__zts_direct");
    bool ok = JS_IsBool(direct) && JS_ToBool(ctx, direct);
    JS_FreeValue(ctx, direct);
    if (!ok)
        return nullptr;
    JSValue holder = JS_GetPropertyStr(ctx, fn, "__zts_mctx");
    if (!JS_IsObject(holder))
    {
        JS_FreeValue(ctx, holder);
        return nullptr;
    }
    EnsureMethodCtxClass(JS_GetRuntime(ctx));
    MethodMarshalCtx* mctx = reinterpret_cast<MethodMarshalCtx*>(JS_GetOpaque(holder, s_methodCtxClassId));
    JS_FreeValue(ctx, holder);
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

        const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(field->type);
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

static void AttachStaticLiteralFields(JSContext* ctx, JSValue typeObj, Il2CppClass* klass)
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

        const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(field->type);
        if (meta == nullptr)
            continue;

        void* storage = alloca((size_t)meta->size);
        il2cpp::vm::Field::StaticGetValue(field, storage);
        JSValue js = meta->cs2jsWriter(ctx, storage, meta);
        JS_SetPropertyStr(ctx, typeObj, field->name, js);
    }
}

static void AttachStaticFields(JSContext* ctx, JSValue typeObj, TypeBinding* binding, Il2CppClass* klass)
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

        const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(field->type);
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

        JSValue getter = CreateMethodFunction(ctx, fctx, StaticFieldGetter, 0, false);
        JSValue setter = CreateMethodFunction(ctx, fctx, StaticFieldSetter, 1, false);
        JSAtom atom = JS_NewAtom(ctx, field->name);
        JS_DefinePropertyGetSet(ctx, typeObj, atom, getter, setter, JS_PROP_C_W_E);
        JS_FreeAtom(ctx, atom);
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
            delete getter;
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

static JSValue ThrowReadOnlyProperty(JSContext* ctx, JSValueConst /*this_val*/, int /*argc*/, JSValueConst* /*argv*/)
{
    return JS_ThrowTypeError(ctx, "zts: property is read-only");
}

static void AttachStaticProperties(JSContext* ctx, JSValue typeObj, TypeBinding* binding, Il2CppClass* klass)
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
            delete getter;
            continue;
        }

        MetaInfo info = {};
        info.kind = MetaKind::Property;
        info.methodCtx = getter;
        info.setterCtx = setter;
        binding->staticMap[property->name] = info;
        binding->memberKeys.insert(property->name);

        JSValue jsGetter = JS_UNDEFINED;
        JSValue jsSetter = JS_UNDEFINED;
        if (getter != nullptr)
            jsGetter = CreateMethodFunction(ctx, getter, StaticPropertyGetter, 0, false);
        if (setter != nullptr)
            jsSetter = CreateMethodFunction(ctx, setter, StaticPropertySetter, 1, false);
        else if (getter != nullptr)
            jsSetter = JS_NewCFunction(ctx, ThrowReadOnlyProperty, "set", 1);
        JSAtom atom = JS_NewAtom(ctx, property->name);
        JS_DefinePropertyGetSet(ctx, typeObj, atom, jsGetter, jsSetter, JS_PROP_C_W_E);
        JS_FreeAtom(ctx, atom);
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
            if (!MetadataUtil::IsExtensionMethod(method) || method->is_generic)
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

static JSValue InvokeStaticDispatch(
    JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    OverloadGroup* group = reinterpret_cast<OverloadGroup*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (group == nullptr)
        return JS_ThrowInternalError(ctx, "zts: missing overload group");
    MethodMarshalCtx* mctx = MethodOverloadResolver::Resolve(ctx, group, argc, argv);
    if (mctx == nullptr)
        return JS_ThrowTypeError(ctx, "zts: no matching overload");
    return MethodBridge::InvokeJs2Cs(ctx, nullptr, argc, argv, mctx);
}

static JSValue InvokeInstanceDispatch(
    JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    OverloadGroup* group = reinterpret_cast<OverloadGroup*>(JS_GetOpaque(func_data[0], s_methodCtxClassId));
    if (group == nullptr)
        return JS_ThrowInternalError(ctx, "zts: missing overload group");
    Il2CppObject* target = ObjectRegistry::Get(ctx, this_val);
    if (target == nullptr)
        return JS_ThrowTypeError(ctx, "zts: instance method requires a CLR object receiver");
    MethodMarshalCtx* mctx = MethodOverloadResolver::Resolve(ctx, group, argc, argv);
    if (mctx == nullptr)
        return JS_ThrowTypeError(ctx, "zts: no matching overload");
    return MethodBridge::InvokeJs2Cs(ctx, target, argc, argv, mctx);
}

static void RegisterUniqueMethods(
    JSContext* ctx,
    TypeBinding* binding,
    NameMetaMap& map,
    JSValue targetObj,
    const std::vector<const MethodInfo*>& methods,
    JSCFunctionData* directInvoker,
    JSCFunctionData* dispatchInvoker,
    bool attachToObject)
{
    std::unordered_map<std::string, std::vector<const MethodInfo*>> groups;
    for (const MethodInfo* method : methods)
    {
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
                JSValue fn = CreateMethodFunction(ctx, built[0], directInvoker, built[0]->minArity, true);
                JS_SetPropertyStr(ctx, targetObj, methodName, fn);
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
            JSValue fn = CreateMethodFunction(ctx, group, dispatchInvoker, 0, false);
            JS_SetPropertyStr(ctx, targetObj, methodName, fn);
        }

        for (size_t i = 0; i < built.size(); ++i)
        {
            binding->ownedNames.push_back(MethodOverloadResolver::BuildSignatureKey(builtMethods[i]));
            const char* sigKey = binding->ownedNames.back().c_str();
            MetaInfo sigInfo = {};
            sigInfo.kind = MetaKind::Method;
            sigInfo.methodCtx = built[i];
            map[sigKey] = sigInfo;
            binding->memberKeys.insert(sigKey);
            if (attachToObject)
            {
                JSValue fn = CreateMethodFunction(ctx, built[i], directInvoker, built[i]->minArity, true);
                JS_SetPropertyStr(ctx, targetObj, sigKey, fn);
            }
        }
    }
}

void MetaBinding::AttachInstanceMembers(JSContext* ctx, JSValueConst jsObj, TypeBinding* binding)
{
    for (const auto& kv : binding->instanceMap)
    {
        const MetaInfo& info = kv.second;
        if (info.kind == MetaKind::Method && info.methodCtx != nullptr)
        {
            JSValue fn = CreateInstanceMethodFunction(ctx, info.methodCtx);
            JS_SetPropertyStr(ctx, jsObj, kv.first, fn);
            continue;
        }

        if (info.kind == MetaKind::MethodDispatch && info.overloadGroup != nullptr)
        {
            JSValue fn = CreateMethodFunction(ctx, info.overloadGroup, InvokeInstanceDispatch, 0, false);
            JS_SetPropertyStr(ctx, jsObj, kv.first, fn);
            continue;
        }

        if (info.kind == MetaKind::Field && info.fieldCtx != nullptr)
        {
            JSValue getter = CreateMethodFunction(ctx, info.fieldCtx, FieldGetter, 0, false);
            JSValue setter = CreateMethodFunction(ctx, info.fieldCtx, FieldSetter, 1, false);
            JSAtom atom = JS_NewAtom(ctx, kv.first);
            JS_DefinePropertyGetSet(ctx, jsObj, atom, getter, setter, JS_PROP_C_W_E);
            JS_FreeAtom(ctx, atom);
            continue;
        }

        if (info.kind == MetaKind::Property)
        {
            JSValue getter = JS_UNDEFINED;
            JSValue setter = JS_UNDEFINED;
            if (info.methodCtx != nullptr)
                getter = CreateMethodFunction(ctx, info.methodCtx, PropertyGetter, 0, false);
            if (info.setterCtx != nullptr)
                setter = CreateMethodFunction(ctx, info.setterCtx, PropertySetter, 1, false);
            else if (info.methodCtx != nullptr)
                setter = JS_NewCFunction(ctx, ThrowReadOnlyProperty, "set", 1);
            JSAtom atom = JS_NewAtom(ctx, kv.first);
            JS_DefinePropertyGetSet(ctx, jsObj, atom, getter, setter, JS_PROP_C_W_E);
            JS_FreeAtom(ctx, atom);
        }
    }
}

JSValue MetaBinding::WrapStrictMiss(JSContext* ctx, JSValue obj)
{
    JSValue global = JS_GetGlobalObject(ctx);
    JSValue wrapFn = JS_GetPropertyStr(ctx, global, "__zts_wrap_miss");
    JS_FreeValue(ctx, global);
    if (!JS_IsFunction(ctx, wrapFn))
    {
        JS_FreeValue(ctx, wrapFn);
        return obj;
    }
    JSValue argv[1] = { obj };
    JSValue wrapped = JS_Call(ctx, wrapFn, JS_UNDEFINED, 1, argv);
    JS_FreeValue(ctx, wrapFn);
    if (JS_IsException(wrapped))
        return wrapped;
    JS_FreeValue(ctx, obj);
    return wrapped;
}

JSValue MetaBinding::WrapDelegateCall(JSContext* ctx, JSValue obj)
{
    JSValue global = JS_GetGlobalObject(ctx);
    JSValue wrapFn = JS_GetPropertyStr(ctx, global, "__zts_wrap_delegate_call");
    JS_FreeValue(ctx, global);
    if (!JS_IsFunction(ctx, wrapFn))
    {
        JS_FreeValue(ctx, wrapFn);
        return obj;
    }
    JSValue argv[1] = { obj };
    JSValue wrapped = JS_Call(ctx, wrapFn, JS_UNDEFINED, 1, argv);
    JS_FreeValue(ctx, wrapFn);
    if (JS_IsException(wrapped))
        return wrapped;
    JS_FreeValue(ctx, obj);
    return wrapped;
}

static void BuildBinding(JSContext* ctx, TypeBinding* binding)
{
    Il2CppClass* klass = binding->klass;
    il2cpp::vm::Class::Init(klass);
    ObjectRegistry::Initialize(JS_GetRuntime(ctx));
    EnsureMethodCtxClass(JS_GetRuntime(ctx));

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

    JSValue holder = JS_NewObjectClass(ctx, s_methodCtxClassId);
    JS_SetOpaque(holder, binding);
    JSValue typeFn = JS_NewCFunctionData(ctx, TypeConstruct, 1, 0, 1, &holder);
    JS_FreeValue(ctx, holder);
    JS_SetConstructorBit(ctx, typeFn, 1);

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

    binding->instanceProto = JS_NewObject(ctx);
    MetaBinding::AttachInstanceMembers(ctx, binding->instanceProto, binding);
    if (klass->rank > 0)
        ArrayBinding::AttachMembers(ctx, binding->instanceProto, binding, klass);
    binding->byobjInstanceProto = JS_UNDEFINED;
    binding->byvalInstanceProto = JS_UNDEFINED;

    AttachStaticLiteralFields(ctx, typeFn, klass);
    AttachStaticFields(ctx, typeFn, binding, klass);
    AttachStaticProperties(ctx, typeFn, binding, klass);

    if (il2cpp::vm::Class::IsValuetype(klass) && !klass->enumtype)
    {
        /* ByObj IEO aliases instanceProto; ByVal IEO is a distinct proto with the same member set. */
        binding->byobjInstanceProto = binding->instanceProto;
        binding->byvalInstanceProto = JS_NewObject(ctx);
        MetaBinding::AttachInstanceMembers(ctx, binding->byvalInstanceProto, binding);

        JS_SetPropertyStr(ctx, typeFn, "__struct", JS_NewBool(ctx, 1));
        JS_SetPropertyStr(ctx, typeFn, "__byvalInstanceProto", JS_DupValue(ctx, binding->byvalInstanceProto));
        JS_SetPropertyStr(ctx, typeFn, "__byobjInstanceProto", JS_DupValue(ctx, binding->byobjInstanceProto));
        AttachStructDefault(ctx, typeFn, binding);
    }
    if (klass->nullabletype)
        JS_SetPropertyStr(ctx, typeFn, "__nullable", JS_NewBool(ctx, 1));

    std::string fullName = MetadataUtil::BuildTypeFullName(klass);
    JS_SetPropertyStr(ctx, typeFn, "__zts_type_name", JS_NewString(ctx, fullName.c_str()));

    binding->typeObjectRaw = JS_DupValue(ctx, typeFn);
    binding->typeObject = MetaBinding::WrapStrictMiss(ctx, typeFn);
    binding->hasJsObject = true;
}

TypeBinding* MetaBinding::EnsureBinding(JSContext* ctx, Il2CppClass* klass)
{
    auto it = s_bindings.find(klass);
    if (it != s_bindings.end())
        return it->second;

    TypeBinding* binding = new TypeBinding();
    binding->klass = klass;
    binding->typeObject = JS_UNDEFINED;
    binding->typeObjectRaw = JS_UNDEFINED;
    binding->instanceProto = JS_UNDEFINED;
    binding->byvalInstanceProto = JS_UNDEFINED;
    binding->byobjInstanceProto = JS_UNDEFINED;
    binding->hasJsObject = false;
    BuildBinding(ctx, binding);
    s_bindings[klass] = binding;
    return binding;
}

Il2CppClass* MetaBinding::TryGetKlassFromTypeValue(JSContext* ctx, JSValueConst typeVal)
{
    if (ctx == nullptr || !JS_IsObject(typeVal))
        return nullptr;
    for (const auto& kv : s_bindings)
    {
        TypeBinding* binding = kv.second;
        if (!binding->hasJsObject)
            continue;
        if (!JS_IsUndefined(binding->typeObject) && JS_StrictEq(ctx, typeVal, binding->typeObject))
            return binding->klass;
        if (!JS_IsUndefined(binding->typeObjectRaw) && JS_StrictEq(ctx, typeVal, binding->typeObjectRaw))
            return binding->klass;
    }

    /* Fallback: match STO __zts_type_name (Proxy StrictEq can miss across Dup edges). */
    JSValue nameVal = JS_GetPropertyStr(ctx, typeVal, "__zts_type_name");
    if (JS_IsException(nameVal) || !JS_IsString(nameVal))
    {
        JS_FreeValue(ctx, nameVal);
        return nullptr;
    }
    const char* cstr = JS_ToCString(ctx, nameVal);
    JS_FreeValue(ctx, nameVal);
    if (cstr == nullptr)
        return nullptr;
    std::string fullName = cstr;
    JS_FreeCString(ctx, cstr);
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

void MetaBinding::Reset(JSContext* ctx)
{
    for (auto& kv : s_bindings)
    {
        TypeBinding* binding = kv.second;
        if (binding->hasJsObject && ctx != nullptr)
        {
            if (!JS_IsUndefined(binding->typeObject))
            {
                JS_FreeValue(ctx, binding->typeObject);
                binding->typeObject = JS_UNDEFINED;
            }
            if (!JS_IsUndefined(binding->typeObjectRaw))
            {
                JS_FreeValue(ctx, binding->typeObjectRaw);
                binding->typeObjectRaw = JS_UNDEFINED;
            }
            if (!JS_IsUndefined(binding->byvalInstanceProto))
            {
                JS_FreeValue(ctx, binding->byvalInstanceProto);
                binding->byvalInstanceProto = JS_UNDEFINED;
            }
            /* byobjInstanceProto aliases instanceProto for structs — free once via instanceProto. */
            binding->byobjInstanceProto = JS_UNDEFINED;
            if (!JS_IsUndefined(binding->instanceProto))
            {
                JS_FreeValue(ctx, binding->instanceProto);
                binding->instanceProto = JS_UNDEFINED;
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
