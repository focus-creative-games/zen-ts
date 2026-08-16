#pragma once

#include "MarshalDefs.h"

namespace zts
{
class StructMarshal
{
public:
    static void Js2CsStruct(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsStruct(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static void Js2CsNullable(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsNullable(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
};
}
