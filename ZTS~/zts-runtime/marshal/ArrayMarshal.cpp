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

#include "ArrayMarshal.h"
#include "MarshalMeta.h"
#include "ObjectRegistry.h"

#include "../mt/MetaBinding.h"
#include "../utils/MetadataUtil.h"

#include "il2cpp-api.h"
#include "vm/Array.h"
#include "vm/Class.h"
#include "vm/Object.h"

namespace zts
{
void ArrayMarshal::Js2CsSzArray(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta)
{
    Il2CppArray** out = reinterpret_cast<Il2CppArray**>(address);
    if (JS_IsNull(value) || JS_IsUndefined(value))
    {
        *out = nullptr;
        return;
    }

    Il2CppClass* arrayKlass = meta != nullptr ? meta->typeKlass : nullptr;
    if (arrayKlass == nullptr)
    {
        JS_ThrowTypeError(ctx, "zts: missing array marshal meta");
        return;
    }

    if (ObjectRegistry::IsZtsObject(ctx, value))
    {
        Il2CppObject* obj = ObjectRegistry::Get(ctx, value);
        if (obj == nullptr || !il2cpp::vm::Class::IsAssignableFrom(arrayKlass, obj->klass))
        {
            JS_ThrowTypeError(ctx, "zts: argument mismatch: expected array %s", MetadataUtil::GetTypeFullName(arrayKlass));
            return;
        }
        *out = reinterpret_cast<Il2CppArray*>(obj);
        return;
    }

    if (!JS_IsArray(ctx, value))
    {
        JS_ThrowTypeError(ctx, "zts: expected JS Array or CLR array");
        return;
    }

    JSValue lenVal = JS_GetPropertyStr(ctx, value, "length");
    int32_t length = 0;
    if (JS_ToInt32(ctx, &length, lenVal))
    {
        JS_FreeValue(ctx, lenVal);
        return;
    }
    JS_FreeValue(ctx, lenVal);
    if (length < 0)
    {
        JS_ThrowRangeError(ctx, "zts: invalid array length");
        return;
    }

    Il2CppClass* elementKlass = arrayKlass->element_class;
    Il2CppArray* arr = il2cpp::vm::Array::New(elementKlass, length);
    const MarshalMetaInfo* elemMeta = MarshalMeta::TryCreateDefault(&elementKlass->byval_arg);
    if (elemMeta == nullptr)
    {
        JS_ThrowTypeError(ctx, "zts: array element type not supported yet");
        return;
    }

    int32_t elementSize = il2cpp::vm::Class::GetArrayElementSize(elementKlass);
    for (int32_t i = 0; i < length; ++i)
    {
        JSValue item = JS_GetPropertyUint32(ctx, value, (uint32_t)i);
        if (JS_IsException(item))
            return;
        void* slot = il2cpp_array_addr_with_size(arr, elementSize, i);
        if (elemMeta->passByValue)
        {
            void* temp = nullptr;
            elemMeta->js2csWriter(ctx, item, &temp, elemMeta);
            JS_FreeValue(ctx, item);
            if (JS_HasException(ctx))
                return;
            *reinterpret_cast<void**>(slot) = temp;
        }
        else
        {
            elemMeta->js2csWriter(ctx, item, slot, elemMeta);
            JS_FreeValue(ctx, item);
            if (JS_HasException(ctx))
                return;
        }
    }

    *out = arr;
}

JSValue ArrayMarshal::Cs2JsSzArray(JSContext* ctx, void* address, const MarshalMetaInfo* meta)
{
    Il2CppArray* arr = *reinterpret_cast<Il2CppArray**>(address);
    if (arr == nullptr)
        return JS_NULL;
    Il2CppClass* view = meta != nullptr && meta->typeKlass != nullptr
        ? meta->typeKlass
        : reinterpret_cast<Il2CppObject*>(arr)->klass;
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, view);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}
}
