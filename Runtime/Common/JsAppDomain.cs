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

namespace ZTS
{
    /// <summary>
    /// Host-facing facade. Concrete work is performed by an <see cref="IJsRuntime"/>
    /// created on demand from <c>ZTS.Mono</c> (Editor) or <c>ZTS.Il2Cpp</c> (Player).
    /// </summary>
    public static class JsAppDomain
    {
        private static IJsRuntime s_runtime;
        private static Func<string, object> s_pendingResetLoader;

        public static void Initialize(Func<string, object> moduleLoader)
        {
            if (moduleLoader == null)
            {
                throw new ArgumentNullException(nameof(moduleLoader));
            }

            EnsureRuntime();
            s_runtime.Initialize(WrapLoader(moduleLoader));
            JsFramePump.EnsureRegistered();
        }

        /// <summary>
        /// Schedule a rebuild of the single main JS runtime/context.
        /// Teardown / re-init runs at Unity EndOfFrame via <see cref="JsFramePump"/> (not immediately).
        /// After it applies, prior <see cref="GetFunction{T}"/> delegates are invalid.
        /// </summary>
        public static void Reset(Func<string, object> moduleLoader)
        {
            if (moduleLoader == null)
            {
                throw new ArgumentNullException(nameof(moduleLoader));
            }

            EnsureRuntime();
            s_pendingResetLoader = WrapLoader(moduleLoader);
            JsFramePump.EnsureRegistered();

#if UNITY_EDITOR
            // Edit-mode / batch -executeMethod has no FramePump; apply reset synchronously.
            if (!UnityEngine.Application.isPlaying)
            {
                FlushPendingReset();
            }
#endif
        }

        /// <summary>
        /// Bind a JS module export to a closed delegate of type <typeparamref name="T"/>.
        /// Must be called after <see cref="Initialize"/>. Does not guarantee instance reuse across calls.
        /// </summary>
        public static T GetFunction<T>(string jsModule, string jsExportName)
            where T : MulticastDelegate
        {
            EnsureRuntime();
            if (string.IsNullOrEmpty(jsModule))
            {
                throw new ArgumentException("jsModule must be non-empty.", nameof(jsModule));
            }

            if (string.IsNullOrEmpty(jsExportName))
            {
                throw new ArgumentException("jsExportName must be non-empty.", nameof(jsExportName));
            }

            jsModule = JsModuleSpecifier.Canonicalize(jsModule);
            if (JsModuleSpecifier.IsCsharp(jsModule))
            {
                throw new JsScriptException(
                    "zts: GetFunction must not use csharp: type modules; exports are type objects.");
            }

            TsExportManifest.WarnIfUnknown(jsModule, jsExportName);
            Delegate bound = s_runtime.GetFunction(typeof(T), jsModule, jsExportName);
            return (T)bound;
        }

        internal static void ProcessPendingRefReleases()
        {
            s_runtime?.ProcessPendingRefReleases();
        }

        /// <summary>
        /// Apply a scheduled <see cref="Reset"/> immediately (same as FramePump EndOfFrame).
        /// Useful in Play Mode smoke tests that must continue in the same frame after Reset.
        /// </summary>
        public static void ApplyPendingResetNow()
        {
            FlushPendingReset();
        }

        /// <summary>
        /// Apply a scheduled <see cref="Reset"/> if any. Called from <see cref="JsFramePump"/> at EndOfFrame.
        /// </summary>
        internal static void FlushPendingReset()
        {
            if (s_pendingResetLoader == null || s_runtime == null)
            {
                return;
            }

            Func<string, object> loader = s_pendingResetLoader;
            s_pendingResetLoader = null;
            s_runtime.Reset(loader);
            JsFramePump.EnsureRegistered();
        }

        private static void EnsureRuntime()
        {
            if (s_runtime != null)
            {
                return;
            }

#if UNITY_EDITOR
            const string typeName = "ZTS.JsMonoAppDomain";
            const string assemblyName = "ZTS.Mono";
#else
            const string typeName = "ZTS.JsIl2CppAppDomain";
            const string assemblyName = "ZTS.Il2Cpp";
#endif
            string hostTypeName = typeName + ", " + assemblyName;
            Type hostType = Type.GetType(hostTypeName);
            if (hostType == null)
            {
                // Il2Cpp / some strippers: assembly-qualified GetType can miss; scan loaded asms.
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    hostType = assembly.GetType(typeName, throwOnError: false);
                    if (hostType != null)
                    {
                        break;
                    }
                }
            }

            if (hostType == null)
            {
                throw new InvalidOperationException(
                    $"ZTS backend type not found: '{hostTypeName}'. Ensure the matching package assembly is loaded.");
            }

            Type runtimeType = hostType.GetNestedType("Runtime", BindingFlags.NonPublic);
            if (runtimeType == null || !typeof(IJsRuntime).IsAssignableFrom(runtimeType))
            {
                throw new InvalidOperationException(
                    $"ZTS backend '{hostTypeName}' is missing a non-public nested Runtime : IJsRuntime.");
            }

            s_runtime = (IJsRuntime)Activator.CreateInstance(runtimeType, nonPublic: true);
        }

        private static Func<string, object> WrapLoader(Func<string, object> inner)
        {
            return specifier =>
            {
                if (JsModuleSpecifier.IsCsharp(specifier))
                {
                    return CsharpVirtualModule.Synthesize(specifier);
                }

                string canonical = JsModuleSpecifier.Canonicalize(specifier);
                return inner(canonical);
            };
        }
    }
}
