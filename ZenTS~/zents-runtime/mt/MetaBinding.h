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
using FieldMarshalCtx = FieldPlan<MarshalMetaInfo>;
using MetaInfo = MemberPlan<MethodMarshalCtx, FieldMarshalCtx, OverloadGroup>;
using NameMetaMap = std::unordered_map<const char*, MetaInfo, CsStringHash, CsStringEqual>;

struct TypeBinding : TypeBindingPlan<MethodMarshalCtx, MetaInfo, OverloadGroup>
{
    js::Value typeObject;
    js::Value typeObjectRaw;
    /** IEO prototype (not miss-wrapped). Classes: ByObj; structs: alias of byobjInstanceProto. */
    js::Value instanceProto;
    /** Struct ByVal instance dispatch proto; undefined for reference types. */
    js::Value byvalInstanceProto;
    /** Struct ByObj instance dispatch proto; undefined for reference types (use instanceProto). */
    js::Value byobjInstanceProto;
    bool hasJsObject;
};

class MetaBinding
{
public:
    static TypeBinding* EnsureBinding(js::Context* ctx, Il2CppClass* klass);
    static Il2CppClass* TryGetKlassFromTypeValue(js::Context* ctx, js::ValueConst typeVal);
    static MethodMarshalCtx* ResolveGtap(js::Context* ctx, const std::vector<MethodMarshalCtx*>& methods, int argc, js::ValueConst* argv, int* prefix);
    static const MethodInfo* FindPublicMethod(Il2CppClass* owner, const char* key, int staticDomain, int genericArity);
    static MethodMarshalCtx* CreateMethodMarshalCtx(const MethodInfo* method);
    static js::Value CreateInstanceMethodFunction(js::Context* ctx, MethodMarshalCtx* mctx, int mode = 0);
    static js::Value CreateStaticMethodFunction(js::Context* ctx, MethodMarshalCtx* mctx);
    static MethodMarshalCtx* TryGetDirectMethodCtx(js::Context* ctx, js::ValueConst fn);
    static void AttachInstanceMembers(js::Context* ctx, js::ValueConst jsObj, TypeBinding* binding);
    static js::Value WrapStrictMiss(js::Context* ctx, js::Value obj);
    static js::Value WrapDelegateCall(js::Context* ctx, js::Value obj);
    static void Reset(js::Context* ctx);
};
}
