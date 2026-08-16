#pragma once

#include "../ZTSCommon.h"

namespace zts
{
    class JsGlobalRefs
    {
    public:
        static void Initialize();
        static void Clear();
        static void ClearAndFreeAll(JSContext* ctx);

        /// Dup and store; returns stable ref index (>= 0).
        static int Store(JSContext* ctx, JSValue value);
        /// Borrowed value (do not Free unless you Dup first).
        static JSValue Get(int refIndex);
        static void FreeAndRelease(JSContext* ctx, int refIndex);
    };
}
