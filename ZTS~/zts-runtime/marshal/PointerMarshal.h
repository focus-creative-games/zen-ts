#pragma once

#include "MarshalDefs.h"

namespace zts
{
class PointerMarshal
{
public:
    static void Reset();

    static const MarshalMetaInfo* Create(const Il2CppType* ptrType);

    static void Js2CsPointer(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsPointer(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static JSValue Push(JSContext* ctx, void* address, const Il2CppType* ptrType);
    static void* Pop(JSContext* ctx, JSValueConst value, const Il2CppType* expectedPtrType);
};
}
