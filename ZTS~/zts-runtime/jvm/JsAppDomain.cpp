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

#include "JsAppDomain.h"
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

void JsAppDomain::InitializeProcessOnce()
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

void JsAppDomain::InitializeState()
{
    IL2CPP_ASSERT(!JsEnv::IsAlive());
    JsEnv::Initialize();
}

void JsAppDomain::ShutdownState()
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

void JsAppDomain::Initialize()
{
    InitializeProcessOnce();
    if (!JsEnv::IsAlive())
        InitializeState();
}

void JsAppDomain::InitializeFromManaged(Il2CppDelegate* moduleLoaderDelegate)
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

void JsAppDomain::ResetFromManaged(Il2CppDelegate* moduleLoaderDelegate)
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
