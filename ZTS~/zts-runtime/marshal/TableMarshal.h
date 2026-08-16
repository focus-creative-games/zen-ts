#pragma once

#include "MarshalDefs.h"

#include <string>
#include <vector>

namespace zts
{
struct TableMarshalMeta : MarshalMetaInfo
{
    std::vector<std::string> members;
    std::vector<bool> optional;
};

class TableMarshal
{
public:
    static const MarshalMetaInfo* Create(
        const Il2CppType* type,
        Il2CppClass* klass,
        const std::vector<std::string>& memberSpecs,
        MarshalAsKind kind);

    static void Js2CsTable(JSContext* ctx, JSValueConst value, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsTable(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    /** Pop Members.Length consecutive JS args starting at jsStart into a struct. */
    static void Js2CsUnpacked(
        JSContext* ctx, JSValueConst* argv, int jsStart, int argc, void* address, const MarshalMetaInfo* meta);
    static JSValue Cs2JsUnpacked(JSContext* ctx, void* address, const MarshalMetaInfo* meta);

    static int GetJsArgSlotCount(const MarshalMetaInfo* meta);
};
}
