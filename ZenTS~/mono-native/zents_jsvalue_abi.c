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

/*
 * Win64 ABI shims: MSVC passes/returns 16-byte JSValue by hidden pointer.
 * Mono P/Invoke often mismatches that; expose pointer-based wrappers instead.
 */
#include "quickjs.h"
#include <string.h>

#ifdef _WIN32
#define ZENTS_EXPORT __declspec(dllexport)
#else
#define ZENTS_EXPORT
#endif

ZENTS_EXPORT void zents_JS_Eval(JSContext *ctx, const char *input, size_t input_len,
                            const char *filename, int eval_flags, JSValue *out)
{
    *out = JS_Eval(ctx, input, input_len, filename, eval_flags);
}

ZENTS_EXPORT void zents_JS_Call(JSContext *ctx, JSValue *func_obj, JSValue *this_obj,
                            int argc, JSValue *argv, JSValue *out)
{
    *out = JS_Call(ctx, *func_obj, *this_obj, argc, argv);
}

ZENTS_EXPORT void zents_JS_GetException(JSContext *ctx, JSValue *out)
{
    *out = JS_GetException(ctx);
}

ZENTS_EXPORT void zents_JS_Throw(JSContext *ctx, JSValue *obj, JSValue *out)
{
    *out = JS_Throw(ctx, *obj);
}

ZENTS_EXPORT void zents_JS_NewObject(JSContext *ctx, JSValue *out)
{
    *out = JS_NewObject(ctx);
}

ZENTS_EXPORT void zents_JS_GetGlobalObject(JSContext *ctx, JSValue *out)
{
    *out = JS_GetGlobalObject(ctx);
}

ZENTS_EXPORT int zents_JS_SetPropertyStr(JSContext *ctx, JSValue *this_obj,
                                     const char *prop, JSValue *val)
{
    return JS_SetPropertyStr(ctx, *this_obj, prop, *val);
}

ZENTS_EXPORT void zents_JS_GetPropertyStr(JSContext *ctx, JSValue *this_obj,
                                      const char *prop, JSValue *out)
{
    *out = JS_GetPropertyStr(ctx, *this_obj, prop);
}

ZENTS_EXPORT int zents_JS_ToInt32(JSContext *ctx, int *pres, JSValue *val)
{
    return JS_ToInt32(ctx, pres, *val);
}

ZENTS_EXPORT int zents_JS_ToFloat64(JSContext *ctx, double *pres, JSValue *val)
{
    return JS_ToFloat64(ctx, pres, *val);
}

ZENTS_EXPORT int zents_JS_ToBool(JSContext *ctx, JSValue *val)
{
    return JS_ToBool(ctx, *val);
}

ZENTS_EXPORT const char *zents_JS_ToCStringLen2(JSContext *ctx, size_t *plen,
                                           JSValue *val, int cesu8)
{
    return JS_ToCStringLen2(ctx, plen, *val, cesu8);
}

ZENTS_EXPORT void zents_JS_NewStringLen(JSContext *ctx, const char *str, size_t len,
                                    JSValue *out)
{
    *out = JS_NewStringLen(ctx, str, len);
}

ZENTS_EXPORT int zents_JS_IsFunction(JSContext *ctx, JSValue *val)
{
    return JS_IsFunction(ctx, *val);
}

ZENTS_EXPORT void zents_JS_EvalFunction(JSContext *ctx, JSValue *fun_obj, JSValue *out)
{
    *out = JS_EvalFunction(ctx, *fun_obj);
}

ZENTS_EXPORT int zents_JS_ResolveModule(JSContext *ctx, JSValue *fun_obj)
{
    return JS_ResolveModule(ctx, *fun_obj);
}

ZENTS_EXPORT void zents___JS_FreeValue(JSContext *ctx, JSValue *v)
{
    __JS_FreeValue(ctx, *v);
}

ZENTS_EXPORT void zents_JS_NewCFunction2(JSContext *ctx, JSCFunction *func, const char *name,
                                     int length, int cproto, int magic, JSValue *out)
{
    *out = JS_NewCFunction2(ctx, func, name, length, (JSCFunctionEnum)cproto, magic);
}

ZENTS_EXPORT void zents_JS_LoadModule(JSContext *ctx, const char *basename,
                                  const char *filename, JSValue *out)
{
    *out = JS_LoadModule(ctx, basename, filename);
}

ZENTS_EXPORT void zents_JS_GetModuleNamespace(JSContext *ctx, JSModuleDef *m, JSValue *out)
{
    *out = JS_GetModuleNamespace(ctx, m);
}

ZENTS_EXPORT int zents_JS_PromiseState(JSContext *ctx, JSValue *promise)
{
    return (int)JS_PromiseState(ctx, *promise);
}

ZENTS_EXPORT void zents_JS_PromiseResult(JSContext *ctx, JSValue *promise, JSValue *out)
{
    *out = JS_PromiseResult(ctx, *promise);
}

ZENTS_EXPORT void zents_JS_NewError(JSContext *ctx, JSValue *out)
{
    *out = JS_NewError(ctx);
}

ZENTS_EXPORT int zents_JS_DefinePropertyValueStr(JSContext *ctx, JSValue *this_obj,
                                             const char *prop, JSValue *val, int flags)
{
    return JS_DefinePropertyValueStr(ctx, *this_obj, prop, *val, flags);
}

ZENTS_EXPORT int zents_JS_IsArray(JSContext *ctx, JSValue *val)
{
    return JS_IsArray(ctx, *val);
}

ZENTS_EXPORT void zents_JS_NewArray(JSContext *ctx, JSValue *out)
{
    *out = JS_NewArray(ctx);
}

ZENTS_EXPORT int zents_JS_SetPropertyUint32(JSContext *ctx, JSValue *this_obj,
                                        uint32_t idx, JSValue *val)
{
    return JS_SetPropertyUint32(ctx, *this_obj, idx, *val);
}

ZENTS_EXPORT void zents_JS_GetPropertyUint32(JSContext *ctx, JSValue *this_obj,
                                         uint32_t idx, JSValue *out)
{
    *out = JS_GetPropertyUint32(ctx, *this_obj, idx);
}

ZENTS_EXPORT void zents_JS_NewObjectProto(JSContext *ctx, JSValue *proto, JSValue *out)
{
    *out = JS_NewObjectProto(ctx, *proto);
}

ZENTS_EXPORT int zents_JS_SetPrototype(JSContext *ctx, JSValue *obj, JSValue *proto)
{
    return JS_SetPrototype(ctx, *obj, *proto);
}

ZENTS_EXPORT int zents_JS_SetConstructor(JSContext *ctx, JSValue *func_obj, JSValue *proto)
{
    return JS_SetConstructor(ctx, *func_obj, *proto);
}

ZENTS_EXPORT int zents_JS_SetConstructorBit(JSContext *ctx, JSValue *func_obj, int val)
{
    return JS_SetConstructorBit(ctx, *func_obj, val);
}

ZENTS_EXPORT int zents_JS_DefinePropertyGetSet(JSContext *ctx, JSValue *this_obj,
                                          JSAtom prop, JSValue *getter, JSValue *setter,
                                          int flags)
{
    return JS_DefinePropertyGetSet(ctx, *this_obj, prop, *getter, *setter, flags);
}

ZENTS_EXPORT JSAtom zents_JS_NewAtom(JSContext *ctx, const char *str)
{
    return JS_NewAtom(ctx, str);
}

ZENTS_EXPORT void zents_JS_FreeAtom(JSContext *ctx, JSAtom atom)
{
    JS_FreeAtom(ctx, atom);
}
