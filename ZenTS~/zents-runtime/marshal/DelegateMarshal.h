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

namespace zents
{
    /// Managed layout must match Runtime/Il2Cpp/JsMethod.cs field order after object header.
    struct JsMethod
    {
        Il2CppObject object;
        bool disposed;
        JSContext* ctx;
        int32_t funcRef;
        const void* methodMarshalCtx; // MethodMarshalCtx* for Invoke (CS→JS writers)
    };

    class DelegateMarshal
    {
    public:
        static void Reset();

        static Il2CppDelegate* CreateFromFuncRef(JSContext* ctx, Il2CppClass* delegateClass, int funcRef);

        /** Reuse the same CLR delegate for the same JS function + delegate type (event add/remove). */
        static Il2CppDelegate* GetOrCreateFromJsFunction(
            JSContext* ctx, Il2CppClass* delegateClass, JSValueConst jsFunc);

        /** If del was created from a JS function (JsMethod target), dup that function into outFn. */
        static bool TryGetBoundJsFunction(JSContext* ctx, Il2CppDelegate* del, JSValue* outFn);

        /** Push CLR delegate as callable JS (bound JS fn, or exotic + [[Call]] via wrap). */
        static JSValue PushToJs(JSContext* ctx, Il2CppDelegate* del, Il2CppClass* viewKlass);
    };
}
