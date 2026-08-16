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

#include "ArrayBinding.h"
#include "MetaBinding.h"

#include "../marshal/MarshalMeta.h"
#include "../marshal/ObjectRegistry.h"
#include "../utils/MetadataUtil.h"

#include "il2cpp-api.h"
#include "vm/Array.h"
#include "vm/Class.h"
#include "vm/Object.h"

#include <cstring>
#include <vector>

namespace zts
{
namespace
{
static JSClassID s_arrayCtxClassId = 0;
static JSRuntime* s_arrayCtxRuntime = nullptr;

struct ArrayCtx
{
    TypeBinding* binding;
    Il2CppClass* arrayKlass;
    const MarshalMetaInfo* elementMeta;
    int32_t rank;
    int32_t elementSize;
};

static void EnsureArrayCtxClass(JSRuntime* rt)
{
    if (rt == nullptr || s_arrayCtxRuntime == rt)
        return;
    JS_NewClassID(&s_arrayCtxClassId);
    JSClassDef def = {};
    def.class_name = "ZtsArrayCtx";
    JS_NewClass(rt, s_arrayCtxClassId, &def);
    s_arrayCtxRuntime = rt;
}

static ArrayCtx* CtxFromFuncData(JSValue* func_data)
{
    return reinterpret_cast<ArrayCtx*>(JS_GetOpaque(func_data[0], s_arrayCtxClassId));
}

static Il2CppArray* ArrayFromThis(JSContext* ctx, JSValueConst this_val)
{
    Il2CppObject* obj = ObjectRegistry::Get(ctx, this_val);
    if (obj == nullptr || obj->klass == nullptr || obj->klass->rank < 1)
        return nullptr;
    return reinterpret_cast<Il2CppArray*>(obj);
}

static bool ReadIndices(JSContext* ctx, int argc, JSValueConst* argv, int32_t rank, std::vector<int32_t>& out)
{
    if (argc < rank)
        return false;
    out.resize((size_t)rank);
    for (int32_t i = 0; i < rank; ++i)
    {
        int32_t idx = 0;
        if (JS_ToInt32(ctx, &idx, argv[i]))
            return false;
        out[(size_t)i] = idx;
    }
    return true;
}

static JSValue ArrayLengthGet(
    JSContext* ctx, JSValueConst this_val, int /*argc*/, JSValueConst* /*argv*/, int /*magic*/, JSValue* /*func_data*/)
{
    Il2CppArray* arr = ArrayFromThis(ctx, this_val);
    if (arr == nullptr)
        return JS_ThrowTypeError(ctx, "zts: length: not an array");
    return JS_NewInt32(ctx, (int32_t)il2cpp::vm::Array::GetLength(arr));
}

static JSValue ArrayLengthSet(
    JSContext* ctx, JSValueConst /*this_val*/, int /*argc*/, JSValueConst* /*argv*/, int /*magic*/, JSValue* /*func_data*/)
{
    return JS_ThrowTypeError(ctx, "zts: array length is read-only");
}

static JSValue ArrayGet(
    JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    ArrayCtx* actx = CtxFromFuncData(func_data);
    Il2CppArray* arr = ArrayFromThis(ctx, this_val);
    if (actx == nullptr || arr == nullptr)
        return JS_ThrowTypeError(ctx, "zts: get: not an array");
    if (actx->elementMeta == nullptr)
        return JS_ThrowTypeError(ctx, "zts: array element type not supported yet");

    std::vector<int32_t> indices;
    if (!ReadIndices(ctx, argc, argv, actx->rank, indices))
        return JS_ThrowTypeError(ctx, "zts: get expects %d index argument(s)", actx->rank);

    void* slot = nullptr;
    if (actx->rank == 1)
    {
        slot = il2cpp_array_addr_with_size(arr, actx->elementSize, indices[0]);
    }
    else
    {
        il2cpp_array_size_t flat = ArrayIndexFromIndices(arr, indices.data());
        slot = il2cpp_array_addr_with_size(arr, actx->elementSize, (int32_t)flat);
    }

    void* tempStorage = nullptr;
    void* storage = actx->elementMeta->passByValue ? &tempStorage : alloca((size_t)actx->elementMeta->size);
    if (actx->elementMeta->passByValue)
        tempStorage = *reinterpret_cast<void**>(slot);
    else
        std::memcpy(storage, slot, (size_t)actx->elementMeta->size);
    return actx->elementMeta->cs2jsWriter(ctx, storage, actx->elementMeta);
}

static JSValue ArraySet(
    JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv, int /*magic*/, JSValue* func_data)
{
    ArrayCtx* actx = CtxFromFuncData(func_data);
    Il2CppArray* arr = ArrayFromThis(ctx, this_val);
    if (actx == nullptr || arr == nullptr)
        return JS_ThrowTypeError(ctx, "zts: set: not an array");
    if (actx->elementMeta == nullptr)
        return JS_ThrowTypeError(ctx, "zts: array element type not supported yet");
    if (argc < actx->rank + 1)
        return JS_ThrowTypeError(ctx, "zts: set expects %d index(es) and a value", actx->rank);

    std::vector<int32_t> indices;
    if (!ReadIndices(ctx, argc, argv, actx->rank, indices))
        return JS_ThrowTypeError(ctx, "zts: set: invalid indices");

    if (actx->rank != 1)
    {
        il2cpp_array_size_t flat = ArrayIndexFromIndices(arr, indices.data());
        void* slot = il2cpp_array_addr_with_size(arr, actx->elementSize, (int32_t)flat);
        void* tempStorage = nullptr;
        void* storage = actx->elementMeta->passByValue ? &tempStorage : alloca((size_t)actx->elementMeta->size);
        actx->elementMeta->js2csWriter(ctx, argv[actx->rank], storage, actx->elementMeta);
        if (JS_HasException(ctx))
            return JS_EXCEPTION;
        if (actx->elementMeta->passByValue)
            *reinterpret_cast<void**>(slot) = tempStorage;
        else
            std::memcpy(slot, storage, (size_t)actx->elementMeta->size);
        return JS_UNDEFINED;
    }

    void* slot = il2cpp_array_addr_with_size(arr, actx->elementSize, indices[0]);
    void* tempStorage = nullptr;
    void* storage = actx->elementMeta->passByValue ? &tempStorage : alloca((size_t)actx->elementMeta->size);
    actx->elementMeta->js2csWriter(ctx, argv[actx->rank], storage, actx->elementMeta);
    if (JS_HasException(ctx))
        return JS_EXCEPTION;

    if (actx->elementMeta->passByValue)
        *reinterpret_cast<void**>(slot) = tempStorage;
    else
        std::memcpy(slot, storage, (size_t)actx->elementMeta->size);
    return JS_UNDEFINED;
}
} // namespace

void ArrayBinding::AttachMembers(JSContext* ctx, JSValue proto, TypeBinding* binding, Il2CppClass* arrayKlass)
{
    if (ctx == nullptr || arrayKlass == nullptr || arrayKlass->rank < 1)
        return;

    EnsureArrayCtxClass(JS_GetRuntime(ctx));
    auto* actx = new ArrayCtx();
    actx->binding = binding;
    actx->arrayKlass = arrayKlass;
    actx->rank = (int32_t)arrayKlass->rank;
    actx->elementSize = il2cpp::vm::Class::GetArrayElementSize(arrayKlass->element_class);
    actx->elementMeta = MarshalMeta::TryCreateDefault(&arrayKlass->element_class->byval_arg);

    JSValue holder = JS_NewObjectClass(ctx, s_arrayCtxClassId);
    JS_SetOpaque(holder, actx);

    JSValue getFn = JS_NewCFunctionData(ctx, ArrayGet, actx->rank, 0, 1, &holder);
    JSValue setFn = JS_NewCFunctionData(ctx, ArraySet, actx->rank + 1, 0, 1, &holder);
    JS_SetPropertyStr(ctx, proto, "get", getFn);
    JS_SetPropertyStr(ctx, proto, "set", setFn);

    JSValue lengthGet = JS_NewCFunctionData(ctx, ArrayLengthGet, 0, 0, 1, &holder);
    JSValue lengthSet = JS_NewCFunctionData(ctx, ArrayLengthSet, 1, 0, 1, &holder);
    JSAtom atom = JS_NewAtom(ctx, "length");
    JS_DefinePropertyGetSet(ctx, proto, atom, lengthGet, lengthSet, JS_PROP_C_W_E);
    JS_FreeAtom(ctx, atom);
    JS_FreeValue(ctx, holder);

    if (binding != nullptr)
    {
        binding->memberKeys.insert("get");
        binding->memberKeys.insert("set");
        binding->memberKeys.insert("length");
    }
}
}
