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

namespace zents
{
namespace
{
static js::ClassID s_objectClassId = 0;
static js::Runtime* s_registeredRuntime = nullptr;

static Il2CppObject** s_objects = nullptr;
static TypeBinding** s_bindings = nullptr;
static int32_t s_capacity = 0;
static int32_t s_next = 1; /* 1-based slots so JS __zents_id is never falsy 0 */
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

static void ObjectFinalizer(js::Runtime* /*rt*/, js::Value val)
{
    void* opaque = js::GetOpaque(val, s_objectClassId);
    if (opaque == nullptr)
        return;
    FreeSlot((uint32_t)(uintptr_t)opaque);
}

/* Works for raw ZentsObject and Proxy-wrapped instances (Mono-aligned __zents_id). */
static bool TryGetSlot(js::Context* ctx, js::ValueConst value, uint32_t* outSlot)
{
    if (ctx == nullptr || outSlot == nullptr || !js::IsObject(value))
        return false;

    if (s_registeredRuntime != nullptr && js::GetClassID(value) == s_objectClassId)
    {
        void* opaque = js::GetOpaque(value, s_objectClassId);
        if (opaque != nullptr)
        {
            *outSlot = (uint32_t)(uintptr_t)opaque;
            return true;
        }
    }

    js::Value idVal = js::GetPropertyStr(ctx, value, "__zents_id");
    if (js::IsException(idVal))
        return false;
    int32_t id = 0;
    bool ok = js::IsNumber(idVal) && js::ToInt32(ctx, &id, idVal) == 0 && id >= 0;
    js::FreeValue(ctx, idVal);
    if (!ok)
        return false;
    *outSlot = (uint32_t)id;
    return true;
}
} // namespace

void ObjectRegistry::Initialize(js::Runtime* rt)
{
    if (rt == nullptr)
        return;
    /* js::NewClass is per-runtime; reuse ClassID across domains. */
    if (s_registeredRuntime == rt)
        return;
    js::NewClassID(&s_objectClassId);
    js::ClassDef def = {};
    def.class_name = "ZentsObject";
    def.finalizer = ObjectFinalizer;
    js::NewClass(rt, s_objectClassId, &def);
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
js::Value ResolveByObjProto(TypeBinding* binding)
{
    if (binding == nullptr)
        return js::Undefined();
    if (!js::IsUndefined(binding->byobjInstanceProto) && js::IsObject(binding->byobjInstanceProto))
        return binding->byobjInstanceProto;
    return binding->instanceProto;
}

js::Value ResolveByValProto(TypeBinding* binding)
{
    if (binding == nullptr)
        return js::Undefined();
    if (!js::IsUndefined(binding->byvalInstanceProto) && js::IsObject(binding->byvalInstanceProto))
        return binding->byvalInstanceProto;
    return ResolveByObjProto(binding);
}

js::Value PushWithProto(
    js::Context* ctx, Il2CppObject* obj, TypeBinding* binding, js::ValueConst proto, const char* udKind)
{
    if (obj == nullptr || binding == nullptr)
        JsException::Throw("zents: ObjectRegistry::Push null");

    ObjectRegistry::Initialize(js::GetRuntime(ctx));
    uint32_t slot = AllocSlot(obj, binding);
    js::Value js = js::NewObjectClass(ctx, s_objectClassId);
    js::SetOpaque(js, (void*)(uintptr_t)slot);
    if (!js::IsUndefined(proto) && js::IsObject(proto))
    {
        if (js::SetPrototype(ctx, js, proto) < 0)
            JsException::Throw("zents: failed to set instance prototype");
    }
    /* Members live on instanceProto — do not AttachInstanceMembers per Push. */
    js::SetPropertyStr(ctx, js, "__zents_id", js::NewInt32(ctx, (int32_t)slot));
    if (udKind != nullptr)
        js::SetPropertyStr(ctx, js, "__zents_ud_kind", js::NewString(ctx, udKind));
    return js;
}
} // namespace

js::Value ObjectRegistry::Push(js::Context* ctx, Il2CppObject* obj, TypeBinding* binding)
{
    if (obj == nullptr)
        return js::Null();

    js::Value js = PushUnwrapped(ctx, obj, binding);
    if (js::IsException(js))
        return js;
    return MetaBinding::WrapStrictMiss(ctx, js);
}

js::Value ObjectRegistry::PushUnwrapped(js::Context* ctx, Il2CppObject* obj, TypeBinding* binding)
{
    return PushWithProto(ctx, obj, binding, ResolveByObjProto(binding), "byobj");
}

js::Value ObjectRegistry::PushByVal(js::Context* ctx, Il2CppObject* obj, TypeBinding* binding)
{
    js::Value js = PushWithProto(ctx, obj, binding, ResolveByValProto(binding), "byval");
    if (js::IsException(js))
        return js;
    return MetaBinding::WrapStrictMiss(ctx, js);
}

Il2CppObject* ObjectRegistry::Get(js::Context* ctx, js::ValueConst value)
{
    uint32_t slot = 0;
    if (!TryGetSlot(ctx, value, &slot))
        return nullptr;
    if (s_objects == nullptr || slot >= (uint32_t)s_next)
        return nullptr;
    return s_objects[slot];
}

TypeBinding* ObjectRegistry::GetBinding(js::Context* ctx, js::ValueConst value)
{
    uint32_t slot = 0;
    if (!TryGetSlot(ctx, value, &slot))
        return nullptr;
    if (s_bindings == nullptr || slot >= (uint32_t)s_next)
        return nullptr;
    return s_bindings[slot];
}

bool ObjectRegistry::IsZentsObject(js::Context* ctx, js::ValueConst value)
{
    uint32_t slot = 0;
    return TryGetSlot(ctx, value, &slot);
}

bool ObjectRegistry::IsByVal(js::Context* ctx, js::ValueConst value)
{
    if (!IsZentsObject(ctx, value))
        return false;
    js::Value kind = js::GetPropertyStr(ctx, value, "__zents_ud_kind");
    if (js::IsException(kind))
        return false;
    bool ok = false;
    if (js::IsString(kind))
    {
        const char* s = js::ToCString(ctx, kind);
        ok = s != nullptr && std::strcmp(s, "byval") == 0;
        if (s != nullptr)
            js::FreeCString(ctx, s);
    }
    js::FreeValue(ctx, kind);
    return ok;
}
}
