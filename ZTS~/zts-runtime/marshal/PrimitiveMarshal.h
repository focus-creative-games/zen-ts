#pragma once

#include "MarshalDefs.h"

namespace zts
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
