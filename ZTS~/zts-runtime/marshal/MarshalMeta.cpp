#include "MarshalMeta.h"
#include "ArrayMarshal.h"
#include "ObjectMarshal.h"
#include "PointerMarshal.h"
#include "PrimitiveMarshal.h"
#include "StructMarshal.h"

#include "vm/Class.h"
#include "vm/Type.h"

#include <unordered_map>

namespace zts
{
namespace
{
std::unordered_map<const Il2CppType*, const MarshalMetaInfo*> s_cache;

const MarshalMetaInfo* MakeMeta(
    const Il2CppType* type,
    Il2CppClass* typeKlass,
    FnMarshalJs2Cs js2cs,
    FnMarshalCs2Js cs2js,
    int32_t size,
    bool passByValue)
{
    auto* meta = new MarshalMetaInfo();
    meta->js2csWriter = js2cs;
    meta->cs2jsWriter = cs2js;
    meta->type = type;
    meta->typeKlass = typeKlass;
    meta->size = size;
    meta->passByValue = passByValue;
    meta->jsArgSlots = 1;
    meta->marshalAsKind = MarshalAsKind::None;
    return meta;
}

const MarshalMetaInfo* MakeEnum(const Il2CppType* type, Il2CppClass* klass)
{
    const Il2CppType* underlying = il2cpp::vm::Type::GetUnderlyingType(type);
    if (underlying == nullptr || underlying == type)
        underlying = &il2cpp_defaults.int32_class->byval_arg;
    const MarshalMetaInfo* base = MarshalMeta::TryCreateDefault(underlying);
    if (base == nullptr)
        return nullptr;
    return MakeMeta(type, klass, base->js2csWriter, base->cs2jsWriter, base->size, false);
}

const MarshalMetaInfo* MakeClass(const Il2CppType* type, Il2CppClass* klass)
{
    return MakeMeta(
        type,
        klass,
        ObjectMarshal::Js2CsObject,
        ObjectMarshal::Cs2JsObject,
        sizeof(Il2CppObject*),
        true);
}

const MarshalMetaInfo* MakeStruct(const Il2CppType* type, Il2CppClass* klass)
{
    int32_t size = (int32_t)klass->instance_size - (int32_t)sizeof(Il2CppObject);
    if (size <= 0)
        return nullptr;
    return MakeMeta(type, klass, StructMarshal::Js2CsStruct, StructMarshal::Cs2JsStruct, size, false);
}

const MarshalMetaInfo* MakeNullable(const Il2CppType* type, Il2CppClass* klass)
{
    int32_t size = (int32_t)klass->instance_size - (int32_t)sizeof(Il2CppObject);
    if (size <= 0)
        return nullptr;
    return MakeMeta(type, klass, StructMarshal::Js2CsNullable, StructMarshal::Cs2JsNullable, size, false);
}

const MarshalMetaInfo* MakeSzArray(const Il2CppType* type, Il2CppClass* klass)
{
    return MakeMeta(
        type,
        klass,
        ArrayMarshal::Js2CsSzArray,
        ArrayMarshal::Cs2JsSzArray,
        sizeof(Il2CppArray*),
        true);
}
} // namespace

const MarshalMetaInfo* MarshalMeta::TryCreateDefault(const Il2CppType* type)
{
    if (type == nullptr)
        return nullptr;

    /* byref A → marshal as A into a temp; MethodBridge passes address. */
    if (type->byref)
    {
        Il2CppClass* klass = il2cpp::vm::Class::FromIl2CppType(type);
        if (klass == nullptr)
            return nullptr;
        il2cpp::vm::Class::Init(klass);
        return TryCreateDefault(&klass->byval_arg);
    }

    auto it = s_cache.find(type);
    if (it != s_cache.end())
        return it->second;

    Il2CppClass* klass = il2cpp::vm::Class::FromIl2CppType(type);
    if (klass != nullptr)
        il2cpp::vm::Class::Init(klass);

    const MarshalMetaInfo* meta = nullptr;
    switch (type->type)
    {
    case IL2CPP_TYPE_VOID:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsVoid, PrimitiveMarshal::Cs2JsVoid, 0, false);
        break;
    case IL2CPP_TYPE_BOOLEAN:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsBool, PrimitiveMarshal::Cs2JsBool, sizeof(bool), false);
        break;
    case IL2CPP_TYPE_CHAR:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsChar, PrimitiveMarshal::Cs2JsChar, sizeof(Il2CppChar), false);
        break;
    case IL2CPP_TYPE_I1:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsInt8, PrimitiveMarshal::Cs2JsInt8, sizeof(int8_t), false);
        break;
    case IL2CPP_TYPE_U1:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsUInt8, PrimitiveMarshal::Cs2JsUInt8, sizeof(uint8_t), false);
        break;
    case IL2CPP_TYPE_I2:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsInt16, PrimitiveMarshal::Cs2JsInt16, sizeof(int16_t), false);
        break;
    case IL2CPP_TYPE_U2:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsUInt16, PrimitiveMarshal::Cs2JsUInt16, sizeof(uint16_t), false);
        break;
    case IL2CPP_TYPE_I4:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsInt32, PrimitiveMarshal::Cs2JsInt32, sizeof(int32_t), false);
        break;
    case IL2CPP_TYPE_U4:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsUInt32, PrimitiveMarshal::Cs2JsUInt32, sizeof(uint32_t), false);
        break;
    case IL2CPP_TYPE_I8:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsInt64, PrimitiveMarshal::Cs2JsInt64, sizeof(int64_t), false);
        break;
    case IL2CPP_TYPE_U8:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsUInt64, PrimitiveMarshal::Cs2JsUInt64, sizeof(uint64_t), false);
        break;
    case IL2CPP_TYPE_R4:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsFloat, PrimitiveMarshal::Cs2JsFloat, sizeof(float), false);
        break;
    case IL2CPP_TYPE_R8:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsDouble, PrimitiveMarshal::Cs2JsDouble, sizeof(double), false);
        break;
    case IL2CPP_TYPE_I:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsIntPtr, PrimitiveMarshal::Cs2JsIntPtr, sizeof(intptr_t), false);
        break;
    case IL2CPP_TYPE_U:
        meta = MakeMeta(type, klass, PrimitiveMarshal::Js2CsUIntPtr, PrimitiveMarshal::Cs2JsUIntPtr, sizeof(uintptr_t), false);
        break;
    case IL2CPP_TYPE_STRING:
        meta = MakeMeta(
            type,
            il2cpp_defaults.string_class,
            PrimitiveMarshal::Js2CsString,
            PrimitiveMarshal::Cs2JsString,
            sizeof(Il2CppString*),
            true);
        break;
    case IL2CPP_TYPE_VALUETYPE:
        if (klass != nullptr && klass->enumtype)
            meta = MakeEnum(type, klass);
        else if (klass != nullptr)
            meta = MakeStruct(type, klass);
        break;
    case IL2CPP_TYPE_CLASS:
    case IL2CPP_TYPE_OBJECT:
        if (klass != nullptr)
            meta = MakeClass(type, klass);
        break;
    case IL2CPP_TYPE_SZARRAY:
        if (klass != nullptr)
            meta = MakeSzArray(type, klass);
        break;
    case IL2CPP_TYPE_ARRAY:
        if (klass != nullptr)
            meta = MakeSzArray(type, klass);
        break;
    case IL2CPP_TYPE_PTR:
        meta = PointerMarshal::Create(type);
        break;
    case IL2CPP_TYPE_GENERICINST:
        if (klass != nullptr && klass->nullabletype)
            meta = MakeNullable(type, klass);
        else if (klass != nullptr && il2cpp::vm::Class::IsValuetype(klass))
        {
            if (klass->enumtype)
                meta = MakeEnum(type, klass);
            else
                meta = MakeStruct(type, klass);
        }
        else if (klass != nullptr)
            meta = MakeClass(type, klass);
        break;
    default:
        break;
    }

    if (meta != nullptr)
        s_cache[type] = meta;
    return meta;
}
}
