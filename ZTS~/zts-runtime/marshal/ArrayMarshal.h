#pragma once

#include "MarshalDefs.h"

namespace zts
{
class ArrayMarshal
{
public:
    static void Js2CsSzArray(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsSzArray(JSContext* ctx, void* address, const MarshalMetaInfo* meta);
};
}
