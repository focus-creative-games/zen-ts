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

#include "JsGlobalRefs.h"

#include <vector>

namespace zents
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
