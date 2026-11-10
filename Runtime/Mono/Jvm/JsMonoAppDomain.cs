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
using System.IO;
using System.Reflection;
using ZenTS.DelegateImpl;
using ZenTS.Jvm;
using ZenTS.Mt;
using ZenTS.Utils;
using UnityEngine;

namespace ZenTS
{
    /// <summary>
    /// Editor Mono backend entry. Invoked by <see cref="JsAppDomain"/> via reflective
    /// construction of nested <see cref="Runtime"/>.
    /// </summary>
    public static class JsMonoAppDomain
    {
        private static JsEnv _env;
        private static int _generation;

#if UNITY_EDITOR
        static JsMonoAppDomain()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            UnityEditor.EditorApplication.quitting += Shutdown;
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingEditMode ||
                    state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                    Shutdown();
            };
        }
#endif


        public static JsEnv Env
        {
            get
            {
                if (_env == null)
                {
                    throw new InvalidOperationException("ZenTS is not initialized. Call JsAppDomain.Initialize first.");
                }

                return _env;
            }
        }

        public static void Initialize(Func<string, object> moduleLoader)
        {
            if (_env != null)
            {
                throw new InvalidOperationException(
                    "ZenTS is already initialized. Call JsAppDomain.Reset to rebuild the JS domain.");
            }

            CreateEnv(moduleLoader);
        }

        public static void Reset(Func<string, object> moduleLoader)
        {
            Shutdown();
            CreateEnv(moduleLoader);
        }

        private static Action _ensureEditorPlugins;
        private static void CreateEnv(Func<string, object> moduleLoader)
        {
#if UNITY_EDITOR
            if (_ensureEditorPlugins == null)
                _ensureEditorPlugins = (Action)Delegate.CreateDelegate(typeof(Action),
                    Type.GetType("ZenTS.NativeEditorPlugins, ZenTS.Editor", true).GetMethod("Ensure"));
            _ensureEditorPlugins();
#endif
            _generation++;
#if UNITY_EDITOR
            if (!JsMarshalAsXmlRegistry.IsLoaded)
                LoadJsMarshalAsXmlFromSettings();
            if (!JsAliasXmlRegistry.IsLoaded)
                LoadJsAliasXmlFromSettings();
            if (!JsExtensionXmlRegistry.IsLoaded)
                LoadJsExtensionXmlFromSettings();
#endif
            _env = new JsEnv();
            _env.Initialize(moduleLoader, _generation);
            _env.Activate();
            // Native __zents_* hooks + zentslib.js (installs CSharp Proxy).
            ZentsLib.RegisterGlobals(_env);
            AssemblyRegistry.EnsureCSharpRoot(_env);
            DynamicBridgeFactory.InvalidateGeneration(_generation);
            TryJsDebuggerStart();
        }

#if UNITY_EDITOR
        private static void LoadJsMarshalAsXmlFromSettings()
        {
            try
            {
                if (!EditorSettingsAccess.TryGetInstance(out object settings, out Type settingsType))
                {
                    return;
                }

                EditorSettingsAccess.TryGetField(settings, settingsType, "EffectiveMarshalAsXmlPaths", out string[] paths);
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                JsMarshalAsXmlRegistry.Load(paths, projectRoot);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZenTS] MarshalAs XML load failed at Initialize:\n" + ex.Message);
                throw;
            }
        }

        private static void LoadJsAliasXmlFromSettings()
        {
            try
            {
                if (!EditorSettingsAccess.TryGetInstance(out object settings, out Type settingsType))
                {
                    return;
                }

                EditorSettingsAccess.TryGetField(settings, settingsType, "EffectiveAliasXmlPaths", out string[] paths);
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                JsAliasXmlRegistry.Load(paths, projectRoot);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZenTS] JsAlias XML load failed at Initialize:\n" + ex.Message);
                throw;
            }
        }

        private static void LoadJsExtensionXmlFromSettings()
        {
            try
            {
                if (!EditorSettingsAccess.TryGetInstance(out object settings, out Type settingsType))
                {
                    return;
                }

                EditorSettingsAccess.TryGetField(settings, settingsType, "jsExtensionXmlPaths", out string[] paths);
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                JsExtensionXmlRegistry.Load(paths, projectRoot);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZenTS] JsExtensions XML load failed at Initialize:\n" + ex.Message);
                throw;
            }
        }
