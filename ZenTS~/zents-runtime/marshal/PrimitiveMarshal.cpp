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
static bool RejectBigInt(js::Context* ctx, js::ValueConst value)
{
    if (js::IsBigInt(ctx, value))
    {
        js::ThrowTypeError(ctx, "zents: BigInt marshal is not supported");
        return true;
    }
    return false;
}

static bool ToNumber(js::Context* ctx, js::ValueConst value, double* out)
{
    if (RejectBigInt(ctx, value))
        return false;
    if (!js::IsNumber(value))
    {
        js::ThrowTypeError(ctx, "zents: cannot convert value to number (bad format)");
        return false;
    }
    if (js::ToFloat64(ctx, out, value))
        return false;
    return true;
}

template <typename T>
static bool StoreIntegral(js::Context* ctx, js::ValueConst value, void* address, T minV, T maxV)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return false;
    if (!std::isfinite(d) || std::floor(d) != d)
    {
        js::ThrowTypeError(ctx, "zents: cannot convert value to integer number");
        return false;
    }
    if (d < (double)minV || d > (double)maxV)
    {
        js::ThrowRangeError(ctx, "zents: integer out of range");
        return false;
    }
    *reinterpret_cast<T*>(address) = (T)d;
    return true;
}
} // namespace

void PrimitiveMarshal::Js2CsBool(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    if (js::IsBool(value))
    {
        *reinterpret_cast<bool*>(address) = js::ToBool(ctx, value) != 0;
        return;
    }
    js::ThrowTypeError(ctx, "zents: expected boolean");
}

js::Value PrimitiveMarshal::Cs2JsBool(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewBool(ctx, *reinterpret_cast<bool*>(address));
}

void PrimitiveMarshal::Js2CsInt8(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<int8_t>(ctx, value, address, INT8_MIN, INT8_MAX);
}

js::Value PrimitiveMarshal::Cs2JsInt8(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewInt32(ctx, *reinterpret_cast<int8_t*>(address));
}

void PrimitiveMarshal::Js2CsUInt8(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<uint8_t>(ctx, value, address, 0, UINT8_MAX);
}

js::Value PrimitiveMarshal::Cs2JsUInt8(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewInt32(ctx, *reinterpret_cast<uint8_t*>(address));
}

void PrimitiveMarshal::Js2CsInt16(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<int16_t>(ctx, value, address, INT16_MIN, INT16_MAX);
}

js::Value PrimitiveMarshal::Cs2JsInt16(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewInt32(ctx, *reinterpret_cast<int16_t*>(address));
}

void PrimitiveMarshal::Js2CsUInt16(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<uint16_t>(ctx, value, address, 0, UINT16_MAX);
}

js::Value PrimitiveMarshal::Cs2JsUInt16(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewInt32(ctx, *reinterpret_cast<uint16_t*>(address));
}

void PrimitiveMarshal::Js2CsInt32(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<int32_t>(ctx, value, address, INT32_MIN, INT32_MAX);
}

js::Value PrimitiveMarshal::Cs2JsInt32(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewInt32(ctx, *reinterpret_cast<int32_t*>(address));
}

void PrimitiveMarshal::Js2CsUInt32(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<uint32_t>(ctx, value, address, 0u, UINT32_MAX);
}

js::Value PrimitiveMarshal::Cs2JsUInt32(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewUint32(ctx, *reinterpret_cast<uint32_t*>(address));
}

void PrimitiveMarshal::Js2CsInt64(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    if (!std::isfinite(d) || std::floor(d) != d)
    {
        js::ThrowTypeError(ctx, "zents: expected integer number");
        return;
    }
    if(d < -9223372036854775808.0 || d >= 9223372036854775808.0){js::ThrowRangeError(ctx,"zents: Int64 out of range");return;}
    *reinterpret_cast<int64_t*>(address) = (int64_t)d;
}

js::Value PrimitiveMarshal::Cs2JsInt64(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    int64_t v = *reinterpret_cast<int64_t*>(address);
    if (v >= INT32_MIN && v <= INT32_MAX)
        return js::NewInt32(ctx, (int32_t)v);
    return js::NewFloat64(ctx, (double)v);
}

void PrimitiveMarshal::Js2CsUInt64(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    if (!std::isfinite(d) || std::floor(d) != d || d < 0)
    {
        js::ThrowTypeError(ctx, "zents: expected non-negative integer number");
        return;
    }
    if(d >= 18446744073709551616.0){js::ThrowRangeError(ctx,"zents: UInt64 out of range");return;}
    *reinterpret_cast<uint64_t*>(address) = (uint64_t)d;
}

js::Value PrimitiveMarshal::Cs2JsUInt64(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    uint64_t v = *reinterpret_cast<uint64_t*>(address);
    if (v <= (uint64_t)INT32_MAX)
        return js::NewInt32(ctx, (int32_t)v);
    return js::NewFloat64(ctx, (double)v);
}

void PrimitiveMarshal::Js2CsFloat(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    *reinterpret_cast<float*>(address) = (float)d;
}

js::Value PrimitiveMarshal::Cs2JsFloat(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewFloat64(ctx, *reinterpret_cast<float*>(address));
}

