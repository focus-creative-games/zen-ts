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
using ZenTS.Jvm;
namespace ZenTS.Jvm
{
    internal interface IMonoCallbacks
    {
        void ThrowError(IntPtr ctx, string message);
        void ThrowPendingJsException(IntPtr ctx);
        void ThrowManaged(IntPtr ctx, Exception ex);
        void EnsureInitialized(IntPtr ctx);
        void Reset(IntPtr ctx);
        void Reset();
        void SetPendingException(JsValue value);
        JsValue NewCFunction(IntPtr ctx, JsCFunction callback, string name, int length);
        JsValue NewCFunctionMagic(IntPtr ctx, JsCFunctionMagic callback, string name, int length, int userMagic);
        JsValue ReturnErrorSentinel(IntPtr ctx, string message);
    }
}
namespace ZenTS.Utils
{
    internal static class JsCallbackBoundary
    {
        public static void ThrowError(IntPtr ctx, string message) => EngineApi.Callbacks.ThrowError(ctx, message);
        public static void ThrowPendingJsException(IntPtr ctx) => EngineApi.Callbacks.ThrowPendingJsException(ctx);
        public static void ThrowManaged(IntPtr ctx, Exception ex) => EngineApi.Callbacks.ThrowManaged(ctx, ex);
    }
    internal static class JsCallbackGate
    {
        public const int ErrorSentinelTag = JsValueUtil.CallbackErrorSentinelTag;
        public static void EnsureInitialized(IntPtr ctx) => EngineApi.Callbacks.EnsureInitialized(ctx);
        public static void Reset(IntPtr ctx) => EngineApi.Callbacks.Reset(ctx);
        public static void Reset() => EngineApi.Callbacks.Reset();
        public static void SetPendingException(JsValue value) => EngineApi.Callbacks.SetPendingException(value);
        public static JsValue NewCFunction(IntPtr ctx, JsCFunction callback, string name, int length) => EngineApi.Callbacks.NewCFunction(ctx, callback, name, length);
        public static JsValue NewCFunctionMagic(IntPtr ctx, JsCFunctionMagic callback, string name, int length, int userMagic) => EngineApi.Callbacks.NewCFunctionMagic(ctx, callback, name, length, userMagic);
        public static JsValue ReturnErrorSentinel(IntPtr ctx, string message) => EngineApi.Callbacks.ReturnErrorSentinel(ctx, message);
    }
}
