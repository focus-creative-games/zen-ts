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

#include "TableMarshal.h"
#include "MarshalMeta.h"

#include "vm/Class.h"
#include "vm/Field.h"
#include "vm/Object.h"

#include <cstring>

namespace zts
{
namespace
{
void ParseSpec(const std::string& spec, std::string& name, bool& optional)
{
    optional = false;
    name = spec;
    if (!name.empty() && name.back() == '?')
    {
        optional = true;
        name.pop_back();
    }
}

FieldInfo* FindInstanceField(Il2CppClass* klass, const char* name)
{
    for (Il2CppClass* walk = klass; walk != nullptr; walk = walk->parent)
    {
        if (walk == il2cpp_defaults.object_class)
            break;
        il2cpp::vm::Class::SetupFields(walk);
        for (uint16_t i = 0; i < walk->field_count; ++i)
        {
            FieldInfo* f = &walk->fields[i];
            if (il2cpp::vm::Field::IsInstance(f) && std::strcmp(f->name, name) == 0)
                return f;
        }
    }
    return nullptr;
}

void* FieldAddr(void* structAddr, FieldInfo* field)
{
    return reinterpret_cast<uint8_t*>(structAddr) + field->offset - sizeof(Il2CppObject);
}

void* NullableValueAddr(Il2CppClass* nullableKlass, void* dataAddr)
{
    il2cpp::vm::Class::SetupFields(nullableKlass);
    return reinterpret_cast<uint8_t*>(dataAddr) + nullableKlass->fields[1].offset - sizeof(Il2CppObject);
}

void ResolveMemberStorage(Il2CppClass* storageKlass, void* address, Il2CppClass** outMemberKlass, void** outMemberAddr)
{
    if (storageKlass != nullptr && storageKlass->nullabletype)
    {
        *outMemberKlass = il2cpp::vm::Class::GetNullableArgument(storageKlass);
        *outMemberAddr = NullableValueAddr(storageKlass, address);
        return;
    }
    *outMemberKlass = storageKlass;
    *outMemberAddr = address;
}

void WriteFieldFromJs(
    JSContext* ctx,
    JSValueConst prop,
    void* structAddr,
    FieldInfo* field,
    const MarshalMetaInfo* fieldMeta,
    const char* name)
{
    if (field == nullptr || fieldMeta == nullptr)
    {
        JS_ThrowTypeError(ctx, "zts: Table/Unpacked unknown member '%s'", name);
        return;
    }

    void* slot = FieldAddr(structAddr, field);
    void* tempStorage = nullptr;
    void* dest = fieldMeta->passByValue ? &tempStorage : slot;
    fieldMeta->js2csWriter(ctx, prop, dest, fieldMeta);
    if (JS_HasException(ctx))
        return;
    if (fieldMeta->passByValue)
        *reinterpret_cast<void**>(slot) = tempStorage;
}

JSValue ReadFieldToJs(
    JSContext* ctx, void* structAddr, FieldInfo* field, const MarshalMetaInfo* fieldMeta)
{
    if (field == nullptr || fieldMeta == nullptr)
        return JS_UNDEFINED;
    void* slot = FieldAddr(structAddr, field);
    return fieldMeta->cs2jsWriter(ctx, slot, fieldMeta);
}
} // namespace

const MarshalMetaInfo* TableMarshal::Create(
    const Il2CppType* type,
    Il2CppClass* klass,
    const std::vector<std::string>& memberSpecs,
    MarshalAsKind kind)
{
    auto* tm = new TableMarshalMeta();
    tm->type = type;
    tm->typeKlass = klass;
    tm->size = (int32_t)klass->instance_size - (int32_t)sizeof(Il2CppObject);
    tm->passByValue = false;
    tm->marshalAsKind = kind;

    Il2CppClass* fieldOwner = klass;
    if (klass != nullptr && klass->nullabletype)
        fieldOwner = il2cpp::vm::Class::GetNullableArgument(klass);

    for (const std::string& spec : memberSpecs)
    {
        std::string name;
        bool opt = false;
        ParseSpec(spec, name, opt);
        if (kind == MarshalAsKind::UnpackedValues && opt)
        {
            /* Mirror Mono: UnpackedValues rejects optional '?' specs. */
            delete tm;
            return nullptr;
        }
        tm->members.push_back(name);
        tm->optional.push_back(opt);

        FieldInfo* field = FindInstanceField(fieldOwner, name.c_str());
        tm->fields.push_back(field);
        const MarshalMetaInfo* fieldMeta =
            field != nullptr ? MarshalMeta::TryCreateDefault(field->type) : nullptr;
        tm->fieldMetas.push_back(fieldMeta);
    }

    if (kind == MarshalAsKind::UnpackedValues)
    {
        tm->js2csWriter = nullptr; /* multi-arg path via Js2CsUnpacked */
        tm->cs2jsWriter = Cs2JsUnpacked;
        tm->jsArgSlots = (int32_t)tm->members.size();
    }
    else
    {
        tm->js2csWriter = Js2CsTable;
        tm->cs2jsWriter = Cs2JsTable;
        tm->jsArgSlots = 1;
    }
    return tm;
}

int TableMarshal::GetJsArgSlotCount(const MarshalMetaInfo* meta)
{
    if (meta == nullptr)
        return 1;
    return meta->jsArgSlots > 0 ? meta->jsArgSlots : 1;
}

void TableMarshal::Js2CsTable(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta)
{
    auto* tm = static_cast<const TableMarshalMeta*>(meta);
    Il2CppClass* storageKlass = meta->typeKlass;
    std::memset(address, 0, (size_t)meta->size);

    if (JS_IsNull(value) || JS_IsUndefined(value))
    {
        /* Mono v1 quirk retained by matrix: null Table → NRE-like message. */
        JS_ThrowTypeError(ctx, "Object reference not set to an instance of an object.");
        return;
    }
    if (!JS_IsObject(value))
    {
        JS_ThrowTypeError(ctx, "zts: Table marshal expects a plain object");
        return;
    }

    Il2CppClass* memberKlass = nullptr;
    void* memberAddr = nullptr;
    ResolveMemberStorage(storageKlass, address, &memberKlass, &memberAddr);

    for (size_t i = 0; i < tm->members.size(); ++i)
    {
        const char* name = tm->members[i].c_str();
        JSValue prop = JS_GetPropertyStr(ctx, value, name);
        if (JS_IsUndefined(prop))
        {
            JS_FreeValue(ctx, prop);
            if (tm->optional[i])
                continue;
            JS_ThrowTypeError(ctx, "zts: Table missing required member '%s'", name);
            return;
        }

        WriteFieldFromJs(ctx, prop, memberAddr, tm->fields[i], tm->fieldMetas[i], name);
        JS_FreeValue(ctx, prop);
        if (JS_HasException(ctx))
            return;
    }

    if (storageKlass != nullptr && storageKlass->nullabletype)
        *reinterpret_cast<uint8_t*>(address) = 1;
}

JSValue TableMarshal::Cs2JsTable(JSContext* ctx, void* address, const MarshalMetaInfo* meta)
{
    auto* tm = static_cast<const TableMarshalMeta*>(meta);
    Il2CppClass* storageKlass = meta->typeKlass;
    if (storageKlass != nullptr && storageKlass->nullabletype
        && !il2cpp::vm::Object::NullableHasValue(storageKlass, address))
        return JS_NULL;

    Il2CppClass* memberKlass = nullptr;
    void* memberAddr = nullptr;
    ResolveMemberStorage(storageKlass, address, &memberKlass, &memberAddr);

    JSValue obj = JS_NewObject(ctx);
    for (size_t i = 0; i < tm->members.size(); ++i)
    {
        const char* name = tm->members[i].c_str();
        JSValue prop = ReadFieldToJs(ctx, memberAddr, tm->fields[i], tm->fieldMetas[i]);
        if (JS_IsException(prop))
            return prop;
        if (JS_IsUndefined(prop))
            continue;
        JS_SetPropertyStr(ctx, obj, name, prop);
    }
    return obj;
}

void TableMarshal::Js2CsUnpacked(
    JSContext* ctx, JSValueConst* argv, int jsStart, int argc, void* address, const MarshalMetaInfo* meta)
{
    auto* tm = static_cast<const TableMarshalMeta*>(meta);
    Il2CppClass* storageKlass = meta->typeKlass;
    std::memset(address, 0, (size_t)meta->size);

    const int slots = (int)tm->members.size();
    if (jsStart + slots > argc)
    {
        /* Soft-fill with zeroed struct (Mono PopArgs defaulting behavior). */
        return;
    }

    Il2CppClass* memberKlass = nullptr;
    void* memberAddr = nullptr;
    ResolveMemberStorage(storageKlass, address, &memberKlass, &memberAddr);

    for (int i = 0; i < slots; ++i)
    {
        const char* name = tm->members[(size_t)i].c_str();
        WriteFieldFromJs(
            ctx, argv[jsStart + i], memberAddr, tm->fields[(size_t)i], tm->fieldMetas[(size_t)i], name);
        if (JS_HasException(ctx))
            return;
    }

    if (storageKlass != nullptr && storageKlass->nullabletype)
        *reinterpret_cast<uint8_t*>(address) = 1;
}

JSValue TableMarshal::Cs2JsUnpacked(JSContext* ctx, void* address, const MarshalMetaInfo* meta)
{
    auto* tm = static_cast<const TableMarshalMeta*>(meta);
    Il2CppClass* storageKlass = meta->typeKlass;
    if (storageKlass != nullptr && storageKlass->nullabletype
        && !il2cpp::vm::Object::NullableHasValue(storageKlass, address))
        return JS_NULL;

    Il2CppClass* memberKlass = nullptr;
    void* memberAddr = nullptr;
    ResolveMemberStorage(storageKlass, address, &memberKlass, &memberAddr);

    JSValue arr = JS_NewArray(ctx);
    for (size_t i = 0; i < tm->members.size(); ++i)
    {
        JSValue prop = ReadFieldToJs(ctx, memberAddr, tm->fields[i], tm->fieldMetas[i]);
        if (JS_IsException(prop))
            return prop;
        JS_SetPropertyUint32(ctx, arr, (uint32_t)i, prop);
    }
    return arr;
}
}
