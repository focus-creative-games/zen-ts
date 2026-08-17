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
static std::string ReadStringArg(JSContext* ctx, JSValueConst* argv, int index)
{
    const char* cstr = JS_ToCString(ctx, argv[index]);
    if (cstr == nullptr)
        return std::string();
    std::string result = cstr;
    JS_FreeCString(ctx, cstr);
    return result;
}

static void BindGlobal(JSContext* ctx, const char* name, JSCFunction* fn, int length)
{
    JSValue global = JS_GetGlobalObject(ctx);
    JSValue func = JS_NewCFunction(ctx, fn, name, length);
    JS_SetPropertyStr(ctx, global, name, func);
    JS_FreeValue(ctx, global);
}

static Il2CppClass* KlassFromTypeObject(JSContext* ctx, JSValueConst typeVal)
{
    if (Il2CppClass* live = MetaBinding::TryGetKlassFromTypeValue(ctx, typeVal))
        return live;

    if (JS_IsString(typeVal))
    {
        const char* cstr = JS_ToCString(ctx, typeVal);
        if (cstr == nullptr)
            return nullptr;
        std::string fullName = cstr;
        JS_FreeCString(ctx, cstr);
        return MetadataUtil::ResolveTypeByName(fullName.c_str());
    }

    JSValue nameVal = JS_GetPropertyStr(ctx, typeVal, "__zents_type_name");
    if (JS_IsException(nameVal))
        return nullptr;
    if (!JS_IsString(nameVal))
    {
        JS_FreeValue(ctx, nameVal);
        return nullptr;
    }
    const char* cstr = JS_ToCString(ctx, nameVal);
    JS_FreeValue(ctx, nameVal);
    if (cstr == nullptr)
        return nullptr;
    std::string fullName = cstr;
    JS_FreeCString(ctx, cstr);

    return MetadataUtil::ResolveTypeByName(fullName.c_str());
}

static JSValue EnsureAssemblyCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zents: ensure_assembly requires a name");
    std::string name = ReadStringArg(ctx, argv, 0);
    if (name.empty())
        return JS_EXCEPTION;
    if (MetadataUtil::ResolveAssembly(name.c_str()) == nullptr)
        return JS_ThrowReferenceError(ctx, "zents: assembly not found: %s", name.c_str());
    return JS_UNDEFINED;
}

static JSValue ResolveTypeCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: resolve_type requires assembly and type name");
    std::string asmName = ReadStringArg(ctx, argv, 0);
    std::string typeName = ReadStringArg(ctx, argv, 1);
    if (asmName.empty() || typeName.empty())
        return JS_EXCEPTION;

    const Il2CppAssembly* assembly = MetadataUtil::ResolveAssembly(asmName.c_str());
    if (assembly == nullptr)
        return JS_ThrowReferenceError(ctx, "zents: assembly not found: %s", asmName.c_str());

    Il2CppClass* klass = MetadataUtil::ResolveType(assembly, typeName.c_str());
    if (klass == nullptr)
        return JS_ThrowReferenceError(ctx, "zents: type not found: %s", typeName.c_str());

    return TypeRegistry::PushTypeObject(ctx, klass);
}

static TypeBinding* EnsureSystemTypeBinding(JSContext* ctx)
{
    /* Minimal binding: do not BuildBinding(System.Type) ? reflection surface is huge. */
    static TypeBinding* s_systemTypeBinding = nullptr;
    if (s_systemTypeBinding != nullptr)
        return s_systemTypeBinding;
    s_systemTypeBinding = new TypeBinding();
    s_systemTypeBinding->klass = il2cpp_defaults.systemtype_class;
    s_systemTypeBinding->typeObject = JS_UNDEFINED;
    s_systemTypeBinding->typeObjectRaw = JS_UNDEFINED;
    s_systemTypeBinding->instanceProto = JS_UNDEFINED;
    s_systemTypeBinding->hasJsObject = false;
    ObjectRegistry::Initialize(JS_GetRuntime(ctx));
    return s_systemTypeBinding;
}

