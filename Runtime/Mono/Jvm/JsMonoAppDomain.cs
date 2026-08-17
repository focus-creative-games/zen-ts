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

        private static void CreateEnv(Func<string, object> moduleLoader)
        {
            _generation++;
#if UNITY_EDITOR
            LoadJsMarshalAsXmlFromSettings();
            LoadJsAliasXmlFromSettings();
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

                EditorSettingsAccess.TryGetField(settings, settingsType, "marshalAsXmlPaths", out string[] paths);
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

                EditorSettingsAccess.TryGetField(settings, settingsType, "jsAliasXmlPaths", out string[] paths);
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
            // Drop every managed JSValue hold before FreeContext/FreeRuntime, otherwise
            // JS_FreeRuntime asserts on a non-empty gc_obj_list (quickjs_build.c ~2481).
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
            IntPtr rt = QuickJsDll.JS_NewRuntime();
            if (rt == IntPtr.Zero)
            {
                throw new JsScriptException("zents: JS_NewRuntime failed");
            }

            IntPtr ctx = QuickJsDll.JS_NewContext(rt);
            if (ctx == IntPtr.Zero)
            {
                QuickJsDll.JS_FreeRuntime(rt);
                throw new JsScriptException("zents: JS_NewContext failed");
            }

            QuickJsDll.JS_FreeContext(ctx);
            QuickJsDll.JS_FreeRuntime(rt);
        }

        /// <summary>Batch diagnostic: JSValue out-shims (NewObject / GetGlobalObject).</summary>
        public static void SmokeJsValueAbi()
        {
            IntPtr rt = QuickJsDll.JS_NewRuntime();
            IntPtr ctx = QuickJsDll.JS_NewContext(rt);
            try
            {
                JSValue obj = QuickJsDll.JS_NewObject(ctx);
                JSValue global = QuickJsDll.JS_GetGlobalObject(ctx);
                JsValueUtil.Free(ctx, obj);
                JsValueUtil.Free(ctx, global);
            }
            finally
            {
                QuickJsDll.JS_FreeContext(ctx);
                QuickJsDll.JS_FreeRuntime(rt);
            }
        }

        /// <summary>Batch diagnostic: stepwise domain init to locate native crashes.</summary>
        public static void SmokeInitSteps()
        {
            UnityEngine.Debug.Log("[ZenTS] step1 NewRuntime");
            IntPtr rt = QuickJsDll.JS_NewRuntime();
            QuickJsDll.js_std_init_handlers(rt);
            UnityEngine.Debug.Log("[ZenTS] step2 NewContext");
            IntPtr ctx = QuickJsDll.JS_NewContext(rt);
            UnityEngine.Debug.Log("[ZenTS] step3 SetContextOpaque/helpers");
            QuickJsDll.JS_SetContextOpaque(ctx, System.Runtime.InteropServices.GCHandle.ToIntPtr(System.Runtime.InteropServices.GCHandle.Alloc("smoke")));
            QuickJsDll.js_std_add_helpers(ctx, 0, IntPtr.Zero);
            UnityEngine.Debug.Log("[ZenTS] step4 JsCallbackGate.EnsureInitialized");
            ZenTS.Utils.JsCallbackGate.EnsureInitialized(ctx);
            UnityEngine.Debug.Log("[ZenTS] step5 EvalScript");
            var env = new JsEnv();
            // minimal eval via raw API
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("1+1");
            JSValue r = QuickJsDll.JS_Eval(ctx, bytes, (UIntPtr)bytes.Length, "<smoke>", QuickJsDll.JsEvalTypeGlobal);
            UnityEngine.Debug.Log($"[ZenTS] step5 eval tag={r.Tag} int={r.UInt64}");
            JsValueUtil.Free(ctx, r);
            UnityEngine.Debug.Log("[ZenTS] step6 NewObject/SetProperty");
            JSValue obj = QuickJsDll.JS_NewObject(ctx);
            JSValue global = QuickJsDll.JS_GetGlobalObject(ctx);
            QuickJsDll.JS_SetPropertyStr(ctx, global, "CSharp", obj); // consumes obj
            UnityEngine.Debug.Log("[ZenTS] step7 NewCFunction via gate");
            JSValue fn = ZenTS.Utils.JsCallbackGate.NewCFunction(ctx, SmokeCb, "smoke_cb", 0);
            QuickJsDll.JS_SetPropertyStr(ctx, global, "smoke_cb", JsValueUtil.Dup(fn));
            UnityEngine.Debug.Log("[ZenTS] step8 call smoke_cb");
            JSValue callRet = QuickJsDll.JS_Call(ctx, fn, JsValueUtil.Undefined, 0, IntPtr.Zero);
            UnityEngine.Debug.Log($"[ZenTS] step8 callRet tag={callRet.Tag} val={callRet.UInt64}");
            JsValueUtil.Free(ctx, callRet);
            JsValueUtil.Free(ctx, fn);
            JsValueUtil.Free(ctx, global);
            QuickJsDll.JS_FreeContext(ctx);
            QuickJsDll.js_std_free_handlers(rt);
            QuickJsDll.JS_FreeRuntime(rt);
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
            JSValue er = QuickJsDll.JS_Eval(ctx, emb, (UIntPtr)emb.Length, "zentslib.js", QuickJsDll.JsEvalTypeGlobal | QuickJsDll.JsEvalFlagStrict);
            UnityEngine.Debug.Log($"[ZenTS] step9 embedded tag={er.Tag} isExc={JsValueUtil.IsException(er)}");
            if (JsValueUtil.IsException(er))
            {
                JSValue ex = QuickJsDll.JS_GetException(ctx);
                UnityEngine.Debug.LogError("[ZenTS] embedded exception");
                JsValueUtil.Free(ctx, ex);
            }
            JsValueUtil.Free(ctx, er);
            UnityEngine.Debug.Log("[ZenTS] SmokeInitSteps ALL OK");
        }

        [AOT.MonoPInvokeCallback(typeof(JsCFunction))]
        private static JSValue SmokeCb(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
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
