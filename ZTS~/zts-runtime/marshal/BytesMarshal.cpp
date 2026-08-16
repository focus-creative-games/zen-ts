#include "BytesMarshal.h"

#include "vm/Array.h"
#include "vm/Class.h"

#include <cstring>

namespace zts
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
        JS_ThrowTypeError(ctx, "zts: undefined is not assignable to byte[] (Bytes)");
        return;
    }
    if (!JS_IsString(value))
    {
        JS_ThrowTypeError(ctx, "zts: [TsMarshalAs(Bytes)] requires a JS string (raw octets)");
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
    JS_ThrowTypeError(ctx, "zts: [TsMarshalAs(Bytes)] pop requires byte[].");
}

JSValue BytesMarshal::Cs2JsBytesTypeMismatch(JSContext* ctx, void* /*address*/, const MarshalMetaInfo* /*meta*/)
{
    return JS_ThrowTypeError(ctx, "zts: [TsMarshalAs(Bytes)] push requires byte[].");
}
}
