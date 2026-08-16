#include "TsAppDomain.h"
#include "JsInternalCalls.h"
#include "JsEnv.h"
#include "JsLoader.h"
#include "JsGlobalRefs.h"

#include "../utils/MetadataUtil.h"

#include "vm/Exception.h"

#include <cstdlib>

#if defined(_MSC_VER)
#include <csignal>
#endif

namespace zts
{
static bool s_processInitialized = false;
static bool s_hostInitialized = false;

#if defined(_MSC_VER)
static void ZtsOnSigAbrt(int)
{
    /* Best-effort breadcrumb if something still calls abort() outside QuickJS hook. */
    FILE* f = std::fopen("zts_il2cpp_assert.log", "a");
    if (f != nullptr)
    {
        std::fputs("========== SIGABRT ==========\n", f);
        std::fflush(f);
        std::fclose(f);
    }
    _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
    _exit(3);
}
#endif

void TsAppDomain::InitializeProcessOnce()
{
    if (s_processInitialized)
        return;

#if defined(_MSC_VER)
    /* Suppress CRT abort modal for any remaining abort() outside QuickJS redefine. */
    _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
    std::signal(SIGABRT, ZtsOnSigAbrt);
#endif

    MetadataUtil::Initialize();
    JsLoader::RegisterRoots();
    JsInternalCalls::RegisterCoreInternalCalls();
    s_processInitialized = true;
}

void TsAppDomain::InitializeState()
{
    IL2CPP_ASSERT(!JsEnv::IsAlive());
    JsEnv::Initialize();
}

void TsAppDomain::ShutdownState()
{
    if (!JsEnv::IsAlive())
    {
        s_hostInitialized = false;
        return;
    }

    JsEnv::Shutdown();
    JsLoader::Clear();
    s_hostInitialized = false;
}

void TsAppDomain::Initialize()
{
    InitializeProcessOnce();
    if (!JsEnv::IsAlive())
        InitializeState();
}

void TsAppDomain::InitializeFromManaged(Il2CppDelegate* moduleLoaderDelegate)
{
    if (moduleLoaderDelegate == nullptr)
    {
        il2cpp::vm::Exception::Raise(
            il2cpp::vm::Exception::GetArgumentNullException("moduleLoader"));
    }

    InitializeProcessOnce();
    if (!JsEnv::IsAlive())
        InitializeState();

    if (s_hostInitialized)
    {
        il2cpp::vm::Exception::Raise(
            il2cpp::vm::Exception::GetInvalidOperationException(
                "ZTS already initialized. Call Reset to rebuild the JS context."));
    }

    JsLoader::SetModuleLoader(moduleLoaderDelegate);
    s_hostInitialized = true;
}

void TsAppDomain::ResetFromManaged(Il2CppDelegate* moduleLoaderDelegate)
{
    if (moduleLoaderDelegate == nullptr)
    {
        il2cpp::vm::Exception::Raise(
            il2cpp::vm::Exception::GetArgumentNullException("moduleLoader"));
    }

    InitializeProcessOnce();
    ShutdownState();
    InitializeState();
    JsLoader::SetModuleLoader(moduleLoaderDelegate);
    s_hostInitialized = true;
}
}
