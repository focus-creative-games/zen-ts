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

#pragma once

#include "../ZenTSCommon.h"

#include <string>
#include <unordered_map>

namespace zents
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
