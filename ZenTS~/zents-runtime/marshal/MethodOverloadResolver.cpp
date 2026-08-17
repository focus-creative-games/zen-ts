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

#include "../utils/MetadataUtil.h"

#include "vm/Class.h"
#include "vm/Field.h"

#include <cmath>
#include <cstring>
#include <limits>
#include <string>

namespace zents
{
namespace
{
ConversionKind ScorePrimitiveNumber(JSContext* ctx, JSValueConst value, bool requireIntegral)
{
    if (!JS_IsNumber(value) || JS_IsBigInt(ctx, value))
        return ConversionKind::None;
    double d = 0;
    if (JS_ToFloat64(ctx, &d, value))
        return ConversionKind::None;
    if (!std::isfinite(d))
        return ConversionKind::None;
    if (requireIntegral)
        return std::floor(d) == d ? ConversionKind::Identity : ConversionKind::None;
    return std::floor(d) == d ? ConversionKind::ImplicitNumeric : ConversionKind::Identity;
}

ConversionKind ScoreFieldValue(JSContext* ctx, JSValueConst value, const Il2CppType* fieldType)
{
    if (fieldType == nullptr)
        return ConversionKind::None;
    switch (fieldType->type)
    {
    case IL2CPP_TYPE_BOOLEAN:
        return JS_IsBool(value) ? ConversionKind::Identity : ConversionKind::None;
    case IL2CPP_TYPE_CHAR:
    case IL2CPP_TYPE_I1:
    case IL2CPP_TYPE_U1:
    case IL2CPP_TYPE_I2:
    case IL2CPP_TYPE_U2:
    case IL2CPP_TYPE_I4:
    case IL2CPP_TYPE_U4:
        return ScorePrimitiveNumber(ctx, value, true);
    case IL2CPP_TYPE_I8:
    case IL2CPP_TYPE_U8:
    case IL2CPP_TYPE_I:
    case IL2CPP_TYPE_U:
        if (!JS_IsNumber(value) || JS_IsBigInt(ctx, value))
            return ConversionKind::None;
        return ConversionKind::ImplicitExtendedInteger;
    case IL2CPP_TYPE_R4:
    case IL2CPP_TYPE_R8:
        return ScorePrimitiveNumber(ctx, value, false);
    case IL2CPP_TYPE_STRING:
        if (JS_IsString(value))
            return ConversionKind::Identity;
        if (JS_IsNull(value))
            return ConversionKind::NullLiteral;
        return ConversionKind::None;
    default:
        return ConversionKind::None;
    }
}

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

bool ScoreUnpackedArgs(
    JSContext* ctx,
    JSValueConst* argv,
    int jsIndex,
    int argc,
    const MarshalMetaInfo* paramMeta,
    int* outScore)
{
    auto* tm = static_cast<const TableMarshalMeta*>(paramMeta);
    const int slots = (int)tm->members.size();
    if (jsIndex + slots > argc)
        return false;
    int score = 0;
    for (int i = 0; i < slots; ++i)
    {
        FieldInfo* field = FindInstanceFieldByName(paramMeta->typeKlass, tm->members[(size_t)i].c_str());
        if (field == nullptr)
            return false;
        ConversionKind kind = ScoreFieldValue(ctx, argv[jsIndex + i], field->type);
        if (kind == ConversionKind::None)
            return false;
        score += (int)kind;
    }
    *outScore = score;
    return true;
}
} // namespace

ConversionKind MethodOverloadResolver::GetConversionKind(
    JSContext* ctx, JSValueConst value, const MarshalMetaInfo* paramMeta)
{
    if (paramMeta == nullptr || paramMeta->type == nullptr)
        return ConversionKind::None;

    if (paramMeta->marshalAsKind == MarshalAsKind::Table)
    {
        if (JS_IsNull(value) || JS_IsUndefined(value))
            return ConversionKind::None;
        return JS_IsObject(value) ? ConversionKind::Identity : ConversionKind::None;
    }

    const Il2CppType* paramType = paramMeta->type;
    if (JS_IsUndefined(value))
        return ConversionKind::None;

    switch (paramType->type)
    {
    case IL2CPP_TYPE_BOOLEAN:
        return JS_IsBool(value) ? ConversionKind::Identity : ConversionKind::None;

    case IL2CPP_TYPE_CHAR:
    case IL2CPP_TYPE_I1:
    case IL2CPP_TYPE_U1:
    case IL2CPP_TYPE_I2:
    case IL2CPP_TYPE_U2:
    case IL2CPP_TYPE_I4:
    case IL2CPP_TYPE_U4:
        return ScorePrimitiveNumber(ctx, value, true);

    case IL2CPP_TYPE_I8:
    case IL2CPP_TYPE_U8:
    case IL2CPP_TYPE_I:
    case IL2CPP_TYPE_U:
        if (!JS_IsNumber(value) || JS_IsBigInt(ctx, value))
            return ConversionKind::None;
        return ConversionKind::ImplicitExtendedInteger;

    case IL2CPP_TYPE_R4:
    case IL2CPP_TYPE_R8:
        return ScorePrimitiveNumber(ctx, value, false);

    case IL2CPP_TYPE_STRING:
        if (JS_IsString(value))
            return ConversionKind::Identity;
        if (JS_IsNull(value))
            return ConversionKind::NullLiteral;
        return ConversionKind::None;

    case IL2CPP_TYPE_VALUETYPE:
        if (paramMeta->typeKlass != nullptr && paramMeta->typeKlass->enumtype)
            return ScorePrimitiveNumber(ctx, value, true);
        /* Default struct ByObj */
        if (ObjectRegistry::IsZentsObject(ctx, value))
            return ConversionKind::Identity;
        return ConversionKind::None;

    case IL2CPP_TYPE_PTR:
        if (JS_IsNull(value))
            return ConversionKind::NullLiteral;
        if (JS_IsObject(value))
        {
            JSValue flag = JS_GetPropertyStr(ctx, value, "__zents_pointer");
            bool isPtr = JS_IsBool(flag) && JS_ToBool(ctx, flag);
            JS_FreeValue(ctx, flag);
            if (isPtr)
                return ConversionKind::Identity;
        }
        return ConversionKind::None;

    case IL2CPP_TYPE_CLASS:
    case IL2CPP_TYPE_OBJECT:
    case IL2CPP_TYPE_GENERICINST:
    {
        if (JS_IsNull(value))
            return ConversionKind::NullLiteral;
        if (ObjectRegistry::IsZentsObject(ctx, value))
        {
            Il2CppObject* obj = ObjectRegistry::Get(ctx, value);
            if (obj == nullptr || paramMeta->typeKlass == nullptr)
                return ConversionKind::None;
            if (paramMeta->typeKlass == obj->klass)
                return ConversionKind::Identity;
            if (il2cpp::vm::Class::IsAssignableFrom(paramMeta->typeKlass, obj->klass))
                return ConversionKind::ImplicitReference;
            return ConversionKind::None;
        }
        if (paramMeta->typeKlass == il2cpp_defaults.object_class)
        {
            if (JS_IsBool(value) || JS_IsNumber(value) || JS_IsString(value))
                return ConversionKind::ImplicitBoxing;
        }
        if (MetadataUtil::IsDelegateClass(paramMeta->typeKlass) && JS_IsFunction(ctx, value))
            return ConversionKind::ImplicitReference;
        return ConversionKind::None;
    }

    default:
        return ConversionKind::None;
    }
}

MethodMarshalCtx* MethodOverloadResolver::Resolve(
    JSContext* ctx, OverloadGroup* group, int argc, JSValueConst* argv)
{
    if (group == nullptr || group->candidates.empty())
        return nullptr;

    MethodMarshalCtx* best = nullptr;
    int bestScore = std::numeric_limits<int>::max();
    int bestParamCount = std::numeric_limits<int>::max();
    bool ambiguous = false;

    for (MethodMarshalCtx* candidate : group->candidates)
    {
        /* Allow argc < minArity: missing params filled with type/optional defaults. */
        if (candidate == nullptr || argc > candidate->arity)
            continue;

        int score = 0;
        bool ok = true;
        int jsIndex = 0;
        const uint8_t clrStart = candidate->isExtension ? 1 : 0;
        const uint8_t clrArity = candidate->method->parameters_count;
        for (uint8_t pi = clrStart; pi < clrArity; ++pi)
        {
            const MarshalMetaInfo* paramMeta = candidate->paramsMeta[pi];
            if (paramMeta->marshalAsKind == MarshalAsKind::UnpackedValues)
            {
                int part = 0;
                const int slots = TableMarshal::GetJsArgSlotCount(paramMeta);
                if (jsIndex + slots > argc)
                {
                    /* Trailing unpacked defaults: accept without scoring. */
                    jsIndex += slots;
                    continue;
                }
                if (!ScoreUnpackedArgs(ctx, argv, jsIndex, argc, paramMeta, &part))
                {
                    ok = false;
                    break;
                }
                score += part;
                jsIndex += slots;
                continue;
            }

            if (jsIndex >= argc)
                break;

            ConversionKind kind = GetConversionKind(ctx, argv[jsIndex], paramMeta);
            if (kind == ConversionKind::None)
            {
                ok = false;
                break;
            }
            score += (int)kind;
            jsIndex += 1;
        }
        if (!ok)
            continue;

        const int paramCount = (int)candidate->method->parameters_count;
        if (score < bestScore
            || (score == bestScore && paramCount < bestParamCount))
        {
            bestScore = score;
            bestParamCount = paramCount;
            best = candidate;
            ambiguous = false;
        }
        else if (score == bestScore && paramCount == bestParamCount)
        {
            ambiguous = true;
        }
    }

    if (ambiguous)
        return nullptr;
    return best;
}

std::string MethodOverloadResolver::BuildSignatureKey(const MethodInfo* method)
{
    std::string key = method->name;
    key += "(";
    for (uint8_t i = 0; i < method->parameters_count; ++i)
    {
        if (i > 0)
            key += ",";
        Il2CppClass* pk = il2cpp::vm::Class::FromIl2CppType(method->parameters[i]);
        key += MetadataUtil::BuildTypeFullName(pk);
    }
    key += ")";
    return key;
}
}
