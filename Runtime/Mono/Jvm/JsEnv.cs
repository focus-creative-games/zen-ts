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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ZenTS.Jvm;
using ZenTS.Utils;

namespace ZenTS
{
    /// <summary>V8 runtime wrapper for the Editor Mono backend.</summary>
    public sealed class JsEnv : IDisposable
    {
        internal static readonly object SyncRoot = new object();

        private IntPtr _runtime;
        private IntPtr _context;
        private Func<string, object> _moduleLoader;
        private readonly Dictionary<string, JsValue> _moduleNamespaces = new Dictionary<string, JsValue>(StringComparer.Ordinal);
        private bool _disposed;

        internal IntPtr Context => _context;
        internal IntPtr Runtime => _runtime;
        internal int Generation
        {
            get; private set;
        }

        public bool IsAlive => !_disposed && _context != IntPtr.Zero;

        internal void Initialize(Func<string, object> moduleLoader, int generation)
        {
            JsMainThread.EnsureMainThread();
            if (_context != IntPtr.Zero)
            {
                throw new InvalidOperationException("JsEnv is already initialized.");
            }

            _moduleLoader = moduleLoader ?? throw new ArgumentNullException(nameof(moduleLoader));
            Generation = generation;

            _runtime = EngineApi.NewRuntime();
            UnityEngine.Debug.Log($"[ZenTS] Editor engine={EngineApi.Backend}");
            if (_runtime == IntPtr.Zero)
            {
                throw new JsScriptException("zents: JS_NewRuntime failed.");
            }

            EngineApi.StdInitHandlers(_runtime);

            _context = EngineApi.NewContext(_runtime);
            if (_context == IntPtr.Zero)
            {
                EngineApi.FreeRuntime(_runtime);
                _runtime = IntPtr.Zero;
                throw new JsScriptException("zents: JS_NewContext failed.");
            }

            EngineApi.SetContextOpaque(_context, GCHandle.ToIntPtr(GCHandle.Alloc(this)));
            EngineApi.StdAddHelpers(_context, 0, IntPtr.Zero);
            JsCallbackGate.EnsureInitialized(_context);
            // Host loader then receives the normalized specifier (e.g. "assert.js").
            EngineApi.SetModuleLoaderFunc(_runtime, IntPtr.Zero, ModuleLoaderCallback, IntPtr.Zero);
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
            JsValue result = EngineApi.Eval(
                _context,
                bytes,
                (UIntPtr)bytes.Length,
                filename ?? "<eval>",
                EngineApi.JsEvalTypeGlobal);

            if (JsValueUtil.IsException(result) || JsValueUtil.GetTag(result) == JsValueUtil.TagException)
            {
                ThrowPendingException();
            }

            JsValueUtil.Free(_context, result);
        }

