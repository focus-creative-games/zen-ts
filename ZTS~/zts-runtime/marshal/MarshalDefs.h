#pragma once

#include "../ZTSCommon.h"

namespace zts
{
struct MarshalMetaInfo;

typedef void (*FnMarshalJs2Cs)(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
typedef JSValue (*FnMarshalCs2Js)(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

/* TsMarshalType special forms used by Table / UnpackedValues. */
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

struct MethodMarshalCtx
{
    const MethodInfo* method;
    FnJs2CsInvoker js2CsInvoker;
    const MarshalMetaInfo** paramsMeta;
    const MarshalMetaInfo* retMeta;
    int32_t arity;    /* max JS argv slots */
    int32_t minArity; /* required JS argv slots (optional trailing omitted) */
    bool sealed;
    bool isExtension;
    bool hasParamsArray; /* trailing params T[] — single JS slot, may be omitted */
};
}
