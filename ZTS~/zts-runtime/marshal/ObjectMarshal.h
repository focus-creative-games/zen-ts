#pragma once

#include "MarshalDefs.h"

namespace zts
{
class ObjectMarshal
{
public:
    static void Js2CsObject(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsObject(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
};
}