static JSValue TypeOfCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zents: typeof expects a type object");
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
        return JS_ThrowTypeError(ctx, "zents: typeof expects a type object");

    Il2CppReflectionType* reflectionType = il2cpp::vm::Reflection::GetTypeObject(&klass->byval_arg);
    TypeBinding* binding = EnsureSystemTypeBinding(ctx);
    JSValue handle = ObjectRegistry::PushUnwrapped(ctx, reinterpret_cast<Il2CppObject*>(reflectionType), binding);
    if (JS_IsException(handle))
        return handle;
    std::string fullName = MetadataUtil::BuildTypeFullName(klass);
    JS_SetPropertyStr(ctx, handle, "__zents_type_name", JS_NewString(ctx, fullName.c_str()));
    return handle;
}

static JSValue MakeSzArrayTypeCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zents: make_szarray_type requires element type");
    Il2CppClass* element = KlassFromTypeObject(ctx, argv[0]);
    if (element == nullptr)
        return JS_ThrowTypeError(ctx, "zents: make_szarray_type expects a type object");
    Il2CppClass* arrayKlass = il2cpp::vm::Class::GetArrayClass(element, 1);
    if (arrayKlass == nullptr)
        return JS_ThrowInternalError(ctx, "zents: failed to create szarray type");
    return TypeRegistry::PushTypeObject(ctx, arrayKlass);
}

static JSValue NewSzArrayByElementCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: new_szarray_by_element_type requires type and length");
    Il2CppClass* element = KlassFromTypeObject(ctx, argv[0]);
    if (element == nullptr)
        return JS_ThrowTypeError(ctx, "zents: expected element type");
    int32_t length = 0;
    if (JS_ToInt32(ctx, &length, argv[1]))
        return JS_EXCEPTION;
    if (length < 0)
        return JS_ThrowRangeError(ctx, "ArgumentOutOfRangeException: Non-negative number required.");
    Il2CppArray* arr = il2cpp::vm::Array::New(element, length);
    Il2CppClass* arrayKlass = il2cpp::vm::Class::GetArrayClass(element, 1);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, arrayKlass);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}

static JSValue NewSzArrayByTypeCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: new_szarray_by_szarray_type requires type and length");
    Il2CppClass* arrayKlass = KlassFromTypeObject(ctx, argv[0]);
    if (arrayKlass == nullptr || arrayKlass->rank != 1)
        return JS_ThrowTypeError(ctx, "zents: expected szarray type");
    int32_t length = 0;
    if (JS_ToInt32(ctx, &length, argv[1]))
        return JS_EXCEPTION;
    if (length < 0)
        return JS_ThrowRangeError(ctx, "ArgumentOutOfRangeException: Non-negative number required.");
    Il2CppArray* arr = il2cpp::vm::Array::New(arrayKlass->element_class, length);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, arrayKlass);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}

static JSValue ToArrayCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_UNDEFINED;
    Il2CppObject* obj = ObjectRegistry::Get(ctx, argv[0]);
    if (obj == nullptr || obj->klass->rank == 0)
        return JS_ThrowTypeError(ctx, "zents: to_array expects a CLR szarray");

    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(obj);
    int32_t length = (int32_t)il2cpp::vm::Array::GetLength(arr);
    JSValue jsArr = JS_NewArray(ctx);
    Il2CppClass* elementKlass = obj->klass->element_class;
    const MarshalMetaInfo* elemMeta = MarshalMeta::TryCreateDefault(&elementKlass->byval_arg);
    if (elemMeta == nullptr)
        return JS_ThrowTypeError(ctx, "zents: array element type not supported yet");
    int32_t elementSize = il2cpp::vm::Class::GetArrayElementSize(elementKlass);
    for (int32_t i = 0; i < length; ++i)
    {
        void* slot = il2cpp_array_addr_with_size(arr, elementSize, i);
        JSValue item = elemMeta->cs2jsWriter(ctx, slot, elemMeta);
        if (JS_IsException(item))
            return item;
        JS_SetPropertyUint32(ctx, jsArr, (uint32_t)i, item);
    }
    return jsArr;
}

static JSValue GetTypeFromNameCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zents: get_type_from_name requires a name");
    std::string name = ReadStringArg(ctx, argv, 0);
    if (name.empty())
        return JS_ThrowReferenceError(ctx, "zents: type not found: ");

    Il2CppClass* klass = MetadataUtil::ResolveTypeByName(name.c_str());
    if (klass == nullptr)
        return JS_ThrowReferenceError(ctx, "zents: type not found: %s", name.c_str());
    return TypeRegistry::PushTypeObject(ctx, klass);
}

