#pragma once

#include "../ZTSCommon.h"

struct Il2CppClass;

namespace zts
{
class TypeRegistry
{
public:
    static JSValue PushTypeObject(JSContext* ctx, Il2CppClass* klass);
    static void Reset(JSContext* ctx);
};
}
