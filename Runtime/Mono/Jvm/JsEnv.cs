using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ZTS.Jvm;
using ZTS.Utils;

namespace ZTS
{
    /// <summary>
    /// QuickJS runtime wrapper for Editor Mono backend.
    /// </summary>
    public sealed class JsEnv : IDisposable
    {
        internal static readonly object SyncRoot = new object();

        private IntPtr _runtime;
        private IntPtr _context;
        private Func<string, object> _moduleLoader;
        private readonly Dictionary<string, JSValue> _moduleNamespaces = new Dictionary<string, JSValue>(StringComparer.Ordinal);
        private bool _disposed;

        internal IntPtr Context => _context;
        internal IntPtr Runtime => _runtime;
        internal int Generation { get; private set; }

        public bool IsAlive => !_disposed && _context != IntPtr.Zero;

        internal void Initialize(Func<string, object> moduleLoader, int generation)
        {
            if (_context != IntPtr.Zero)
            {
                throw new InvalidOperationException("JsEnv is already initialized.");
            }

            _moduleLoader = moduleLoader ?? throw new ArgumentNullException(nameof(moduleLoader));
            Generation = generation;

            _runtime = QuickJsDll.JS_NewRuntime();
            if (_runtime == IntPtr.Zero)
            {
                throw new TsScriptException("zts: JS_NewRuntime failed.");
            }

            QuickJsDll.js_std_init_handlers(_runtime);

            _context = QuickJsDll.JS_NewContext(_runtime);
            if (_context == IntPtr.Zero)
            {
                QuickJsDll.JS_FreeRuntime(_runtime);
                _runtime = IntPtr.Zero;
                throw new TsScriptException("zts: JS_NewContext failed.");
            }

            QuickJsDll.JS_SetContextOpaque(_context, GCHandle.ToIntPtr(GCHandle.Alloc(this)));
            QuickJsDll.js_std_add_helpers(_context, 0, IntPtr.Zero);
            JsCallbackGate.EnsureInitialized(_context);
            // module_normalize = NULL → QuickJS default (resolves ./ and ../ against base module name).
            // Host loader then receives the normalized specifier (e.g. "assert.js").
            QuickJsDll.JS_SetModuleLoaderFunc(_runtime, IntPtr.Zero, ModuleLoaderCallback, IntPtr.Zero);
        }

        internal void SetModuleLoader(Func<string, object> moduleLoader)
        {
            _moduleLoader = moduleLoader ?? throw new ArgumentNullException(nameof(moduleLoader));
        }

