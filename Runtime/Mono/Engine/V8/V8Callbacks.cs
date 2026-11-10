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
namespace ZenTS.Jvm
{
    internal sealed class V8Callbacks : IMonoCallbacks
    {
        private readonly List<Delegate> roots = new List<Delegate>();
        [ThreadStatic]
        private static IntPtr current;
        public void ThrowError(IntPtr ctx, string message)
        {
            JsValue err = EngineApi.NewError(ctx);
            EngineApi.SetPropertyStr(ctx, err, "message",
                                     EngineApi.NewString(ctx, message.StartsWith("zents:", StringComparison.Ordinal)
                                                                  ? message
                                                                  : "zents: " + message));
            EngineApi.Throw(ctx, err);
            ZenTS.Utils.NestedJsCallPendingError.Clear();
        }
        public void ThrowPendingJsException(IntPtr ctx)
        {
        }
        public void ThrowManaged(IntPtr ctx, Exception ex) => ThrowError(ctx, ex.Message);
        public void EnsureInitialized(IntPtr ctx)
        {
        }
        public void Reset(IntPtr ctx)
        {
            roots.Clear();
        }
        public void Reset() => Reset(IntPtr.Zero);
        public void SetPendingException(JsValue value)
        {
            if (current == IntPtr.Zero)
                throw new InvalidOperationException("No active V8 callback");
            EngineApi.Throw(current, value);
        }
        public JsValue NewCFunction(IntPtr ctx, JsCFunction fn, string name,
                                    int length) => NewCFunctionMagic(ctx, (c, t, n, a, m) => fn(c, t, n, a), name,
                                                                     length, 0);
        public JsValue NewCFunctionMagic(IntPtr ctx, JsCFunctionMagic fn, string name, int length, int magic)
        {
            JsCFunctionMagicOut adapter =
                (IntPtr c, ref JsValue receiver, int count, IntPtr argv, int m, out JsValue result) =>
            {
                IntPtr previous = current;
                current = c;
                try
                {
                    result = fn(c, receiver, count, argv, m);
                }
                catch (Exception ex)
                {
                    ThrowManaged(c, ex);
                    result = JsValueUtil.Exception;
                }
                finally
                {
                    current = previous;
                }
            };
            roots.Add(adapter);
            roots.Add(fn);
            V8Native.ze_NewManaged(ctx, Marshal.GetFunctionPointerForDelegate(adapter), magic, length,
                                   name ?? string.Empty, out JsValue value);
            return value;
        }
        public JsValue ReturnErrorSentinel(IntPtr ctx, string message)
        {
            ThrowError(ctx, message);
            return JsValueUtil.MakeErrorSentinel();
        }
    }
}