static JSValue CastCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: cast requires value and type");
    Il2CppClass* klass = KlassFromTypeObject(ctx, argv[1]);
    if (klass == nullptr)
        return JS_ThrowTypeError(ctx, "zents: cast target is not a type");

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(&klass->byval_arg);
    if (meta == nullptr)
        return JS_ThrowTypeError(ctx, "zents: cast type not supported yet");

    void* storage = alloca((size_t)(meta->size > 0 ? meta->size : sizeof(void*)));
    void* tempStorage = nullptr;
    void* dest = meta->passByValue ? &tempStorage : storage;
    meta->js2csWriter(ctx, argv[0], dest, meta);
    if (JS_HasException(ctx))
        return JS_EXCEPTION;
    void* src = meta->passByValue ? &tempStorage : storage;
    return meta->cs2jsWriter(ctx, src, meta);
}

static JSValue BoxCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: box requires type and value");
    Il2CppClass* klass = KlassFromTypeObject(ctx, argv[0]);
    if (klass == nullptr)
        return JS_ThrowTypeError(ctx, "zents: box expects a type object");
    if (!il2cpp::vm::Class::IsValuetype(klass))
        return JS_ThrowTypeError(ctx, "zents: box expects a value type or enum");

    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(&klass->byval_arg);
    if (meta == nullptr)
        return JS_ThrowTypeError(ctx, "zents: box type not supported yet");

    void* storage = alloca((size_t)meta->size);
    meta->js2csWriter(ctx, argv[1], storage, meta);
    if (JS_HasException(ctx))
        return JS_EXCEPTION;

    Il2CppObject* boxed = il2cpp::vm::Object::Box(klass, storage);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, klass);
    return ObjectRegistry::Push(ctx, boxed, binding);
}

static JSValue UnboxCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_UNDEFINED;
    Il2CppObject* obj = ObjectRegistry::Get(ctx, argv[0]);
    if (obj == nullptr)
        return JS_ThrowTypeError(ctx, "zents: unbox expects a CLR object");

    Il2CppClass* klass = obj->klass;
    const MarshalMetaInfo* meta = MarshalMeta::TryCreateDefault(&klass->byval_arg);
    if (meta == nullptr)
        return JS_ThrowTypeError(ctx, "zents: unbox type not supported yet");

    if (il2cpp::vm::Class::IsValuetype(klass))
    {
        void* raw = ObjectUnbox(obj);
        return meta->cs2jsWriter(ctx, raw, meta);
    }

    Il2CppObject* storage = obj;
    return meta->cs2jsWriter(ctx, &storage, meta);
}

static JSValue MakeGenericTypeCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: make_generic_type requires open type and args");
    Il2CppClass* open = KlassFromTypeObject(ctx, argv[0]);
    if (open == nullptr || !open->is_generic)
        return JS_ThrowTypeError(ctx, "zents: make_generic_type expects an open generic type");

    JSValue argsVal = argv[1];
    if (!JS_IsArray(ctx, argsVal))
        return JS_ThrowTypeError(ctx, "zents: make_generic_type args must be an array");

    JSValue lenVal = JS_GetPropertyStr(ctx, argsVal, "length");
    int32_t count = 0;
    if (JS_ToInt32(ctx, &count, lenVal))
    {
        JS_FreeValue(ctx, lenVal);
        return JS_EXCEPTION;
    }
    JS_FreeValue(ctx, lenVal);
    if (count <= 0)
        return JS_ThrowTypeError(ctx, "zents: make_generic_type requires type arguments");

    const Il2CppType** types = (const Il2CppType**)alloca(sizeof(Il2CppType*) * (size_t)count);
    for (int32_t i = 0; i < count; ++i)
    {
        JSValue item = JS_GetPropertyUint32(ctx, argsVal, (uint32_t)i);
        if (JS_IsException(item))
            return item;
        Il2CppClass* argKlass = KlassFromTypeObject(ctx, item);
        JS_FreeValue(ctx, item);
        if (argKlass == nullptr)
            return JS_ThrowTypeError(ctx, "zents: invalid generic type argument");
        types[i] = &argKlass->byval_arg;
    }

    Il2CppClass* inflated = il2cpp::vm::Class::GetInflatedGenericInstanceClass(open, types, (uint32_t)count);
    if (inflated == nullptr)
        return JS_ThrowInternalError(ctx, "zents: failed to inflate generic type");
    return TypeRegistry::PushTypeObject(ctx, inflated);
}

