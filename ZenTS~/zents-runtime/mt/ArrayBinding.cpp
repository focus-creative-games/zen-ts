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
#include "gc/GarbageCollector.h"

#include <cstring>
#include <vector>

namespace zents
{
namespace
{
static js::ClassID s_arrayCtxClassId = 0;
static js::Runtime* s_arrayCtxRuntime = nullptr;

struct ArrayCtx
{
    TypeBinding* binding;
    Il2CppClass* arrayKlass;
    const MarshalMetaInfo* elementMeta;
    int32_t rank;
    int32_t elementSize;
};

static void EnsureArrayCtxClass(js::Runtime* rt)
{
    if (rt == nullptr || s_arrayCtxRuntime == rt)
        return;
    js::NewClassID(&s_arrayCtxClassId);
    js::ClassDef def = {};
    def.class_name = "ZentsArrayCtx";
    js::NewClass(rt, s_arrayCtxClassId, &def);
    s_arrayCtxRuntime = rt;
}

static ArrayCtx* CtxFromFuncData(js::Value* func_data)
{
    return reinterpret_cast<ArrayCtx*>(js::GetOpaque(func_data[0], s_arrayCtxClassId));
}

static Il2CppArray* ArrayFromThis(js::Context* ctx, js::ValueConst this_val)
{
    Il2CppObject* obj = ObjectRegistry::Get(ctx, this_val);
    if (obj == nullptr || obj->klass == nullptr || obj->klass->rank < 1)
        return nullptr;
    return reinterpret_cast<Il2CppArray*>(obj);
}

static bool ReadIndices(js::Context* ctx, int argc, js::ValueConst* argv, int32_t rank, std::vector<int32_t>& out)
{
    if (argc < rank)
        return false;
    out.resize((size_t)rank);
    for (int32_t i = 0; i < rank; ++i)
    {
        int32_t idx = 0;
        if (js::ToInt32(ctx, &idx, argv[i]))
            return false;
        out[(size_t)i] = idx;
    }
    return true;
}

// Address calculation macros do not check bounds. Validate every dimension
// before touching storage, including rank-one arrays with nonzero lower bounds.
static bool CheckedArrayIndex(js::Context* ctx, Il2CppArray* arr,
    const std::vector<int32_t>& indices, il2cpp_array_size_t& flat)
{
    flat = 0;
    for (size_t i = 0; i < indices.size(); ++i)
    {
        int64_t lower = arr->bounds != nullptr ? arr->bounds[i].lower_bound : 0;
        il2cpp_array_size_t length = arr->bounds != nullptr ? arr->bounds[i].length : arr->max_length;
        int64_t offset = static_cast<int64_t>(indices[i]) - lower;
        if (offset < 0 || static_cast<uint64_t>(offset) >= length)
        {
            js::ThrowRangeError(ctx, "zents: array index out of bounds");
            return false;
        }
        flat = flat * length + static_cast<il2cpp_array_size_t>(offset);
    }
    return true;
}

static js::Value ArrayLengthGet(
    js::Context* ctx, js::ValueConst this_val, int /*argc*/, js::ValueConst* /*argv*/, int /*magic*/, js::Value* /*func_data*/)
{
    Il2CppArray* arr = ArrayFromThis(ctx, this_val);
    if (arr == nullptr)
        return js::ThrowTypeError(ctx, "zents: length: not an array");
    return js::NewInt32(ctx, (int32_t)il2cpp::vm::Array::GetLength(arr));
}

static js::Value ArrayLengthSet(
    js::Context* ctx, js::ValueConst /*this_val*/, int /*argc*/, js::ValueConst* /*argv*/, int /*magic*/, js::Value* /*func_data*/)
{
    return js::ThrowTypeError(ctx, "zents: array length is read-only");
}

static js::Value ArrayGet(
    js::Context* ctx, js::ValueConst this_val, int argc, js::ValueConst* argv, int /*magic*/, js::Value* func_data)
{
    ArrayCtx* actx = CtxFromFuncData(func_data);
    Il2CppArray* arr = ArrayFromThis(ctx, this_val);
    if (actx == nullptr || arr == nullptr)
        return js::ThrowTypeError(ctx, "zents: get: not an array");
    if (actx->elementMeta == nullptr)
        return js::ThrowTypeError(ctx, "zents: array element type not supported yet");

    std::vector<int32_t> indices;
    if (!ReadIndices(ctx, argc, argv, actx->rank, indices))
        return js::ThrowTypeError(ctx, "zents: get expects %d index argument(s)", actx->rank);

    il2cpp_array_size_t flat = 0;
    if (!CheckedArrayIndex(ctx, arr, indices, flat)) return js::Exception();
    void* slot = il2cpp_array_addr_with_size(arr, actx->elementSize, flat);

    void* tempStorage = nullptr;
    void* storage = actx->elementMeta->passByValue ? &tempStorage : alloca((size_t)actx->elementMeta->size);
    if (actx->elementMeta->passByValue)
        tempStorage = *reinterpret_cast<void**>(slot);
    else
        std::memcpy(storage, slot, (size_t)actx->elementMeta->size);
    return actx->elementMeta->cs2jsWriter(ctx, storage, actx->elementMeta);
}

static js::Value ArraySet(
    js::Context* ctx, js::ValueConst this_val, int argc, js::ValueConst* argv, int /*magic*/, js::Value* func_data)
{
    ArrayCtx* actx = CtxFromFuncData(func_data);
    Il2CppArray* arr = ArrayFromThis(ctx, this_val);
    if (actx == nullptr || arr == nullptr)
        return js::ThrowTypeError(ctx, "zents: set: not an array");
    if (actx->elementMeta == nullptr)
        return js::ThrowTypeError(ctx, "zents: array element type not supported yet");
    if (argc < actx->rank + 1)
        return js::ThrowTypeError(ctx, "zents: set expects %d index(es) and a value", actx->rank);

    std::vector<int32_t> indices;
    if (!ReadIndices(ctx, argc, argv, actx->rank, indices))
        return js::ThrowTypeError(ctx, "zents: set: invalid indices");

    il2cpp_array_size_t flat = 0;
    if (!CheckedArrayIndex(ctx, arr, indices, flat)) return js::Exception();
    void* slot = il2cpp_array_addr_with_size(arr, actx->elementSize, flat);
    void* tempStorage = nullptr;
    void* storage = actx->elementMeta->passByValue ? &tempStorage : alloca((size_t)actx->elementMeta->size);
    actx->elementMeta->js2csWriter(ctx, argv[actx->rank], storage, actx->elementMeta);
    if (js::HasException(ctx))
        return js::Exception();

    if (actx->elementMeta->passByValue)
        *reinterpret_cast<void**>(slot) = tempStorage;
    else
        std::memcpy(slot, storage, (size_t)actx->elementMeta->size);
    if (arr->klass->has_references)
        il2cpp::gc::GarbageCollector::SetWriteBarrier(reinterpret_cast<void**>(slot), (size_t)actx->elementSize);
    return js::Undefined();
}
} // namespace

void ArrayBinding::AttachMembers(js::Context* ctx, js::Value proto, TypeBinding* binding, Il2CppClass* arrayKlass)
{
    if (ctx == nullptr || arrayKlass == nullptr || arrayKlass->rank < 1)
        return;

    EnsureArrayCtxClass(js::GetRuntime(ctx));
    auto* actx = new ArrayCtx();
    actx->binding = binding;
    actx->arrayKlass = arrayKlass;
    actx->rank = (int32_t)arrayKlass->rank;
    actx->elementSize = il2cpp::vm::Class::GetArrayElementSize(arrayKlass->element_class);
    actx->elementMeta = MarshalMeta::TryCreateDefault(&arrayKlass->element_class->byval_arg);

    js::Value holder = js::NewObjectClass(ctx, s_arrayCtxClassId);
    js::SetOpaque(holder, actx);

    js::Value getFn = js::NewCFunctionData(ctx, ArrayGet, actx->rank, 0, 1, &holder);
    js::Value setFn = js::NewCFunctionData(ctx, ArraySet, actx->rank + 1, 0, 1, &holder);
    js::SetPropertyStr(ctx, proto, "get", getFn);
    js::SetPropertyStr(ctx, proto, "set", setFn);

    js::Value lengthGet = js::NewCFunctionData(ctx, ArrayLengthGet, 0, 0, 1, &holder);
    js::Value lengthSet = js::NewCFunctionData(ctx, ArrayLengthSet, 1, 0, 1, &holder);
    js::Atom atom = js::NewAtom(ctx, "length");
    js::DefinePropertyGetSet(ctx, proto, atom, lengthGet, lengthSet, js::PropertyConfigurableWritableEnumerable);
    js::FreeAtom(ctx, atom);
    js::FreeValue(ctx, holder);

    if (binding != nullptr)
    {
        binding->memberKeys.insert("get");
        binding->memberKeys.insert("set");
        binding->memberKeys.insert("length");
    }
}
}
