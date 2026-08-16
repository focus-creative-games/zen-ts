#pragma once

#include "MarshalDefs.h"

namespace zts
{
class MarshalMeta
{
public:
    /// Returns nullptr when the type is not yet supported (M2: Int32 only).
    static const MarshalMetaInfo* TryCreateDefault(const Il2CppType* type);
};
}