static JSValue ToBytesCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zents: to_bytes expects CLR array");
    Il2CppObject* obj = ObjectRegistry::Get(ctx, argv[0]);
    if (obj == nullptr || obj->klass->rank == 0)
        return JS_ThrowTypeError(ctx, "zents: to_bytes expects CLR array");

    Il2CppClass* element = obj->klass->element_class;
    if (element == il2cpp_defaults.boolean_class || element == il2cpp_defaults.char_class
        || element == il2cpp_defaults.string_class || (!il2cpp::vm::Class::IsValuetype(element) && !element->enumtype))
        return JS_ThrowTypeError(ctx, "zents: to_bytes: element type is not blittable");

    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(obj);
    uint32_t byteLen = il2cpp::vm::Array::GetByteLength(arr);
    char* src = il2cpp::vm::Array::GetFirstElementAddress(arr);
    JSValue jsArr = JS_NewArray(ctx);
    for (uint32_t i = 0; i < byteLen; ++i)
        JS_SetPropertyUint32(ctx, jsArr, i, JS_NewInt32(ctx, (uint8_t)src[i]));
    return jsArr;
}

static JSValue ToDelegateCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: to_delegate requires function and delegate type");
    if (!JS_IsFunction(ctx, argv[0]))
        return JS_ThrowTypeError(ctx, "zents: to_delegate expects a function");
    Il2CppClass* delKlass = KlassFromTypeObject(ctx, argv[1]);
    if (delKlass == nullptr || !MetadataUtil::IsDelegateClass(delKlass))
        return JS_ThrowTypeError(ctx, "zents: to_delegate expects delegate type");

    int funcRef = JsGlobalRefs::Store(ctx, argv[0]);
    Il2CppDelegate* del = DelegateMarshal::CreateFromFuncRef(ctx, delKlass, funcRef);
    if (del == nullptr)
    {
        JsGlobalRefs::FreeAndRelease(ctx, funcRef);
        return JS_ThrowTypeError(ctx, "zents: failed to create delegate");
    }
    return DelegateMarshal::PushToJs(ctx, del, delKlass);
}

static JSValue RegisterMethodCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: register_method requires name and function");
    std::string name = ReadStringArg(ctx, argv, 0);
    if (name.empty())
        return JS_ThrowTypeError(ctx, "zents: register_method requires a non-empty name");

    MethodMarshalCtx* mctx = MetaBinding::TryGetDirectMethodCtx(ctx, argv[1]);
    if (mctx == nullptr || mctx->method == nullptr)
        return JS_ThrowTypeError(ctx, "zents: register_method expects a direct method function (not dispatch)");

    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, mctx->method->klass);
    if (binding->memberKeys.find(name) != binding->memberKeys.end())
        return JS_ThrowTypeError(ctx, "zents: register_method conflict: %s", name.c_str());

    bool isStatic = MetadataUtil::IsStaticMethod(mctx->method);
    JSValue target = isStatic ? binding->typeObjectRaw : binding->instanceProto;
    if (!JS_IsObject(target))
        return JS_ThrowTypeError(ctx, "zents: register_method: type binding incomplete");

    binding->memberKeys.insert(name);
    JS_SetPropertyStr(ctx, target, name.c_str(), JS_DupValue(ctx, argv[1]));
    if (!isStatic && !JS_IsUndefined(binding->byvalInstanceProto) && JS_IsObject(binding->byvalInstanceProto)
        && !JS_StrictEq(ctx, binding->byvalInstanceProto, target))
    {
        JS_SetPropertyStr(ctx, binding->byvalInstanceProto, name.c_str(), JS_DupValue(ctx, argv[1]));
    }
    return JS_UNDEFINED;
}

static std::unordered_map<const MethodInfo*, JSValue> s_closedGenericMethodCache;

