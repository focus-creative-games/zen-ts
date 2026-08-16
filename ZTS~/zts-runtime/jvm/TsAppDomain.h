#pragma once

#include "../ZTSCommon.h"

namespace zts
{
    class TsAppDomain
    {
    public:
        static void Initialize();
        static void InitializeFromManaged(Il2CppDelegate* moduleLoaderDelegate);
        static void ResetFromManaged(Il2CppDelegate* moduleLoaderDelegate);

    private:
        static void InitializeProcessOnce();
        static void InitializeState();
        static void ShutdownState();
    };
}
