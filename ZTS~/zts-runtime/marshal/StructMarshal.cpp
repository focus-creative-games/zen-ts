#include "StructMarshal.h"
#include "MarshalMeta.h"
#include "ObjectRegistry.h"

#include "../mt/MetaBinding.h"
#include "../utils/MetadataUtil.h"

#include "vm/Class.h"
#include "vm/Object.h"

#include <cstring>

namespace zts
{
namespace
{
void* NullableValueAddr(Il2CppClass* nullableKlass, void* dataAddr)
{
    il2cpp::vm::Class::SetupFields(nullableKlass);
    return reinterpret_cast<uint8_t*>(dataAddr) + nullableKlass->fields[1].offset - sizeof(Il2CppObject);
}

void NullableClear(Il2CppClass* nullableKlass, void* dataAddr)
{
    size_t size = (size_t)nullableKlass->instance_size - sizeof(Il2CppObject);
    std::memset(dataAddr, 0, size);
}

void NullableSetHasValue(void* dataAddr)
{
    *reinterpret_cast<uint8_t*>(dataAddr) = 1;
}
} // namespace

void StructMarshal::Js2CsStruct(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta)
{
    if (meta == nullptr || meta->typeKlass == nullptr)
    {
        JS_ThrowTypeError(ctx, "zts: missing struct marshal meta");
        return;
    }

    Il2CppClass* klass = meta->typeKlass;
    size_t size = (size_t)(klass->instance_size - sizeof(Il2CppObject));

    if (JS_IsNull(value) || JS_IsUndefined(value))
    {
        JS_ThrowTypeError(ctx, "zts: struct argument cannot be null");
        return;
    }

    if (ObjectRegistry::IsZtsObject(ctx, value))
    {
        Il2CppObject* obj = ObjectRegistry::Get(ctx, value);
        if (obj == nullptr || !il2cpp::vm::Class::IsAssignableFrom(klass, obj->klass))
        {
            JS_ThrowTypeError(
                ctx,
                "zts: argument mismatch: expected struct %s",
                MetadataUtil::GetTypeFullName(klass));
            return;
        }
        void* src = ObjectUnbox(obj);
        std::memcpy(address, src, size);
        return;
    }

    JS_ThrowTypeError(
        ctx,
        "zts: argument mismatch: expected struct %s",
        MetadataUtil::GetTypeFullName(klass));
}

JSValue StructMarshal::Cs2JsStruct(JSContext* ctx, void* address, const MarshalMetaInfo* meta)
{
    if (meta == nullptr || meta->typeKlass == nullptr)
        return JS_ThrowTypeError(ctx, "zts: missing struct marshal meta");

    Il2CppClass* klass = meta->typeKlass;
    Il2CppObject* boxed = il2cpp::vm::Object::Box(klass, address);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, klass);
    return ObjectRegistry::PushByVal(ctx, boxed, binding);
}

void StructMarshal::Js2CsNullable(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta)
{
    if (meta == nullptr || meta->typeKlass == nullptr)
    {
        JS_ThrowTypeError(ctx, "zts: missing nullable marshal meta");
        return;
    }

    Il2CppClass* nullableKlass = meta->typeKlass;
    if (JS_IsNull(value) || JS_IsUndefined(value))
    {
        NullableClear(nullableKlass, address);
        return;
    }

    Il2CppClass* element = il2cpp::vm::Class::GetNullableArgument(nullableKlass);
    const MarshalMetaInfo* elemMeta = MarshalMeta::TryCreateDefault(&element->byval_arg);
    if (elemMeta == nullptr)
    {
        JS_ThrowTypeError(ctx, "zts: nullable element type not supported");
        return;
    }

    void* valueAddr = NullableValueAddr(nullableKlass, address);
    elemMeta->js2csWriter(ctx, value, valueAddr, elemMeta);
    if (JS_HasException(ctx))
        return;
    NullableSetHasValue(address);
}

JSValue StructMarshal::Cs2JsNullable(JSContext* ctx, void* address, const MarshalMetaInfo* meta)
{
    if (meta == nullptr || meta->typeKlass == nullptr)
        return JS_ThrowTypeError(ctx, "zts: missing nullable marshal meta");

    Il2CppClass* nullableKlass = meta->typeKlass;
    if (!il2cpp::vm::Object::NullableHasValue(nullableKlass, address))
        return JS_NULL;

    Il2CppClass* element = il2cpp::vm::Class::GetNullableArgument(nullableKlass);
    const MarshalMetaInfo* elemMeta = MarshalMeta::TryCreateDefault(&element->byval_arg);
    if (elemMeta == nullptr)
        return JS_ThrowTypeError(ctx, "zts: nullable element type not supported");
    return elemMeta->cs2jsWriter(ctx, NullableValueAddr(nullableKlass, address), elemMeta);
}
}
