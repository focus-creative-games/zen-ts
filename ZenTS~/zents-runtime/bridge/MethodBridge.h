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
#include "vm/Class.h"
#include "../marshal/MethodOverloadResolver.h"

#include "BridgeDefs.h"
#include "../utils/MetadataUtil.h"

namespace zents
{
class MethodBridge
{
public:
    static void Initialize();
    static js::Value DefaultInvoke(
        js::Context* ctx,
        void* target,
        int argc,
        js::ValueConst* argv,
        const MethodInfo* method,
        const MethodMarshalCtx* mctx);

    static FnJs2CsInvoker ResolveMethodInvoker(const MethodInfo* method);

    static inline js::Value InvokeJs2Cs(
        js::Context* ctx,
        void* target,
        int argc,
        js::ValueConst* argv,
        const MethodMarshalCtx* mctx)
    {
        if (!MethodOverloadResolver::IsApplicable(ctx, mctx, argc, argv))
            return js::ThrowTypeError(ctx, "zents: argument mismatch for %s", mctx->method->name);
        if(target != nullptr)
        {
            auto* object=reinterpret_cast<Il2CppObject*>(target);
            Il2CppClass* expected=mctx->isExtension?mctx->paramsMeta[0]->typeKlass:mctx->method->klass;
            if(expected && !il2cpp::vm::Class::IsAssignableFrom(expected,object->klass))
                return js::ThrowTypeError(ctx,"zents: incompatible method receiver");
        }
        const MethodInfo* method = MetadataUtil::ResolveInvokeMethod(mctx->method, target, mctx->sealed);
        return mctx->js2CsInvoker(ctx, target, argc, argv, method, mctx);
    }
};
}
