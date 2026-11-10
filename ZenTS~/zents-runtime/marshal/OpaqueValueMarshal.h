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

#include "../ZenTSCommon.h"

namespace zents
{
struct MarshalMetaInfo;
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
    static js::Value Push(js::Context* ctx, void* valueAddress, const Il2CppType* type, const MarshalMetaInfo* meta = nullptr);

    /// Read handle → default CS→JS of the slot (deref byref first).
    static js::Value GetValue(js::Context* ctx, js::ValueConst handle);

    /// Write JS value into the slot (deref byref first).
    static void SetValue(js::Context* ctx, js::ValueConst handle, js::ValueConst value);

    /// Copy struct slot into a ByVal exotic (to_user_data).
    static js::Value ToUserData(js::Context* ctx, js::ValueConst handle);

    static bool IsOpaqueHandle(js::Context* ctx, js::ValueConst value);
    /** If handle is a live OpaqueValue, returns the slot address (for byref bind). */
    static bool TryGetValueAddress(js::Context* ctx, js::ValueConst handle, void** outAddr, const Il2CppType* expectedType = nullptr);
};
}
