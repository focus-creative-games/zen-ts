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

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace ZenTS.Editor
{
    public static class V8Acceptance
    {
        private static void Run(string type, string method)
        {
            Type target = Type.GetType(type + ", ZenTS.Tests", true);
            target.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        }
        private const string ReloadPending = "ZenTS.P2.V8.ReloadPending";
        private static int LiveRuntimes()
        {
            Type backend = Type.GetType("ZenTS.Jvm.V8Native, ZenTS.Mono", true);
            return (int)backend.GetMethod("ze_GetLiveRuntimeCount", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
        }

        public static void RunDomainReloadTest()
        {
            try
            {
                JsAppDomain.Reset(
                    _ => "export function value(){return 42;}");
                if (JsAppDomain.GetFunction<Func<int>>("reload", "value")() != 42 || LiveRuntimes() != 1)
                    throw new InvalidOperationException("Initial V8 runtime count/value mismatch");
                SessionState.SetBool(ReloadPending, true);
                EditorUtility.RequestScriptReload();
            }
            catch (Exception ex)
            {
                SessionState.SetBool(ReloadPending, false);
                Debug.LogError("[ZenTS][P2] Domain Reload FAILED: " + ex);
                EditorApplication.Exit(1);
            }
        }

        [InitializeOnLoadMethod]
        private static void ResumeDomainReload()
        {
            if (SessionState.GetBool(ReloadPending, false))
                EditorApplication.delayCall += FinishDomainReload;
        }

        private static void FinishDomainReload()
        {
            SessionState.SetBool(ReloadPending, false);
            try
            {
                if (LiveRuntimes() != 0)
                    throw new InvalidOperationException("Native V8 runtime leaked across Domain Reload");
                JsAppDomain.Reset(
                    _ => "export function value(){return 43;}");
                if (JsAppDomain.GetFunction<Func<int>>("reload", "value")() != 43 || LiveRuntimes() != 1)
                    throw new InvalidOperationException("V8 did not restart after Domain Reload");
                Debug.Log("[ZenTS][P2] Domain Reload PASS: native runtime count 1 -> 0 -> 1");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZenTS][P2] Domain Reload FAILED: " + ex);
                EditorApplication.Exit(1);
            }
        }

        public static void InstallAndConfigureWin64()
        {
            try
            {
                string compilerOptions = Environment.GetEnvironmentVariable("_CL_");
                string il2CppOptions = PlayerSettings.GetAdditionalIl2CppArgs();
                try
                {
                    V8BuildSetup.ConfigureBuildEnvironment();
                    if (!(Environment.GetEnvironmentVariable("_CL_") ?? string.Empty).Contains("/MT /U_DEBUG") ||
                        !PlayerSettings.GetAdditionalIl2CppArgs().Contains(V8BuildSetup.CacheMarker))
                        throw new InvalidOperationException("V8 build environment was not configured");
                }
                finally
                {
                    V8BuildSetup.RestoreBuildEnvironment();
                }
                if (compilerOptions != Environment.GetEnvironmentVariable("_CL_") ||
                    il2CppOptions != PlayerSettings.GetAdditionalIl2CppArgs())
                    throw new InvalidOperationException("V8 build environment was not restored");
                Debug.Log("[ZenTS][P2] Build environment configuration/restoration PASS");
                new LocalInstaller().InstallLocal();
                V8BuildSetup.ConfigureExport(System.IO.Path.GetFullPath("Build-Win64"));
                Debug.Log("[ZenTS][P2] V8 Install and Win64 export configuration OK");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZenTS][P2] V8 install/export configuration FAILED:\n" + ex);
                EditorApplication.Exit(1);
            }
        }

        public static void RunEditorTests()
        {
            try
            {
                if (EditorUtility.scriptCompilationFailed)
                    throw new InvalidOperationException("C# compilation failed");
                JsAppDomain.Reset(
                    name =>
                        name == "p2-v8-acceptance"
                            ? "export function verify(){if(globalThis.__zents_engine!=='V8')throw Error('Expected V8 " +
                                  "backend');return __zents_engine+':'+__zents_engine_version;}"
                            : throw new InvalidOperationException("Unexpected acceptance module: " + name));
                string actual = JsAppDomain.GetFunction<Func<string>>("p2-v8-acceptance.js", "verify")();
                if (!actual.StartsWith("V8:13.6.233.17", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected engine: " + actual);
                Debug.Log("[ZenTS][P2] Actual Editor engine=" + actual);
                Run("ZenTS.Tests.MonoSmokeRunner", "RunAll");
                Run("ZenTS.Tests.TsDemoRunner", "RunAll");
                Debug.Log("[ZenTS][P2] Editor V8 acceptance ALL OK");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZenTS][P2] Editor V8 acceptance FAILED:\n" + ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
