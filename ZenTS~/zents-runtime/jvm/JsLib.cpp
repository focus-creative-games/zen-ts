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

#include "JsLib.h"
#include "JsEnv.h"
#include "JsGlobalRefs.h"

#include "../marshal/MarshalMeta.h"
#include "../marshal/ObjectRegistry.h"
#include "../marshal/OpaqueValueMarshal.h"
#include "../marshal/DelegateMarshal.h"
#include "../marshal/StructMarshal.h"
#include "../mt/MetaBinding.h"
#include "../mt/TypeRegistry.h"
#include "../utils/MetadataUtil.h"
#include "ZentsLibScript.inc"

#include "il2cpp-api.h"
#include "vm/Array.h"
#include "vm/Assembly.h"
#include "vm/Class.h"
#include "vm/MetadataCache.h"
#include "vm/Object.h"
#include "vm/Reflection.h"

#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

namespace zents
{
namespace
{
static std::string ReadStringArg(js::Context* ctx, js::ValueConst* argv, int index)
{
    const char* cstr = js::ToCString(ctx, argv[index]);
    if (cstr == nullptr)
        return std::string();
    std::string result = cstr;
    js::FreeCString(ctx, cstr);
    return result;
}

static void BindGlobal(js::Context* ctx, const char* name, js::CFunction* fn, int length)
{
    js::Value global = js::GetGlobalObject(ctx);
    js::Value func = js::NewCFunction(ctx, fn, name, length);
    js::SetPropertyStr(ctx, global, name, func);
    js::FreeValue(ctx, global);
}

static Il2CppClass* KlassFromTypeObject(js::Context* ctx, js::ValueConst typeVal)
{
    if (Il2CppClass* live = MetaBinding::TryGetKlassFromTypeValue(ctx, typeVal))
        return live;

    if (js::IsString(typeVal))
    {
        const char* cstr = js::ToCString(ctx, typeVal);
        if (cstr == nullptr)
            return nullptr;
        std::string fullName = cstr;
        js::FreeCString(ctx, cstr);
        return MetadataUtil::ResolveTypeByName(fullName.c_str());
    }

    js::Value nameVal = js::GetPropertyStr(ctx, typeVal, "__zents_type_name");
    if (js::IsException(nameVal))
        return nullptr;
    if (!js::IsString(nameVal))
    {
        js::FreeValue(ctx, nameVal);
        return nullptr;
    }
    const char* cstr = js::ToCString(ctx, nameVal);
    js::FreeValue(ctx, nameVal);
    if (cstr == nullptr)
        return nullptr;
    std::string fullName = cstr;
    js::FreeCString(ctx, cstr);

    return MetadataUtil::ResolveTypeByName(fullName.c_str());
}

static js::Value EnsureAssemblyCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: ensure_assembly requires a name");
    std::string name = ReadStringArg(ctx, argv, 0);
    if (name.empty())
        return js::Exception();
    if (MetadataUtil::ResolveAssembly(name.c_str()) == nullptr)
        return js::ThrowReferenceError(ctx, "zents: assembly not found: %s", name.c_str());
    return js::Undefined();
}

static js::Value ResolveTypeCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: resolve_type requires assembly and type name");
    std::string asmName = ReadStringArg(ctx, argv, 0);
    std::string typeName = ReadStringArg(ctx, argv, 1);
    if (asmName.empty() || typeName.empty())
        return js::Exception();

    const Il2CppAssembly* assembly = MetadataUtil::ResolveAssembly(asmName.c_str());
    if (assembly == nullptr)
        return js::ThrowReferenceError(ctx, "zents: assembly not found: %s", asmName.c_str());

    Il2CppClass* klass = MetadataUtil::ResolveType(assembly, typeName.c_str());
    if (klass == nullptr)
        return js::ThrowReferenceError(ctx, "zents: type not found: %s", typeName.c_str());

    return TypeRegistry::PushTypeObject(ctx, klass);
}

