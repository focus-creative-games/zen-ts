#pragma once

#include "../ZTSCommon.h"

namespace zts
{
    class JsLoader
    {
    public:
        static void RegisterRoots();
        static void SetModuleLoader(Il2CppDelegate* moduleLoader);
        static Il2CppDelegate* GetModuleLoader();
        static void Clear();
        static void InstallOnRuntime(JSRuntime* rt);

    private:
        static JSModuleDef* ModuleLoaderCallback(JSContext* ctx, const char* moduleName, void* opaque);
        static void LoadModuleSource(const char* moduleName, std::string& source);
    };
}
