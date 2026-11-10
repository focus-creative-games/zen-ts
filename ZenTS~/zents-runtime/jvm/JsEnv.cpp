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
#include "utils/Exception.h"
#include "vm/Thread.h"
#include "vm/Exception.h"
#include <cstdio>

namespace zents
{
static js::Runtime* s_runtime = nullptr;
static js::Context* s_context = nullptr;
static int s_generation = 0;
static std::unordered_map<std::string, js::Value> s_moduleNamespaces;

static void EnsureMainThread()
{
    static thread_local const bool isMainThread=il2cpp::vm::Thread::Current()!=nullptr
        &&il2cpp::vm::Thread::Current()==il2cpp::vm::Thread::Main();
    if(!isMainThread)il2cpp::vm::Exception::Raise(il2cpp::vm::Exception::GetInvalidOperationException(
        "ZenTS can only be called from the Unity main thread."));
}

static js::Value TranslateClrException(js::Context* ctx,std::exception_ptr exception)
{
    try { std::rethrow_exception(exception); }
    catch(const Il2CppExceptionWrapper& error)
    {
        std::string message=il2cpp::utils::Exception::FormatException(error.ex);
        return js::ThrowInternalError(ctx,"zents: %s",message.c_str());
    }
    catch(const std::exception& error){return js::ThrowInternalError(ctx,"zents: %s",error.what());}
    catch(...){return js::ThrowInternalError(ctx,"zents: unknown CLR callback failure");}
}

void JsEnv::ClearModuleNamespaceCache()
{
    if (s_context == nullptr)
    {
        s_moduleNamespaces.clear();
        return;
    }

    for (auto& kv : s_moduleNamespaces)
        js::FreeValue(s_context, kv.second);
    s_moduleNamespaces.clear();
}

void JsEnv::Initialize()
{
    EnsureMainThread();
    if (s_context != nullptr)
        JsException::Throw("JsEnv is already initialized");

    s_runtime = js::NewRuntime();
    if(s_runtime)js::SetExternalExceptionHandler(s_runtime,TranslateClrException);
    if (s_runtime == nullptr)
        JsException::Throw("JS_NewRuntime failed");

    js::StdInitHandlers(s_runtime);

    s_context = js::NewContext(s_runtime);
    if (s_context == nullptr)
    {
        js::StdFreeHandlers(s_runtime);
        js::FreeRuntime(s_runtime);
        s_runtime = nullptr;
        JsException::Throw("JS_NewContext failed");
    }

    js::StdAddHelpers(s_context, 0, nullptr);
    std::fprintf(stdout, "[ZenTS] Player engine=%s\n", js::GetBackend() == js::Backend::V8 ? "V8 (static)" : "HostJs (platform VM)");
    std::fflush(stdout);
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
    EnsureMainThread();
    if (s_context != nullptr)
    {
        ClearModuleNamespaceCache();
        JsGlobalRefs::ClearAndFreeAll(s_context);
        JsLib::Reset(s_context);

        /* Drop CSharp proxy cache so Dup'd type objects can reach refcount 0. */
        {
            js::Value global = js::GetGlobalObject(s_context);
            js::SetPropertyStr(s_context, global, "CSharp", js::Undefined());
            js::FreeValue(s_context, global);
        }

        TypeRegistry::Reset(s_context);
        PointerMarshal::Reset();
        DelegateMarshal::Reset();
        MarshalAsXmlTable::Clear();
        AliasXmlTable::Clear();
        ExtensionXmlTable::Clear();
        if (s_runtime != nullptr)
            js::RunGC(s_runtime);

        js::FreeContext(s_context);
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
        js::StdFreeHandlers(s_runtime);
        js::FreeRuntime(s_runtime);
        s_runtime = nullptr;
    }
}

bool JsEnv::IsAlive()
{
    return s_context != nullptr;
}

js::Runtime* JsEnv::GetRuntime()
{
    return s_runtime;
}

js::Context* JsEnv::GetContext()
{
    EnsureMainThread();
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

    js::Context* jobCtx = nullptr;
    bool pollForeground = js::GetBackend() == js::Backend::V8;
    while (pollForeground || js::IsJobPending(s_runtime))
    {
        pollForeground = false;
        int status = js::ExecutePendingJob(s_runtime, &jobCtx);
        if (status < 0)
        {
            js::Context* errCtx = jobCtx != nullptr ? jobCtx : s_context;
            js::Value ex = js::GetException(errCtx);
            std::string msg = FormatJsValue(errCtx, ex);
            js::FreeValue(errCtx, ex);
            JsException::ThrowFormat("pending job failed: %s", msg.c_str());
        }
    }
}

js::Value JsEnv::LoadModuleNamespace(const char* moduleName)
{
    if (s_context == nullptr)
        JsException::Throw("ZenTS is not initialized");

    auto it = s_moduleNamespaces.find(moduleName);
    if (it != s_moduleNamespaces.end())
        return js::DupValue(s_context, it->second);

    js::Value promise = js::LoadModule(s_context, moduleName, moduleName);
    if (js::IsException(promise))
        ThrowPendingException();

    DrainPendingJobs();

    int state = js::PromiseState(s_context, promise);
    if (state == js::PromisePending)
    {
        js::FreeValue(s_context, promise);
        JsException::ThrowFormat("module '%s' promise still pending", moduleName);
    }

    js::Value ns = js::PromiseResult(s_context, promise);
    js::FreeValue(s_context, promise);

    if (state == js::PromiseRejected)
    {
        std::string msg = FormatJsValue(s_context, ns);
        js::FreeValue(s_context, ns);
        JsException::ThrowFormat("module '%s' rejected: %s", moduleName, msg.c_str());
    }

    if (js::IsException(ns))
        ThrowPendingException();

    s_moduleNamespaces[moduleName] = js::DupValue(s_context, ns);
    return ns;
}

js::Value JsEnv::GetModuleExport(const char* moduleName, const char* exportName)
{
    js::Value ns = LoadModuleNamespace(moduleName);
    js::Value exp = js::GetPropertyStr(s_context, ns, exportName);
    js::FreeValue(s_context, ns);

    if (js::IsException(exp))
        ThrowPendingException();

    if (!js::IsFunction(s_context, exp))
    {
        js::FreeValue(s_context, exp);
        JsException::ThrowFormat("export '%s.%s' is not callable", moduleName, exportName);
    }

    return exp;
}

void JsEnv::ThrowPendingException()
{
    js::Value ex = js::GetException(s_context);
    std::string msg = FormatJsValue(s_context, ex);
    js::FreeValue(s_context, ex);
    JsException::ThrowFormat("%s", msg.c_str());
}

std::string JsEnv::FormatJsValue(js::Context* ctx, js::Value val)
{
    if (js::IsUndefined(val))
        return "undefined";
    if (js::IsNull(val))
        return "null";

    if (js::IsObject(val))
    {
        js::Value msg = js::GetPropertyStr(ctx, val, "message");
        if (js::IsString(msg))
        {
            const char* cstr = js::ToCString(ctx, msg);
            std::string result = cstr != nullptr ? cstr : "JS exception";
            if (cstr != nullptr)
                js::FreeCString(ctx, cstr);
            js::FreeValue(ctx, msg);
            if (!result.empty())
                return result;
        }
        else
        {
            js::FreeValue(ctx, msg);
        }
    }

    const char* cstr = js::ToCString(ctx, val);
    if (cstr == nullptr)
        return "JS exception";
    std::string result = cstr;
    js::FreeCString(ctx, cstr);
    return result;
}
}