static TypeBinding* EnsureSystemTypeBinding(js::Context* ctx)
{
    /* Minimal binding: do not BuildBinding(System.Type) ? reflection surface is huge. */
    static TypeBinding* s_systemTypeBinding = nullptr;
    if (s_systemTypeBinding != nullptr)
        return s_systemTypeBinding;
    s_systemTypeBinding = new TypeBinding();
    s_systemTypeBinding->klass = il2cpp_defaults.systemtype_class;
    s_systemTypeBinding->typeObject = js::Undefined();
    s_systemTypeBinding->typeObjectRaw = js::Undefined();
    s_systemTypeBinding->instanceProto = js::Undefined();
    s_systemTypeBinding->hasJsObject = false;
    ObjectRegistry::Initialize(js::GetRuntime(ctx));
    return s_systemTypeBinding;
}

static js::Value TypeOfCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: typeof expects a type object");
    Il2CppClass* klass = KlassFromTypeObject(ctx, argv[0]);
    if (klass == nullptr && ObjectRegistry::IsZentsObject(ctx, argv[0]))
    {
        Il2CppObject* obj = ObjectRegistry::Get(ctx, argv[0]);
        if (obj != nullptr && obj->klass == il2cpp_defaults.systemtype_class)
        {
            Il2CppReflectionType* rt = reinterpret_cast<Il2CppReflectionType*>(obj);
            if (rt->type != nullptr)
                klass = il2cpp::vm::Class::FromIl2CppType(rt->type);
        }
    }
    if (klass == nullptr)
        return js::ThrowTypeError(ctx, "zents: typeof expects a type object");

    Il2CppReflectionType* reflectionType = il2cpp::vm::Reflection::GetTypeObject(&klass->byval_arg);
    TypeBinding* binding = EnsureSystemTypeBinding(ctx);
    js::Value handle = ObjectRegistry::PushUnwrapped(ctx, reinterpret_cast<Il2CppObject*>(reflectionType), binding);
    if (js::IsException(handle))
        return handle;
    std::string fullName = MetadataUtil::BuildTypeFullName(klass);
    js::SetPropertyStr(ctx, handle, "__zents_type_name", js::NewString(ctx, fullName.c_str()));
    return handle;
}

static js::Value MakeSzArrayTypeCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: make_szarray_type requires element type");
    Il2CppClass* element = KlassFromTypeObject(ctx, argv[0]);
    if (element == nullptr)
        return js::ThrowTypeError(ctx, "zents: make_szarray_type expects a type object");
    Il2CppClass* arrayKlass = il2cpp::vm::Class::GetArrayClass(element, 1);
    if (arrayKlass == nullptr)
        return js::ThrowInternalError(ctx, "zents: failed to create szarray type");
    return TypeRegistry::PushTypeObject(ctx, arrayKlass);
}

static js::Value NewSzArrayByElementCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: new_szarray_by_element_type requires type and length");
    Il2CppClass* element = KlassFromTypeObject(ctx, argv[0]);
    if (element == nullptr)
        return js::ThrowTypeError(ctx, "zents: expected element type");
    int32_t length = 0;
    if (js::ToInt32(ctx, &length, argv[1]))
        return js::Exception();
    if (length < 0)
        return js::ThrowRangeError(ctx, "ArgumentOutOfRangeException: Non-negative number required.");
    Il2CppArray* arr = il2cpp::vm::Array::New(element, length);
    Il2CppClass* arrayKlass = il2cpp::vm::Class::GetArrayClass(element, 1);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, arrayKlass);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}

static js::Value NewSzArrayByTypeCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: new_szarray_by_szarray_type requires type and length");
    Il2CppClass* arrayKlass = KlassFromTypeObject(ctx, argv[0]);
    if (arrayKlass == nullptr || arrayKlass->rank != 1)
        return js::ThrowTypeError(ctx, "zents: expected szarray type");
    int32_t length = 0;
    if (js::ToInt32(ctx, &length, argv[1]))
        return js::Exception();
    if (length < 0)
        return js::ThrowRangeError(ctx, "ArgumentOutOfRangeException: Non-negative number required.");
    Il2CppArray* arr = il2cpp::vm::Array::New(arrayKlass->element_class, length);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, arrayKlass);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}

