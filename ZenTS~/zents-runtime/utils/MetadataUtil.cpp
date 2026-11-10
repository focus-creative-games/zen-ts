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

#include "MetadataUtil.h"
#include "JsException.h"

#include "../marshal/MarshalAsXmlTable.h"

#include "vm/Assembly.h"
#include "metadata/GenericMetadata.h"
#include "vm/Class.h"
#include "vm/Method.h"
#include "vm/Array.h"
#include "vm/MetadataCache.h"
#include "vm/Reflection.h"
#include "vm/Runtime.h"
#include "vm/Type.h"
#include "utils/StringUtils.h"

#include <cstring>
#include <string>
#include <unordered_set>
#include <vector>

namespace zents
{
static Il2CppClass* s_jsMethodClass = nullptr;
static Il2CppClass* s_jsScriptExceptionClass = nullptr;
static Il2CppClass* s_jsAliasAttributeClass = nullptr;
static Il2CppClass* s_jsExtensionAttributeClass = nullptr;
static Il2CppClass* s_jsMarshalAsAttributeClass = nullptr;
static Il2CppClass* s_extensionAttributeClass = nullptr;
static Il2CppClass* s_paramArrayAttributeClass = nullptr;

void MetadataUtil::Initialize()
{
    const Il2CppAssembly* common = ResolveAssembly("ZenTS.Common");
    if (common != nullptr)
    {
        s_jsScriptExceptionClass = il2cpp::vm::Class::FromName(common->image, "ZenTS", "JsScriptException");
        s_jsAliasAttributeClass = il2cpp::vm::Class::FromName(common->image, "ZenTS", "JsAliasAttribute");
        s_jsExtensionAttributeClass = il2cpp::vm::Class::FromName(common->image, "ZenTS", "JsExtensionAttribute");
        s_jsMarshalAsAttributeClass = il2cpp::vm::Class::FromName(common->image, "ZenTS", "JsMarshalAsAttribute");
    }

    const Il2CppAssembly* mscorlib = ResolveAssembly("mscorlib");
    if (mscorlib == nullptr)
        mscorlib = ResolveAssembly("System.Runtime");
    if (mscorlib != nullptr)
    {
        s_extensionAttributeClass = il2cpp::vm::Class::FromName(
            mscorlib->image, "System.Runtime.CompilerServices", "ExtensionAttribute");
        s_paramArrayAttributeClass = il2cpp::vm::Class::FromName(mscorlib->image, "System", "ParamArrayAttribute");
    }

    const Il2CppAssembly* il2cppAsm = ResolveAssembly("ZenTS.Il2Cpp");
    if (il2cppAsm != nullptr)
    {
        s_jsMethodClass = il2cpp::vm::Class::FromName(il2cppAsm->image, "ZenTS", "JsMethod");
        if (s_jsMethodClass != nullptr)
            il2cpp::vm::Class::Init(s_jsMethodClass);
    }
}

const Il2CppAssembly* MetadataUtil::ResolveAssembly(const char* name)
{
    return il2cpp::vm::Assembly::GetLoadedAssembly(name);
}

Il2CppClass* MetadataUtil::ResolveType(const Il2CppAssembly* assembly, const char* typeName)
{
    IL2CPP_ASSERT(assembly != nullptr);
    IL2CPP_ASSERT(typeName != nullptr);

    const Il2CppImage* image = assembly->image;
    IL2CPP_ASSERT(image != nullptr);

    const char* nestSep = strrchr(typeName, '+');
    if (nestSep == nullptr)
    {
        const char* dot = strrchr(typeName, '.');
        if (dot != nullptr)
        {
            std::string ns(typeName, (size_t)(dot - typeName));
            return il2cpp::vm::Class::FromName(image, ns.c_str(), dot + 1);
        }
        return il2cpp::vm::Class::FromName(image, "", typeName);
    }

    std::string parentName(typeName, (size_t)(nestSep - typeName));
    Il2CppClass* parent = ResolveType(assembly, parentName.c_str());
    if (parent == nullptr)
        return nullptr;
    il2cpp::vm::Class::Init(parent);

    void* iter = nullptr;
    while (Il2CppClass* nestedType = il2cpp::vm::Class::GetNestedTypes(parent, &iter))
    {
        if (strcmp(nestedType->name, nestSep + 1) == 0)
            return nestedType;
    }
    return nullptr;
}

Il2CppClass* MetadataUtil::ResolveTypeByName(const char* typeName)
{
    if (typeName == nullptr || typeName[0] == '\0')
        return nullptr;

    std::string name(typeName);

    /* Simple / open-generic / AQN names (no '[' → not closed generic or array). */
    if (name.find('[') == std::string::npos)
    {
        size_t comma = name.find(',');
        std::string typeOnly = comma == std::string::npos ? name : name.substr(0, comma);
        while (!typeOnly.empty() && (typeOnly.back() == ' ' || typeOnly.back() == '\t'))
            typeOnly.pop_back();

        il2cpp::vm::AssemblyVector* assemblies = il2cpp::vm::Assembly::GetAllAssemblies();
        if (assemblies == nullptr)
            return nullptr;

        if (comma != std::string::npos)
        {
            std::string asmName = name.substr(comma + 1);
            while (!asmName.empty() && (asmName.front() == ' ' || asmName.front() == '\t'))
                asmName.erase(asmName.begin());
            size_t asmComma = asmName.find(',');
            if (asmComma != std::string::npos)
                asmName = asmName.substr(0, asmComma);
            const Il2CppAssembly* assembly = ResolveAssembly(asmName.c_str());
            if (assembly != nullptr)
            {
                Il2CppClass* klass = ResolveType(assembly, typeOnly.c_str());
                if (klass != nullptr)
                    return klass;
            }
        }

        for (const Il2CppAssembly* assembly : *assemblies)
        {
            Il2CppClass* klass = ResolveType(assembly, typeOnly.c_str());
            if (klass != nullptr)
                return klass;
        }
        return nullptr;
    }

    /* Arrays: Element[] / Element[,] (not closed generics). */
    if (!name.empty() && name.back() == ']' && name.find('`') == std::string::npos)
    {
        size_t bracket = name.find('[');
        if (bracket != std::string::npos && bracket > 0)
        {
            std::string elemName = name.substr(0, bracket);
            int rank = 1;
            for (size_t i = bracket; i < name.size(); ++i)
            {
                if (name[i] == ',')
                    rank++;
            }
            Il2CppClass* element = ResolveTypeByName(elemName.c_str());
            if (element != nullptr)
                return il2cpp::vm::Class::GetArrayClass(element, (uint32_t)rank);
            return nullptr;
        }
    }

    /* Closed generic: Open`N[[Arg],...] or Open`N[Arg,...] */
    size_t tick = name.find('`');
    size_t brOpen = (tick == std::string::npos) ? std::string::npos : name.find('[', tick);
    if (tick != std::string::npos && brOpen != std::string::npos && name.back() == ']')
    {
        std::string openName = name.substr(0, brOpen);
        Il2CppClass* open = ResolveTypeByName(openName.c_str());
        if (open == nullptr || !open->is_generic)
            return nullptr;

        std::string inner = name.substr(brOpen + 1, name.size() - brOpen - 2);
        std::vector<std::string> argNames;
        size_t i = 0;
        while (i < inner.size())
        {
            while (i < inner.size() && (inner[i] == ',' || inner[i] == ' '))
                ++i;
            if (i >= inner.size())
                break;
            if (inner[i] == '[')
            {
                int depth = 0;
                size_t start = i;
                for (; i < inner.size(); ++i)
                {
                    if (inner[i] == '[')
                        ++depth;
                    else if (inner[i] == ']')
                    {
                        --depth;
                        if (depth == 0)
                        {
                            ++i;
                            break;
                        }
                    }
                }
                std::string one = inner.substr(start + 1, i - start - 2);
                size_t c = one.find(',');
                if (c != std::string::npos)
                    one = one.substr(0, c);
                while (!one.empty() && one.back() == ' ')
                    one.pop_back();
                argNames.push_back(one);
            }
            else
            {
                size_t start = i;
                while (i < inner.size() && inner[i] != ',')
                    ++i;
                std::string one = inner.substr(start, i - start);
                while (!one.empty() && one.back() == ' ')
                    one.pop_back();
                argNames.push_back(one);
            }
        }

        if (argNames.empty())
            return nullptr;

        const Il2CppType** args = (const Il2CppType**)alloca(sizeof(Il2CppType*) * argNames.size());
        for (size_t a = 0; a < argNames.size(); ++a)
        {
            Il2CppClass* argKlass = ResolveTypeByName(argNames[a].c_str());
            if (argKlass == nullptr)
                return nullptr;
            args[a] = &argKlass->byval_arg;
        }
        Il2CppClass* inflated =
            il2cpp::vm::Class::GetInflatedGenericInstanceClass(open, args, (uint32_t)argNames.size());
        if (inflated != nullptr)
            il2cpp::vm::Class::Init(inflated);
        return inflated;
    }

    return nullptr;
}

Il2CppClass* MetadataUtil::GetJsMethodClass()
{
    if (s_jsMethodClass == nullptr)
        Initialize();
    return s_jsMethodClass;
}

Il2CppClass* MetadataUtil::GetJsScriptExceptionClass()
{
    if (s_jsScriptExceptionClass == nullptr)
        Initialize();
    return s_jsScriptExceptionClass;
}

const char* MetadataUtil::GetTypeFullName(Il2CppClass* klass)
{
    static thread_local std::string s_buf;
    s_buf = BuildTypeFullName(klass);
    return s_buf.c_str();
}

std::string MetadataUtil::BuildTypeFullName(Il2CppClass* klass)
{
    if (klass == nullptr)
        return "<null>";
    /* Include generic args / arrays (e.g. List`1[System.Int32], Int32[]). */
    return il2cpp::vm::Type::GetName(&klass->byval_arg, IL2CPP_TYPE_NAME_FORMAT_FULL_NAME);
}

std::string MetadataUtil::FormatParameterSignature(const MethodInfo* method)
{
    if (method == nullptr || method->parameters_count == 0)
        return "()";

    std::string signature = "(";
    for (int i = 0; i < method->parameters_count; ++i)
    {
        if (i > 0)
            signature.push_back(',');
        signature += il2cpp::vm::Type::GetName(method->parameters[i], IL2CPP_TYPE_NAME_FORMAT_FULL_NAME);
    }
    signature.push_back(')');
    return signature;
}

const MethodInfo* MetadataUtil::FindMethodByParameterSignature(Il2CppClass* klass, const char* name, const char* parameterSignature)
{
    for (Il2CppClass* cursor = klass; cursor != nullptr; cursor = cursor->parent)
    {
        EnsureMethods(cursor);
        for (uint16_t i = 0; i < cursor->method_count; ++i)
        {
            const MethodInfo* method = cursor->methods[i];
            if (strcmp(method->name, name) != 0)
                continue;
            if (FormatParameterSignature(method) == parameterSignature)
                return method;
        }
    }
    return nullptr;
}

const MethodInfo* MetadataUtil::FindMethodByParameterSignature(Il2CppClass* klass, const char* name, const char* parameterSignature, bool isStatic)
{
    for (Il2CppClass* cursor = klass; cursor != nullptr; cursor = cursor->parent)
    {
        EnsureMethods(cursor);
        for (uint16_t i = 0; i < cursor->method_count; ++i)
        {
            const MethodInfo* method = cursor->methods[i];
            if (strcmp(method->name, name) != 0)
                continue;
            const bool methodIsStatic = (method->flags & METHOD_ATTRIBUTE_STATIC) != 0;
            if (methodIsStatic != isStatic)
                continue;
            if (FormatParameterSignature(method) == parameterSignature)
                return method;
        }
    }
    return nullptr;
}

bool MetadataUtil::TryReadJsAlias(const MethodInfo* method, std::string& aliasOut)
{
    aliasOut.clear();
    if (method == nullptr)
        return false;
    if (s_jsAliasAttributeClass == nullptr)
        Initialize();
    if (s_jsAliasAttributeClass == nullptr)
        return false;
    if (!il2cpp::vm::Method::HasAttribute(method, s_jsAliasAttributeClass))
        return false;

    Il2CppMetadataCustomAttributeHandle handle =
        il2cpp::vm::MetadataCache::GetCustomAttributeTypeToken(method->klass->image, il2cpp::vm::Method::GetToken(method));
    Il2CppObject* attr = il2cpp::vm::Reflection::GetCustomAttribute(handle, s_jsAliasAttributeClass);
    if (attr == nullptr)
        return false;

    const PropertyInfo* aliasProperty = il2cpp::vm::Class::GetPropertyFromName(attr->klass, "Alias");
    if (aliasProperty == nullptr || aliasProperty->get == nullptr)
        return false;

    Il2CppException* exc = nullptr;
    Il2CppObject* aliasValue = il2cpp::vm::Runtime::Invoke(aliasProperty->get, attr, nullptr, &exc);
    if (exc != nullptr || aliasValue == nullptr)
        return false;

    Il2CppString* aliasStr = reinterpret_cast<Il2CppString*>(aliasValue);
    aliasOut = il2cpp::utils::StringUtils::Utf16ToUtf8(
        il2cpp::utils::StringUtils::GetChars(aliasStr),
        il2cpp::utils::StringUtils::GetLength(aliasStr));
    return !aliasOut.empty();
}

bool MetadataUtil::IsExtensionMethod(const MethodInfo* method)
{
    if (method == nullptr || !IsStaticMethod(method) || method->parameters_count < 1)
        return false;
    if (s_extensionAttributeClass == nullptr)
        Initialize();
    if (s_extensionAttributeClass == nullptr)
        return false;
    return il2cpp::vm::Method::HasAttribute(method, s_extensionAttributeClass);
}

bool MetadataUtil::TryReadJsExtensionTypes(Il2CppClass* klass, std::vector<Il2CppClass*>& outExtensionClasses)
{
    outExtensionClasses.clear();
    if (klass == nullptr)
        return false;
    if (s_jsExtensionAttributeClass == nullptr)
        Initialize();
    if (s_jsExtensionAttributeClass == nullptr)
        return false;
    if (!il2cpp::vm::Class::HasAttribute(klass, s_jsExtensionAttributeClass))
        return false;

    Il2CppReflectionType* typeObj = il2cpp::vm::Reflection::GetTypeObject(&klass->byval_arg);

    Il2CppObject** attrArray = nullptr;
    int32_t count = 0;
#if ZENTS_UNITY_VERSION >= 20220000
    Il2CppArray* attrs = il2cpp::vm::Reflection::GetCustomAttrsInfo(
        reinterpret_cast<Il2CppObject*>(typeObj), s_jsExtensionAttributeClass);
    if (attrs == nullptr || attrs->max_length == 0)
        return false;
    attrArray = il2cpp_array_addr(attrs, Il2CppObject*, 0);
    count = (int32_t)attrs->max_length;
#else
    CustomAttributesCache* cache = il2cpp::vm::Reflection::GetCustomAttrsInfo(
        reinterpret_cast<Il2CppObject*>(typeObj));
    if (cache == nullptr || cache->count == 0)
        return false;
    std::vector<Il2CppObject*> jsExtensionAttrs;
    for (int i = 0; i < cache->count; i++)
    {
        Il2CppObject* attr = cache->attributes[i];
        if (attr != nullptr && il2cpp::vm::Class::IsAssignableFrom(s_jsExtensionAttributeClass, attr->klass))
            jsExtensionAttrs.push_back(attr);
    }
    if (jsExtensionAttrs.empty())
        return false;
    attrArray = jsExtensionAttrs.data();
    count = (int32_t)jsExtensionAttrs.size();
#endif

    std::unordered_set<Il2CppClass*> seen;
    for (int32_t a = 0; a < count; ++a)
    {
        Il2CppObject* attr = attrArray[a];
        if (attr == nullptr)
            continue;
        const PropertyInfo* typesProperty = il2cpp::vm::Class::GetPropertyFromName(attr->klass, "ExtensionTypes");
        if (typesProperty == nullptr || typesProperty->get == nullptr)
            continue;

        Il2CppException* exc = nullptr;
        Il2CppObject* typesObj = il2cpp::vm::Runtime::Invoke(typesProperty->get, attr, nullptr, &exc);
        if (exc != nullptr || typesObj == nullptr)
            continue;

        Il2CppArray* types = reinterpret_cast<Il2CppArray*>(typesObj);
        for (il2cpp_array_size_t i = 0; i < types->max_length; ++i)
        {
            Il2CppReflectionType* rt = il2cpp_array_get(types, Il2CppReflectionType*, i);
            if (rt == nullptr)
                continue;
            Il2CppClass* extKlass = il2cpp::vm::Class::FromSystemType(rt);
            if (extKlass != nullptr && seen.insert(extKlass).second)
                outExtensionClasses.push_back(extKlass);
        }
    }
    return !outExtensionClasses.empty();
}

bool MetadataUtil::ParameterHasBytesMarshal(const MethodInfo* method, int paramIndex)
{
    int32_t kind = 0;
    if (!TryReadJsMarshalAs(method, paramIndex, &kind, nullptr))
        return false;
    return kind == 2; /* JsMarshalType.Bytes */
}

static bool ReadMarshalAttribute(const Il2CppImage* image,uint32_t token,int32_t* outKind,std::vector<std::string>* outMembers)
{
    if(!image||!token||!s_jsMarshalAsAttributeClass)return false;
    auto handle=il2cpp::vm::MetadataCache::GetCustomAttributeTypeToken(image,token);
    Il2CppObject* attr=il2cpp::vm::Reflection::GetCustomAttribute(handle,s_jsMarshalAsAttributeClass);
    if(!attr)return false;
            const PropertyInfo* kindProp = il2cpp::vm::Class::GetPropertyFromName(attr->klass, "JsMarshalType");
            if (kindProp == nullptr || kindProp->get == nullptr)
                return false;

            Il2CppException* exc = nullptr;
            Il2CppObject* boxed = il2cpp::vm::Runtime::Invoke(kindProp->get, attr, nullptr, &exc);
            if (exc != nullptr || boxed == nullptr)
                return false;
            *outKind = *reinterpret_cast<int32_t*>(ObjectUnbox(boxed));

            if (outMembers != nullptr && (*outKind == 5 || *outKind == 4)) /* Table / UnpackedValues */
            {
                const PropertyInfo* membersProp = il2cpp::vm::Class::GetPropertyFromName(attr->klass, "Members");
                if (membersProp != nullptr && membersProp->get != nullptr)
                {
                    Il2CppObject* membersObj = il2cpp::vm::Runtime::Invoke(membersProp->get, attr, nullptr, &exc);
                    if (exc == nullptr && membersObj != nullptr)
                    {
                        Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(membersObj);
                        int32_t n = (int32_t)il2cpp::vm::Array::GetLength(arr);
                        for (int32_t i = 0; i < n; ++i)
                        {
                            Il2CppString* s = il2cpp_array_get(arr, Il2CppString*, i);
                            if (s == nullptr)
                                continue;
                            outMembers->push_back(il2cpp::utils::StringUtils::Utf16ToUtf8(
                                il2cpp::utils::StringUtils::GetChars(s),
                                il2cpp::utils::StringUtils::GetLength(s)));
                        }
                    }
                }
            }
            return true;
}
// Open generic XML slots are validated again against their closed CLR type.
static void ValidateClosedXmlMarshal(const Il2CppType* type,int32_t kind,bool isReturn=false)
{
    if(!type || il2cpp::metadata::GenericMetadata::ContainsGenericParameters(type))return;
    Il2CppClass* klass=il2cpp::vm::Class::FromIl2CppType(type);
    if(!klass)return;
    const bool nullable=klass->nullabletype;
    Il2CppClass* effective=nullable?klass->element_class:klass;
    const Il2CppType* value=&effective->byval_arg;
    const bool decimal=std::strcmp(effective->namespaze,"System")==0&&std::strcmp(effective->name,"Decimal")==0;
    const bool primitive=(value->type>=IL2CPP_TYPE_BOOLEAN&&value->type<=IL2CPP_TYPE_R8)
        ||value->type==IL2CPP_TYPE_I||value->type==IL2CPP_TYPE_U||decimal;
    const bool pointer=value->type==IL2CPP_TYPE_PTR||value->type==IL2CPP_TYPE_FNPTR;
    const bool structure=il2cpp::vm::Class::IsValuetype(effective)&&!effective->enumtype&&!primitive;
    bool valid=true;
    switch(kind)
    {
    case 0:break;
    case 1:valid=!primitive&&!effective->enumtype&&!pointer;break;
    case 2:valid=value->type==IL2CPP_TYPE_SZARRAY&&effective->element_class==il2cpp_defaults.byte_class;break;
    case 3:valid=!primitive||decimal||value->type==IL2CPP_TYPE_TYPEDBYREF;break;
    case 4:valid=structure&&!nullable;break;
    case 5:valid=structure;break;
    case 6:valid=isReturn&&!il2cpp::vm::Class::IsValuetype(klass)&&type->type!=IL2CPP_TYPE_STRING
        &&!il2cpp::vm::Class::IsAssignableFrom(il2cpp_defaults.multicastdelegate_class,klass);break;
    case 7:valid=!nullable&&(value->type==IL2CPP_TYPE_I8||value->type==IL2CPP_TYPE_U8);break;
    default:valid=false;break;
    }
    if(!valid)JsException::Throw("zents: invalid XML MarshalAs on closed member");
}
static bool ReadXmlMember(const Il2CppImage* image,uint32_t token,int32_t* kind,std::vector<std::string>* members)
{
    JsMarshalAsResolvedData rule;if(!MarshalAsXmlTable::TryGet(image,token,rule))return false;
    *kind=(int32_t)rule.marshalType;if(members)members->assign(rule.members.begin(),rule.members.end());return true;
}
static bool ReadTypeMarshal(const Il2CppType* type,int32_t* kind,std::vector<std::string>* members)
{
    Il2CppClass* klass=il2cpp::vm::Class::FromIl2CppType(type);
    if(!klass || il2cpp::vm::Class::IsGeneric(klass) || klass->generic_class)return false;
    if(ReadMarshalAttribute(klass->image,klass->token,kind,members))return true;
    return ReadXmlMember(klass->image,klass->token,kind,members);
}
bool MetadataUtil::TryReadFieldMarshalAs(FieldInfo* field,int32_t* kind,std::vector<std::string>* members)
{
    Initialize();*kind=0;if(members)members->clear();
    if(ReadMarshalAttribute(field->parent->image,field->token,kind,members))return true;
    if(ReadXmlMember(field->parent->image,field->token,kind,members))
    {ValidateClosedXmlMarshal(field->type,*kind);return true;}
    return ReadTypeMarshal(field->type,kind,members);
}
bool MetadataUtil::TryReadJsMarshalAs(const MethodInfo* method,int paramIndex,int32_t* kind,std::vector<std::string>* members)
{
    if(!method||!kind)return false;Initialize();*kind=0;if(members)members->clear();
    uint32_t token=il2cpp::vm::Method::GetParameterToken(method,paramIndex);
    if(ReadMarshalAttribute(method->klass->image,token,kind,members))return true;
    const PropertyInfo* property=nullptr;
    void* iterator=nullptr;
    while(const PropertyInfo* p=il2cpp::vm::Class::GetProperties(method->klass,&iterator))
        if((paramIndex<0&&p->get==method)||(paramIndex==method->parameters_count-1&&p->set==method)){property=p;break;}
    if(property&&ReadMarshalAttribute(method->klass->image,property->token,kind,members))return true;
    JsMarshalAsResolvedData xml;
    if(MarshalAsXmlTable::TryGetForMethodSlot(method,paramIndex,xml)){*kind=(int32_t)xml.marshalType;ValidateClosedXmlMarshal(paramIndex<0?method->return_type:method->parameters[paramIndex],*kind,paramIndex<0);if(members)members->assign(xml.members.begin(),xml.members.end());return true;}
    if(property&&ReadXmlMember(method->klass->image,property->token,kind,members)){ValidateClosedXmlMarshal(paramIndex<0?method->return_type:method->parameters[paramIndex],*kind,paramIndex<0);return true;}
    const Il2CppType* type=paramIndex<0?method->return_type:method->parameters[paramIndex];
    return ReadTypeMarshal(type,kind,members);
}

bool MetadataUtil::IsParamsParameter(const MethodInfo* method, int paramIndex)
{
    if (method == nullptr || paramIndex < 0 || paramIndex >= method->parameters_count)
        return false;
    if (s_paramArrayAttributeClass == nullptr)
        Initialize();
    if (s_paramArrayAttributeClass == nullptr)
        return false;

    uint32_t token = il2cpp::vm::Method::GetParameterToken(method, paramIndex);
    if (token == 0)
        return false;

    Il2CppMetadataCustomAttributeHandle handle =
        il2cpp::vm::MetadataCache::GetCustomAttributeTypeToken(method->klass->image, token);
    Il2CppObject* attr = il2cpp::vm::Reflection::GetCustomAttribute(handle, s_paramArrayAttributeClass);
    return attr != nullptr;
}
}
namespace zents {
static bool HasPublicDefaultCtor(Il2CppClass* klass)
{
    if(il2cpp::vm::Class::IsValuetype(klass))return true;
    if(klass->flags & TYPE_ATTRIBUTE_ABSTRACT)return false;
    il2cpp::vm::Class::SetupMethods(klass);
    for(uint16_t i=0;i<klass->method_count;++i){const MethodInfo* m=klass->methods[i];if(MetadataUtil::IsPublicMethod(m)&&std::strcmp(m->name,".ctor")==0&&m->parameters_count==0)return true;}
    return false;
}
bool MetadataUtil::MethodGenericTypeArgsSatisfyConstraints(const MethodInfo* openMethod, const Il2CppType* const* typeArgs, uint32_t typeArgCount)
{
    if (openMethod == nullptr || typeArgs == nullptr)
        return false;

    Il2CppMetadataGenericContainerHandle container = openMethod->genericContainerHandle;
    if (!container && openMethod->methodMetadataHandle) container = il2cpp::vm::MetadataCache::GetGenericContainerFromMethod(openMethod->methodMetadataHandle);
    if (container == nullptr)
        return false;

    if (il2cpp::vm::MetadataCache::GetGenericContainerCount(container) != typeArgCount)
        return false;

    for (uint32_t i = 0; i < typeArgCount; ++i)
    {
        Il2CppMetadataGenericParameterHandle gp =
            il2cpp::vm::MetadataCache::GetGenericParameterFromIndex(container, (GenericContainerParameterIndex)i);
        Il2CppClass* argKlass = il2cpp::vm::Class::FromIl2CppType(typeArgs[i]);
        if (argKlass == nullptr)
            return false;
        il2cpp::vm::Class::Init(argKlass);

        const uint16_t flags = il2cpp::vm::MetadataCache::GetGenericParameterFlags(gp);
        if ((flags & IL2CPP_GENERIC_PARAMETER_ATTRIBUTE_REFERENCE_TYPE_CONSTRAINT) != 0)
        {
            if (il2cpp::vm::Class::IsValuetype(argKlass))
                return false;
        }
        if ((flags & IL2CPP_GENERIC_PARAMETER_ATTRIBUTE_NOT_NULLABLE_VALUE_TYPE_CONSTRAINT) != 0)
        {
            // where T : struct — value type and not Nullable<T>
            if (!il2cpp::vm::Class::IsValuetype(argKlass) || argKlass->nullabletype)
                return false;
        }
        if ((flags & IL2CPP_GENERIC_PARAMETER_ATTRIBUTE_DEFAULT_CONSTRUCTOR_CONSTRAINT) != 0)
        {
            if (!HasPublicDefaultCtor(argKlass))
                return false;
        }

        const int16_t constraintCount = il2cpp::vm::MetadataCache::GetGenericConstraintCount(gp);
        for (int16_t c = 0; c < constraintCount; ++c)
        {
            const Il2CppType* constraintType =
                il2cpp::vm::MetadataCache::GetGenericParameterConstraintFromIndex(gp, (GenericParameterConstraintIndex)c);
            if (constraintType == nullptr)
                continue;

            // Inflate constraints that refer to other method type parameters.
            const Il2CppType* effectiveConstraint = constraintType;
            if (constraintType->type == IL2CPP_TYPE_MVAR || constraintType->type == IL2CPP_TYPE_VAR)
            {
                Il2CppMetadataGenericParameterHandle constraintGp =
                    il2cpp::vm::MetadataCache::GetGenericParameterFromType(constraintType);
                Il2CppGenericParameterInfo info = il2cpp::vm::MetadataCache::GetGenericParameterInfo(constraintGp);
                if ((uint32_t)info.num >= typeArgCount)
                    return false;
                effectiveConstraint = typeArgs[info.num];
            }

            Il2CppClass* constraintKlass = il2cpp::vm::Class::FromIl2CppType(effectiveConstraint);
            if (constraintKlass == nullptr)
                return false;
            il2cpp::vm::Class::Init(constraintKlass);

            // System.Object / ValueType / Enum appear as special flags; ValueType/Enum as type
            // constraints still require assignability (struct → ValueType, not string).
            if (!il2cpp::vm::Class::IsAssignableFrom(constraintKlass, argKlass))
                return false;
        }
    }

    return true;
}


}