static JSValue MakeGenericMethodCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: make_generic_method requires method and type args");

    MethodMarshalCtx* openCtx = MetaBinding::TryGetDirectMethodCtx(ctx, argv[0]);
    if (openCtx == nullptr || openCtx->method == nullptr)
        return JS_ThrowTypeError(ctx, "zents: make_generic_method expects a direct method function (not dispatch)");
    const MethodInfo* openMethod = openCtx->method;
    if (!openMethod->is_generic)
        return JS_ThrowTypeError(ctx, "zents: make_generic_method requires an open generic method definition");

    JSValue argsVal = argv[1];
    if (!JS_IsArray(ctx, argsVal))
        return JS_ThrowTypeError(ctx, "zents: make_generic_method args must be an array");

    JSValue lenVal = JS_GetPropertyStr(ctx, argsVal, "length");
    int32_t count = 0;
    if (JS_ToInt32(ctx, &count, lenVal))
    {
        JS_FreeValue(ctx, lenVal);
        return JS_EXCEPTION;
    }
    JS_FreeValue(ctx, lenVal);
    if (count < 0)
        return JS_ThrowTypeError(ctx, "zents: make_generic_method args length invalid");

    Il2CppMetadataGenericContainerHandle container = openMethod->genericContainerHandle;
    if (container == nullptr && openMethod->methodMetadataHandle != nullptr)
        container = il2cpp::vm::MetadataCache::GetGenericContainerFromMethod(openMethod->methodMetadataHandle);
    if (container == nullptr)
        return JS_ThrowTypeError(ctx, "zents: make_generic_method: missing generic container");
    const uint32_t expectedArgs = il2cpp::vm::MetadataCache::GetGenericContainerCount(container);
    if ((uint32_t)count != expectedArgs)
    {
        return JS_ThrowTypeError(
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
        JSValue item = JS_GetPropertyUint32(ctx, argsVal, (uint32_t)i);
        if (JS_IsException(item))
            return item;
        Il2CppClass* argKlass = KlassFromTypeObject(ctx, item);
        JS_FreeValue(ctx, item);
        if (argKlass == nullptr)
            return JS_ThrowTypeError(ctx, "zents: invalid generic method type argument");
        il2cpp::vm::Class::Init(argKlass);
        types[i] = &argKlass->byval_arg;
    }

    const MethodInfo* closed =
        il2cpp::vm::MetadataCache::GetGenericInstanceMethod(openMethod, types, (uint32_t)count);
    if (closed == nullptr)
        return JS_ThrowTypeError(ctx, "zents: failed to inflate generic method");

    auto it = s_closedGenericMethodCache.find(closed);
    if (it != s_closedGenericMethodCache.end())
        return JS_DupValue(ctx, it->second);

    MethodMarshalCtx* closedCtx = MetaBinding::CreateMethodMarshalCtx(closed);
    if (closedCtx == nullptr)
        return JS_ThrowTypeError(ctx, "zents: closed generic method not marshalable yet");

    JSValue fn = MetadataUtil::IsStaticMethod(closed)
        ? MetaBinding::CreateStaticMethodFunction(ctx, closedCtx)
        : MetaBinding::CreateInstanceMethodFunction(ctx, closedCtx);
    s_closedGenericMethodCache[closed] = JS_DupValue(ctx, fn);
    return fn;
}

static JSValue CreateSignatureCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    JSValue argsVal = JS_UNDEFINED;
    bool freeArgs = false;
    if (argc >= 1 && JS_IsArray(ctx, argv[0]))
    {
        argsVal = argv[0];
    }
    else
    {
        argsVal = JS_NewArray(ctx);
        freeArgs = true;
        for (int i = 0; i < argc; ++i)
            JS_SetPropertyUint32(ctx, argsVal, (uint32_t)i, JS_DupValue(ctx, argv[i]));
    }

    JSValue lenVal = JS_GetPropertyStr(ctx, argsVal, "length");
    int32_t count = 0;
    if (JS_ToInt32(ctx, &count, lenVal))
    {
        JS_FreeValue(ctx, lenVal);
        if (freeArgs)
            JS_FreeValue(ctx, argsVal);
        return JS_EXCEPTION;
    }
    JS_FreeValue(ctx, lenVal);

    std::string sig = "(";
    for (int32_t i = 0; i < count; ++i)
    {
        if (i > 0)
            sig += ',';
        JSValue item = JS_GetPropertyUint32(ctx, argsVal, (uint32_t)i);
        Il2CppClass* klass = KlassFromTypeObject(ctx, item);
        JS_FreeValue(ctx, item);
        if (klass == nullptr)
        {
            if (freeArgs)
                JS_FreeValue(ctx, argsVal);
            return JS_ThrowTypeError(ctx, "zents: create_signature expects type arguments");
        }
        sig += MetadataUtil::BuildTypeFullName(klass);
    }
    sig += ')';
    if (freeArgs)
        JS_FreeValue(ctx, argsVal);
    return JS_NewString(ctx, sig.c_str());
}

