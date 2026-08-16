#pragma once

#include "../ZTSCommon.h"

namespace zts
{
    /// Managed layout must match Runtime/Il2Cpp/TsMethod.cs field order after object header.
    struct TsMethod
    {
        Il2CppObject object;
        bool disposed;
        JSContext* ctx;
        int32_t funcRef;
        const void* methodMarshalCtx; // unused in M1; reserved for M2+
    };

    class DelegateMarshal
    {
    public:
        static void Reset();

        static Il2CppDelegate* CreateFromFuncRef(JSContext* ctx, Il2CppClass* delegateClass, int funcRef);

        /** Reuse the same CLR delegate for the same JS function + delegate type (event add/remove). */
        static Il2CppDelegate* GetOrCreateFromJsFunction(
            JSContext* ctx, Il2CppClass* delegateClass, JSValueConst jsFunc);

        /** If del was created from a JS function (TsMethod target), dup that function into outFn. */
        static bool TryGetBoundJsFunction(JSContext* ctx, Il2CppDelegate* del, JSValue* outFn);

        /** Push CLR delegate as callable JS (bound JS fn, or exotic + [[Call]] via wrap). */
        static JSValue PushToJs(JSContext* ctx, Il2CppDelegate* del, Il2CppClass* viewKlass);
    };
}
