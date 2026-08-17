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

#include "PrimitiveMarshal.h"

#include "vm/String.h"
#include "utils/StringUtils.h"

#include <cmath>
#include <cstdint>
#include <limits>
#include <string>

namespace zents
{
namespace
{
static bool RejectBigInt(JSContext* ctx, JSValueConst value)
{
    if (JS_IsBigInt(ctx, value))
    {
        JS_ThrowTypeError(ctx, "zents: BigInt marshal is not supported");
        return true;
    }
    return false;
}

static bool ToNumber(JSContext* ctx, JSValueConst value, double* out)
{
    if (RejectBigInt(ctx, value))
        return false;
    if (!JS_IsNumber(value))
    {
        JS_ThrowTypeError(ctx, "zents: cannot convert value to number (bad format)");
        return false;
    }
    if (JS_ToFloat64(ctx, out, value))
        return false;
    return true;
}

template <typename T>
static bool StoreIntegral(JSContext* ctx, JSValueConst value, void* address, T minV, T maxV)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return false;
    if (!std::isfinite(d) || std::floor(d) != d)
    {
        JS_ThrowTypeError(ctx, "zents: cannot convert value to integer number");
        return false;
    }
    if (d < (double)minV || d > (double)maxV)
    {
        JS_ThrowRangeError(ctx, "zents: integer out of range");
        return false;
    }
    *reinterpret_cast<T*>(address) = (T)d;
    return true;
}
} // namespace

void PrimitiveMarshal::Js2CsBool(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    if (JS_IsBool(value))
    {
        *reinterpret_cast<bool*>(address) = JS_ToBool(ctx, value) != 0;
        return;
    }
    if (JS_IsNumber(value))
    {
        double d = 0;
        if (JS_ToFloat64(ctx, &d, value))
            return;
        *reinterpret_cast<bool*>(address) = d != 0.0;
        return;
    }
    JS_ThrowTypeError(ctx, "zents: expected boolean");
}

JSValue PrimitiveMarshal::Cs2JsBool(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewBool(ctx, *reinterpret_cast<bool*>(address));
}

void PrimitiveMarshal::Js2CsInt8(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<int8_t>(ctx, value, address, INT8_MIN, INT8_MAX);
}

JSValue PrimitiveMarshal::Cs2JsInt8(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewInt32(ctx, *reinterpret_cast<int8_t*>(address));
}

void PrimitiveMarshal::Js2CsUInt8(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<uint8_t>(ctx, value, address, 0, UINT8_MAX);
}

JSValue PrimitiveMarshal::Cs2JsUInt8(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewInt32(ctx, *reinterpret_cast<uint8_t*>(address));
}

void PrimitiveMarshal::Js2CsInt16(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<int16_t>(ctx, value, address, INT16_MIN, INT16_MAX);
}

JSValue PrimitiveMarshal::Cs2JsInt16(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewInt32(ctx, *reinterpret_cast<int16_t*>(address));
}

void PrimitiveMarshal::Js2CsUInt16(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<uint16_t>(ctx, value, address, 0, UINT16_MAX);
}

JSValue PrimitiveMarshal::Cs2JsUInt16(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewInt32(ctx, *reinterpret_cast<uint16_t*>(address));
}

void PrimitiveMarshal::Js2CsInt32(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<int32_t>(ctx, value, address, INT32_MIN, INT32_MAX);
}

JSValue PrimitiveMarshal::Cs2JsInt32(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewInt32(ctx, *reinterpret_cast<int32_t*>(address));
}

void PrimitiveMarshal::Js2CsUInt32(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<uint32_t>(ctx, value, address, 0u, UINT32_MAX);
}

JSValue PrimitiveMarshal::Cs2JsUInt32(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewUint32(ctx, *reinterpret_cast<uint32_t*>(address));
}

void PrimitiveMarshal::Js2CsInt64(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    if (!std::isfinite(d) || std::floor(d) != d)
    {
        JS_ThrowTypeError(ctx, "zents: expected integer number");
        return;
    }
    *reinterpret_cast<int64_t*>(address) = (int64_t)d;
}

JSValue PrimitiveMarshal::Cs2JsInt64(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    int64_t v = *reinterpret_cast<int64_t*>(address);
    if (v >= INT32_MIN && v <= INT32_MAX)
        return JS_NewInt32(ctx, (int32_t)v);
    return JS_NewFloat64(ctx, (double)v);
}