        internal void Activate()
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException("JsEnv is not initialized.");
            }
        }

        internal void Shutdown()
        {
            Dispose();
        }

        public void EvalScript(string source, string filename = "<eval>")
        {
            EnsureAlive();
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            byte[] bytes = Encoding.UTF8.GetBytes(source);
            JSValue result = QuickJsDll.JS_Eval(
                _context,
                bytes,
                (UIntPtr)bytes.Length,
                filename ?? "<eval>",
                QuickJsDll.JsEvalTypeGlobal);

            if (JsValueUtil.IsException(result) || JsValueUtil.GetTag(result) == JsValueUtil.TagException)
            {
                ThrowPendingException();
            }

            JsValueUtil.Free(_context, result);
        }

        internal JSValue LoadModuleNamespace(string moduleName)
        {
            EnsureAlive();
            if (string.IsNullOrEmpty(moduleName))
            {
                throw new ArgumentException("moduleName must be non-empty.", nameof(moduleName));
            }

            lock (_moduleNamespaces)
            {
                if (_moduleNamespaces.TryGetValue(moduleName, out JSValue cached))
                {
                    return JsValueUtil.Dup(cached);
                }
            }

            // JS_LoadModule returns a Promise that fulfills with the module namespace.
            JSValue promise = QuickJsDll.JS_LoadModule(_context, moduleName, moduleName);
            if (JsValueUtil.IsException(promise))
            {
                ThrowPendingException();
            }

            try
            {
                DrainPendingJobs();

                int state = QuickJsDll.JS_PromiseState(_context, promise);
                if (state == QuickJsDll.JsPromisePending)
                {
                    throw new TsScriptException($"zts: module '{moduleName}' promise still pending after draining jobs.");
                }

                JSValue ns = QuickJsDll.JS_PromiseResult(_context, promise);
                if (state == QuickJsDll.JsPromiseRejected)
                {
                    string message = FormatJsValue(_context, ns);
                    int tag = JsValueUtil.GetTag(ns);
                    if (string.IsNullOrEmpty(message) || message == "[unsupported type]" ||
                        tag == 4 /* JS_TAG_UNINITIALIZED */)
                    {
                        // Fall back to pending exception / ToString path.
                        JSValue pending = QuickJsDll.JS_GetException(_context);
                        try
                        {
                            if (JsValueUtil.GetNormTag(pending) != JsValueUtil.TagUndefined &&
                                JsValueUtil.GetNormTag(pending) != JsValueUtil.TagNull &&
                                JsValueUtil.GetTag(pending) != 4)
                            {
                                message = FormatJsValue(_context, pending);
                            }
                        }
                        finally
                        {
                            JsValueUtil.Free(_context, pending);
                        }
                    }

                    JsValueUtil.Free(_context, ns);
                    throw new TsScriptException($"zts: module '{moduleName}' rejected (tag={tag}): {message}");
                }

                if (JsValueUtil.IsException(ns))
                {
                    ThrowPendingException();
                }

                lock (_moduleNamespaces)
                {
                    if (_moduleNamespaces.TryGetValue(moduleName, out JSValue cached))
                    {
                        JsValueUtil.Free(_context, ns);
                        return JsValueUtil.Dup(cached);
                    }

                    _moduleNamespaces[moduleName] = JsValueUtil.Dup(ns);
                }

                return ns;
            }
            finally
            {
                JsValueUtil.Free(_context, promise);
            }
        }

        private void DrainPendingJobs()
        {
            while (QuickJsDll.JS_IsJobPending(_runtime) != 0)
            {
                int status = QuickJsDll.JS_ExecutePendingJob(_runtime, out IntPtr jobCtx);
                if (status < 0)
                {
                    IntPtr errCtx = jobCtx != IntPtr.Zero ? jobCtx : _context;
                    JSValue ex = QuickJsDll.JS_GetException(errCtx);
                    string message = FormatJsValue(errCtx, ex);
                    JsValueUtil.Free(errCtx, ex);
                    throw new TsScriptException($"zts: pending job failed: {message}");
                }
            }
        }

        internal JSValue GetModuleExport(string moduleName, string exportName)
        {
            JSValue ns = LoadModuleNamespace(moduleName);
            try
            {
                JSValue export = QuickJsDll.JS_GetPropertyStr(_context, ns, exportName);
                if (JsValueUtil.IsException(export))
                {
                    ThrowPendingException();
                }

                if (QuickJsDll.JS_IsFunction(_context, export) == 0)
                {
                    JsValueUtil.Free(_context, export);
                    throw new TsScriptException($"zts: export '{moduleName}.{exportName}' is not callable.");
                }

                return export;
            }
            finally
            {
                JsValueUtil.Free(_context, ns);
            }
        }

        internal void ThrowPendingException()
        {
            JSValue ex = QuickJsDll.JS_GetException(_context);
            string message = FormatJsValue(_context, ex);
            JsValueUtil.Free(_context, ex);
            throw new TsScriptException($"zts: {message}");
        }

        internal static string FormatJsValue(IntPtr ctx, JSValue val)
        {
            if (JsValueUtil.GetNormTag(val) == JsValueUtil.TagUndefined)
            {
                return "undefined";
            }

            if (JsValueUtil.GetNormTag(val) == JsValueUtil.TagNull)
            {
                return "null";
            }

            // Prefer Error.message when present.
            if (JsValueUtil.GetNormTag(val) == JsValueUtil.TagObject)
            {
                JSValue msg = QuickJsDll.JS_GetPropertyStr(ctx, val, "message");
                try
                {
                    if (JsValueUtil.GetNormTag(msg) == JsValueUtil.TagString)
                    {
                        IntPtr mptr = QuickJsDll.JS_ToCStringLen2(ctx, out UIntPtr mlen, msg, 0);
                        if (mptr != IntPtr.Zero)
                        {
                            try
                            {
                                string m = Marshal.PtrToStringUTF8(mptr, (int)mlen);
                                if (!string.IsNullOrEmpty(m) && m != "[unsupported type]")
                                {
                                    return m;
                                }
                            }
                            finally
                            {
                                QuickJsDll.JS_FreeCString(ctx, mptr);
                            }
                        }
                    }
                }
                finally
                {
                    JsValueUtil.Free(ctx, msg);
                }
            }

            IntPtr cstr = QuickJsDll.JS_ToCStringLen2(ctx, out UIntPtr len, val, 0);
            if (cstr == IntPtr.Zero)
            {
                return $"JS exception (tag={JsValueUtil.GetTag(val)})";
            }

            try
            {
                return Marshal.PtrToStringUTF8(cstr, (int)len) ?? "JS exception";
            }
            finally
            {
                QuickJsDll.JS_FreeCString(ctx, cstr);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _moduleLoader = null;

            lock (_moduleNamespaces)
            {
                foreach (JSValue ns in _moduleNamespaces.Values)
                {
                    JsValueUtil.Free(_context, ns);
                }

                _moduleNamespaces.Clear();
            }

            if (_context != IntPtr.Zero)
            {
                IntPtr opaque = QuickJsDll.JS_GetContextOpaque(_context);
                if (opaque != IntPtr.Zero)
                {
                    GCHandle.FromIntPtr(opaque).Free();
                }

                QuickJsDll.JS_FreeContext(_context);
                _context = IntPtr.Zero;
            }

            if (_runtime != IntPtr.Zero)
            {
                QuickJsDll.js_std_free_handlers(_runtime);
                QuickJsDll.JS_FreeRuntime(_runtime);
                _runtime = IntPtr.Zero;
            }
        }

        private void EnsureAlive()
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException("JsEnv has been disposed or is not initialized.");
            }
        }

        [MonoJsCallback(typeof(JsModuleLoaderFunc))]
        [AOT.MonoPInvokeCallback(typeof(JsModuleLoaderFunc))]
        private static IntPtr ModuleLoaderCallback(IntPtr ctx, IntPtr moduleNamePtr, IntPtr opaque)
        {
            try
            {
                string moduleName = Marshal.PtrToStringUTF8(moduleNamePtr);
                JsEnv env = FromContext(ctx);
                object loaded = env._moduleLoader(moduleName);
                if (loaded == null)
                {
                    JsCallbackBoundary.ThrowError(ctx, $"module loader returned null for '{moduleName}'");
                    return IntPtr.Zero;
                }

                if (!(loaded is string source))
                {
                    JsCallbackBoundary.ThrowError(ctx, $"module loader for '{moduleName}' must return string source.");
                    return IntPtr.Zero;
                }

                byte[] bytes = Encoding.UTF8.GetBytes(source);
                JSValue compiled = QuickJsDll.JS_Eval(
                    ctx,
                    bytes,
                    (UIntPtr)bytes.Length,
                    moduleName,
                    QuickJsDll.JsEvalTypeModule | QuickJsDll.JsEvalFlagCompileOnly);

                if (QuickJsDll.IsException(compiled) || JsValueUtil.IsException(compiled))
                {
                    JsCallbackBoundary.ThrowPendingJsException(ctx);
                    return IntPtr.Zero;
                }

                // Module is already referenced by the context module table; free the MODULE JSValue.
                IntPtr moduleDef = JsValueUtil.GetPtr(compiled);
                JsValueUtil.Free(ctx, compiled);
                return moduleDef;
            }
            catch (Exception ex)
            {
                JsCallbackBoundary.ThrowError(ctx, ex.Message);
                return IntPtr.Zero;
            }
        }

        internal static JsEnv FromContext(IntPtr ctx)
        {
            IntPtr opaque = QuickJsDll.JS_GetContextOpaque(ctx);
            if (opaque == IntPtr.Zero)
            {
                throw new InvalidOperationException("JsEnv context opaque is missing.");
            }

            return (JsEnv)GCHandle.FromIntPtr(opaque).Target;
        }
    }
}
