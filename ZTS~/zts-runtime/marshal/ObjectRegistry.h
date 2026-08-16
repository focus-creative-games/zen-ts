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
struct TypeBinding;

/// Minimal object registry: GC root array of Il2CppObject* + JS class opaque slot.
/// ByObj (classes / boxed) and ByVal (struct copy carrier) share the slot table;
/// morph is distinguished by proto + `__zts_ud_kind`.
class ObjectRegistry
{
public:
    static void Initialize(JSRuntime* rt);
    static void Reset();

    /** Push ByObj exotic (class instance or boxed valuetype). */
    static JSValue Push(JSContext* ctx, Il2CppObject* obj, TypeBinding* binding);
    /** Like Push but skips miss-Proxy wrap (needed for System.Type + host-owned tags). */
    static JSValue PushUnwrapped(JSContext* ctx, Il2CppObject* obj, TypeBinding* binding);
    /** Push ByVal exotic for a boxed struct copy (payload = Unbox(obj)). */
    static JSValue PushByVal(JSContext* ctx, Il2CppObject* obj, TypeBinding* binding);
    static Il2CppObject* Get(JSContext* ctx, JSValueConst value);
    static TypeBinding* GetBinding(JSContext* ctx, JSValueConst value);
    static bool IsZtsObject(JSContext* ctx, JSValueConst value);
    /** True when value is a ByVal exotic (`__zts_ud_kind` == "byval"). */
    static bool IsByVal(JSContext* ctx, JSValueConst value);
};
}
