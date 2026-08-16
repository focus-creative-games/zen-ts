#pragma once

#include "../ZTSCommon.h"

#include <string>
#include <unordered_map>

namespace zts
{
    class JsEnv
    {
    public:
        static void Initialize();
        static void Shutdown();
        static bool IsAlive();

        static JSRuntime* GetRuntime();
        static JSContext* GetContext();
        static int GetGeneration();

        static void DrainPendingJobs();
        static JSValue LoadModuleNamespace(const char* moduleName);
        static JSValue GetModuleExport(const char* moduleName, const char* exportName);

        static void ThrowPendingException();
        static std::string FormatJsValue(JSContext* ctx, JSValue val);

    private:
        static void ClearModuleNamespaceCache();
    };
}
