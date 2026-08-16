#pragma once

#include "../ZTSCommon.h"

struct Il2CppClass;

namespace zts
{
struct TypeBinding;

/// Sz/Md-array ByObj IEO: get / set / length (Docs/spec/02-TYPE-SYSTEM §7).
class ArrayBinding
{
public:
    static void AttachMembers(JSContext* ctx, JSValue proto, TypeBinding* binding, Il2CppClass* arrayKlass);
};
}
