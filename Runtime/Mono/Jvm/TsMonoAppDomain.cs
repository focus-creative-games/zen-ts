using System;
using System.IO;
using System.Reflection;
using ZTS.DelegateImpl;
using ZTS.Jvm;
using ZTS.Mt;
using UnityEngine;

namespace ZTS
{
    /// <summary>
    /// Editor Mono backend entry. Invoked by <see cref="TsAppDomain"/> via reflective
    /// construction of nested <see cref="Runtime"/>.
    /// </summary>
    public static class TsMonoAppDomain
    {
        private static JsEnv _env;
        private static int _generation;

        public static JsEnv Env
        {
            get
            {
                if (_env == null)
                {
                    throw new InvalidOperationException("ZTS is not initialized. Call TsAppDomain.Initialize first.");
                }

                return _env;
            }
        }

        public static void Initialize(Func<string, object> moduleLoader)
        {
            if (_env != null)
            {
                throw new InvalidOperationException(
                    "ZTS is already initialized. Call TsAppDomain.Reset to rebuild the JS domain.");
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
            TsXmlOverlayLoader.Reset();
            TryLoadXmlOverlays();
            _env = new JsEnv();
            _env.Initialize(moduleLoader, _generation);
            _env.Activate();
            // Native __zts_* hooks + ztslib.js (installs CSharp Proxy).
            ZtsLib.RegisterGlobals(_env);
            AssemblyRegistry.EnsureCSharpRoot(_env);
            DynamicBridgeFactory.InvalidateGeneration(_generation);
            TryJsDebuggerStart();
        }

        private static void TryLoadXmlOverlays()
        {
            string testsXml = Path.Combine(Application.streamingAssetsPath, "Tests", "Xml");
            TsXmlOverlayLoader.TryLoadFromDirectory(testsXml);

            string settingsXml = Path.Combine(Application.streamingAssetsPath, "ZTS", "Xml");
            TsXmlOverlayLoader.TryLoadFromDirectory(settingsXml);
        }

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
            ZtsLib.ResetGenericMethodCache(ctx);
            ZTS.Utils.JsCallbackGate.Reset(ctx);
            _env.Shutdown();
            _env = null;
            AssemblyRegistry.Reset();
            TypeRegistry.Reset();
            Emit.ArrayBinding.Reset();
            Emit.MethodTagRegistry.Reset();
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
            const string typeName = "ZTS.Editor.Diagnostics.JsDebuggerBootstrap, ZTS.Editor";
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
                Debug.LogWarning("[ZTS] JS debugger hook: " + ex.Message);
            }
        }