static js::Value ToArrayCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::Undefined();
    Il2CppObject* obj = ObjectRegistry::Get(ctx, argv[0]);
    if (obj == nullptr || obj->klass->rank == 0)
        return js::ThrowTypeError(ctx, "zents: to_array expects a CLR szarray");

    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(obj);
    int32_t length = (int32_t)il2cpp::vm::Array::GetLength(arr);
    js::Value jsArr = js::NewArray(ctx);
    Il2CppClass* elementKlass = obj->klass->element_class;
    const MarshalMetaInfo* elemMeta = MarshalMeta::TryCreateDefault(&elementKlass->byval_arg);
    if (elemMeta == nullptr)
        return js::ThrowTypeError(ctx, "zents: array element type not supported yet");
    int32_t elementSize = il2cpp::vm::Class::GetArrayElementSize(elementKlass);
    for (int32_t i = 0; i < length; ++i)
    {
        void* slot = il2cpp_array_addr_with_size(arr, elementSize, i);
        js::Value item = elemMeta->cs2jsWriter(ctx, slot, elemMeta);
        if (js::IsException(item))
            return item;
        js::SetPropertyUint32(ctx, jsArr, (uint32_t)i, item);
    }
    return jsArr;
}

static js::Value GetTypeFromNameCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: get_type_from_name requires a name");
    std::string name = ReadStringArg(ctx, argv, 0);
    if (name.empty())
        return js::ThrowReferenceError(ctx, "zents: type not found: ");

    Il2CppClass* klass = MetadataUtil::ResolveTypeByName(name.c_str());
    if (klass == nullptr)
        return js::ThrowReferenceError(ctx, "zents: type not found: %s", name.c_str());
    return TypeRegistry::PushTypeObject(ctx, klass);
}

static js::Value CastCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: cast requires value and type");
    Il2CppClass* klass = KlassFromTypeObject(ctx, argv[1]);
    if (klass == nullptr)
        return js::ThrowTypeError(ctx, "zents: cast target is not a type");

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(&klass->byval_arg);
    if (meta == nullptr)
        return js::ThrowTypeError(ctx, "zents: cast type not supported yet");

    void* storage = alloca((size_t)(meta->size > 0 ? meta->size : sizeof(void*)));
    void* tempStorage = nullptr;
    void* dest = meta->passByValue ? &tempStorage : storage;
    meta->js2csWriter(ctx, argv[0], dest, meta);
    if (js::HasException(ctx))
        return js::Exception();
    void* src = meta->passByValue ? &tempStorage : storage;
    return meta->cs2jsWriter(ctx, src, meta);
}

static js::Value BoxCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: box requires type and value");
    Il2CppClass* klass = KlassFromTypeObject(ctx, argv[0]);
    if (klass == nullptr)
        return js::ThrowTypeError(ctx, "zents: box expects a type object");
    if (!il2cpp::vm::Class::IsValuetype(klass))
        return js::ThrowTypeError(ctx, "zents: box expects a value type or enum");

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(&klass->byval_arg);
    if (meta == nullptr)
        return js::ThrowTypeError(ctx, "zents: box type not supported yet");

    void* storage = alloca((size_t)meta->size);
    meta->js2csWriter(ctx, argv[1], storage, meta);
    if (js::HasException(ctx))
        return js::Exception();

    Il2CppObject* boxed = il2cpp::vm::Object::Box(klass, storage);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, klass);
    return ObjectRegistry::Push(ctx, boxed, binding);
}

static js::Value UnboxCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::Undefined();
    Il2CppObject* obj = ObjectRegistry::Get(ctx, argv[0]);
    if (obj == nullptr)
        return js::ThrowTypeError(ctx, "zents: unbox expects a CLR object");

    Il2CppClass* klass = obj->klass;
    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(&klass->byval_arg);
    if (meta == nullptr)
        return js::ThrowTypeError(ctx, "zents: unbox type not supported yet");

    if (il2cpp::vm::Class::IsValuetype(klass))
    {
        void* raw = ObjectUnbox(obj);
        return meta->cs2jsWriter(ctx, raw, meta);
    }

    Il2CppObject* storage = obj;
    return meta->cs2jsWriter(ctx, &storage, meta);
}

