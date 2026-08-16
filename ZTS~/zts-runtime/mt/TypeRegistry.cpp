#include "TypeRegistry.h"
#include "MetaBinding.h"

namespace zts
{
JSValue TypeRegistry::PushTypeObject(JSContext* ctx, Il2CppClass* klass)
{
    TypeBinding* binding = MetaBinding::EnsureBinding(ctx, klass);
    return JS_DupValue(ctx, binding->typeObject);
}

void TypeRegistry::Reset(JSContext* ctx)
{
    MetaBinding::Reset(ctx);
}
}
