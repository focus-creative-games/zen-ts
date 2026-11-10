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

#pragma once

#include "MarshalDefs.h"

namespace zents
{
class PrimitiveMarshal
{
public:
    static void Js2CsBool(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsBool(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsInt8(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsInt8(js::Context* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUInt8(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsUInt8(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsInt16(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsInt16(js::Context* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUInt16(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsUInt16(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsInt32(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsInt32(js::Context* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUInt32(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsUInt32(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsInt64(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsInt64(js::Context* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUInt64(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsUInt64(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsFloat(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsFloat(js::Context* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsDouble(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsDouble(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsChar(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsChar(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsIntPtr(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsIntPtr(js::Context* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUIntPtr(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsUIntPtr(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsString(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsString(js::Context* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsVoid(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta);
    static js::Value Cs2JsVoid(js::Context* ctx, void* address, const MarshalMetaInfo* meta);
};
}