static js::Value MakeGenericTypeCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: make_generic_type requires open type and args");
    Il2CppClass* open = KlassFromTypeObject(ctx, argv[0]);
    if (open == nullptr || !open->is_generic)
        return js::ThrowTypeError(ctx, "zents: make_generic_type expects an open generic type");

    js::Value argsVal = argv[1];
    if (!js::IsArray(ctx, argsVal))
        return js::ThrowTypeError(ctx, "zents: make_generic_type args must be an array");

    js::Value lenVal = js::GetPropertyStr(ctx, argsVal, "length");
    int32_t count = 0;
    if (js::ToInt32(ctx, &count, lenVal))
    {
        js::FreeValue(ctx, lenVal);
        return js::Exception();
    }
    js::FreeValue(ctx, lenVal);
    if (count <= 0)
        return js::ThrowTypeError(ctx, "zents: make_generic_type requires type arguments");

    const Il2CppType** types = (const Il2CppType**)alloca(sizeof(Il2CppType*) * (size_t)count);
    for (int32_t i = 0; i < count; ++i)
    {
        js::Value item = js::GetPropertyUint32(ctx, argsVal, (uint32_t)i);
        if (js::IsException(item))
            return item;
        Il2CppClass* argKlass = KlassFromTypeObject(ctx, item);
        js::FreeValue(ctx, item);
        if (argKlass == nullptr)
            return js::ThrowTypeError(ctx, "zents: invalid generic type argument");
        types[i] = &argKlass->byval_arg;
    }

    Il2CppClass* inflated = il2cpp::vm::Class::GetInflatedGenericInstanceClass(open, types, (uint32_t)count);
    if (inflated == nullptr)
        return js::ThrowInternalError(ctx, "zents: failed to inflate generic type");
    return TypeRegistry::PushTypeObject(ctx, inflated);
}

static js::Value ToBytesCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: to_bytes expects CLR array");
    Il2CppObject* obj = ObjectRegistry::Get(ctx, argv[0]);
    if (obj == nullptr || obj->klass->rank == 0)
        return js::ThrowTypeError(ctx, "zents: to_bytes expects CLR array");

    Il2CppClass* element = obj->klass->element_class;
    if (element->has_references || !il2cpp::vm::Class::IsBlittable(element)
        || element == il2cpp_defaults.boolean_class || element == il2cpp_defaults.char_class
        || element == il2cpp_defaults.string_class || (!il2cpp::vm::Class::IsValuetype(element) && !element->enumtype))
        return js::ThrowTypeError(ctx, "zents: to_bytes: element type is not blittable");

    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(obj);
    uint32_t byteLen = il2cpp::vm::Array::GetByteLength(arr);
    char* src = il2cpp::vm::Array::GetFirstElementAddress(arr);
    js::Value jsArr = js::NewArray(ctx);
    for (uint32_t i = 0; i < byteLen; ++i)
        js::SetPropertyUint32(ctx, jsArr, i, js::NewInt32(ctx, (uint8_t)src[i]));
    return jsArr;
}

static js::Value ToDelegateCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: to_delegate requires function and delegate type");
    if (!js::IsFunction(ctx, argv[0]))
        return js::ThrowTypeError(ctx, "zents: to_delegate expects a function");
    Il2CppClass* delKlass = KlassFromTypeObject(ctx, argv[1]);
    if (delKlass == nullptr || !MetadataUtil::IsDelegateClass(delKlass))
        return js::ThrowTypeError(ctx, "zents: to_delegate expects delegate type");

    int funcRef = JsGlobalRefs::Store(ctx, argv[0]);
    Il2CppDelegate* del = DelegateMarshal::CreateFromFuncRef(ctx, delKlass, funcRef);
    if (del == nullptr)
    {
        JsGlobalRefs::FreeAndRelease(ctx, funcRef);
        return js::ThrowTypeError(ctx, "zents: failed to create delegate");
    }
    return DelegateMarshal::PushToJs(ctx, del, delKlass);
}



static std::unordered_map<const MethodInfo*, js::Value> s_closedGenericMethodCache;

