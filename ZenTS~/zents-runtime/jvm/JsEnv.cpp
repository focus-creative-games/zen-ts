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

#include "JsEnv.h"
#include "JsLoader.h"
#include "JsGlobalRefs.h"
#include "JsLib.h"

#include "../bridge/MethodBridge.h"
#include "../marshal/PointerMarshal.h"
#include "../marshal/DelegateMarshal.h"
#include "../mt/TypeRegistry.h"
#include "../mt/AliasXmlTable.h"
#include "../mt/ExtensionXmlTable.h"
#include "../marshal/MarshalAsXmlTable.h"
#include "../utils/JsException.h"
#include "../generated/MarshalBindings.h"
#include "../generated/AliasBindings.h"
#include "../generated/ExtensionBindings.h"

#include <string>

namespace zents
{
static JSRuntime* s_runtime = nullptr;
static JSContext* s_context = nullptr;
static int s_generation = 0;
static std::unordered_map<std::string, JSValue> s_moduleNamespaces;

void JsEnv::ClearModuleNamespaceCache()
{
    if (s_context == nullptr)
    {
        s_moduleNamespaces.clear();
        return;
    }

    for (auto& kv : s_moduleNamespaces)
        JS_FreeValue(s_context, kv.second);
    s_moduleNamespaces.clear();
}

void JsEnv::Initialize()
{
    if (s_context != nullptr)
        JsException::Throw("JsEnv is already initialized");

    s_runtime = JS_NewRuntime();
    if (s_runtime == nullptr)
        JsException::Throw("JS_NewRuntime failed");

    js_std_init_handlers(s_runtime);

    s_context = JS_NewContext(s_runtime);
    if (s_context == nullptr)
    {
        js_std_free_handlers(s_runtime);
        JS_FreeRuntime(s_runtime);
        s_runtime = nullptr;
        JsException::Throw("JS_NewContext failed");
    }

    js_std_add_helpers(s_context, 0, nullptr);
    JsLoader::InstallOnRuntime(s_runtime);
    JsGlobalRefs::Initialize();
    MethodBridge::Initialize();
    RegisterMarshalBindingTables();
    RegisterAliasBindingTables();
    RegisterExtensionBindingTables();
    JsLib::RegisterGlobals(s_context);
    s_generation++;
}

void JsEnv::Shutdown()
{
    if (s_context != nullptr)
    {
        ClearModuleNamespaceCache();
        JsGlobalRefs::ClearAndFreeAll(s_context);
        JsLib::Reset(s_context);

        /* Drop CSharp proxy cache so Dup'd type objects can reach refcount 0. */
        {
            JSValue global = JS_GetGlobalObject(s_context);
            JS_SetPropertyStr(s_context, global, "CSharp", JS_UNDEFINED);
            JS_FreeValue(s_context, global);
        }

        TypeRegistry::Reset(s_context);
        PointerMarshal::Reset();
        DelegateMarshal::Reset();
        MarshalAsXmlTable::Clear();
        AliasXmlTable::Clear();
        ExtensionXmlTable::Clear();
        if (s_runtime != nullptr)
            JS_RunGC(s_runtime);

        JS_FreeContext(s_context);
        s_context = nullptr;
    }
    else
    {
        JsLib::Reset(nullptr);
        TypeRegistry::Reset(nullptr);
        PointerMarshal::Reset();
        DelegateMarshal::Reset();
        MarshalAsXmlTable::Clear();
        AliasXmlTable::Clear();
        ExtensionXmlTable::Clear();
    }

    if (s_runtime != nullptr)
    {
        js_std_free_handlers(s_runtime);
        JS_FreeRuntime(s_runtime);
        s_runtime = nullptr;
    }
}

bool JsEnv::IsAlive()
{
    return s_context != nullptr;
}

JSRuntime* JsEnv::GetRuntime()
{
    return s_runtime;
}

JSContext* JsEnv::GetContext()
{
    return s_context;
}

int JsEnv::GetGeneration()
{
    return s_generation;
}

void JsEnv::DrainPendingJobs()
{
    if (s_runtime == nullptr)
        return;

    JSContext* jobCtx = nullptr;
    while (JS_IsJobPending(s_runtime))
    {
        int status = JS_ExecutePendingJob(s_runtime, &jobCtx);
        if (status < 0)
        {
            JSContext* errCtx = jobCtx != nullptr ? jobCtx : s_context;
            JSValue ex = JS_GetException(errCtx);
            std::string msg = FormatJsValue(errCtx, ex);
            JS_FreeValue(errCtx, ex);
            JsException::ThrowFormat("pending job failed: %s", msg.c_str());
        }
    }
}

JSValue JsEnv::LoadModuleNamespace(const char* moduleName)
{
    if (s_context == nullptr)
        JsException::Throw("ZenTS is not initialized");

    auto it = s_moduleNamespaces.find(moduleName);
    if (it != s_moduleNamespaces.end())
        return JS_DupValue(s_context, it->second);

    JSValue promise = JS_LoadModule(s_context, moduleName, moduleName);
    if (JS_IsException(promise))
        ThrowPendingException();

    DrainPendingJobs();

    int state = JS_PromiseState(s_context, promise);
    if (state == JS_PROMISE_PENDING)
    {
        JS_FreeValue(s_context, promise);
        JsException::ThrowFormat("module '%s' promise still pending", moduleName);
    }

    JSValue ns = JS_PromiseResult(s_context, promise);
    JS_FreeValue(s_context, promise);

    if (state == JS_PROMISE_REJECTED)
    {
        std::string msg = FormatJsValue(s_context, ns);
        JS_FreeValue(s_context, ns);
        JsException::ThrowFormat("module '%s' rejected: %s", moduleName, msg.c_str());
    }

    if (JS_IsException(ns))
        ThrowPendingException();

    s_moduleNamespaces[moduleName] = JS_DupValue(s_context, ns);
    return ns;
}

JSValue JsEnv::GetModuleExport(const char* moduleName, const char* exportName)
{
    JSValue ns = LoadModuleNamespace(moduleName);
    JSValue exp = JS_GetPropertyStr(s_context, ns, exportName);
    JS_FreeValue(s_context, ns);

    if (JS_IsException(exp))
        ThrowPendingException();

    if (!JS_IsFunction(s_context, exp))
    {
        JS_FreeValue(s_context, exp);
        JsException::ThrowFormat("export '%s.%s' is not callable", moduleName, exportName);
    }

    return exp;
}

void JsEnv::ThrowPendingException()
{
    JSValue ex = JS_GetException(s_context);
    std::string msg = FormatJsValue(s_context, ex);
    JS_FreeValue(s_context, ex);
    JsException::ThrowFormat("%s", msg.c_str());
}

std::string JsEnv::FormatJsValue(JSContext* ctx, JSValue val)
{
    if (JS_IsUndefined(val))
        return "undefined";
    if (JS_IsNull(val))
        return "null";

    if (JS_IsObject(val))
    {
        JSValue msg = JS_GetPropertyStr(ctx, val, "message");
        if (JS_IsString(msg))
        {
            const char* cstr = JS_ToCString(ctx, msg);
            std::string result = cstr != nullptr ? cstr : "JS exception";
            if (cstr != nullptr)
                JS_FreeCString(ctx, cstr);
            JS_FreeValue(ctx, msg);
            if (!result.empty())
                return result;
        }
        else
        {
            JS_FreeValue(ctx, msg);
        }
    }

    const char* cstr = JS_ToCString(ctx, val);
    if (cstr == nullptr)
        return "JS exception";
    std::string result = cstr;
    JS_FreeCString(ctx, cstr);
    return result;
}
}
