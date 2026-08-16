#include "JsInternalCalls.h"
#include "TsAppDomain.h"
#include "JsEnv.h"
#include "JsGlobalRefs.h"

#include "../marshal/DelegateMarshal.h"
#include "../utils/TsException.h"
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

static void TsIl2CppAppDomain_InitializeInternal(Il2CppDelegate* moduleLoader)
{
    TsAppDomain::InitializeFromManaged(moduleLoader);
}

static void TsIl2CppAppDomain_ResetInternal(Il2CppDelegate* moduleLoader)
{
    TsAppDomain::ResetFromManaged(moduleLoader);
}

static void TsIl2CppAppDomain_ProcessPendingRefReleases()
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

static void TsMethod_AddPendingRef(JSContext* /*ctx*/, int32_t refIndex)
{
    std::lock_guard<std::mutex> lock(s_pendingMutex);
    s_pendingRefReleases.push_back(refIndex);
}

static Il2CppObject* TsIl2CppAppDomain_GetFunctionInternal(
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
        TsException::Throw("ZTS is not initialized. Call TsAppDomain.Initialize first.");

    Il2CppClass* delegateClass = il2cpp::vm::Class::FromIl2CppType(delegateTypeObj->type);
    il2cpp::vm::Class::Init(delegateClass);
    if (!il2cpp::vm::Class::IsSubclassOf(delegateClass, il2cpp_defaults.multicastdelegate_class, false))
    {
        TsException::ThrowFormat(
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
        "ZTS.TsIl2CppAppDomain::InitializeInternal",
        (Il2CppMethodPointer)TsIl2CppAppDomain_InitializeInternal);
    il2cpp::vm::InternalCalls::Add(
        "ZTS.TsIl2CppAppDomain::ResetInternal",
        (Il2CppMethodPointer)TsIl2CppAppDomain_ResetInternal);
    il2cpp::vm::InternalCalls::Add(
        "ZTS.TsIl2CppAppDomain::ProcessPendingRefReleases",
        (Il2CppMethodPointer)TsIl2CppAppDomain_ProcessPendingRefReleases);
    il2cpp::vm::InternalCalls::Add(
        "ZTS.TsIl2CppAppDomain::GetFunctionInternal",
        (Il2CppMethodPointer)TsIl2CppAppDomain_GetFunctionInternal);
    il2cpp::vm::InternalCalls::Add(
        "ZTS.TsMethod::AddPendingRef",
        (Il2CppMethodPointer)TsMethod_AddPendingRef);
}
}