static js::Value MakeGenericMethodCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: make_generic_method requires method and type args");

    MethodMarshalCtx* openCtx = MetaBinding::TryGetDirectMethodCtx(ctx, argv[0]);
    if (openCtx == nullptr || openCtx->method == nullptr)
        return js::ThrowTypeError(ctx, "zents: make_generic_method expects a direct method function (not dispatch)");
    const MethodInfo* openMethod = openCtx->method;
    if (!openMethod->is_generic)
        return js::ThrowTypeError(ctx, "zents: make_generic_method requires an open generic method definition");

    js::Value argsVal = argv[1];
    if (!js::IsArray(ctx, argsVal))
        return js::ThrowTypeError(ctx, "zents: make_generic_method args must be an array");

    js::Value lenVal = js::GetPropertyStr(ctx, argsVal, "length");
    int32_t count = 0;
    if (js::ToInt32(ctx, &count, lenVal))
    {
        js::FreeValue(ctx, lenVal);
        return js::Exception();
    }
    js::FreeValue(ctx, lenVal);
    if (count < 0)
        return js::ThrowTypeError(ctx, "zents: make_generic_method args length invalid");

    Il2CppMetadataGenericContainerHandle container = openMethod->genericContainerHandle;
    if (container == nullptr && openMethod->methodMetadataHandle != nullptr)
        container = il2cpp::vm::MetadataCache::GetGenericContainerFromMethod(openMethod->methodMetadataHandle);
    if (container == nullptr)
        return js::ThrowTypeError(ctx, "zents: make_generic_method: missing generic container");
    const uint32_t expectedArgs = il2cpp::vm::MetadataCache::GetGenericContainerCount(container);
    if ((uint32_t)count != expectedArgs)
    {
        return js::ThrowTypeError(
            ctx,
            "zents: make_generic_method expects %u type argument(s), got %d",
            expectedArgs,
            count);
    }

    const Il2CppType** types = count > 0
        ? (const Il2CppType**)alloca(sizeof(Il2CppType*) * (size_t)count)
        : nullptr;
    for (int32_t i = 0; i < count; ++i)
    {
        js::Value item = js::GetPropertyUint32(ctx, argsVal, (uint32_t)i);
        if (js::IsException(item))
            return item;
        Il2CppClass* argKlass = KlassFromTypeObject(ctx, item);
        js::FreeValue(ctx, item);
        if (argKlass == nullptr)
            return js::ThrowTypeError(ctx, "zents: invalid generic method type argument");
        il2cpp::vm::Class::Init(argKlass);
        types[i] = &argKlass->byval_arg;
    }

    if (!MetadataUtil::MethodGenericTypeArgsSatisfyConstraints(openMethod, types, (uint32_t)count))
        return js::ThrowTypeError(ctx, "zents: generic type constraints not satisfied");
    const MethodInfo* closed =
        il2cpp::vm::MetadataCache::GetGenericInstanceMethod(openMethod, types, (uint32_t)count);
    if (closed == nullptr)
        return js::ThrowTypeError(ctx, "zents: failed to inflate generic method");

    auto it = s_closedGenericMethodCache.find(closed);
    if (it != s_closedGenericMethodCache.end())
        return js::DupValue(ctx, it->second);

    MethodMarshalCtx* closedCtx = MetaBinding::CreateMethodMarshalCtx(closed);
    if (closedCtx == nullptr)
        return js::ThrowTypeError(ctx, "zents: closed generic method not marshalable yet");

    js::Value fn = MetadataUtil::IsStaticMethod(closed)
        ? MetaBinding::CreateStaticMethodFunction(ctx, closedCtx)
        : MetaBinding::CreateInstanceMethodFunction(ctx, closedCtx);
    s_closedGenericMethodCache[closed] = js::DupValue(ctx, fn);
    return fn;
}

