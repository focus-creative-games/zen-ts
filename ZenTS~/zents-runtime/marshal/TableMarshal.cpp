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
#include "gc/GarbageCollector.h"
#include "MarshalMeta.h"
#include "MethodOverloadResolver.h"
#include "../utils/MetadataUtil.h"
#include "vm/Runtime.h"
#include "vm/Exception.h"

#include "vm/Class.h"
#include "vm/Field.h"
#include "vm/Object.h"

#include <cstring>

namespace zents
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

const PropertyInfo* FindInstanceProperty(Il2CppClass* klass,const char* name)
{
    for(Il2CppClass* walk=klass;walk;walk=walk->parent)
    {
        void* it=nullptr;
        while(const PropertyInfo* p=il2cpp::vm::Class::GetProperties(walk,&it))
            if(std::strcmp(p->name,name)==0 && p->get && MetadataUtil::IsPublicMethod(p->get) && !MetadataUtil::IsStaticMethod(p->get) && p->get->parameters_count==0)return p;
    }
    return nullptr;
}
void WriteMember(js::Context* ctx,js::ValueConst prop,void* address,const TableMarshalMeta* tm,size_t i)
{
    FieldInfo* field=tm->fields[i];auto* property=tm->properties[i];auto* meta=tm->fieldMetas[i];
    if(!meta||(!field&&!property)){js::ThrowTypeError(ctx,"zents: unknown Composite member '%s'",tm->members[i].c_str());return;}
    if(MethodOverloadResolver::GetConversionKind(ctx,prop,meta)==ConversionKind::None){js::ThrowTypeError(ctx,"zents: invalid Composite member '%s'",tm->members[i].c_str());return;}
    void* storage=alloca((size_t)meta->size);meta->js2csWriter(ctx,prop,storage,meta);if(js::HasException(ctx))return;
    if(field)
    {
        void* slot=FieldAddr(address,field);std::memcpy(slot,storage,(size_t)meta->size);
        if(field->parent->has_references)il2cpp::gc::GarbageCollector::SetWriteBarrier(reinterpret_cast<void**>(slot),(size_t)meta->size);
    }
    else
    {
        if(!property->set||!MetadataUtil::IsPublicMethod(property->set)){js::ThrowTypeError(ctx,"zents: Composite property is read-only");return;}
        void* args[]={meta->passByValue?*reinterpret_cast<void**>(storage):storage};Il2CppException* exception=nullptr;
        il2cpp::vm::Runtime::Invoke(property->set,address,args,&exception);if(exception)il2cpp::vm::Exception::Raise(exception);
    }
}
js::Value ReadMember(js::Context* ctx,void* address,const TableMarshalMeta* tm,size_t i)
{
    auto* meta=tm->fieldMetas[i];if(!meta)return js::ThrowTypeError(ctx,"zents: unknown Composite member");
    if(tm->fields[i])return meta->cs2jsWriter(ctx,FieldAddr(address,tm->fields[i]),meta);
    auto* property=tm->properties[i];if(!property||!property->get)return js::ThrowTypeError(ctx,"zents: Composite property has no getter");
    Il2CppException* exception=nullptr;Il2CppObject* value=il2cpp::vm::Runtime::Invoke(property->get,address,nullptr,&exception);
    if(exception)il2cpp::vm::Exception::Raise(exception);
    if(meta->passByValue)return meta->cs2jsWriter(ctx,&value,meta);
    return meta->cs2jsWriter(ctx,ObjectUnbox(value),meta);
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

        FieldInfo* field=FindInstanceField(fieldOwner,name.c_str());
        const PropertyInfo* property=field?nullptr:FindInstanceProperty(fieldOwner,name.c_str());
        tm->fields.push_back(field);tm->properties.push_back(property);
        int32_t strategy=0;std::vector<std::string> nested;
        const Il2CppType* memberType=field?field->type:property?property->get->return_type:nullptr;
        if(field)MetadataUtil::TryReadFieldMarshalAs(field,&strategy,&nested);
        else if(property)MetadataUtil::TryReadJsMarshalAs(property->get,-1,&strategy,&nested);
        const MarshalMetaInfo* memberMeta=memberType?MarshalMeta::WithStrategy(memberType,strategy):nullptr;
        if(memberType&&(strategy==4||strategy==5))memberMeta=Create(memberType,il2cpp::vm::Class::FromIl2CppType(memberType),nested,(MarshalAsKind)strategy);
        tm->fieldMetas.push_back(memberMeta);

    }

    if (kind == MarshalAsKind::UnpackedValues)
    {
        tm->js2csWriter = Js2CsPacked; /* single JS result; arguments use Js2CsUnpacked */
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

void TableMarshal::Js2CsTable(js::Context* ctx, js::ValueConst value, void* address, const MarshalMetaInfo* meta)
{
    auto* tm = static_cast<const TableMarshalMeta*>(meta);
    Il2CppClass* storageKlass = meta->typeKlass;
    std::memset(address, 0, (size_t)meta->size);

    if (js::IsNull(value) || js::IsUndefined(value))
    {
        if(storageKlass && storageKlass->nullabletype)return;
        js::ThrowTypeError(ctx, "zents: non-nullable Table requires an object");
        return;
    }
    if (!js::IsObject(value))
    {
        js::ThrowTypeError(ctx, "zents: Table marshal expects a plain object");
        return;
    }

    Il2CppClass* memberKlass = nullptr;
    void* memberAddr = nullptr;
    ResolveMemberStorage(storageKlass, address, &memberKlass, &memberAddr);

    for (size_t i = 0; i < tm->members.size(); ++i)
    {
        const char* name = tm->members[i].c_str();
        int own=js::HasOwnProperty(ctx,value,name);
        if(own!=1){if(own==0&&tm->optional[i])continue;js::ThrowTypeError(ctx,"zents: Table missing member '%s'",name);return;}
        js::Value prop=js::GetPropertyStr(ctx,value,name);

        WriteMember(ctx,prop,memberAddr,tm,i);
        js::FreeValue(ctx, prop);
        if (js::HasException(ctx))
            return;
    }

    if (storageKlass != nullptr && storageKlass->nullabletype)
        *reinterpret_cast<uint8_t*>(address) = 1;
}

js::Value TableMarshal::Cs2JsTable(js::Context* ctx, void* address, const MarshalMetaInfo* meta)
{
    auto* tm = static_cast<const TableMarshalMeta*>(meta);
    Il2CppClass* storageKlass = meta->typeKlass;
    if (storageKlass != nullptr && storageKlass->nullabletype
        && !il2cpp::vm::Object::NullableHasValue(storageKlass, address))
        return js::Null();

    Il2CppClass* memberKlass = nullptr;
    void* memberAddr = nullptr;
    ResolveMemberStorage(storageKlass, address, &memberKlass, &memberAddr);

    js::Value obj = js::NewObject(ctx);
    for (size_t i = 0; i < tm->members.size(); ++i)
    {
        const char* name = tm->members[i].c_str();
        js::Value prop = ReadMember(ctx,memberAddr,tm,i);
        if (js::IsException(prop))
            return prop;
        if (js::IsUndefined(prop))
            continue;
        js::SetPropertyStr(ctx, obj, name, prop);
    }
    return obj;
}

void TableMarshal::Js2CsUnpacked(
    js::Context* ctx, js::ValueConst* argv, int jsStart, int argc, void* address, const MarshalMetaInfo* meta)
{
    auto* tm = static_cast<const TableMarshalMeta*>(meta);
    Il2CppClass* storageKlass = meta->typeKlass;
    std::memset(address, 0, (size_t)meta->size);

    const int slots = (int)tm->members.size();
    if (jsStart + slots > argc)
    {
        js::ThrowTypeError(ctx,"zents: incomplete UnpackedValues argument");
        return;
    }

    Il2CppClass* memberKlass = nullptr;
    void* memberAddr = nullptr;
    ResolveMemberStorage(storageKlass, address, &memberKlass, &memberAddr);

    for (int i = 0; i < slots; ++i)
    {
        const char* name = tm->members[(size_t)i].c_str();
        WriteMember(ctx,argv[jsStart+i],memberAddr,tm,(size_t)i);
        if (js::HasException(ctx))
            return;
    }

    if (storageKlass != nullptr && storageKlass->nullabletype)
        *reinterpret_cast<uint8_t*>(address) = 1;
}

js::Value TableMarshal::Cs2JsUnpacked(js::Context* ctx, void* address, const MarshalMetaInfo* meta)
{
    auto* tm = static_cast<const TableMarshalMeta*>(meta);
    Il2CppClass* storageKlass = meta->typeKlass;
    if (storageKlass != nullptr && storageKlass->nullabletype
        && !il2cpp::vm::Object::NullableHasValue(storageKlass, address))
        return js::Null();

    Il2CppClass* memberKlass = nullptr;
    void* memberAddr = nullptr;
    ResolveMemberStorage(storageKlass, address, &memberKlass, &memberAddr);

    js::Value arr = js::NewArray(ctx);
    for (size_t i = 0; i < tm->members.size(); ++i)
    {
        js::Value prop = ReadMember(ctx,memberAddr,tm,i);
        if (js::IsException(prop))
            return prop;
        js::SetPropertyUint32(ctx, arr, (uint32_t)i, prop);
    }
    return arr;
}
}

namespace zents {
void TableMarshal::Js2CsPacked(js::Context* ctx,js::ValueConst value,void* address,const MarshalMetaInfo* meta)
{
    auto* tm=static_cast<const TableMarshalMeta*>(meta);
    if(!js::IsArray(ctx,value)){js::ThrowTypeError(ctx,"zents: UnpackedValues result requires an Array");return;}
    js::Value length=js::GetPropertyStr(ctx,value,"length");int32_t count=0;js::ToInt32(ctx,&count,length);js::FreeValue(ctx,length);
    if(count!=(int32_t)tm->members.size()){js::ThrowTypeError(ctx,"zents: UnpackedValues result length mismatch");return;}
    std::vector<js::Value> args(count);
    for(int32_t i=0;i<count;++i)args[i]=js::GetPropertyUint32(ctx,value,(uint32_t)i);
    Js2CsUnpacked(ctx,args.data(),0,count,address,meta);
    for(auto arg:args)js::FreeValue(ctx,arg);
}
}
