#pragma once

#include "MarshalDefs.h"

#include <vector>

namespace zts
{
enum class ConversionKind : int
{
    None = 1000,
    Identity = 0,
    ImplicitNumeric = 1,
    ImplicitExtendedInteger = 2,
    ImplicitReference = 3,
    ImplicitBoxing = 4,
    NullLiteral = 5,
};

struct OverloadGroup
{
    std::vector<MethodMarshalCtx*> candidates;
};

class MethodOverloadResolver
{
public:
    static ConversionKind GetConversionKind(JSContext* ctx, JSValueConst value, const MarshalMetaInfo* paramMeta);
    static MethodMarshalCtx* Resolve(JSContext* ctx, OverloadGroup* group, int argc, JSValueConst* argv);
    static std::string BuildSignatureKey(const MethodInfo* method);
};
}
