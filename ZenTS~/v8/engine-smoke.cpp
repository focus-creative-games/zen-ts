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

#include "../zents-runtime/engine/EngineApi.h"
#include <cassert>
#include <cstdio>
#include <cstring>
#include <string>
using namespace zents::engine;
static int finalized = 0;
static ClassID payloadClass = 0;
static void Finalize(Runtime *, Value)
{
    ++finalized;
}
static Value Sum(Context *c, Value, int argc, Value *args)
{
    assert(argc == 2);
    int a, b;
    assert(ToInt32(c, &a, args[0]) == 0);
    assert(ToInt32(c, &b, args[1]) == 0);
    return NewInt32(c, a + b);
}
static Value Fail(Context *c, Value, int, Value *)
{
    return ThrowTypeError(c, "expected-native-error");
}
static Module *Loader(Context *c, const char *name, void *)
{
    const char *source = std::strcmp(name, "dep.js") == 0
                             ? "export const value=40;"
                             : "import {value} from './dep.js';export function answer(){return value+2;}";
    Value v = Eval(c, source, std::strlen(source), name, EvalModule | EvalCompileOnly);
    Module *m = GetCompiledModule(v);
    FreeValue(c, v);
    return m;
}
int main()
{
    for (int cycle = 0; cycle < 3; ++cycle)
    {
        Runtime *rt = NewRuntime();
        assert(GetBackend() == Backend::V8);
        Context *c = NewContext(rt);
        StdAddHelpers(c, 0, nullptr);
        Value global = GetGlobalObject(c);
        SetPropertyStr(c, global, "sum", NewCFunction(c, Sum, "sum", 2));
        SetPropertyStr(c, global, "fail", NewCFunction(c, Fail, "fail", 0));
        const char *script =
            "if(__zents_engine!=='V8')throw Error('wrong backend');if(sum(20,22)!==42)throw "
            "Error('callback');try{fail();throw Error('missing "
            "error')}catch(e){if(!String(e).includes('expected-native-error'))throw e;}globalThis.o={x:1};o;";
        Value v = Eval(c, script, std::strlen(script), "engine-smoke.js", EvalGlobal);
        assert(!IsException(v));
        void *identity = GetIdentity(v);
        RunGC(rt);
        Value again = GetPropertyStr(c, global, "o");
        assert(GetIdentity(again) == identity);
        FreeValue(c, again);
        FreeValue(c, v);
        const char *micro = "globalThis.microtask=0;Promise.resolve().then(()=>{microtask=42;});";
        v = Eval(c, micro, std::strlen(micro), "micro.js", EvalGlobal);
        FreeValue(c, v);
        Context *jobs = nullptr;
        while (IsJobPending(rt))
            assert(ExecutePendingJob(rt, &jobs) >= 0);
        v = GetPropertyStr(c, global, "microtask");
        int n = 0;
        ToInt32(c, &n, v);
        assert(n == 42);
        FreeValue(c, v);
        SetModuleLoaderFunc(rt, nullptr, Loader, nullptr);
        Value promise = LoadModule(c, "main.js", "main.js");
        assert(!IsException(promise));
        assert(PromiseState(c, promise) == 1);
        Value ns = PromiseResult(c, promise);
        Value fn = GetPropertyStr(c, ns, "answer");
        v = Call(c, fn, Undefined(), 0, nullptr);
        ToInt32(c, &n, v);
        assert(n == 42);
        FreeValue(c, v);
        FreeValue(c, fn);
        FreeValue(c, ns);
        FreeValue(c, promise);
        const char *cleanup = "globalThis.cleanupCount=0;globalThis.registry=new "
                              "FinalizationRegistry(()=>{cleanupCount++;});(()=>{registry.register({},0);})();";
        v = Eval(c, cleanup, std::strlen(cleanup), "cleanup.js", EvalGlobal);
        assert(!IsException(v));
        FreeValue(c, v);
        int cleaned = 0;
        for (int attempt = 0; attempt < 5 && !cleaned; ++attempt)
        {
            RunGC(rt);
            assert(ExecutePendingJob(rt, &jobs) >= 0);
            v = GetPropertyStr(c, global, "cleanupCount");
            ToInt32(c, &cleaned, v);
            FreeValue(c, v);
        }
        assert(cleaned == 1);
        if (!payloadClass)
            NewClassID(&payloadClass);
        ClassDef def{"SmokePayload", Finalize};
        NewClass(rt, payloadClass, &def);
        Value payload = NewObjectClass(c, payloadClass);
        SetOpaque(payload, reinterpret_cast<void *>(42));
        assert(GetOpaque(payload, payloadClass) == reinterpret_cast<void *>(42));
        FreeValue(c, payload);
        RunGC(rt);
        FreeValue(c, global);
        FreeContext(c);
        FreeRuntime(rt);
    }
    assert(finalized == 3);
    std::puts("V8 engine smoke PASS: callbacks/errors/modules/microtasks/moving-GC "
              "identity/FinalizationRegistry/finalizers/3 runtime "
              "generations");
}
