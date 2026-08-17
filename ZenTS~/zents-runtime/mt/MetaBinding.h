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

#include "../ZenTSCommon.h"
#include "../marshal/MarshalDefs.h"
#include "../marshal/MethodOverloadResolver.h"
#include "../utils/CsStringHash.h"

#include <list>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace zents
{
enum class MetaKind : uint8_t
{
    Method = 0,
    Field = 1,
    Property = 2,
    MethodDispatch = 3,
};

struct FieldMarshalCtx
{
    FieldInfo* field;
    const MarshalMetaInfo* meta;
};

struct MetaInfo
{
    MetaKind kind;
    MethodMarshalCtx* methodCtx;  /* Method, or Property getter */
    MethodMarshalCtx* setterCtx;  /* Property setter */
    FieldMarshalCtx* fieldCtx;
    OverloadGroup* overloadGroup; /* MethodDispatch */
};

typedef std::unordered_map<const char*, MetaInfo, CsStringHash, CsStringEqual> NameMetaMap;

struct TypeBinding
{
    Il2CppClass* klass;
    NameMetaMap staticMap;
    NameMetaMap instanceMap;
    std::vector<MethodMarshalCtx*> ctors;
    std::list<std::string> ownedNames;
    std::vector<OverloadGroup*> ownedGroups;
    std::unordered_set<std::string> memberKeys;
    JSValue typeObject;
    JSValue typeObjectRaw;
    /** IEO prototype (not miss-wrapped). Classes: ByObj; structs: alias of byobjInstanceProto. */
    JSValue instanceProto;
    /** Struct ByVal instance dispatch proto; undefined for reference types. */
    JSValue byvalInstanceProto;
    /** Struct ByObj instance dispatch proto; undefined for reference types (use instanceProto). */
    JSValue byobjInstanceProto;
    bool hasJsObject;
};

class MetaBinding
{
public:
    static TypeBinding* EnsureBinding(JSContext* ctx, Il2CppClass* klass);
    static Il2CppClass* TryGetKlassFromTypeValue(JSContext* ctx, JSValueConst typeVal);
    static MethodMarshalCtx* CreateMethodMarshalCtx(const MethodInfo* method);
    static JSValue CreateInstanceMethodFunction(JSContext* ctx, MethodMarshalCtx* mctx);
    static JSValue CreateStaticMethodFunction(JSContext* ctx, MethodMarshalCtx* mctx);
    static MethodMarshalCtx* TryGetDirectMethodCtx(JSContext* ctx, JSValueConst fn);
    static void AttachInstanceMembers(JSContext* ctx, JSValueConst jsObj, TypeBinding* binding);
    static JSValue WrapStrictMiss(JSContext* ctx, JSValue obj);
    static JSValue WrapDelegateCall(JSContext* ctx, JSValue obj);
    static void Reset(JSContext* ctx);
};
}
