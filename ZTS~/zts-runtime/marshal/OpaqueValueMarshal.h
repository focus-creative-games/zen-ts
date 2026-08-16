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
