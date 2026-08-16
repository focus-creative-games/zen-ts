#pragma once

#include "../ZTSCommon.h"

namespace zts
{
class OpaqueParameterScope
{
public:
    OpaqueParameterScope();
    ~OpaqueParameterScope();

    static void Reset();

private:
    size_t _oldStackSize;
};

class OpaqueValueMarshal
{
public:
    /// Push an opaque handle object (C#→JS byref / OpaqueValue).
    static JSValue Push(JSContext* ctx, void* valueAddress, const Il2CppType* type);

    /// Read handle → default CS→JS of the slot (deref byref first).
    static JSValue GetValue(JSContext* ctx, JSValueConst handle);

    /// Write JS value into the slot (deref byref first).
    static void SetValue(JSContext* ctx, JSValueConst handle, JSValueConst value);

    /// Copy struct slot into a ByVal exotic (to_user_data).
    static JSValue ToUserData(JSContext* ctx, JSValueConst handle);

    static bool IsOpaqueHandle(JSContext* ctx, JSValueConst value);
    /** If handle is a live OpaqueValue, returns the slot address (for byref bind). */
    static bool TryGetValueAddress(JSContext* ctx, JSValueConst handle, void** outAddr);
};
}
