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

#include "ObjectMarshal.h"
#include "DelegateMarshal.h"
#include "ObjectRegistry.h"
#include "PrimitiveMarshal.h"

#include "../jvm/JsGlobalRefs.h"
#include "../mt/MetaBinding.h"
#include "../utils/MetadataUtil.h"

#include "vm/Class.h"
#include "vm/Object.h"

#include <cmath>
#include <cstdint>

namespace zents
{
void ObjectMarshal::Js2CsObject(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta)
{
    Il2CppObject** out = reinterpret_cast<Il2CppObject**>(address);
    if (js::IsNull(value))
    {
        *out = nullptr;
        return;
    }

    Il2CppClass* expected = meta != nullptr ? meta->typeKlass : nullptr;
    if (expected == nullptr)
    {
        js::ThrowTypeError(ctx, "zents: missing class marshal meta");
        return;
    }

    /* Existing ByObj / Proxy-wrapped CLR instance. */
    if (ObjectRegistry::IsZentsObject(ctx, value))
    {
        Il2CppObject* obj = ObjectRegistry::Get(ctx, value);
        if (obj == nullptr)
        {
            js::ThrowTypeError(ctx, "zents: invalid object handle");
            return;
        }
        if (!il2cpp::vm::Class::IsAssignableFrom(expected, obj->klass))
        {
            js::ThrowTypeError(
                ctx,
                "zents: argument mismatch: object is not of type %s",
                MetadataUtil::GetTypeFullName(expected));
            return;
        }
        *out = obj;
        return;
    }

    /* System.Object: box primitives / wrap string. */
    if (expected == il2cpp_defaults.object_class)
    {
        if (js::IsBool(value))
        {
            bool b = js::ToBool(ctx, value) != 0;
            *out = il2cpp::vm::Object::Box(il2cpp_defaults.boolean_class, &b);
            return;
        }
        if (js::IsNumber(value))
        {
            double d = 0;
            if (js::ToFloat64(ctx, &d, value))
                return;
            if (std::floor(d) == d && d >= (double)INT32_MIN && d <= (double)INT32_MAX)
            {
                int32_t i = (int32_t)d;
                *out = il2cpp::vm::Object::Box(il2cpp_defaults.int32_class, &i);
            }
            else
            {
                *out = il2cpp::vm::Object::Box(il2cpp_defaults.double_class, &d);
            }
            return;
        }
        if (js::IsString(value))
        {
            Il2CppString* storage = nullptr;
            PrimitiveMarshal::Js2CsString(ctx, value, &storage, meta);
            if (js::HasException(ctx))
                return;
            *out = reinterpret_cast<Il2CppObject*>(storage);
            return;
        }
    }

    /* Delegate: JS function → CLR delegate. */
    if (MetadataUtil::IsDelegateClass(expected))
    {
        if (!js::IsFunction(ctx, value))
        {
            js::ThrowTypeError(ctx, "zents: expected function for delegate argument");
            return;
        }
        Il2CppDelegate* del = DelegateMarshal::GetOrCreateFromJsFunction(ctx, expected, value);
        if (del == nullptr)
        {
            js::ThrowTypeError(ctx, "zents: failed to create delegate");
            return;
        }
        *out = reinterpret_cast<Il2CppObject*>(del);
        return;
    }

    js::ThrowTypeError(
        ctx,
        "zents: cannot convert value to object of type %s",
        MetadataUtil::GetTypeFullName(expected));
}

js::Value ObjectMarshal::Cs2JsObject(js::Context* ctx, void* address, const MarshalMetaInfo* meta)
{
    Il2CppObject* obj = *reinterpret_cast<Il2CppObject**>(address);
    if (obj == nullptr)
        return js::Null();

    if (MetadataUtil::IsDelegateClass(obj->klass))
    {
        Il2CppClass* view = meta != nullptr && meta->typeKlass != nullptr ? meta->typeKlass : obj->klass;
        return DelegateMarshal::PushToJs(ctx, reinterpret_cast<Il2CppDelegate*>(obj), view);
    }

    Il2CppClass* view = meta != nullptr && meta->typeKlass != nullptr ? meta->typeKlass : obj->klass;
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, view);
    return ObjectRegistry::Push(ctx, obj, binding);
}
}
