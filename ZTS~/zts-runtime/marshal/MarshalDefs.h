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

#include "../ZTSCommon.h"

namespace zts
{
struct MarshalMetaInfo;

typedef void (*FnMarshalJs2Cs)(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
typedef JSValue (*FnMarshalCs2Js)(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

/* JsMarshalType special forms used by Table / UnpackedValues. */
enum class MarshalAsKind : uint8_t
{
    None = 0,
    UnpackedValues = 4,
    Table = 5,
};

struct MarshalMetaInfo
{
    FnMarshalJs2Cs js2csWriter;
    FnMarshalCs2Js cs2jsWriter;
    const Il2CppType* type;
    Il2CppClass* typeKlass;
    int32_t size;
    bool passByValue; // true → params[i] is the pointer value itself (ref types)
    int32_t jsArgSlots; // JS argv slots consumed (UnpackedValues = Members.Length)
    MarshalAsKind marshalAsKind;
};

struct MethodMarshalCtx;

typedef JSValue (*FnJs2CsInvoker)(
    JSContext* ctx,
    void* target,
    int argc,
    JSValueConst* argv,
    const MethodInfo* method,
    const MethodMarshalCtx* mctx);

/** Trailing C# default params materialized once at bind (no metadata blob parse per call). */
struct MethodDefaultArgs
{
    uint8_t firstDefaultParamIndex;
    uint8_t defaultParamCount;
    void** defaultValueSlots;          /* [defaultParamCount] valuetype native buffers */
    Il2CppObject** defaultObjectSlots; /* [defaultParamCount]; nullptr if no reference defaults */
};

struct MethodMarshalCtx
{
    const MethodInfo* method;
    FnJs2CsInvoker js2CsInvoker;
    const MarshalMetaInfo** paramsMeta;
    const MarshalMetaInfo* retMeta;
    const MethodDefaultArgs* defaults; /* nullptr when method has no optional defaults */
    int32_t arity;    /* max JS argv slots */
    int32_t minArity; /* required JS argv slots (optional trailing omitted) */
    bool sealed;
    bool isExtension;
    bool hasParamsArray; /* trailing params T[] — single JS slot, may be omitted */
};
}