static bool ReadIntJsArray(JSContext* ctx, JSValueConst jsArray, std::vector<int32_t>& out, const char* label)
{
    if (!JS_IsArray(ctx, jsArray))
    {
        JS_ThrowTypeError(ctx, "zents: %s must be a JS Array", label);
        return false;
    }
    JSValue lenVal = JS_GetPropertyStr(ctx, jsArray, "length");
    int32_t count = 0;
    if (JS_ToInt32(ctx, &count, lenVal))
    {
        JS_FreeValue(ctx, lenVal);
        return false;
    }
    JS_FreeValue(ctx, lenVal);
    out.resize((size_t)count);
    for (int32_t i = 0; i < count; ++i)
    {
        JSValue item = JS_GetPropertyUint32(ctx, jsArray, (uint32_t)i);
        if (JS_ToInt32(ctx, &out[(size_t)i], item))
        {
            JS_FreeValue(ctx, item);
            return false;
        }
        JS_FreeValue(ctx, item);
    }
    return true;
}

static JSValue MakeMdArrayTypeCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: make_mdarray_type requires element type and rank");
    Il2CppClass* element = KlassFromTypeObject(ctx, argv[0]);
    if (element == nullptr)
        return JS_ThrowTypeError(ctx, "zents: make_mdarray_type expects element type");
    int32_t rank = 0;
    if (JS_ToInt32(ctx, &rank, argv[1]))
        return JS_EXCEPTION;
    if (rank < 1 || rank > 32)
        return JS_ThrowRangeError(ctx, "zents: make_mdarray_type rank must be in [1, 32]");
    Il2CppClass* arrayKlass = il2cpp::vm::Class::GetArrayClass(element, (uint32_t)rank);
    if (arrayKlass == nullptr)
        return JS_ThrowInternalError(ctx, "zents: failed to create mdarray type");
    return TypeRegistry::PushTypeObject(ctx, arrayKlass);
}

static JSValue NewMdArrayBySpecCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 3)
        return JS_ThrowTypeError(ctx, "zents: new_mdarray_by_spec requires element, lowbounds, sizes");
    Il2CppClass* element = KlassFromTypeObject(ctx, argv[0]);
    if (element == nullptr)
        return JS_ThrowTypeError(ctx, "zents: expected element type");
    std::vector<int32_t> lowBounds;
    std::vector<int32_t> sizes;
    if (!ReadIntJsArray(ctx, argv[1], lowBounds, "lowbounds") || !ReadIntJsArray(ctx, argv[2], sizes, "sizes"))
        return JS_EXCEPTION;
    if (lowBounds.size() != sizes.size() || sizes.empty())
        return JS_ThrowTypeError(ctx, "zents: new_mdarray_by_spec: lowbounds/sizes rank mismatch");

    uint32_t rank = (uint32_t)sizes.size();
    Il2CppClass* arrayKlass = il2cpp::vm::Class::GetArrayClass(element, rank);
    auto* lengths = (il2cpp_array_size_t*)alloca(sizeof(il2cpp_array_size_t) * rank);
    auto* lowers = (il2cpp_array_size_t*)alloca(sizeof(il2cpp_array_size_t) * rank);
    for (uint32_t i = 0; i < rank; ++i)
    {
        if (sizes[i] < 0)
            return JS_ThrowRangeError(ctx, "ArgumentOutOfRangeException: Non-negative number required.");
        lengths[i] = (il2cpp_array_size_t)sizes[i];
        lowers[i] = (il2cpp_array_size_t)lowBounds[i];
    }
    Il2CppArray* arr = il2cpp::vm::Array::NewFull(arrayKlass, lengths, lowers);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, arrayKlass);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}

