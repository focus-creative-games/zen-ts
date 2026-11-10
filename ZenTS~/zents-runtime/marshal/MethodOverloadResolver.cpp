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

#include "MethodOverloadResolver.h"
#include "ObjectRegistry.h"
#include "TableMarshal.h"
#include "MarshalMeta.h"
#include "OpaqueValueMarshal.h"
#include "il2cpp-tabledefs.h"

#include "../utils/MetadataUtil.h"

#include "vm/Class.h"
#include "vm/Field.h"
#include "vm/Type.h"

#include <cmath>
#include <cstring>
#include <limits>
#include <string>

namespace zents
{
namespace
{
FieldInfo* FindInstanceFieldByName(Il2CppClass* klass, const char* name)
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

} // namespace

ConversionKind MethodOverloadResolver::GetConversionKind(js::Context* ctx, js::ValueConst value, const MarshalMetaInfo* meta)
{
    if(!meta||!meta->type)return ConversionKind::None;
    Il2CppClass* klass=meta->typeKlass;
    if(meta->marshalAsKind==MarshalAsKind::BigInt){uint64_t bits=0;return MarshalMeta::TryReadBigInt(ctx,value,meta->type,&bits)?ConversionKind::Identity:ConversionKind::None;}
    if(js::IsBigInt(ctx,value))return ConversionKind::None;
    bool nullable=klass&&klass->nullabletype;
    if(js::IsUndefined(value))return nullable?ConversionKind::NullLiteral:ConversionKind::None;
    if(js::IsNull(value))return nullable||meta->passByValue||meta->type->type==IL2CPP_TYPE_PTR?ConversionKind::NullLiteral:ConversionKind::None;
    if(meta->marshalAsKind==MarshalAsKind::Table)return js::IsObject(value)&&!js::IsArray(ctx,value)&&!js::IsFunction(ctx,value)&&!ObjectRegistry::IsZentsObject(ctx,value)?ConversionKind::Identity:ConversionKind::None;
    if(nullable){auto* element=il2cpp::vm::Class::GetNullableArgument(klass);return GetConversionKind(ctx,value,MarshalMeta::TryCreateDefault(&element->byval_arg));}
    if(Il2CppObject* obj=ObjectRegistry::Get(ctx,value))
    {
        if(klass==obj->klass)return ConversionKind::Identity;
        if(klass&&il2cpp::vm::Class::IsAssignableFrom(klass,obj->klass))return ConversionKind::ImplicitReference;
        return ConversionKind::None;
    }
    if(meta->marshalAsKind==MarshalAsKind::Bytes){size_t n=0;return js::GetByteArrayData(ctx,value,&n)?ConversionKind::Identity:ConversionKind::None;}
    const Il2CppType* type=meta->type;
    bool isEnum=klass&&klass->enumtype;
    if(isEnum)type=il2cpp::vm::Class::GetEnumBaseType(klass);
    if(type->type==IL2CPP_TYPE_BOOLEAN)return js::IsBool(value)?ConversionKind::Identity:ConversionKind::None;
    if(type->type==IL2CPP_TYPE_STRING)return meta->marshalAsKind!=MarshalAsKind::Object&&js::IsString(value)?ConversionKind::Identity:ConversionKind::None;
    if(type->type==IL2CPP_TYPE_SZARRAY)return js::IsArray(ctx,value)?ConversionKind::Identity:ConversionKind::None;
    if(type->type==IL2CPP_TYPE_PTR)
    {
        js::Value flag=js::GetPropertyStr(ctx,value,"__zents_pointer");bool ok=js::IsBool(flag)&&js::ToBool(ctx,flag);js::FreeValue(ctx,flag);return ok?ConversionKind::Identity:ConversionKind::None;
    }
    if(klass==il2cpp_defaults.object_class && (js::IsNumber(value)||js::IsBool(value)||js::IsString(value)))return js::IsString(value)?ConversionKind::ImplicitReference:ConversionKind::ImplicitBoxing;
    if(MetadataUtil::IsDelegateClass(klass)&&js::IsFunction(ctx,value))return ConversionKind::Identity;
    if(!js::IsNumber(value))return ConversionKind::None;
    double d=0;if(js::ToFloat64(ctx,&d,value))return ConversionKind::None;
    bool integral=std::isfinite(d)&&std::floor(d)==d;
    if(type->type==IL2CPP_TYPE_R4)return !std::isfinite(d)||std::abs(d)<=std::numeric_limits<float>::max()?(integral?ConversionKind::ImplicitNumeric:ConversionKind::Identity):ConversionKind::None;
    if(type->type==IL2CPP_TYPE_R8)return integral?ConversionKind::ImplicitNumeric:ConversionKind::Identity;
    if(!integral)return ConversionKind::None;
    bool valid=false,extended=false;
    switch(type->type)
    {
    case IL2CPP_TYPE_I1:valid=d>=INT8_MIN&&d<=INT8_MAX;break;
    case IL2CPP_TYPE_U1:valid=d>=0&&d<=UINT8_MAX;break;
    case IL2CPP_TYPE_I2:valid=d>=INT16_MIN&&d<=INT16_MAX;break;
    case IL2CPP_TYPE_CHAR:case IL2CPP_TYPE_U2:valid=d>=0&&d<=UINT16_MAX;break;
    case IL2CPP_TYPE_I4:valid=d>=INT32_MIN&&d<=INT32_MAX;break;
    case IL2CPP_TYPE_U4:valid=d>=0&&d<=UINT32_MAX;break;
    case IL2CPP_TYPE_I:case IL2CPP_TYPE_I8:valid=d>=-9223372036854775808.0&&d<9223372036854775808.0;extended=true;break;
    case IL2CPP_TYPE_U:case IL2CPP_TYPE_U8:valid=d>=0&&d<18446744073709551616.0;extended=true;break;
    default:break;
    }
    return !valid?ConversionKind::None:isEnum?ConversionKind::ImplicitEnumeration:extended?ConversionKind::ImplicitNumeric:ConversionKind::Identity;
}
namespace {
struct Match { MethodMarshalCtx* method; std::vector<int> kinds; int optional=0; int expanded=0; };
bool MatchArgs(js::Context* ctx,MethodMarshalCtx* mc,int argc,js::ValueConst* argv,Match& match)
{
    if(!mc||mc->method->is_generic)return false;
    match.method=mc;match.kinds.resize(argc);int slot=0;
    for(uint8_t i=mc->isExtension?1:0;i<mc->method->parameters_count;++i)
    {
        const Il2CppType* type=mc->method->parameters[i];const MarshalMetaInfo* meta=mc->paramsMeta[i];
        if((type->attrs&PARAM_ATTRIBUTE_OUT)&&!(type->attrs&PARAM_ATTRIBUTE_IN))continue;
        if(mc->hasParamsArray&&i==mc->method->parameters_count-1)
        {
            int available=argc-slot;if(!available){match.optional++;return true;}
            Il2CppObject* obj=available==1?ObjectRegistry::Get(ctx,argv[slot]):nullptr;
            if(obj&&meta->typeKlass&&il2cpp::vm::Class::IsAssignableFrom(meta->typeKlass,obj->klass)){match.kinds[slot]=0;return true;}
            match.expanded=1;
            while(slot<argc){auto kind=MethodOverloadResolver::GetConversionKind(ctx,argv[slot],mc->paramsElementMeta);if(kind==ConversionKind::None)return false;match.kinds[slot++]=(int)kind;}
            return true;
        }
        int need=TableMarshal::GetJsArgSlotCount(meta);
        bool optional=(type->attrs&(PARAM_ATTRIBUTE_OPTIONAL|PARAM_ATTRIBUTE_HAS_DEFAULT))!=0;
        if(optional&&(slot==argc||(need==1&&js::IsUndefined(argv[slot])))){match.optional++;if(slot<argc)slot++;continue;}
        if(argc-slot<need)return false;
        if(meta->marshalAsKind==MarshalAsKind::UnpackedValues)
        {
            auto* table=static_cast<const TableMarshalMeta*>(meta);
            for(int j=0;j<need;++j){auto* fm=table->fieldMetas[j];if(!fm)return false;auto kind=MethodOverloadResolver::GetConversionKind(ctx,argv[slot],fm);if(kind==ConversionKind::None)return false;match.kinds[slot++]=(int)kind;}
        }
        else
        {
            void* opaque=nullptr;
            if(type->byref&&OpaqueValueMarshal::TryGetValueAddress(ctx,argv[slot],&opaque,type)&&opaque){match.kinds[slot++]=0;continue;}
            if(type->byref){Il2CppObject* holder=ObjectRegistry::Get(ctx,argv[slot]);if(holder&&holder->klass==meta->typeKlass){match.kinds[slot++]=0;continue;}}
            auto kind=MethodOverloadResolver::GetConversionKind(ctx,argv[slot],meta);if(kind==ConversionKind::None)return false;match.kinds[slot++]=(int)kind;
        }
    }
    return slot==argc;
}
bool Better(const Match& a,const Match& b)
{
    bool better=false,worse=false;
    for(size_t i=0;i<a.kinds.size();++i){if(a.kinds[i]<b.kinds[i])better=true;else if(a.kinds[i]>b.kinds[i])worse=true;}
    if(worse)return false;if(better)return true;
    if(a.optional!=b.optional)return a.optional<b.optional;
    if(a.expanded!=b.expanded)return a.expanded<b.expanded;
    return !a.method->hasParamsArray&&b.method->hasParamsArray;
}
}
bool MethodOverloadResolver::IsApplicable(js::Context* ctx,const MethodMarshalCtx* method,int argc,js::ValueConst* argv)
{Match match;return MatchArgs(ctx,const_cast<MethodMarshalCtx*>(method),argc,argv,match);}
MethodMarshalCtx* MethodOverloadResolver::Resolve(js::Context* ctx,OverloadGroup* group,int argc,js::ValueConst* argv)
{
    if(!group)return nullptr;std::vector<Match> matches;
    for(auto* mc:group->candidates){Match m;if(MatchArgs(ctx,mc,argc,argv,m))matches.push_back(std::move(m));}
    MethodMarshalCtx* winner=nullptr;
    for(const auto& a:matches){bool beaten=false;for(const auto& b:matches)if(a.method!=b.method&&Better(b,a)){beaten=true;break;}if(!beaten){if(winner){js::ThrowTypeError(ctx,"zents: ambiguous overload");return nullptr;}winner=a.method;}}
    return winner;
}

std::string MethodOverloadResolver::BuildSignatureKey(const MethodInfo* method)
{
    std::string key = method->name; key += "(";
    uint8_t start = MetadataUtil::IsExtensionMethod(method) ? 1 : 0;
    for (uint8_t i = start; i < method->parameters_count; ++i)
    {
        if (i > start) key += ",";
        key += il2cpp::vm::Type::GetName(method->parameters[i], IL2CPP_TYPE_NAME_FORMAT_FULL_NAME);
    }
    return key + ")";
}
}
