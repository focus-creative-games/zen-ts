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

#include "StructMarshal.h"
#include "gc/GarbageCollector.h"
#include "MarshalMeta.h"
#include "ObjectRegistry.h"

#include "../mt/MetaBinding.h"
#include "../utils/MetadataUtil.h"

#include "vm/Class.h"
#include "vm/Object.h"

#include <cstring>

namespace zents
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

void StructMarshal::Js2CsStruct(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta)
{
    if (meta == nullptr || meta->typeKlass == nullptr)
    {
        js::ThrowTypeError(ctx, "zents: missing struct marshal meta");
        return;
    }

    Il2CppClass* klass = meta->typeKlass;
    size_t size = (size_t)(klass->instance_size - sizeof(Il2CppObject));

    if (js::IsNull(value) || js::IsUndefined(value))
    {
        js::ThrowTypeError(ctx, "zents: struct argument cannot be null");
        return;
    }

    if (ObjectRegistry::IsZentsObject(ctx, value))
    {
        Il2CppObject* obj = ObjectRegistry::Get(ctx, value);
        if (obj == nullptr || !il2cpp::vm::Class::IsAssignableFrom(klass, obj->klass))
        {
            js::ThrowTypeError(
                ctx,
                "zents: argument mismatch: expected struct %s",
                MetadataUtil::GetTypeFullName(klass));
            return;
        }
        void* src = ObjectUnbox(obj);
        std::memcpy(address, src, size);
        if(klass->has_references)il2cpp::gc::GarbageCollector::SetWriteBarrier(reinterpret_cast<void**>(address),size);
        return;
    }

    js::ThrowTypeError(
        ctx,
        "zents: argument mismatch: expected struct %s",
        MetadataUtil::GetTypeFullName(klass));
}

js::Value StructMarshal::Cs2JsStruct(js::Context* ctx, void* address, const MarshalMetaInfo* meta)
{
    if (meta == nullptr || meta->typeKlass == nullptr)
        return js::ThrowTypeError(ctx, "zents: missing struct marshal meta");

    Il2CppClass* klass = meta->typeKlass;
    Il2CppObject* boxed = il2cpp::vm::Object::Box(klass, address);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, klass);
    return ObjectRegistry::PushByVal(ctx, boxed, binding);
}

void StructMarshal::Js2CsNullable(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta)
{
    if (meta == nullptr || meta->typeKlass == nullptr)
    {
        js::ThrowTypeError(ctx, "zents: missing nullable marshal meta");
        return;
    }

    Il2CppClass* nullableKlass = meta->typeKlass;
    if (js::IsNull(value) || js::IsUndefined(value))
    {
        NullableClear(nullableKlass, address);
        return;
    }

    Il2CppClass* element = il2cpp::vm::Class::GetNullableArgument(nullableKlass);
    const MarshalMetaInfo* elemMeta = MarshalMeta::TryCreateDefault(&element->byval_arg);
    if (elemMeta == nullptr)
    {
        js::ThrowTypeError(ctx, "zents: nullable element type not supported");
        return;
    }

    void* valueAddr = NullableValueAddr(nullableKlass, address);
    elemMeta->js2csWriter(ctx, value, valueAddr, elemMeta);
    if (js::HasException(ctx))
        return;
    NullableSetHasValue(address);
}

js::Value StructMarshal::Cs2JsNullable(js::Context* ctx, void* address, const MarshalMetaInfo* meta)
{
    if (meta == nullptr || meta->typeKlass == nullptr)
        return js::ThrowTypeError(ctx, "zents: missing nullable marshal meta");

    Il2CppClass* nullableKlass = meta->typeKlass;
    if (!il2cpp::vm::Object::NullableHasValue(nullableKlass, address))
        return js::Null();

    Il2CppClass* element = il2cpp::vm::Class::GetNullableArgument(nullableKlass);
    const MarshalMetaInfo* elemMeta = MarshalMeta::TryCreateDefault(&element->byval_arg);
    if (elemMeta == nullptr)
        return js::ThrowTypeError(ctx, "zents: nullable element type not supported");
    return elemMeta->cs2jsWriter(ctx, NullableValueAddr(nullableKlass, address), elemMeta);
}
}