static JSValue NewMdArrayByTypeCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 3)
        return JS_ThrowTypeError(ctx, "zents: new_mdarray_by_mdarray_type requires type, lowbounds, sizes");
    Il2CppClass* arrayKlass = KlassFromTypeObject(ctx, argv[0]);
    if (arrayKlass == nullptr || arrayKlass->rank < 1)
        return JS_ThrowTypeError(ctx, "zents: new_mdarray_by_mdarray_type expects mdarray type");
    std::vector<int32_t> lowBounds;
    std::vector<int32_t> sizes;
    if (!ReadIntJsArray(ctx, argv[1], lowBounds, "lowbounds") || !ReadIntJsArray(ctx, argv[2], sizes, "sizes"))
        return JS_EXCEPTION;
    if (lowBounds.size() != sizes.size() || (int32_t)sizes.size() != arrayKlass->rank)
        return JS_ThrowTypeError(ctx, "zents: new_mdarray_by_mdarray_type: lowbounds/sizes rank mismatch");

    uint32_t rank = (uint32_t)sizes.size();
    auto* lengths = (il2cpp_array_size_t*)alloca(sizeof(il2cpp_array_size_t) * rank);
    auto* lowers = (il2cpp_array_size_t*)alloca(sizeof(il2cpp_array_size_t) * rank);
    for (uint32_t i = 0; i < rank; ++i)
    {
        if (sizes[i] < 0)
            return JS_ThrowRangeError(ctx, "ArgumentOutOfRangeException: Non-negative number required.");
        lengths[i] = (il2cpp_array_size_t)sizes[i];
        lowers[i] = (il2cpp_array_size_t)lowBounds[i];
    }
    Il2CppArray* arr = il2cpp::vm::Array::NewFull(arrayKlass, lengths, lowers);
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, arrayKlass);
    return ObjectRegistry::Push(ctx, reinterpret_cast<Il2CppObject*>(arr), binding);
}

static JSValue GetOpaqueCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zents: get_opaquevalue requires a handle");
    return OpaqueValueMarshal::GetValue(ctx, argv[0]);
}

static JSValue SetOpaqueCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 2)
        return JS_ThrowTypeError(ctx, "zents: set_opaquevalue requires handle and value");
    OpaqueValueMarshal::SetValue(ctx, argv[0], argv[1]);
    if (JS_HasException(ctx))
        return JS_EXCEPTION;
    return JS_UNDEFINED;
}

static JSValue PrintCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    int level = 0;
    if (argc >= 1)
        JS_ToInt32(ctx, &level, argv[0]);
    std::string msg = argc >= 2 ? ReadStringArg(ctx, argv, 1) : std::string();
    const char* prefix = level >= 2 ? "[JS:error] " : (level == 1 ? "[JS:warn] " : "[JS] ");
    // Player: stdout is captured by Unity player log on most platforms.
    std::fprintf(stdout, "%s%s\n", prefix, msg.c_str());
    std::fflush(stdout);
    return JS_UNDEFINED;
}

static JSValue ToUserDataCallback(JSContext* ctx, JSValueConst /*this_val*/, int argc, JSValueConst* argv)
{
    if (argc < 1)
        return JS_ThrowTypeError(ctx, "zents: to_user_data requires an opaque handle");
    return OpaqueValueMarshal::ToUserData(ctx, argv[0]);
}
} // namespace

void JsLib::RegisterGlobals(JSContext* ctx)
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
    BindGlobal(ctx, "__zents_register_method", RegisterMethodCallback, 2);
    BindGlobal(ctx, "__zents_make_generic_method", MakeGenericMethodCallback, 2);
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

    JSValue result = JS_Eval(
        ctx,
        kZentsLibJs,
        std::strlen(kZentsLibJs),
        "zentslib.js",
        JS_EVAL_TYPE_GLOBAL);
    if (JS_IsException(result))
        JsEnv::ThrowPendingException();
    JS_FreeValue(ctx, result);
}

void JsLib::Reset(JSContext* ctx)
{
    for (auto& kv : s_closedGenericMethodCache)
    {
        if (ctx != nullptr)
            JS_FreeValue(ctx, kv.second);
    }
    s_closedGenericMethodCache.clear();
    OpaqueParameterScope::Reset();
}
}