static js::Value ResolveSignatureCallback(js::Context* ctx, js::ValueConst, int argc, js::ValueConst* argv)
{
    if (argc != 2) return js::Undefined();
    Il2CppClass* owner = MetaBinding::TryGetKlassFromTypeValue(ctx, argv[0]);
    bool isStatic = owner != nullptr;
    if (!owner) { TypeBinding* b = ObjectRegistry::GetBinding(ctx, argv[0]); owner = b ? b->klass : nullptr; }
    if (!owner) return js::Undefined();
    std::string key = ReadStringArg(ctx, argv, 1);
    const MethodInfo* method = MetaBinding::FindPublicMethod(owner, key.c_str(), isStatic ? 1 : 0, -1);
    if (!method) return js::Undefined();
    MethodMarshalCtx* mc = MetaBinding::CreateMethodMarshalCtx(method);
    if (!mc) return js::ThrowTypeError(ctx, "zents: method cannot be marshaled");
    return isStatic ? MetaBinding::CreateStaticMethodFunction(ctx, mc) : MetaBinding::CreateInstanceMethodFunction(ctx, mc, il2cpp::vm::Class::IsValuetype(owner)&&!owner->enumtype?(ObjectRegistry::IsByVal(ctx,argv[0])?1:2):0);
}
static js::Value InflateCallback(js::Context* ctx, js::ValueConst thisVal, int argc, js::ValueConst* argv)
{
    if (argc != 3) return js::ThrowTypeError(ctx, "zents: inflate requires type, method and type arguments");
    Il2CppClass* owner = KlassFromTypeObject(ctx, argv[0]);
    if (!owner) return js::ThrowTypeError(ctx, "zents: inflate requires a CLR type");
    std::string key = ReadStringArg(ctx, argv, 1);
    js::Value len = js::GetPropertyStr(ctx, argv[2], "length"); int32_t count = 0; js::ToInt32(ctx, &count, len); js::FreeValue(ctx, len);
    const MethodInfo* method = MetaBinding::FindPublicMethod(owner, key.c_str(), -1, count);
    if (!method) return js::ThrowTypeError(ctx, "zents: generic method not found: %s", key.c_str());
    MethodMarshalCtx* mc = MetaBinding::CreateMethodMarshalCtx(method);
    if (!mc) return js::ThrowTypeError(ctx, "zents: generic method cannot be marshaled");
    js::Value fn = MetadataUtil::IsStaticMethod(method) ? MetaBinding::CreateStaticMethodFunction(ctx, mc) : MetaBinding::CreateInstanceMethodFunction(ctx, mc);
    js::Value args[] = {fn, argv[2]};
    js::Value result = MakeGenericMethodCallback(ctx, thisVal, 2, args); js::FreeValue(ctx, fn); return result;
}

static js::Value CreateSignatureCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    js::Value argsVal = js::Undefined();
    bool freeArgs = false;
    if (argc >= 1 && js::IsArray(ctx, argv[0]))
    {
        argsVal = argv[0];
    }
    else
    {
        argsVal = js::NewArray(ctx);
        freeArgs = true;
        for (int i = 0; i < argc; ++i)
            js::SetPropertyUint32(ctx, argsVal, (uint32_t)i, js::DupValue(ctx, argv[i]));
    }

    js::Value lenVal = js::GetPropertyStr(ctx, argsVal, "length");
    int32_t count = 0;
    if (js::ToInt32(ctx, &count, lenVal))
    {
        js::FreeValue(ctx, lenVal);
        if (freeArgs)
            js::FreeValue(ctx, argsVal);
        return js::Exception();
    }
    js::FreeValue(ctx, lenVal);

    std::string sig = "(";
    for (int32_t i = 0; i < count; ++i)
    {
        if (i > 0)
            sig += ',';
        js::Value item = js::GetPropertyUint32(ctx, argsVal, (uint32_t)i);
        Il2CppClass* klass = KlassFromTypeObject(ctx, item);
        js::FreeValue(ctx, item);
        if (klass == nullptr)
        {
            if (freeArgs)
                js::FreeValue(ctx, argsVal);
            return js::ThrowTypeError(ctx, "zents: create_signature expects type arguments");
        }
        sig += MetadataUtil::BuildTypeFullName(klass);
    }
    sig += ')';
    if (freeArgs)
        js::FreeValue(ctx, argsVal);
    return js::NewString(ctx, sig.c_str());
}

static bool ReadIntJsArray(js::Context* ctx, js::ValueConst jsArray, std::vector<int32_t>& out, const char* label)
{
    if (!js::IsArray(ctx, jsArray))
    {
        js::ThrowTypeError(ctx, "zents: %s must be a JS Array", label);
        return false;
    }
    js::Value lenVal = js::GetPropertyStr(ctx, jsArray, "length");
    int32_t count = 0;
    if (js::ToInt32(ctx, &count, lenVal))
    {
        js::FreeValue(ctx, lenVal);
        return false;
    }
    js::FreeValue(ctx, lenVal);
    out.resize((size_t)count);
    for (int32_t i = 0; i < count; ++i)
    {
        js::Value item = js::GetPropertyUint32(ctx, jsArray, (uint32_t)i);
        if (js::ToInt32(ctx, &out[(size_t)i], item))
        {
            js::FreeValue(ctx, item);
            return false;
        }
        js::FreeValue(ctx, item);
    }
    return true;
}

