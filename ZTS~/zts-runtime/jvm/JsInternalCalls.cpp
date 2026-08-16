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

#include "JsInternalCalls.h"
#include "JsAppDomain.h"
#include "JsEnv.h"
#include "JsGlobalRefs.h"

#include "../marshal/DelegateMarshal.h"
#include "../utils/JsException.h"
#include "../utils/MetadataUtil.h"

#include "vm/InternalCalls.h"
#include "vm/Exception.h"
#include "vm/Class.h"
#include "vm/Type.h"
#include "utils/StringUtils.h"

#include <mutex>
#include <vector>

namespace zts
{
static std::mutex s_pendingMutex;
static std::vector<int> s_pendingRefReleases;

static void JsIl2CppAppDomain_InitializeInternal(Il2CppDelegate* moduleLoader)
{
    JsAppDomain::InitializeFromManaged(moduleLoader);
}

static void JsIl2CppAppDomain_ResetInternal(Il2CppDelegate* moduleLoader)
{
    JsAppDomain::ResetFromManaged(moduleLoader);
}

static void JsIl2CppAppDomain_ProcessPendingRefReleases()
{
    std::vector<int> local;
    {
        std::lock_guard<std::mutex> lock(s_pendingMutex);
        local.swap(s_pendingRefReleases);
    }

    JSContext* ctx = JsEnv::GetContext();
    for (int refIndex : local)
        JsGlobalRefs::FreeAndRelease(ctx, refIndex);
}

static void JsMethod_AddPendingRef(JSContext* /*ctx*/, int32_t refIndex)
{
    std::lock_guard<std::mutex> lock(s_pendingMutex);
    s_pendingRefReleases.push_back(refIndex);
}

static Il2CppObject* JsIl2CppAppDomain_GetFunctionInternal(
    Il2CppReflectionType* delegateTypeObj,
    Il2CppString* jsModule,
    Il2CppString* jsExportName)
{
    if (delegateTypeObj == nullptr)
    {
        il2cpp::vm::Exception::Raise(
            il2cpp::vm::Exception::GetArgumentNullException("delegateType"));
    }
    if (jsModule == nullptr || jsExportName == nullptr)
    {
        il2cpp::vm::Exception::Raise(
            il2cpp::vm::Exception::GetArgumentException("jsModule/jsExportName", "must be non-null"));
    }

    if (!JsEnv::IsAlive())
        JsException::Throw("ZTS is not initialized. Call JsAppDomain.Initialize first.");

    Il2CppClass* delegateClass = il2cpp::vm::Class::FromIl2CppType(delegateTypeObj->type);
    il2cpp::vm::Class::Init(delegateClass);
    if (!il2cpp::vm::Class::IsSubclassOf(delegateClass, il2cpp_defaults.multicastdelegate_class, false))
    {
        JsException::ThrowFormat(
            "Type '%s' is not a MulticastDelegate",
            MetadataUtil::GetTypeFullName(delegateClass));
    }

    std::string moduleName = il2cpp::utils::StringUtils::Utf16ToUtf8(
        il2cpp::utils::StringUtils::GetChars(jsModule));
    std::string exportName = il2cpp::utils::StringUtils::Utf16ToUtf8(
        il2cpp::utils::StringUtils::GetChars(jsExportName));
    if (moduleName.empty() || exportName.empty())
    {
        il2cpp::vm::Exception::Raise(
            il2cpp::vm::Exception::GetArgumentException(
                "jsModule/jsExportName", "must be non-empty"));
    }

    JSContext* ctx = JsEnv::GetContext();
    JSValue func = JsEnv::GetModuleExport(moduleName.c_str(), exportName.c_str());
    int funcRef = JsGlobalRefs::Store(ctx, func);
    JS_FreeValue(ctx, func);

    Il2CppDelegate* del = DelegateMarshal::CreateFromFuncRef(ctx, delegateClass, funcRef);
    return reinterpret_cast<Il2CppObject*>(del);
}

void JsInternalCalls::RegisterCoreInternalCalls()
{
    il2cpp::vm::InternalCalls::Add(
        "ZTS.JsIl2CppAppDomain::InitializeInternal",
        (Il2CppMethodPointer)JsIl2CppAppDomain_InitializeInternal);
    il2cpp::vm::InternalCalls::Add(
        "ZTS.JsIl2CppAppDomain::ResetInternal",
        (Il2CppMethodPointer)JsIl2CppAppDomain_ResetInternal);
    il2cpp::vm::InternalCalls::Add(
        "ZTS.JsIl2CppAppDomain::ProcessPendingRefReleases",
        (Il2CppMethodPointer)JsIl2CppAppDomain_ProcessPendingRefReleases);
    il2cpp::vm::InternalCalls::Add(
        "ZTS.JsIl2CppAppDomain::GetFunctionInternal",
        (Il2CppMethodPointer)JsIl2CppAppDomain_GetFunctionInternal);
    il2cpp::vm::InternalCalls::Add(
        "ZTS.JsMethod::AddPendingRef",
        (Il2CppMethodPointer)JsMethod_AddPendingRef);
}
}
