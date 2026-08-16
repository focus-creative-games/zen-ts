#pragma once

#include "BridgeDefs.h"
#include "../utils/MetadataUtil.h"

namespace zts
{
class MethodBridge
{
public:
    static void Initialize();
    static JSValue DefaultInvoke(
        JSContext* ctx,
        void* target,
        int argc,
        JSValueConst* argv,
        const MethodInfo* method,
        const MethodMarshalCtx* mctx);

    static FnJs2CsInvoker ResolveMethodInvoker(const MethodInfo* method);

    static inline JSValue InvokeJs2Cs(
        JSContext* ctx,
        void* target,
        int argc,
        JSValueConst* argv,
        const MethodMarshalCtx* mctx)
    {
        /* ZTS allows omitting non-optional args (zero/type defaults), matching Mono v1. */
        if (argc > mctx->arity)
        {
            return JS_ThrowTypeError(
                ctx,
                "zts: argument mismatch: expected at most %d argument(s), got %d",
                mctx->arity,
                argc);
        }
        const MethodInfo* method = MetadataUtil::ResolveInvokeMethod(mctx->method, target, mctx->sealed);
        return mctx->js2CsInvoker(ctx, target, argc, argv, method, mctx);
    }
};
}