static js::Value MakeMdArrayTypeCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: make_mdarray_type requires element type and rank");
    Il2CppClass* element = KlassFromTypeObject(ctx, argv[0]);
    if (element == nullptr)
        return js::ThrowTypeError(ctx, "zents: make_mdarray_type expects element type");
    int32_t rank = 0;
    if (js::ToInt32(ctx, &rank, argv[1]))
        return js::Exception();
    if (rank < 1 || rank > 32)
        return js::ThrowRangeError(ctx, "zents: make_mdarray_type rank must be in [1, 32]");
    Il2CppClass* arrayKlass = il2cpp::vm::Class::GetArrayClass(element, (uint32_t)rank);
    if (arrayKlass == nullptr)
        return js::ThrowInternalError(ctx, "zents: failed to create mdarray type");
    return TypeRegistry::PushTypeObject(ctx, arrayKlass);
}

static js::Value NewMdArrayBySpecCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 3)
        return js::ThrowTypeError(ctx, "zents: new_mdarray_by_spec requires element, lowbounds, sizes");
    Il2CppClass* element = KlassFromTypeObject(ctx, argv[0]);
    if (element == nullptr)
        return js::ThrowTypeError(ctx, "zents: expected element type");
    std::vector<int32_t> lowBounds;
    std::vector<int32_t> sizes;
    if (!ReadIntJsArray(ctx, argv[1], lowBounds, "lowbounds") || !ReadIntJsArray(ctx, argv[2], sizes, "sizes"))
        return js::Exception();
    if (lowBounds.size() != sizes.size() || sizes.empty())
        return js::ThrowTypeError(ctx, "zents: new_mdarray_by_spec: lowbounds/sizes rank mismatch");

    uint32_t rank = (uint32_t)sizes.size();
    Il2CppClass* arrayKlass = il2cpp::vm::Class::GetArrayClass(element, rank);
    auto* lengths = (il2cpp_array_size_t*)alloca(sizeof(il2cpp_array_size_t) * rank);
    auto* lowers = (il2cpp_array_size_t*)alloca(sizeof(il2cpp_array_size_t) * rank);
    for (uint32_t i = 0; i < rank; ++i)
    {
        if (sizes[i] < 0)
            return js::ThrowRangeError(ctx, "ArgumentOutOfRangeException: Non-negative number required.");
        lengths[i] = (il2cpp_array_size_t)sizes[i];
        lowers[i] = (il2cpp_array_size_t)lowBounds[i];
    }
    Il2CppArray* arr = il2cpp::vm::Array::NewFull(arrayKlass, lengths, lowers);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, arrayKlass);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}

static js::Value NewMdArrayByTypeCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 3)
        return js::ThrowTypeError(ctx, "zents: new_mdarray_by_mdarray_type requires type, lowbounds, sizes");
    Il2CppClass* arrayKlass = KlassFromTypeObject(ctx, argv[0]);
    if (arrayKlass == nullptr || arrayKlass->rank < 1)
        return js::ThrowTypeError(ctx, "zents: new_mdarray_by_mdarray_type expects mdarray type");
    std::vector<int32_t> lowBounds;
    std::vector<int32_t> sizes;
    if (!ReadIntJsArray(ctx, argv[1], lowBounds, "lowbounds") || !ReadIntJsArray(ctx, argv[2], sizes, "sizes"))
        return js::Exception();
    if (lowBounds.size() != sizes.size() || (int32_t)sizes.size() != arrayKlass->rank)
        return js::ThrowTypeError(ctx, "zents: new_mdarray_by_mdarray_type: lowbounds/sizes rank mismatch");

    uint32_t rank = (uint32_t)sizes.size();
    auto* lengths = (il2cpp_array_size_t*)alloca(sizeof(il2cpp_array_size_t) * rank);
    auto* lowers = (il2cpp_array_size_t*)alloca(sizeof(il2cpp_array_size_t) * rank);
    for (uint32_t i = 0; i < rank; ++i)
    {
        if (sizes[i] < 0)
            return js::ThrowRangeError(ctx, "ArgumentOutOfRangeException: Non-negative number required.");
        lengths[i] = (il2cpp_array_size_t)sizes[i];
        lowers[i] = (il2cpp_array_size_t)lowBounds[i];
    }
    Il2CppArray* arr = il2cpp::vm::Array::NewFull(arrayKlass, lengths, lowers);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, arrayKlass);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}

static js::Value GetOpaqueCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: get_opaquevalue requires a handle");
    return OpaqueValueMarshal::GetValue(ctx, argv[0]);
}

