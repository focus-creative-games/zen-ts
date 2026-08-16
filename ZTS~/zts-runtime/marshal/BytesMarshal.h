#pragma once

#include "MarshalDefs.h"

namespace zts
{
class BytesMarshal
{
public:
    static void Js2CsBytes(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsBytes(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    /** Mono parity: [TsMarshalAs(Bytes)] on a non-byte[] CLR type rejects at marshal time. */
    static void Js2CsBytesTypeMismatch(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsBytesTypeMismatch(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
};
}
