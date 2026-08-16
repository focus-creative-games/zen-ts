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

#include "JsLoader.h"
#include "JsEnv.h"

#include "../utils/JsException.h"

#include "vm/Runtime.h"
#include "vm/String.h"
#include "vm/Array.h"
#include "vm/Exception.h"
#include "gc/GarbageCollector.h"
#include "utils/StringUtils.h"

#include <string>

namespace zts
{
static Il2CppDelegate* s_ModuleLoader = nullptr;
static const MethodInfo* s_moduleLoaderInvoker = nullptr;

void JsLoader::RegisterRoots()
{
    il2cpp::gc::GarbageCollector::RegisterRoot((char*)&s_ModuleLoader, sizeof(Il2CppDelegate*));
}

void JsLoader::SetModuleLoader(Il2CppDelegate* moduleLoader)
{
    IL2CPP_ASSERT(moduleLoader != nullptr);
    s_ModuleLoader = moduleLoader;
    s_moduleLoaderInvoker = il2cpp::vm::Runtime::GetDelegateInvoke(s_ModuleLoader->object.klass);
    IL2CPP_ASSERT(s_moduleLoaderInvoker != nullptr);
}

Il2CppDelegate* JsLoader::GetModuleLoader()
{
    return s_ModuleLoader;
}

void JsLoader::Clear()
{
    s_ModuleLoader = nullptr;
    s_moduleLoaderInvoker = nullptr;
}

void JsLoader::LoadModuleSource(const char* moduleName, std::string& source)
{
    if (s_ModuleLoader == nullptr)
        JsException::Throw("JS module loader is not configured");

    Il2CppString* moduleNameStr = il2cpp::vm::String::New(moduleName);
    void* params[1] = { moduleNameStr };
    Il2CppException* exc = nullptr;
    Il2CppObject* result = il2cpp::vm::Runtime::Invoke(s_moduleLoaderInvoker, s_ModuleLoader, params, &exc);
    if (exc != nullptr)
        JsException::Throw(exc);

    if (result == nullptr)
        JsException::ThrowFormat("module loader returned null for '%s'", moduleName);

    switch (result->klass->byval_arg.type)
    {
    case IL2CPP_TYPE_STRING:
    {
        Il2CppString* sourceStr = (Il2CppString*)result;
        source = il2cpp::utils::StringUtils::Utf16ToUtf8(
            il2cpp::utils::StringUtils::GetChars(sourceStr),
            il2cpp::utils::StringUtils::GetLength(sourceStr));
        return;
    }
    case IL2CPP_TYPE_SZARRAY:
    {
        Il2CppArray* bytes = (Il2CppArray*)result;
        source = std::string(
            (char*)il2cpp::vm::Array::GetFirstElementAddress(bytes),
            (size_t)bytes->max_length);
        return;
    }
    default:
        JsException::ThrowFormat("module loader for '%s' must return string source", moduleName);
    }
}

JSModuleDef* JsLoader::ModuleLoaderCallback(JSContext* ctx, const char* moduleName, void* /*opaque*/)
{
    try
    {
        std::string source;
        LoadModuleSource(moduleName, source);

        JSValue compiled = JS_Eval(
            ctx,
            source.c_str(),
            source.size(),
            moduleName,
            JS_EVAL_TYPE_MODULE | JS_EVAL_FLAG_COMPILE_ONLY);

        if (JS_IsException(compiled))
        {
            // Leave exception on context for QuickJS.
            return nullptr;
        }

        JSModuleDef* def = (JSModuleDef*)JS_VALUE_GET_PTR(compiled);
        JS_FreeValue(ctx, compiled);
        return def;
    }
    catch (Il2CppExceptionWrapper& e)
    {
        JS_Throw(ctx, JS_NewString(ctx, "zts: module loader threw managed exception"));
        (void)e;
        return nullptr;
    }
    catch (...)
    {
        JS_Throw(ctx, JS_NewString(ctx, "zts: module loader failed"));
        return nullptr;
    }
}

void JsLoader::InstallOnRuntime(JSRuntime* rt)
{
    JS_SetModuleLoaderFunc(rt, nullptr, ModuleLoaderCallback, nullptr);
}
}