void PrimitiveMarshal::Js2CsUInt64(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    if (!std::isfinite(d) || std::floor(d) != d || d < 0)
    {
        JS_ThrowTypeError(ctx, "zents: expected non-negative integer number");
        return;
    }
    *reinterpret_cast<uint64_t*>(address) = (uint64_t)d;
}

JSValue PrimitiveMarshal::Cs2JsUInt64(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    uint64_t v = *reinterpret_cast<uint64_t*>(address);
    if (v <= (uint64_t)INT32_MAX)
        return JS_NewInt32(ctx, (int32_t)v);
    return JS_NewFloat64(ctx, (double)v);
}

void PrimitiveMarshal::Js2CsFloat(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    *reinterpret_cast<float*>(address) = (float)d;
}

JSValue PrimitiveMarshal::Cs2JsFloat(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewFloat64(ctx, *reinterpret_cast<float*>(address));
}

void PrimitiveMarshal::Js2CsDouble(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    *reinterpret_cast<double*>(address) = d;
}

JSValue PrimitiveMarshal::Cs2JsDouble(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewFloat64(ctx, *reinterpret_cast<double*>(address));
}

void PrimitiveMarshal::Js2CsChar(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<uint16_t>(ctx, value, address, 0, UINT16_MAX);
}

JSValue PrimitiveMarshal::Cs2JsChar(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return JS_NewInt32(ctx, *reinterpret_cast<Il2CppChar*>(address));
}

void PrimitiveMarshal::Js2CsIntPtr(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    if (!std::isfinite(d) || std::floor(d) != d)
    {
        JS_ThrowTypeError(ctx, "zents: expected integer number");
        return;
    }
    *reinterpret_cast<intptr_t*>(address) = (intptr_t)(int64_t)d;
}

JSValue PrimitiveMarshal::Cs2JsIntPtr(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    intptr_t v = *reinterpret_cast<intptr_t*>(address);
    if (v >= INT32_MIN && v <= INT32_MAX)
        return JS_NewInt32(ctx, (int32_t)v);
    return JS_NewFloat64(ctx, (double)v);
}

void PrimitiveMarshal::Js2CsUIntPtr(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    if (!std::isfinite(d) || std::floor(d) != d || d < 0)
    {
        JS_ThrowTypeError(ctx, "zents: expected non-negative integer number");
        return;
    }
    *reinterpret_cast<uintptr_t*>(address) = (uintptr_t)(uint64_t)d;
}

JSValue PrimitiveMarshal::Cs2JsUIntPtr(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    uintptr_t v = *reinterpret_cast<uintptr_t*>(address);
    if (v <= (uintptr_t)INT32_MAX)
        return JS_NewInt32(ctx, (int32_t)v);
    return JS_NewFloat64(ctx, (double)v);
}

void PrimitiveMarshal::Js2CsString(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    if (JS_IsNull(value) || JS_IsUndefined(value))
    {
        *reinterpret_cast<Il2CppString**>(address) = nullptr;
        return;
    }

    const char* cstr = JS_ToCString(ctx, value);
    if (cstr == nullptr)
        return;
    *reinterpret_cast<Il2CppString**>(address) = il2cpp::vm::String::New(cstr);
    JS_FreeCString(ctx, cstr);
}

JSValue PrimitiveMarshal::Cs2JsString(JSContext* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    Il2CppString* str = *reinterpret_cast<Il2CppString**>(address);
    if (str == nullptr)
        return JS_NULL;
    /* Reuse TLS buffer to avoid per-call std::string heap churn on hot paths. */
    static thread_local std::string s_utf8Scratch;
    s_utf8Scratch = il2cpp::utils::StringUtils::Utf16ToUtf8(
        il2cpp::utils::StringUtils::GetChars(str),
        il2cpp::utils::StringUtils::GetLength(str));
    return JS_NewStringLen(ctx, s_utf8Scratch.c_str(), s_utf8Scratch.size());
}

void PrimitiveMarshal::Js2CsVoid(JSContext* /*ctx*/, JSValueConst /*value*/, void* /*address*/, const MarshalMetaInfo* /*meta*/)
{
}

JSValue PrimitiveMarshal::Cs2JsVoid(JSContext* /*ctx*/, void* /*address*/, const MarshalMetaInfo* /*meta*/)
{
    return JS_UNDEFINED;
}
}
