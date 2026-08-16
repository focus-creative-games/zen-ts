#include "JsGlobalRefs.h"

#include <vector>

namespace zts
{
static std::vector<JSValue> s_refs;
static std::vector<int> s_freeList;
static bool s_initialized = false;

void JsGlobalRefs::Initialize()
{
    s_refs.clear();
    s_freeList.clear();
    s_initialized = true;
}

void JsGlobalRefs::Clear()
{
    s_refs.clear();
    s_freeList.clear();
    s_initialized = false;
}

void JsGlobalRefs::ClearAndFreeAll(JSContext* ctx)
{
    if (ctx != nullptr)
    {
        for (size_t i = 0; i < s_refs.size(); i++)
        {
            if (!JS_IsUndefined(s_refs[i]))
                JS_FreeValue(ctx, s_refs[i]);
        }
    }
    Clear();
}

int JsGlobalRefs::Store(JSContext* ctx, JSValue value)
{
    if (!s_initialized)
        Initialize();

    JSValue dup = JS_DupValue(ctx, value);
    if (!s_freeList.empty())
    {
        int idx = s_freeList.back();
        s_freeList.pop_back();
        s_refs[(size_t)idx] = dup;
        return idx;
    }

    int idx = (int)s_refs.size();
    s_refs.push_back(dup);
    return idx;
}

JSValue JsGlobalRefs::Get(int refIndex)
{
    if (refIndex < 0 || (size_t)refIndex >= s_refs.size())
        return JS_UNDEFINED;
    return s_refs[(size_t)refIndex];
}

void JsGlobalRefs::FreeAndRelease(JSContext* ctx, int refIndex)
{
    if (refIndex < 0 || (size_t)refIndex >= s_refs.size())
        return;
    JSValue v = s_refs[(size_t)refIndex];
    s_refs[(size_t)refIndex] = JS_UNDEFINED;
    s_freeList.push_back(refIndex);
    if (ctx != nullptr && !JS_IsUndefined(v))
        JS_FreeValue(ctx, v);
}
}
