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

#include "PointerMarshal.h"

#include "utils/Memory.h"

#include <cstring>
#include <vector>

namespace zents
{
namespace
{
struct PointerSlot
{
    void* address = nullptr;
    const Il2CppType* pointerType = nullptr;
};

static PointerSlot* s_slots = nullptr;
static int32_t s_capacity = 0;
static int32_t s_next = 0;
static std::vector<uint32_t> s_free;

static void EnsureCapacity(int32_t minCap)
{
    if (minCap <= s_capacity)
        return;
    int32_t newCap = s_capacity == 0 ? 64 : s_capacity;
    while (newCap < minCap)
        newCap *= 2;
    auto* neu = (PointerSlot*)il2cpp::utils::Memory::Malloc(sizeof(PointerSlot) * (size_t)newCap);
    std::memset(neu, 0, sizeof(PointerSlot) * (size_t)newCap);
    if (s_slots != nullptr)
    {
        std::memcpy(neu, s_slots, sizeof(PointerSlot) * (size_t)s_capacity);
        il2cpp::utils::Memory::Free(s_slots);
    }
    s_slots = neu;
    s_capacity = newCap;
}

static uint32_t AllocSlot(void* address, const Il2CppType* pointerType)
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
    s_slots[idx].address = address;
    s_slots[idx].pointerType = pointerType;
    return idx;
}

static bool IsVoidPtr(const Il2CppType* t)
{
    return t != nullptr && t->type == IL2CPP_TYPE_PTR
        && t->data.type != nullptr && t->data.type->type == IL2CPP_TYPE_VOID;
}

static bool PointerTypesMatch(const Il2CppType* actual, const Il2CppType* expected)
{
    if (actual == expected)
        return true;
    if (actual == nullptr || expected == nullptr)
        return false;
    if (IsVoidPtr(expected) || IsVoidPtr(actual))
        return true;
    if (actual->type != IL2CPP_TYPE_PTR || expected->type != IL2CPP_TYPE_PTR)
        return false;
    const Il2CppType* a = actual->data.type;
    const Il2CppType* e = expected->data.type;
    return a != nullptr && e != nullptr && a->type == e->type;
}

static bool HasPointerFlag(JSContext* ctx, JSValueConst value)
{
    JSValue flag = JS_GetPropertyStr(ctx, value, "__zents_pointer");
    bool ok = JS_IsBool(flag) && JS_ToBool(ctx, flag);
    JS_FreeValue(ctx, flag);
    return ok;
}
} // namespace

void PointerMarshal::Reset()
{
    if (s_slots != nullptr)
    {
        il2cpp::utils::Memory::Free(s_slots);
        s_slots = nullptr;
    }
    s_capacity = 0;
    s_next = 0;
    s_free.clear();
}

const MarshalMetaInfo* PointerMarshal::Create(const Il2CppType* ptrType)
{
    auto* meta = new MarshalMetaInfo();
    meta->js2csWriter = Js2CsPointer;
    meta->cs2jsWriter = Cs2JsPointer;
    meta->type = ptrType;
    meta->typeKlass = nullptr;
    meta->size = sizeof(void*);
    meta->passByValue = true;
    meta->jsArgSlots = 1;
    meta->marshalAsKind = MarshalAsKind::None;
    return meta;
}

JSValue PointerMarshal::Push(JSContext* ctx, void* address, const Il2CppType* ptrType)
{
    if (address == nullptr)
        return JS_NULL;

    uint32_t slot = AllocSlot(address, ptrType);
    JSValue obj = JS_NewObject(ctx);
    JS_SetPropertyStr(ctx, obj, "__zents_id", JS_NewInt32(ctx, (int32_t)slot));
    JS_SetPropertyStr(ctx, obj, "__zents_pointer", JS_NewBool(ctx, 1));
    return obj;
}

void* PointerMarshal::Pop(JSContext* ctx, JSValueConst value, const Il2CppType* expectedPtrType)
{
    if (JS_IsNull(value))
        return nullptr;

    if (JS_IsUndefined(value))
    {
        JS_ThrowTypeError(ctx, "zents: undefined is not assignable to Pointer (use null for null pointer)");
        return nullptr;
    }

    if (!JS_IsObject(value) || !HasPointerFlag(ctx, value))
    {
        JS_ThrowTypeError(ctx, "zents: expected Pointer handle");
        return nullptr;
    }

    JSValue idVal = JS_GetPropertyStr(ctx, value, "__zents_id");
    int32_t id = -1;
    bool ok = JS_IsNumber(idVal) && JS_ToInt32(ctx, &id, idVal) == 0;
    JS_FreeValue(ctx, idVal);
    if (!ok || id < 0 || s_slots == nullptr || id >= s_next || s_slots[id].pointerType == nullptr)
    {
        JS_ThrowTypeError(ctx, "zents: expected Pointer handle");
        return nullptr;
    }

    if (!PointerTypesMatch(s_slots[id].pointerType, expectedPtrType))
    {
        JS_ThrowTypeError(ctx, "zents: Pointer type mismatch");
        return nullptr;
    }

    return s_slots[id].address;
}

void PointerMarshal::Js2CsPointer(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta)
{
    void* ptr = Pop(ctx, value, meta->type);
    if (JS_HasException(ctx))
        return;
    *reinterpret_cast<void**>(address) = ptr;
}

JSValue PointerMarshal::Cs2JsPointer(JSContext* ctx, void* address, const MarshalMetaInfo* meta)
{
    void* ptr = *reinterpret_cast<void**>(address);
    return Push(ctx, ptr, meta->type);
}
}