static js::Value SetOpaqueCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 2)
        return js::ThrowTypeError(ctx, "zents: set_opaquevalue requires handle and value");
    OpaqueValueMarshal::SetValue(ctx, argv[0], argv[1]);
    if (js::HasException(ctx))
        return js::Exception();
    return js::Undefined();
}

static js::Value PrintCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    int level = 0;
    if (argc >= 1)
        js::ToInt32(ctx, &level, argv[0]);
    std::string msg = argc >= 2 ? ReadStringArg(ctx, argv, 1) : std::string();
    const char* prefix = level >= 2 ? "[JS:error] " : (level == 1 ? "[JS:warn] " : "[JS] ");
    // Player: stdout is captured by Unity player log on most platforms.
    std::fprintf(stdout, "%s%s\n", prefix, msg.c_str());
    std::fflush(stdout);
    return js::Undefined();
}

static js::Value ToUserDataCallback(js::Context* ctx, js::ValueConst /*this_val*/, int argc, js::ValueConst* argv)
{
    if (argc < 1)
        return js::ThrowTypeError(ctx, "zents: to_user_data requires an opaque handle");
    return OpaqueValueMarshal::ToUserData(ctx, argv[0]);
}
} // namespace

void JsLib::RegisterGlobals(js::Context* ctx)
{
    BindGlobal(ctx, "__zents_ensure_assembly", EnsureAssemblyCallback, 1);
    BindGlobal(ctx, "__zents_resolve_type", ResolveTypeCallback, 2);
    BindGlobal(ctx, "__zents_typeof", TypeOfCallback, 1);
    BindGlobal(ctx, "__zents_get_type_from_name", GetTypeFromNameCallback, 1);
    BindGlobal(ctx, "__zents_cast", CastCallback, 2);
    BindGlobal(ctx, "__zents_box", BoxCallback, 2);
    BindGlobal(ctx, "__zents_unbox", UnboxCallback, 1);
    BindGlobal(ctx, "__zents_make_szarray_type", MakeSzArrayTypeCallback, 1);
    BindGlobal(ctx, "__zents_new_szarray_by_element_type", NewSzArrayByElementCallback, 2);
    BindGlobal(ctx, "__zents_new_szarray_by_szarray_type", NewSzArrayByTypeCallback, 2);
    BindGlobal(ctx, "__zents_to_array", ToArrayCallback, 1);
    BindGlobal(ctx, "__zents_make_generic_type", MakeGenericTypeCallback, 2);
    BindGlobal(ctx, "__zents_resolve_signature", ResolveSignatureCallback, 2);
    BindGlobal(ctx, "__zents_inflate", InflateCallback, 3);
    BindGlobal(ctx, "__zents_make_mdarray_type", MakeMdArrayTypeCallback, 2);
    BindGlobal(ctx, "__zents_new_mdarray_by_spec", NewMdArrayBySpecCallback, 3);
    BindGlobal(ctx, "__zents_new_mdarray_by_mdarray_type", NewMdArrayByTypeCallback, 3);
    BindGlobal(ctx, "__zents_to_bytes", ToBytesCallback, 1);
    BindGlobal(ctx, "__zents_to_delegate", ToDelegateCallback, 2);
    BindGlobal(ctx, "__zents_create_signature", CreateSignatureCallback, 1);
    BindGlobal(ctx, "__zents_get_opaquevalue", GetOpaqueCallback, 1);
    BindGlobal(ctx, "__zents_set_opaquevalue", SetOpaqueCallback, 2);
    BindGlobal(ctx, "__zents_to_user_data", ToUserDataCallback, 1);
    BindGlobal(ctx, "__zents_print", PrintCallback, 2);

    js::Value result = js::Eval(
        ctx,
        kZentsLibJs,
        std::strlen(kZentsLibJs),
        "zentslib.js",
        js::EvalGlobal);
    if (js::IsException(result))
        JsEnv::ThrowPendingException();
    js::FreeValue(ctx, result);
}

void JsLib::Reset(js::Context* ctx)
{
    for (auto& kv : s_closedGenericMethodCache)
    {
        if (ctx != nullptr)
            js::FreeValue(ctx, kv.second);
    }
    s_closedGenericMethodCache.clear();
    OpaqueParameterScope::Reset();
}
}