void PrimitiveMarshal::Js2CsDouble(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    *reinterpret_cast<double*>(address) = d;
}

js::Value PrimitiveMarshal::Cs2JsDouble(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewFloat64(ctx, *reinterpret_cast<double*>(address));
}

void PrimitiveMarshal::Js2CsChar(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    StoreIntegral<uint16_t>(ctx, value, address, 0, UINT16_MAX);
}

js::Value PrimitiveMarshal::Cs2JsChar(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    return js::NewInt32(ctx, *reinterpret_cast<Il2CppChar*>(address));
}

void PrimitiveMarshal::Js2CsIntPtr(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    if (!std::isfinite(d) || std::floor(d) != d)
    {
        js::ThrowTypeError(ctx, "zents: expected integer number");
        return;
    }
    *reinterpret_cast<intptr_t*>(address) = (intptr_t)(int64_t)d;
}

js::Value PrimitiveMarshal::Cs2JsIntPtr(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    intptr_t v = *reinterpret_cast<intptr_t*>(address);
    if (v >= INT32_MIN && v <= INT32_MAX)
        return js::NewInt32(ctx, (int32_t)v);
    return js::NewFloat64(ctx, (double)v);
}

void PrimitiveMarshal::Js2CsUIntPtr(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    double d = 0;
    if (!ToNumber(ctx, value, &d))
        return;
    if (!std::isfinite(d) || std::floor(d) != d || d < 0)
    {
        js::ThrowTypeError(ctx, "zents: expected non-negative integer number");
        return;
    }
    *reinterpret_cast<uintptr_t*>(address) = (uintptr_t)(uint64_t)d;
}

js::Value PrimitiveMarshal::Cs2JsUIntPtr(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    uintptr_t v = *reinterpret_cast<uintptr_t*>(address);
    if (v <= (uintptr_t)INT32_MAX)
        return js::NewInt32(ctx, (int32_t)v);
    return js::NewFloat64(ctx, (double)v);
}

void PrimitiveMarshal::Js2CsString(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* /*meta*/)
{
    if (js::IsNull(value) || js::IsUndefined(value))
    {
        *reinterpret_cast<Il2CppString**>(address) = nullptr;
        return;
    }

    size_t length=0;
    const char* cstr = js::ToCStringLen(ctx, &length, value);
    if (cstr == nullptr)
        return;
    *reinterpret_cast<Il2CppString**>(address) = il2cpp::vm::String::NewLen(cstr,(uint32_t)length);
    js::FreeCString(ctx, cstr);
}

js::Value PrimitiveMarshal::Cs2JsString(js::Context* ctx, void* address, const MarshalMetaInfo* /*meta*/)
{
    Il2CppString* str = *reinterpret_cast<Il2CppString**>(address);
    if (str == nullptr)
        return js::Null();
    /* Reuse TLS buffer to avoid per-call std::string heap churn on hot paths. */
    static thread_local std::string s_utf8Scratch;
    // Unity's Utf16ToUtf8(chars, maximumSize) still stops at NUL. Traverse the
    // actual CLR string length and replace isolated surrogates like JS UTF-8 encoders.
    s_utf8Scratch.clear();
    const Il2CppChar* chars=il2cpp::utils::StringUtils::GetChars(str);
    int length=il2cpp::utils::StringUtils::GetLength(str);
    for(int i=0;i<length;++i)
    {
        uint32_t cp=chars[i];
        if(cp>=0xd800&&cp<=0xdbff){if(i+1<length&&chars[i+1]>=0xdc00&&chars[i+1]<=0xdfff){cp=0x10000+((cp-0xd800)<<10)+(chars[++i]-0xdc00);}else cp=0xfffd;}
        else if(cp>=0xdc00&&cp<=0xdfff)cp=0xfffd;
        if(cp<0x80)s_utf8Scratch.push_back((char)cp);
        else if(cp<0x800){s_utf8Scratch.push_back((char)(0xc0|(cp>>6)));s_utf8Scratch.push_back((char)(0x80|(cp&63)));}
        else if(cp<0x10000){s_utf8Scratch.push_back((char)(0xe0|(cp>>12)));s_utf8Scratch.push_back((char)(0x80|((cp>>6)&63)));s_utf8Scratch.push_back((char)(0x80|(cp&63)));}
        else {s_utf8Scratch.push_back((char)(0xf0|(cp>>18)));s_utf8Scratch.push_back((char)(0x80|((cp>>12)&63)));s_utf8Scratch.push_back((char)(0x80|((cp>>6)&63)));s_utf8Scratch.push_back((char)(0x80|(cp&63)));}
    }
    return js::NewStringLen(ctx, s_utf8Scratch.c_str(), s_utf8Scratch.size());
}

void PrimitiveMarshal::Js2CsVoid(js::Context* /*ctx*/, js::ValueConst /*value*/, void* /*address*/, const MarshalMetaInfo* /*meta*/)
{
}

js::Value PrimitiveMarshal::Cs2JsVoid(js::Context* /*ctx*/, void* /*address*/, const MarshalMetaInfo* /*meta*/)
{
    return js::Undefined();
}
}
