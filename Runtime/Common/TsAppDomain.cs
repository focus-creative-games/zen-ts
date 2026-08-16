using System;
using System.Reflection;

namespace ZTS
{
    /// <summary>
    /// Host-facing facade. Concrete work is performed by an <see cref="ITsRuntime"/>
    /// created on demand from <c>ZTS.Mono</c> (Editor) or <c>ZTS.Il2Cpp</c> (Player).
    /// </summary>
    public static class TsAppDomain
    {
        private static ITsRuntime s_runtime;
        private static Func<string, object> s_pendingResetLoader;

        public static void Initialize(Func<string, object> moduleLoader)
        {
            if (moduleLoader == null)
            {
                throw new ArgumentNullException(nameof(moduleLoader));
            }

            EnsureRuntime();
            s_runtime.Initialize(WrapLoader(moduleLoader));
            TsFramePump.EnsureRegistered();
        }

        /// <summary>
        /// Schedule a rebuild of the single main JS runtime/context.
        /// Teardown / re-init runs at Unity EndOfFrame via <see cref="TsFramePump"/> (not immediately).
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
            TsFramePump.EnsureRegistered();

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
                throw new TsScriptException(
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
        /// Apply a scheduled <see cref="Reset"/> if any. Called from <see cref="TsFramePump"/> at EndOfFrame.
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
            TsFramePump.EnsureRegistered();
        }

        private static void EnsureRuntime()
        {
            if (s_runtime != null)
            {
                return;
            }

#if UNITY_EDITOR
            const string typeName = "ZTS.TsMonoAppDomain";
            const string assemblyName = "ZTS.Mono";
#else
            const string typeName = "ZTS.TsIl2CppAppDomain";
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
            if (runtimeType == null || !typeof(ITsRuntime).IsAssignableFrom(runtimeType))
            {
                throw new InvalidOperationException(
                    $"ZTS backend '{hostTypeName}' is missing a non-public nested Runtime : ITsRuntime.");
            }

            s_runtime = (ITsRuntime)Activator.CreateInstance(runtimeType, nonPublic: true);
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