        /// <summary>Batch diagnostic: NewRuntime/NewContext only.</summary>
        public static void SmokeNativeContext()
        {
            IntPtr rt = QuickJsDll.JS_NewRuntime();
            if (rt == IntPtr.Zero)
            {
                throw new TsScriptException("zts: JS_NewRuntime failed");
            }

            IntPtr ctx = QuickJsDll.JS_NewContext(rt);
            if (ctx == IntPtr.Zero)
            {
                QuickJsDll.JS_FreeRuntime(rt);
                throw new TsScriptException("zts: JS_NewContext failed");
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
            UnityEngine.Debug.Log("[ZTS] step1 NewRuntime");
            IntPtr rt = QuickJsDll.JS_NewRuntime();
            QuickJsDll.js_std_init_handlers(rt);
            UnityEngine.Debug.Log("[ZTS] step2 NewContext");
            IntPtr ctx = QuickJsDll.JS_NewContext(rt);
            UnityEngine.Debug.Log("[ZTS] step3 SetContextOpaque/helpers");
            QuickJsDll.JS_SetContextOpaque(ctx, System.Runtime.InteropServices.GCHandle.ToIntPtr(System.Runtime.InteropServices.GCHandle.Alloc("smoke")));
            QuickJsDll.js_std_add_helpers(ctx, 0, IntPtr.Zero);
            UnityEngine.Debug.Log("[ZTS] step4 JsCallbackGate.EnsureInitialized");
            ZTS.Utils.JsCallbackGate.EnsureInitialized(ctx);
            UnityEngine.Debug.Log("[ZTS] step5 EvalScript");
            var env = new JsEnv();
            // minimal eval via raw API
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("1+1");
            JSValue r = QuickJsDll.JS_Eval(ctx, bytes, (UIntPtr)bytes.Length, "<smoke>", QuickJsDll.JsEvalTypeGlobal);
            UnityEngine.Debug.Log($"[ZTS] step5 eval tag={r.Tag} int={r.UInt64}");
            JsValueUtil.Free(ctx, r);
            UnityEngine.Debug.Log("[ZTS] step6 NewObject/SetProperty");
            JSValue obj = QuickJsDll.JS_NewObject(ctx);
            JSValue global = QuickJsDll.JS_GetGlobalObject(ctx);
            QuickJsDll.JS_SetPropertyStr(ctx, global, "CSharp", obj); // consumes obj
            UnityEngine.Debug.Log("[ZTS] step7 NewCFunction via gate");
            JSValue fn = ZTS.Utils.JsCallbackGate.NewCFunction(ctx, SmokeCb, "smoke_cb", 0);
            QuickJsDll.JS_SetPropertyStr(ctx, global, "smoke_cb", JsValueUtil.Dup(fn));
            UnityEngine.Debug.Log("[ZTS] step8 call smoke_cb");
            JSValue callRet = QuickJsDll.JS_Call(ctx, fn, JsValueUtil.Undefined, 0, IntPtr.Zero);
            UnityEngine.Debug.Log($"[ZTS] step8 callRet tag={callRet.Tag} val={callRet.UInt64}");
            JsValueUtil.Free(ctx, callRet);
            JsValueUtil.Free(ctx, fn);
            JsValueUtil.Free(ctx, global);
            QuickJsDll.JS_FreeContext(ctx);
            QuickJsDll.js_std_free_handlers(rt);
            QuickJsDll.JS_FreeRuntime(rt);
            UnityEngine.Debug.Log("[ZTS] step9 Eval EmbeddedJs");
            var embedded = @"
globalThis.zts = globalThis.zts ?? {};
zts.types = zts.types ?? {
  int32: 'System.Int32',
  float32: 'System.Single',
  float64: 'System.Double',
  boolean: 'System.Boolean',
  string: 'System.String'
};
zts.typeof = function(typeObject) {
  if (typeObject && typeObject.__zts_type_name) return typeObject.__zts_type_name;
  return undefined;
};
zts.cast = function(value, typeObject) { return value; };
zts.box = function(value) { return value; };
zts.register_method = function(name, fn) {
  globalThis.zts[name] = fn;
};
";
            byte[] emb = System.Text.Encoding.UTF8.GetBytes(embedded);
            JSValue er = QuickJsDll.JS_Eval(ctx, emb, (UIntPtr)emb.Length, "ztslib.js", QuickJsDll.JsEvalTypeGlobal | QuickJsDll.JsEvalFlagStrict);
            UnityEngine.Debug.Log($"[ZTS] step9 embedded tag={er.Tag} isExc={JsValueUtil.IsException(er)}");
            if (JsValueUtil.IsException(er))
            {
                JSValue ex = QuickJsDll.JS_GetException(ctx);
                UnityEngine.Debug.LogError("[ZTS] embedded exception");
                JsValueUtil.Free(ctx, ex);
            }
            JsValueUtil.Free(ctx, er);
            UnityEngine.Debug.Log("[ZTS] SmokeInitSteps ALL OK");
        }

        [AOT.MonoPInvokeCallback(typeof(JsCFunction))]
        private static JSValue SmokeCb(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            return JsValueUtil.NewInt32(42);
        }

        public static void SmokeFullCreateEnv()
        {
            UnityEngine.Debug.Log("[ZTS] SmokeFullCreateEnv begin");
            Shutdown();
            UnityEngine.Debug.Log("[ZTS] calling CreateEnv");
            CreateEnv(_ => "export const x = 1;");
            UnityEngine.Debug.Log("[ZTS] CreateEnv OK, shutting down");
            Shutdown();
            UnityEngine.Debug.Log("[ZTS] SmokeFullCreateEnv ALL OK");
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

        private sealed class Runtime : ITsRuntime
        {
            public void Initialize(Func<string, object> moduleLoader)
            {
                TsMonoAppDomain.Initialize(moduleLoader);
            }

            public void Reset(Func<string, object> moduleLoader)
            {
                TsMonoAppDomain.Reset(moduleLoader);
            }

            public void ProcessPendingRefReleases()
            {
                TsMonoAppDomain.ProcessPendingRefReleases();
            }

            public Delegate GetFunction(Type delegateType, string jsModule, string jsExportName)
            {
                return TsMonoAppDomain.GetFunction(delegateType, jsModule, jsExportName);
            }
        }
    }
}