        internal JsValue LoadModuleNamespace(string moduleName)
        {
            EnsureAlive();
            if (string.IsNullOrEmpty(moduleName))
            {
                throw new ArgumentException("moduleName must be non-empty.", nameof(moduleName));
            }

            lock (_moduleNamespaces)
            {
                if (_moduleNamespaces.TryGetValue(moduleName, out JsValue cached))
                {
                    return JsValueUtil.Dup(cached);
                }
            }

            // JS_LoadModule returns a Promise that fulfills with the module namespace.
            JsValue promise = EngineApi.LoadModule(_context, moduleName, moduleName);
            if (JsValueUtil.IsException(promise))
            {
                ThrowPendingException();
            }

            try
            {
                DrainPendingJobs();

                int state = EngineApi.PromiseState(_context, promise);
                if (state == EngineApi.JsPromisePending)
                {
                    throw new JsScriptException($"zents: module '{moduleName}' promise still pending after draining jobs.");
                }

                JsValue ns = EngineApi.PromiseResult(_context, promise);
                if (state == EngineApi.JsPromiseRejected)
                {
                    string message = FormatJsValue(_context, ns);
                    int tag = JsValueUtil.GetTag(ns);
                    if (string.IsNullOrEmpty(message) || message == "[unsupported type]" ||
                        tag == 4 /* JS_TAG_UNINITIALIZED */)
                    {
                        // Fall back to pending exception / ToString path.
                        JsValue pending = EngineApi.GetException(_context);
                        try
                        {
                            if (JsValueUtil.GetNormTag(pending) != JsValueUtil.TagUndefined &&
                                JsValueUtil.GetNormTag(pending) != JsValueUtil.TagNull &&
                                JsValueUtil.GetTag(pending) != JsValueUtil.TagUninitialized)
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
                    throw new JsScriptException($"zents: module '{moduleName}' rejected (tag={tag}): {message}");
                }

                if (JsValueUtil.IsException(ns))
                {
                    ThrowPendingException();
                }

                lock (_moduleNamespaces)
                {
                    if (_moduleNamespaces.TryGetValue(moduleName, out JsValue cached))
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

        internal void DrainPendingJobs()
        {
            bool pollForeground = EngineApi.Backend == EngineBackend.V8;
            while (pollForeground || EngineApi.IsJobPending(_runtime) != 0)
            {
                pollForeground = false;
                int status = EngineApi.ExecutePendingJob(_runtime, out IntPtr jobCtx);
                if (status < 0)
                {
                    IntPtr errCtx = jobCtx != IntPtr.Zero ? jobCtx : _context;
                    JsValue ex = EngineApi.GetException(errCtx);
                    string message = FormatJsValue(errCtx, ex);
                    JsValueUtil.Free(errCtx, ex);
                    throw new JsScriptException($"zents: pending job failed: {message}");
                }
            }
        }

        internal JsValue GetModuleExport(string moduleName, string exportName)
        {
            JsValue ns = LoadModuleNamespace(moduleName);
            try
            {
                JsValue export = EngineApi.GetPropertyStr(_context, ns, exportName);
                if (JsValueUtil.IsException(export))
                {
                    ThrowPendingException();
                }

                if (EngineApi.IsFunction(_context, export) == 0)
                {
                    JsValueUtil.Free(_context, export);
                    throw new JsScriptException($"zents: export '{moduleName}.{exportName}' is not callable.");
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
            JsValue ex = EngineApi.GetException(_context);
            string message = FormatJsValue(_context, ex);
            JsValueUtil.Free(_context, ex);
            throw new JsScriptException($"zents: {message}");
        }

        internal static string FormatJsValue(IntPtr ctx, JsValue val)
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
                JsValue msg = EngineApi.GetPropertyStr(ctx, val, "message");
                try
                {
                    if (JsValueUtil.GetNormTag(msg) == JsValueUtil.TagString)
                    {
                        IntPtr mptr = EngineApi.ToCStringLen2(ctx, out UIntPtr mlen, msg, 0);
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
                                EngineApi.FreeCString(ctx, mptr);
                            }
                        }
                    }
                }
                finally
                {
                    JsValueUtil.Free(ctx, msg);
                }
            }

            IntPtr cstr = EngineApi.ToCStringLen2(ctx, out UIntPtr len, val, 0);
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
                EngineApi.FreeCString(ctx, cstr);
            }
        }

        public void Dispose()
        {
            JsMainThread.EnsureMainThread();
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _moduleLoader = null;

            lock (_moduleNamespaces)
            {
                foreach (JsValue ns in _moduleNamespaces.Values)
                {
                    JsValueUtil.Free(_context, ns);
                }

                _moduleNamespaces.Clear();
            }

            if (_context != IntPtr.Zero)
            {
                IntPtr opaque = EngineApi.GetContextOpaque(_context);
                if (opaque != IntPtr.Zero)
                {
                    GCHandle.FromIntPtr(opaque).Free();
                }

                EngineApi.FreeContext(_context);
                _context = IntPtr.Zero;
            }

            if (_runtime != IntPtr.Zero)
            {
                EngineApi.StdFreeHandlers(_runtime);
                EngineApi.FreeRuntime(_runtime);
                _runtime = IntPtr.Zero;
            }
        }

        private void EnsureAlive()
        {
            JsMainThread.EnsureMainThread();
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
                JsValue compiled = EngineApi.Eval(
                    ctx,
                    bytes,
                    (UIntPtr)bytes.Length,
                    moduleName,
                    EngineApi.JsEvalTypeModule | EngineApi.JsEvalFlagCompileOnly);

                if (EngineApi.IsException(compiled) || JsValueUtil.IsException(compiled))
                {
                    JsCallbackBoundary.ThrowPendingJsException(ctx);
                    return IntPtr.Zero;
                }

                // Module is already referenced by the context module table; free the MODULE JsValue.
                IntPtr moduleDef = JsValueUtil.GetCompiledModule(compiled);
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
            IntPtr opaque = EngineApi.GetContextOpaque(ctx);
            if (opaque == IntPtr.Zero)
            {
                throw new InvalidOperationException("JsEnv context opaque is missing.");
            }

            return (JsEnv)GCHandle.FromIntPtr(opaque).Target;
        }
    }
}
