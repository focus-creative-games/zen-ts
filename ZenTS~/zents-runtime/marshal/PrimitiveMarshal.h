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
    static void Js2CsBool(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsBool(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsInt8(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsInt8(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUInt8(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsUInt8(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsInt16(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsInt16(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUInt16(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsUInt16(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsInt32(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsInt32(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUInt32(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsUInt32(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsInt64(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsInt64(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUInt64(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsUInt64(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsFloat(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsFloat(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsDouble(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsDouble(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsChar(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsChar(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsIntPtr(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsIntPtr(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
    static void Js2CsUIntPtr(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsUIntPtr(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsString(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsString(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsVoid(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsVoid(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
};
}
