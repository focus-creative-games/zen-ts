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
struct TypeBinding;

/// Minimal object registry: GC root array of Il2CppObject* + JS class opaque slot.
/// ByObj (classes / boxed) and ByVal (struct copy carrier) share the slot table;
/// morph is distinguished by proto + `__zents_ud_kind`.
class ObjectRegistry
{
public:
    static void Initialize(js::Runtime* rt);
    static void Reset();

    /** Push ByObj exotic (class instance or boxed valuetype). */
    static js::Value Push(js::Context* ctx, Il2CppObject* obj, TypeBinding* binding);
    /** Like Push but skips miss-Proxy wrap (needed for System.Type + host-owned tags). */
    static js::Value PushUnwrapped(js::Context* ctx, Il2CppObject* obj, TypeBinding* binding);
    /** Push ByVal exotic for a boxed struct copy (payload = Unbox(obj)). */
    static js::Value PushByVal(js::Context* ctx, Il2CppObject* obj, TypeBinding* binding);
    static Il2CppObject* Get(js::Context* ctx, js::ValueConst value);
    static TypeBinding* GetBinding(js::Context* ctx, js::ValueConst value);
    static bool IsZentsObject(js::Context* ctx, js::ValueConst value);
    /** True when value is a ByVal exotic (`__zents_ud_kind` == "byval"). */
    static bool IsByVal(js::Context* ctx, js::ValueConst value);
};
}
