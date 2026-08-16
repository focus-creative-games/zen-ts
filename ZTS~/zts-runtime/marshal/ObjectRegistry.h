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
