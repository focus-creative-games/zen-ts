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

#pragma once
// Engine-independent CLR plans. No JS values, runtime contexts, callbacks or engine headers.
// Backend-specific executable contexts extend these plans and own their engine resources.
// Templates preserve typed executors without void* casts or duplicated plan definitions.
#include <cstdint>
#include <list>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include "../utils/CsStringHash.h"
struct Il2CppType;
struct Il2CppClass;
struct Il2CppObject;
struct MethodInfo;
struct FieldInfo;
namespace zents {
enum class MarshalAsKind : uint8_t
{
    None = 0,
    Object = 1,
    Bytes = 2,
    OpaqueValue = 3,
    UnpackedValues = 4,
    Table = 5,
    ActualTypeUserData = 6,
    BigInt = 7,
};
struct MarshalPlan
{
    const Il2CppType* type;
    Il2CppClass* typeKlass;
    int32_t size;
    bool passByValue; // true → params[i] is the pointer value itself (ref types)
    int32_t jsArgSlots; // JS argv slots consumed (UnpackedValues = Members.Length)
    MarshalAsKind marshalAsKind;
};
struct MethodDefaultArgs
{
    uint8_t firstDefaultParamIndex;
    uint8_t defaultParamCount;
    void** defaultValueSlots;          /* [defaultParamCount] valuetype native buffers */
    Il2CppObject** defaultObjectSlots; /* [defaultParamCount]; nullptr if no reference defaults */
};
template<class TMarshal> struct MethodPlan
{
    const MethodInfo* method;
    const TMarshal** paramsMeta;
    const TMarshal* retMeta;
    const TMarshal* paramsElementMeta;
    const MethodDefaultArgs* defaults; /* nullptr when method has no optional defaults */
    int32_t arity;    /* max JS argv slots */
    int32_t minArity; /* required JS argv slots (optional trailing omitted) */
    bool sealed;
    bool isExtension;
    bool hasParamsArray; /* trailing params T[] — whole CLR array or 0..N elements */
};
enum class MetaKind : uint8_t
{
    Method = 0,
    Field = 1,
    Property = 2,
    MethodDispatch = 3,
};
template<class TMarshal> struct FieldPlan
{
    FieldInfo* field;
    const TMarshal* meta;
};
template<class TMethod> struct OverloadPlan { std::vector<TMethod*> candidates; };
template<class TMethod, class TField, class TGroup> struct MemberPlan
{
    MetaKind kind;
    TMethod* methodCtx;  /* Method, or Property getter */
    TMethod* setterCtx;  /* Property setter */
    TField* fieldCtx;
    TGroup* overloadGroup; /* MethodDispatch */
};
template<class TMethod, class TMember, class TGroup> struct TypeBindingPlan
{
    using MemberMap = std::unordered_map<const char*, TMember, CsStringHash, CsStringEqual>;
    Il2CppClass* klass;
    MemberMap staticMap;
    MemberMap instanceMap;
    std::vector<TMethod*> ctors;
    std::list<std::string> ownedNames;
    std::vector<TGroup*> ownedGroups;
    std::unordered_set<std::string> memberKeys;
};
}
