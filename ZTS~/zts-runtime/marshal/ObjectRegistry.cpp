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

#include "ObjectRegistry.h"
#include "../mt/MetaBinding.h"
#include "../utils/JsException.h"

#include "gc/GarbageCollector.h"
#include "utils/Memory.h"

#include <cstring>
#include <cstdint>
#include <vector>

namespace zts
{
namespace
{
static JSClassID s_objectClassId = 0;
static JSRuntime* s_registeredRuntime = nullptr;

static Il2CppObject** s_objects = nullptr;
static TypeBinding** s_bindings = nullptr;
static int32_t s_capacity = 0;
static int32_t s_next = 1; /* 1-based slots so JS __zts_id is never falsy 0 */
static std::vector<uint32_t> s_free;

static void EnsureCapacity(int32_t minCap)
{
    if (minCap <= s_capacity)
        return;
    int32_t newCap = s_capacity == 0 ? 256 : s_capacity;
    while (newCap < minCap)
        newCap *= 2;

    auto* neuObj = (Il2CppObject**)il2cpp::utils::Memory::Malloc(sizeof(Il2CppObject*) * (size_t)newCap);
    auto* neuBind = (TypeBinding**)il2cpp::utils::Memory::Malloc(sizeof(TypeBinding*) * (size_t)newCap);
    std::memset(neuObj, 0, sizeof(Il2CppObject*) * (size_t)newCap);
    std::memset(neuBind, 0, sizeof(TypeBinding*) * (size_t)newCap);

    if (s_objects != nullptr)
    {
        std::memcpy(neuObj, s_objects, sizeof(Il2CppObject*) * (size_t)s_capacity);
        std::memcpy(neuBind, s_bindings, sizeof(TypeBinding*) * (size_t)s_capacity);
        il2cpp::gc::GarbageCollector::UnregisterRoot((char*)s_objects);
        il2cpp::utils::Memory::Free(s_objects);
        il2cpp::utils::Memory::Free(s_bindings);
    }

    s_objects = neuObj;
    s_bindings = neuBind;
    s_capacity = newCap;
    il2cpp::gc::GarbageCollector::RegisterRoot((char*)s_objects, sizeof(Il2CppObject*) * (size_t)s_capacity);
}

static uint32_t AllocSlot(Il2CppObject* obj, TypeBinding* binding)
{
    uint32_t idx;
    if (!s_free.empty())
    {
        idx = s_free.back();
        s_free.pop_back();
    }
    else
    {
        EnsureCapacity(s_next + 1);
        idx = (uint32_t)s_next++;
    }
    s_objects[idx] = obj;
    s_bindings[idx] = binding;
    return idx;
}

static void FreeSlot(uint32_t idx)
{
    if (s_objects == nullptr || idx >= (uint32_t)s_next)
        return;
    s_objects[idx] = nullptr;
    s_bindings[idx] = nullptr;
    s_free.push_back(idx);
}

static void ObjectFinalizer(JSRuntime* /*rt*/, JSValue val)
{
    void* opaque = JS_GetOpaque(val, s_objectClassId);
    if (opaque == nullptr)
        return;
    FreeSlot((uint32_t)(uintptr_t)opaque);
}

/* Works for raw ZtsObject and Proxy-wrapped instances (Mono-aligned __zts_id). */
static bool TryGetSlot(JSContext* ctx, JSValueConst value, uint32_t* outSlot)
{
    if (ctx == nullptr || outSlot == nullptr || !JS_IsObject(value))
        return false;

    if (s_registeredRuntime != nullptr && JS_GetClassID(value) == s_objectClassId)
    {
        void* opaque = JS_GetOpaque(value, s_objectClassId);
        if (opaque != nullptr)
        {
            *outSlot = (uint32_t)(uintptr_t)opaque;
            return true;
        }
    }

    JSValue idVal = JS_GetPropertyStr(ctx, value, "__zts_id");
    if (JS_IsException(idVal))
        return false;
    int32_t id = 0;
    bool ok = JS_IsNumber(idVal) && JS_ToInt32(ctx, &id, idVal) == 0 && id >= 0;
    JS_FreeValue(ctx, idVal);
    if (!ok)
        return false;
    *outSlot = (uint32_t)id;
    return true;
}
} // namespace

void ObjectRegistry::Initialize(JSRuntime* rt)
{
    if (rt == nullptr)
        return;
    /* JS_NewClass is per-runtime; reuse ClassID across domains. */
    if (s_registeredRuntime == rt)
        return;
    JS_NewClassID(&s_objectClassId);
    JSClassDef def = {};
    def.class_name = "ZtsObject";
    def.finalizer = ObjectFinalizer;
    JS_NewClass(rt, s_objectClassId, &def);
    s_registeredRuntime = rt;
}

void ObjectRegistry::Reset()
{
    if (s_objects != nullptr)
    {
        il2cpp::gc::GarbageCollector::UnregisterRoot((char*)s_objects);
        il2cpp::utils::Memory::Free(s_objects);
        il2cpp::utils::Memory::Free(s_bindings);
        s_objects = nullptr;
        s_bindings = nullptr;
    }
    s_capacity = 0;
    s_next = 1;
    s_free.clear();
    s_registeredRuntime = nullptr;
}

namespace
{
JSValue ResolveByObjProto(TypeBinding* binding)
{
    if (binding == nullptr)
        return JS_UNDEFINED;
    if (!JS_IsUndefined(binding->byobjInstanceProto) && JS_IsObject(binding->byobjInstanceProto))
        return binding->byobjInstanceProto;
    return binding->instanceProto;
}

JSValue ResolveByValProto(TypeBinding* binding)
{
    if (binding == nullptr)
        return JS_UNDEFINED;
    if (!JS_IsUndefined(binding->byvalInstanceProto) && JS_IsObject(binding->byvalInstanceProto))
        return binding->byvalInstanceProto;
    return ResolveByObjProto(binding);
}

JSValue PushWithProto(
    JSContext* ctx, Il2CppObject* obj, TypeBinding* binding, JSValueConst proto, const char* udKind)
{
    if (obj == nullptr || binding == nullptr)
        JsException::Throw("zts: ObjectRegistry::Push null");

    ObjectRegistry::Initialize(JS_GetRuntime(ctx));
    uint32_t slot = AllocSlot(obj, binding);
    JSValue js = JS_NewObjectClass(ctx, s_objectClassId);
    JS_SetOpaque(js, (void*)(uintptr_t)slot);
    if (!JS_IsUndefined(proto) && JS_IsObject(proto))
    {
        if (JS_SetPrototype(ctx, js, proto) < 0)
            JsException::Throw("zts: failed to set instance prototype");
    }
    /* Members live on instanceProto — do not AttachInstanceMembers per Push. */
    JS_SetPropertyStr(ctx, js, "__zts_id", JS_NewInt32(ctx, (int32_t)slot));
    if (udKind != nullptr)
        JS_SetPropertyStr(ctx, js, "__zts_ud_kind", JS_NewString(ctx, udKind));
    return js;
}
} // namespace

JSValue ObjectRegistry::Push(JSContext* ctx, Il2CppObject* obj, TypeBinding* binding)
{
    if (obj == nullptr)
        return JS_NULL;

    JSValue js = PushUnwrapped(ctx, obj, binding);
    if (JS_IsException(js))
        return js;
    return MetaBinding::WrapStrictMiss(ctx, js);
}

JSValue ObjectRegistry::PushUnwrapped(JSContext* ctx, Il2CppObject* obj, TypeBinding* binding)
{
    return PushWithProto(ctx, obj, binding, ResolveByObjProto(binding), "byobj");
}

JSValue ObjectRegistry::PushByVal(JSContext* ctx, Il2CppObject* obj, TypeBinding* binding)
{
    JSValue js = PushWithProto(ctx, obj, binding, ResolveByValProto(binding), "byval");
    if (JS_IsException(js))
        return js;
    return MetaBinding::WrapStrictMiss(ctx, js);
}

Il2CppObject* ObjectRegistry::Get(JSContext* ctx, JSValueConst value)
{
    uint32_t slot = 0;
    if (!TryGetSlot(ctx, value, &slot))
        return nullptr;
    if (s_objects == nullptr || slot >= (uint32_t)s_next)
        return nullptr;
    return s_objects[slot];
}

TypeBinding* ObjectRegistry::GetBinding(JSContext* ctx, JSValueConst value)
{
    uint32_t slot = 0;
    if (!TryGetSlot(ctx, value, &slot))
        return nullptr;
    if (s_bindings == nullptr || slot >= (uint32_t)s_next)
        return nullptr;
    return s_bindings[slot];
}

bool ObjectRegistry::IsZtsObject(JSContext* ctx, JSValueConst value)
{
    uint32_t slot = 0;
    return TryGetSlot(ctx, value, &slot);
}

bool ObjectRegistry::IsByVal(JSContext* ctx, JSValueConst value)
{
    if (!IsZtsObject(ctx, value))
        return false;
    JSValue kind = JS_GetPropertyStr(ctx, value, "__zts_ud_kind");
    if (JS_IsException(kind))
        return false;
    bool ok = false;
    if (JS_IsString(kind))
    {
        const char* s = JS_ToCString(ctx, kind);
        ok = s != nullptr && std::strcmp(s, "byval") == 0;
        if (s != nullptr)
            JS_FreeCString(ctx, s);
    }
    JS_FreeValue(ctx, kind);
    return ok;
}
}
