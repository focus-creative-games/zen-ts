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
using ZTS.Jvm;

namespace ZTS.Utils
{
    internal static class JsCallbackBoundary
    {
        public static void ThrowError(IntPtr ctx, string message)
        {
            string text = message != null && message.StartsWith("zts:", StringComparison.Ordinal)
                ? message
                : $"zts: {message}";
            JSValue err = QuickJsDll.JS_NewError(ctx);
            JSValue msg = QuickJsDll.NewString(ctx, text);
            QuickJsDll.JS_SetPropertyStr(ctx, err, "message", msg);
            JsCallbackGate.SetPendingException(err);
            NestedJsCallPendingError.Clear();
        }

        public static void ThrowPendingJsException(IntPtr ctx)
        {
            JSValue ex = QuickJsDll.JS_GetException(ctx);
            JsCallbackGate.SetPendingException(ex);
        }

        public static void ThrowManaged(IntPtr ctx, Exception ex)
        {
            ThrowError(ctx, ex.Message);
        }
    }

    internal static class JsCallbackGate
    {
        public const int ErrorSentinelTag = JsValueUtil.CallbackErrorSentinelTag;

        [DllImport(QuickJsDllName.MonoGate, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_gate_init")]
        private static extern void NativeGateInit(IntPtr jsThrowOut, int errorSentinelTag);

        [DllImport(QuickJsDllName.MonoGate, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_get_callback_gate")]
        private static extern IntPtr NativeGetCallbackGate();

        [DllImport(QuickJsDllName.MonoGate, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_register_callback")]
        private static extern int NativeRegisterCallback(IntPtr fn);

        [DllImport(QuickJsDllName.MonoGate, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_set_pending_exception")]
        private static extern void NativeSetPendingException(ref JSValue value);

        [DllImport(QuickJsDllName.MonoGate, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_callback_error_sentinel")]
        private static extern int NativeErrorSentinel();

        [DllImport(QuickJsDllName.MonoGate, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_gate_reset")]
        private static extern void NativeGateReset();

        [DllImport(QuickJsDllName.MonoGate, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_take_pending_exception")]
        private static extern int NativeTakePendingException(out JSValue value);

        private static readonly List<Delegate> PinnedCallbacks = new List<Delegate>();
        private static IntPtr _gateFunctionPtr;
        private static bool _initialized;
        private static bool _useGateDll;

        public static void EnsureInitialized(IntPtr ctx)
        {
            if (_initialized)
            {
                return;
            }

            _useGateDll = TryInitGateDll(ctx);
            _initialized = true;
        }

        public static void Reset(IntPtr ctx)
        {
            if (_useGateDll)
            {
                try
                {
                    if (ctx != IntPtr.Zero && NativeTakePendingException(out JSValue pending) != 0)
                    {
                        JsValueUtil.Free(ctx, pending);
                    }

                    NativeGateReset();
                }
                catch (DllNotFoundException)
                {
                }
                catch (EntryPointNotFoundException)
                {
                    try
                    {
                        NativeGateReset();
                    }
                    catch (DllNotFoundException)
                    {
                    }
                }
            }

            PinnedCallbacks.Clear();
            // Keep _initialized/_gateFunctionPtr — DLL and throw shim stay valid across runtimes.
        }

        public static void Reset() => Reset(IntPtr.Zero);

        public static void SetPendingException(JSValue value)
        {
            if (_useGateDll)
            {
                NativeSetPendingException(ref value);
            }
        }

        public static JSValue NewCFunction(IntPtr ctx, JsCFunction callback, string name, int length)
        {
            EnsureInitialized(ctx);

            if (_useGateDll)
            {
                JsCFunctionMagicOut adapter = (IntPtr c, ref JSValue thisVal, int argc, IntPtr argv, int magic, out JSValue result) =>
                {
                    result = callback(c, thisVal, argc, argv);
                };
                PinnedCallbacks.Add(adapter);
                PinnedCallbacks.Add(callback);
                IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(adapter);

                int slot = NativeRegisterCallback(fnPtr);
                if (slot < 0)
                {
                    throw new JsScriptException("zts: callback gate registration failed.");
                }

                return QuickJsDll.JS_NewCFunction2(
                    ctx,
                    _gateFunctionPtr,
                    name ?? string.Empty,
                    length,
                    QuickJsDll.JsCfuncGenericMagic,
                    slot);
            }

            // Direct path is unsafe for 16-byte JSValue returns on Mono; require gate.
            throw new JsScriptException("zts: zts_mono_gate is required on Editor Mono.");
        }

        public static JSValue NewCFunctionMagic(IntPtr ctx, JsCFunctionMagic callback, string name, int length, int userMagic)
        {
            EnsureInitialized(ctx);

            if (_useGateDll)
            {
                JsCFunctionMagicOut adapter = (IntPtr c, ref JSValue thisVal, int argc, IntPtr argv, int magic, out JSValue result) =>
                {
                    result = callback(c, thisVal, argc, argv, userMagic);
                };
                PinnedCallbacks.Add(adapter);
                PinnedCallbacks.Add(callback);
                IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(adapter);

                int slot = NativeRegisterCallback(fnPtr);
                if (slot < 0)
                {
                    throw new JsScriptException("zts: callback gate registration failed.");
                }

                return QuickJsDll.JS_NewCFunction2(
                    ctx,
                    _gateFunctionPtr,
                    name ?? string.Empty,
                    length,
                    QuickJsDll.JsCfuncGenericMagic,
                    slot);
            }

            throw new JsScriptException("zts: zts_mono_gate is required on Editor Mono.");
        }

        public static JSValue ReturnErrorSentinel(IntPtr ctx, string message)
        {
            JsCallbackBoundary.ThrowError(ctx, message);
            return JsValueUtil.MakeErrorSentinel();
        }

        private static bool TryInitGateDll(IntPtr ctx)
        {
            try
            {
                // Use pointer-ABI throw shim (not raw JS_Throw).
                IntPtr throwPtr = NativeExport.Find("zts_JS_Throw");
                if (throwPtr == IntPtr.Zero)
                {
                    return false;
                }

                NativeGateInit(throwPtr, ErrorSentinelTag);
                _gateFunctionPtr = NativeGetCallbackGate();
                _ = NativeErrorSentinel();
                return _gateFunctionPtr != IntPtr.Zero;
            }
            catch (DllNotFoundException)
            {
                JsPrintBuffer.Log("[ZTS] zts_mono_gate not found.");
                return false;
            }
        }

        private static class NativeExport
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            [DllImport("kernel32", CharSet = CharSet.Ansi, EntryPoint = "GetModuleHandleA", SetLastError = true)]
            private static extern IntPtr GetModuleHandle(string lpModuleName);

            [DllImport("kernel32", CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress", SetLastError = true)]
            private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

            internal static IntPtr Find(string symbol)
            {
                string baseName = QuickJsDllName.QuickJs;
                string[] candidates =
                {
                    baseName,
                    baseName + ".dll",
                    "lib" + baseName,
                    "lib" + baseName + ".dll",
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    IntPtr module = GetModuleHandle(candidates[i]);
                    if (module == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr proc = GetProcAddress(module, symbol);
                    if (proc != IntPtr.Zero)
                    {
                        return proc;
                    }
                }

                return IntPtr.Zero;
            }
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            private static readonly IntPtr RTLD_DEFAULT = (IntPtr)(-2);

            [DllImport("libdl.dylib", EntryPoint = "dlsym")]
            private static extern IntPtr dlsym(IntPtr handle, string symbol);

            internal static IntPtr Find(string symbol) => dlsym(RTLD_DEFAULT, symbol);
#else
            private static readonly IntPtr RTLD_DEFAULT = IntPtr.Zero;

            [DllImport("libdl.so.2", EntryPoint = "dlsym")]
            private static extern IntPtr dlsym(IntPtr handle, string symbol);

            internal static IntPtr Find(string symbol) => dlsym(RTLD_DEFAULT, symbol);
#endif
        }
    }
}
