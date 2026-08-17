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

#include "BytesMarshal.h"

#include "vm/Array.h"
#include "vm/Class.h"

#include <cstring>

namespace zents
{
void BytesMarshal::Js2CsBytes(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    if (JS_IsNull(value))
    {
        *reinterpret_cast<Il2CppArray**>(address) = nullptr;
        return;
    }
    if (JS_IsUndefined(value))
    {
        JS_ThrowTypeError(ctx, "zents: undefined is not assignable to byte[] (Bytes)");
        return;
    }
    if (!JS_IsString(value))
    {
        JS_ThrowTypeError(ctx, "zents: [JsMarshalAs(Bytes)] requires a JS string (raw octets)");
        return;
    }

    size_t len = 0;
    const char* cstr = JS_ToCStringLen(ctx, &len, value);
    if (cstr == nullptr)
    {
        *reinterpret_cast<Il2CppArray**>(address) = il2cpp::vm::Array::New(il2cpp_defaults.byte_class, 0);
        return;
    }

    Il2CppArray* arr = il2cpp::vm::Array::New(il2cpp_defaults.byte_class, (il2cpp_array_size_t)len);
    char* dst = il2cpp::vm::Array::GetFirstElementAddress(arr);
    std::memcpy(dst, cstr, len);
    JS_FreeCString(ctx, cstr);
    *reinterpret_cast<Il2CppArray**>(address) = arr;
}

JSValue BytesMarshal::Cs2JsBytes(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    Il2CppArray* arr = *reinterpret_cast<Il2CppArray**>(address);
    if (arr == nullptr)
        return JS_NULL;
    uint32_t len = il2cpp::vm::Array::GetLength(arr);
    char* src = il2cpp::vm::Array::GetFirstElementAddress(arr);
    return JS_NewStringLen(ctx, src, len);
}

void BytesMarshal::Js2CsBytesTypeMismatch(JSContext* ctx, JSValueConst /*value*/, void* /*address*/, const MarshalMetaInfo* /*meta*/)
{
    JS_ThrowTypeError(ctx, "zents: [JsMarshalAs(Bytes)] pop requires byte[].");
}

JSValue BytesMarshal::Cs2JsBytesTypeMismatch(JSContext* ctx, void* /*address*/, const MarshalMetaInfo* /*meta*/)
{
    return JS_ThrowTypeError(ctx, "zents: [JsMarshalAs(Bytes)] push requires byte[].");
}
}