#endif

        private static void Shutdown()
        {
            if (_env == null)
            {
                return;
            }

            TryJsDebuggerStop();
            ProcessPendingRefReleases();
            // Release all managed value handles before destroying the V8 context/runtime.
            IntPtr ctx = _env.Context;
            Marshaling.OpaqueParameterScope.Leave(ctx);
            Marshaling.OpaqueParameterScope.Reset();
            AssemblyRegistry.Release(_env);
            TypeRegistry.Release(_env);
            Emit.ArrayBinding.Release(_env);
            DynamicBridgeFactory.Release(_env);
            ZentsLib.ResetGenericMethodCache(ctx);
            ZenTS.Utils.JsCallbackGate.Reset(ctx);
            _env.Shutdown();
            _env = null;
            AssemblyRegistry.Reset();
            TypeRegistry.Reset();
            Emit.ArrayBinding.Reset();
            Emit.MethodTagRegistry.Reset();
            Emit.MethodEmitter.ResetCaches();
            ObjectRegistry.Reset();
        }

        private static void ProcessPendingRefReleases()
        {
            if (_env != null && _env.IsAlive)
                _env.DrainPendingJobs();
            ObjectRegistry.ProcessPending();
        }

        private static void TryJsDebuggerStart()
        {
            InvokeJsDebugger("TryStart", _env != null ? _env.Runtime : IntPtr.Zero,
                _env != null ? _env.Context : IntPtr.Zero);
        }

        private static void TryJsDebuggerStop()
        {
            InvokeJsDebugger("TryStop", IntPtr.Zero, IntPtr.Zero);
        }

        private static void InvokeJsDebugger(string methodName, IntPtr runtime, IntPtr context)
        {
            const string typeName = "ZenTS.Editor.Diagnostics.JsDebuggerBootstrap, ZenTS.Editor";
            Type bootstrap = Type.GetType(typeName);
            if (bootstrap == null)
            {
                return;
            }

            MethodInfo method = bootstrap.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return;
            }

            try
            {
                if (methodName == "TryStart")
                {
                    method.Invoke(null, new object[] { runtime, context });
                }
                else
                {
                    method.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZenTS] JS debugger hook: " + ex.Message);
            }
        }

        /// <summary>Batch diagnostic: NewRuntime/NewContext only.</summary>
        public static void SmokeNativeContext()
        {
            IntPtr rt = EngineApi.NewRuntime();
            if (rt == IntPtr.Zero)
            {
                throw new JsScriptException("zents: JS_NewRuntime failed");
            }

            IntPtr ctx = EngineApi.NewContext(rt);
            if (ctx == IntPtr.Zero)
            {
                EngineApi.FreeRuntime(rt);
                throw new JsScriptException("zents: JS_NewContext failed");
            }

            EngineApi.FreeContext(ctx);
            EngineApi.FreeRuntime(rt);
        }

        /// <summary>Batch diagnostic: JsValue out-shims (NewObject / GetGlobalObject).</summary>
        public static void SmokeJsValueAbi()
        {
            IntPtr rt = EngineApi.NewRuntime();
            IntPtr ctx = EngineApi.NewContext(rt);
            try
            {
                JsValue obj = EngineApi.NewObject(ctx);
                JsValue global = EngineApi.GetGlobalObject(ctx);
                JsValueUtil.Free(ctx, obj);
                JsValueUtil.Free(ctx, global);
            }
            finally
            {
                EngineApi.FreeContext(ctx);
                EngineApi.FreeRuntime(rt);
            }
        }

        /// <summary>Batch diagnostic: stepwise domain init to locate native crashes.</summary>
        public static void SmokeInitSteps()
        {
            UnityEngine.Debug.Log("[ZenTS] step1 NewRuntime");
            IntPtr rt = EngineApi.NewRuntime();
            EngineApi.StdInitHandlers(rt);
            UnityEngine.Debug.Log("[ZenTS] step2 NewContext");
            IntPtr ctx = EngineApi.NewContext(rt);
            UnityEngine.Debug.Log("[ZenTS] step3 SetContextOpaque/helpers");
            EngineApi.SetContextOpaque(ctx, System.Runtime.InteropServices.GCHandle.ToIntPtr(System.Runtime.InteropServices.GCHandle.Alloc("smoke")));
            EngineApi.StdAddHelpers(ctx, 0, IntPtr.Zero);
            UnityEngine.Debug.Log("[ZenTS] step4 JsCallbackGate.EnsureInitialized");
            ZenTS.Utils.JsCallbackGate.EnsureInitialized(ctx);
            UnityEngine.Debug.Log("[ZenTS] step5 EvalScript");
            var env = new JsEnv();
            // minimal eval via raw API
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("1+1");
            JsValue r = EngineApi.Eval(ctx, bytes, (UIntPtr)bytes.Length, "<smoke>", EngineApi.JsEvalTypeGlobal);
            UnityEngine.Debug.Log($"[ZenTS] step5 eval tag={JsValueUtil.GetTag(r)} int={JsValueUtil.GetUInt64(r)}");
            JsValueUtil.Free(ctx, r);
            UnityEngine.Debug.Log("[ZenTS] step6 NewObject/SetProperty");
            JsValue obj = EngineApi.NewObject(ctx);
            JsValue global = EngineApi.GetGlobalObject(ctx);
            EngineApi.SetPropertyStr(ctx, global, "CSharp", obj); // consumes obj
            UnityEngine.Debug.Log("[ZenTS] step7 NewCFunction via gate");
            JsValue fn = ZenTS.Utils.JsCallbackGate.NewCFunction(ctx, SmokeCb, "smoke_cb", 0);
            EngineApi.SetPropertyStr(ctx, global, "smoke_cb", JsValueUtil.Dup(fn));
            UnityEngine.Debug.Log("[ZenTS] step8 call smoke_cb");
            JsValue callRet = EngineApi.Call(ctx, fn, JsValueUtil.Undefined, 0, IntPtr.Zero);
            UnityEngine.Debug.Log($"[ZenTS] step8 callRet tag={JsValueUtil.GetTag(callRet)} val={JsValueUtil.GetUInt64(callRet)}");
            JsValueUtil.Free(ctx, callRet);
            JsValueUtil.Free(ctx, fn);
            JsValueUtil.Free(ctx, global);
            EngineApi.FreeContext(ctx);
            EngineApi.StdFreeHandlers(rt);
            EngineApi.FreeRuntime(rt);
            UnityEngine.Debug.Log("[ZenTS] step9 Eval EmbeddedJs");
            var embedded = @"
globalThis.zents = globalThis.zents ?? {};
zents.types = zents.types ?? {
  int32: 'System.Int32',
  float32: 'System.Single',
  float64: 'System.Double',
  boolean: 'System.Boolean',
  string: 'System.String'
};
zents.typeof = function(typeObject) {
  if (typeObject && typeObject.__zents_type_name) return typeObject.__zents_type_name;
  return undefined;
};
zents.cast = function(value, typeObject) { return value; };
zents.box = function(value) { return value; };
zents.register_method = function(name, fn) {
  globalThis.zents[name] = fn;
};
";
            byte[] emb = System.Text.Encoding.UTF8.GetBytes(embedded);
            JsValue er = EngineApi.Eval(ctx, emb, (UIntPtr)emb.Length, "zentslib.js", EngineApi.JsEvalTypeGlobal | EngineApi.JsEvalFlagStrict);
            UnityEngine.Debug.Log($"[ZenTS] step9 embedded tag={JsValueUtil.GetTag(er)} isExc={JsValueUtil.IsException(er)}");
            if (JsValueUtil.IsException(er))
            {
                JsValue ex = EngineApi.GetException(ctx);
                UnityEngine.Debug.LogError("[ZenTS] embedded exception");
                JsValueUtil.Free(ctx, ex);
            }
            JsValueUtil.Free(ctx, er);
            UnityEngine.Debug.Log("[ZenTS] SmokeInitSteps ALL OK");
        }

        [AOT.MonoPInvokeCallback(typeof(JsCFunction))]
        private static JsValue SmokeCb(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            return JsValueUtil.NewInt32(42);
        }

        public static void SmokeFullCreateEnv()
        {
            UnityEngine.Debug.Log("[ZenTS] SmokeFullCreateEnv begin");
            Shutdown();
            UnityEngine.Debug.Log("[ZenTS] calling CreateEnv");
            CreateEnv(_ => "export const x = 1;");
            UnityEngine.Debug.Log("[ZenTS] CreateEnv OK, shutting down");
            Shutdown();
            UnityEngine.Debug.Log("[ZenTS] SmokeFullCreateEnv ALL OK");
        }

        private static Delegate GetFunction(Type delegateType, string jsModule, string jsExportName)
        {
            if (delegateType == null)
            {
                throw new ArgumentNullException(nameof(delegateType));
            }

            if (!typeof(MulticastDelegate).IsAssignableFrom(delegateType))
            {
                throw new ArgumentException($"Type '{delegateType.FullName}' is not a MulticastDelegate.", nameof(delegateType));
            }

            return JsDelegateBinder.GetFunction(Env, delegateType, jsModule, jsExportName);
        }

        private sealed class Runtime : IJsRuntime
        {
            public void Initialize(Func<string, object> moduleLoader)
            {
                JsMonoAppDomain.Initialize(moduleLoader);
            }

            public void Reset(Func<string, object> moduleLoader)
            {
                JsMonoAppDomain.Reset(moduleLoader);
            }

            public void ProcessPendingRefReleases()
            {
                JsMonoAppDomain.ProcessPendingRefReleases();
            }

            public Delegate GetFunction(Type delegateType, string jsModule, string jsExportName)
            {
                return JsMonoAppDomain.GetFunction(delegateType, jsModule, jsExportName);
            }
        }
    }
}
