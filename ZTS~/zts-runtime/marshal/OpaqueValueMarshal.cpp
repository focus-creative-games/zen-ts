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

#include "OpaqueValueMarshal.h"
#include "MarshalMeta.h"
#include "StructMarshal.h"

#include "../utils/MetadataUtil.h"
#include "../utils/JsException.h"

#include "vm/Class.h"
#include "vm/Object.h"

#include <cstdint>
#include <cstring>
#include <vector>

namespace zts
{
namespace
{
typedef uint64_t OpaqueParameterHandleType;
typedef uint32_t HandleGenerationType;
typedef uint32_t HandleIndexType;
constexpr size_t kHandleGenerationShift = 32;

struct OpaqueParameterData
{
    HandleGenerationType generation;
    const Il2CppType* type;
    void* valueAddress;
};

static std::vector<OpaqueParameterData> s_opaqueParameterDataStack;
static HandleGenerationType s_handleGeneration = 1;

static OpaqueParameterHandleType ComposeHandle(HandleGenerationType generation, HandleIndexType index)
{
    return ((OpaqueParameterHandleType)generation << kHandleGenerationShift) | (OpaqueParameterHandleType)index;
}

static void ExtractHandle(OpaqueParameterHandleType handle, HandleGenerationType& generation, HandleIndexType& index)
{
    generation = (HandleGenerationType)(handle >> kHandleGenerationShift);
    index = (HandleIndexType)handle;
}

static OpaqueParameterHandleType AllocateOpaqueParameterHandle(const Il2CppType* type, void* valueAddress)
{
    HandleIndexType index = (HandleIndexType)s_opaqueParameterDataStack.size();
    s_opaqueParameterDataStack.push_back(OpaqueParameterData{s_handleGeneration, type, valueAddress});
    return ComposeHandle(s_handleGeneration, index);
}

static bool TryGetData(JSContext* ctx, JSValueConst handle, OpaqueParameterData** outData)
{
    if (outData == nullptr || !JS_IsObject(handle))
        return false;

    JSValue flag = JS_GetPropertyStr(ctx, handle, "__zts_opaque");
    bool isOpaque = JS_IsBool(flag) && JS_ToBool(ctx, flag);
    JS_FreeValue(ctx, flag);
    if (!isOpaque)
        return false;

    JSValue hv = JS_GetPropertyStr(ctx, handle, "__zts_handle");
    int64_t raw = 0;
    bool ok = JS_IsNumber(hv) && JS_ToInt64(ctx, &raw, hv) == 0;
    JS_FreeValue(ctx, hv);
    if (!ok)
        return false;

    HandleGenerationType generation = 0;
    HandleIndexType index = 0;
    ExtractHandle((OpaqueParameterHandleType)raw, generation, index);
    if (index >= s_opaqueParameterDataStack.size())
        return false;
    OpaqueParameterData& data = s_opaqueParameterDataStack[index];
    if (data.generation != generation)
        return false;
    *outData = &data;
    return true;
}

static void ResolveSlot(const OpaqueParameterData& data, const Il2CppType** outType, void** outAddr)
{
    if (data.type->byref)
    {
        static thread_local Il2CppType s_deref;
        s_deref = *data.type;
        s_deref.byref = false;
        *outType = &s_deref;
        *outAddr = *reinterpret_cast<void**>(data.valueAddress);
    }
    else
    {
        *outType = data.type;
        *outAddr = data.valueAddress;
    }
}
} // namespace

OpaqueParameterScope::OpaqueParameterScope()
{
    s_handleGeneration++;
    if (s_handleGeneration == 0)
        s_handleGeneration = 1;
    _oldStackSize = s_opaqueParameterDataStack.size();
}

OpaqueParameterScope::~OpaqueParameterScope()
{
    s_opaqueParameterDataStack.resize(_oldStackSize);
}

void OpaqueParameterScope::Reset()
{
    s_opaqueParameterDataStack.clear();
    s_handleGeneration++;
    if (s_handleGeneration == 0)
        s_handleGeneration = 1;
}

JSValue OpaqueValueMarshal::Push(JSContext* ctx, void* valueAddress, const Il2CppType* type)
{
    OpaqueParameterHandleType handle = AllocateOpaqueParameterHandle(type, valueAddress);
    JSValue obj = JS_NewObject(ctx);
    JS_SetPropertyStr(ctx, obj, "__zts_opaque", JS_NewBool(ctx, 1));
    JS_SetPropertyStr(ctx, obj, "__zts_handle", JS_NewInt64(ctx, (int64_t)handle));
    return obj;
}

bool OpaqueValueMarshal::IsOpaqueHandle(JSContext* ctx, JSValueConst value)
{
    if (ctx == nullptr || !JS_IsObject(value))
        return false;
    JSValue flag = JS_GetPropertyStr(ctx, value, "__zts_opaque");
    bool isOpaque = JS_IsBool(flag) && JS_ToBool(ctx, flag);
    JS_FreeValue(ctx, flag);
    return isOpaque;
}

JSValue OpaqueValueMarshal::GetValue(JSContext* ctx, JSValueConst handle)
{
    if (!IsOpaqueHandle(ctx, handle))
        return JS_ThrowTypeError(ctx, "zts: invalid opaque parameter handle");

    OpaqueParameterData* data = nullptr;
    if (!TryGetData(ctx, handle, &data))
        return JS_ThrowTypeError(ctx, "zts: invalid opaque parameter handle (expired)");

    const Il2CppType* type = nullptr;
    void* addr = nullptr;
    ResolveSlot(*data, &type, &addr);
    if (addr == nullptr)
        return JS_NULL;

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(type);
    if (meta == nullptr)
        return JS_ThrowTypeError(ctx, "zts: opaque value type not supported yet");
    return meta->cs2jsWriter(ctx, addr, meta);
}

void OpaqueValueMarshal::SetValue(JSContext* ctx, JSValueConst handle, JSValueConst value)
{
    if (!IsOpaqueHandle(ctx, handle))
    {
        JS_ThrowTypeError(ctx, "zts: invalid opaque parameter handle");
        return;
    }

    OpaqueParameterData* data = nullptr;
    if (!TryGetData(ctx, handle, &data))
    {
        JS_ThrowTypeError(ctx, "zts: invalid opaque parameter handle (expired)");
        return;
    }

    const Il2CppType* type = nullptr;
    void* addr = nullptr;
    ResolveSlot(*data, &type, &addr);
    if (addr == nullptr)
    {
        JS_ThrowTypeError(ctx, "zts: opaque value target is null");
        return;
    }

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(type);
    if (meta == nullptr)
    {
        JS_ThrowTypeError(ctx, "zts: opaque value type not supported yet");
        return;
    }

    void* tempStorage = nullptr;
    void* dest = meta->passByValue ? &tempStorage : addr;
    meta->js2csWriter(ctx, value, dest, meta);
    if (JS_HasException(ctx))
        return;
    if (meta->passByValue)
        *reinterpret_cast<void**>(addr) = tempStorage;
}

JSValue OpaqueValueMarshal::ToUserData(JSContext* ctx, JSValueConst handle)
{
    if (!IsOpaqueHandle(ctx, handle))
        return JS_ThrowTypeError(ctx, "zts: invalid opaque parameter handle");

    OpaqueParameterData* data = nullptr;
    if (!TryGetData(ctx, handle, &data))
        return JS_ThrowTypeError(ctx, "zts: invalid opaque parameter handle (expired)");

    const Il2CppType* type = nullptr;
    void* addr = nullptr;
    ResolveSlot(*data, &type, &addr);
    if (addr == nullptr)
        return JS_ThrowTypeError(ctx, "zts: to_user_data: null target");

    Il2CppClass* klass = il2cpp::vm::Class::FromIl2CppType(type);
    if (klass == nullptr || !il2cpp::vm::Class::IsValuetype(klass) || klass->enumtype
        || klass == il2cpp_defaults.boolean_class || klass == il2cpp_defaults.char_class
        || klass == il2cpp_defaults.byte_class || klass == il2cpp_defaults.sbyte_class
        || klass == il2cpp_defaults.int16_class || klass == il2cpp_defaults.uint16_class
        || klass == il2cpp_defaults.int32_class || klass == il2cpp_defaults.uint32_class
        || klass == il2cpp_defaults.int64_class || klass == il2cpp_defaults.uint64_class
        || klass == il2cpp_defaults.single_class || klass == il2cpp_defaults.double_class
        || klass == il2cpp_defaults.int_class || klass == il2cpp_defaults.uint_class)
        return JS_ThrowTypeError(ctx, "zts: to_user_data expects a struct opaque value");

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(type);
    if (meta == nullptr)
        return JS_ThrowTypeError(ctx, "zts: to_user_data type not supported");
    return StructMarshal::Cs2JsStruct(ctx, addr, meta);
}

bool OpaqueValueMarshal::TryGetValueAddress(JSContext* ctx, JSValueConst handle, void** outAddr)
{
    if (outAddr == nullptr || !IsOpaqueHandle(ctx, handle))
        return false;

    OpaqueParameterData* data = nullptr;
    if (!TryGetData(ctx, handle, &data))
        return false;
    if (data->valueAddress == nullptr)
        return false;

    /* Push stores the invoker arg slot pointer; byref MethodBridge needs that same address. */
    *outAddr = data->valueAddress;
    return true;
}
}
