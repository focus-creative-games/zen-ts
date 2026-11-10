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
namespace ZenTS.Jvm
{
    // Selected once per assembly; never switch a live runtime or mix backend values.
    internal enum EngineBackend
    {
        V8 = 1, HostJs = 2
    }
    internal interface IMonoEngineApi
    {
        EngineBackend Backend
        {
            get;
        }
        IMonoValueApi Values
        {
            get;
        }
        IMonoCallbacks Callbacks
        {
            get;
        }
        IntPtr NewRuntime();
        void FreeRuntime(IntPtr rt);
        IntPtr NewContext(IntPtr rt);
        void FreeContext(IntPtr ctx);
        IntPtr GetRuntime(IntPtr ctx);
        void SetContextOpaque(IntPtr ctx, IntPtr opaque);
        IntPtr GetContextOpaque(IntPtr ctx);
        void FreeCString(IntPtr ctx, IntPtr ptr);
        void SetModuleLoaderFunc(IntPtr rt, IntPtr moduleNormalize, JsModuleLoaderFunc moduleLoader, IntPtr opaque);
        void StdInitHandlers(IntPtr rt);
        void StdFreeHandlers(IntPtr rt);
        void StdAddHelpers(IntPtr ctx, int argc, IntPtr argv);
        int IsJobPending(IntPtr rt);
        int ExecutePendingJob(IntPtr rt, out IntPtr pctx);
        JsValue Eval(IntPtr ctx, byte[] input, UIntPtr inputLen, string filename, int evalFlags);
        JsValue Call(IntPtr ctx, JsValue funcObj, JsValue thisObj, int argc, IntPtr argv);
        JsValue GetException(IntPtr ctx);
        JsValue Throw(IntPtr ctx, JsValue obj);
        JsValue NewObject(IntPtr ctx);
        JsValue GetGlobalObject(IntPtr ctx);
        int SetPropertyStr(IntPtr ctx, JsValue thisObj, string prop, JsValue val);
        JsValue GetPropertyStr(IntPtr ctx, JsValue thisObj, string prop);
        int ToInt32(IntPtr ctx, out int pres, JsValue val);
        int ToFloat64(IntPtr ctx, out double pres, JsValue val);
        int ToBool(IntPtr ctx, JsValue val);
        IntPtr ToCStringLen2(IntPtr ctx, out UIntPtr plen, JsValue val, int cesu8);
        JsValue NewStringLen(IntPtr ctx, byte[] str, UIntPtr len);
        int IsFunction(IntPtr ctx, JsValue val);
        JsValue EvalFunction(IntPtr ctx, JsValue funObj);
        int ResolveModule(IntPtr ctx, JsValue funObj);
        JsValue LoadModule(IntPtr ctx, string basename, string filename);
        JsValue GetModuleNamespace(IntPtr ctx, IntPtr moduleDef);
        int PromiseState(IntPtr ctx, JsValue promise);
        JsValue PromiseResult(IntPtr ctx, JsValue promise);
        JsValue NewError(IntPtr ctx);
        JsValue NewObjectProto(IntPtr ctx, JsValue proto);
        int SetPrototype(IntPtr ctx, JsValue obj, JsValue proto);
        int SetConstructor(IntPtr ctx, JsValue funcObj, JsValue proto);
        int SetConstructorBit(IntPtr ctx, JsValue funcObj, int val);
        int DefinePropertyGetSet(IntPtr ctx, JsValue thisObj, uint prop, JsValue getter, JsValue setter, int flags);
        uint NewAtom(IntPtr ctx, string str);
        void FreeAtom(IntPtr ctx, uint atom);
        int DefinePropertyValueStr(IntPtr ctx, JsValue thisObj, string prop, JsValue val, int flags);
        int IsArray(IntPtr ctx, JsValue val);
        JsValue NewArray(IntPtr ctx);
        int HasOwnProperty(IntPtr ctx, JsValue value, string name);
        JsValue NewByteArrayCopy(IntPtr ctx, byte[] bytes);
        IntPtr GetByteArrayData(IntPtr ctx, JsValue value, out UIntPtr length);
        int SetPropertyUint32(IntPtr ctx, JsValue thisObj, uint idx, JsValue val);
        JsValue GetPropertyUint32(IntPtr ctx, JsValue thisObj, uint idx);
        bool IsException(JsValue v);
        JsValue NewString(IntPtr ctx, string s);
    }
    internal interface IMonoValueApi
    {
        int GetTag(JsValue v);
        int GetNormTag(JsValue v);
        bool IsException(JsValue v);
        JsValue NewInt32(int val);
        JsValue NewBool(bool val);
        JsValue NewFloat64(double d);
        double GetFloat64(JsValue v);
        IntPtr GetCompiledModule(JsValue v);
        void Free(IntPtr ctx, JsValue v);
        JsValue Dup(JsValue v);
        bool IsErrorSentinel(JsValue v);
        JsValue MakeErrorSentinel();
        ulong GetUInt64(JsValue v);
        IntPtr GetIdentity(JsValue v);
        JsValue Null
        {
            get;
        }
        JsValue Undefined
        {
            get;
        }
        JsValue False
        {
            get;
        }
        JsValue True
        {
            get;
        }
        JsValue Exception
        {
            get;
        }
    }
    internal static class EngineApi
    {
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX
        private static readonly IMonoEngineApi BackendImpl = new V8EngineBackend();
#else
        private static readonly IMonoEngineApi BackendImpl = UnsupportedBackend();
        private static IMonoEngineApi UnsupportedBackend() => throw new PlatformNotSupportedException(
            "ZenTS requires a V8 native plugin for this Editor platform; supported Editor hosts are Windows x64 and macOS with a matching native plugin.");
#endif
        internal static IMonoValueApi ValueBackend => BackendImpl.Values;
        internal static IMonoCallbacks Callbacks => BackendImpl.Callbacks;
        public static EngineBackend Backend => BackendImpl.Backend;
        public const int JsEvalTypeGlobal = 0;
        public const int JsEvalTypeModule = 1;
        public const int JsEvalFlagCompileOnly = 1 << 5;
        public const int JsEvalFlagStrict = 1 << 3;
        public const int JsPromisePending = 0;
        public const int JsPromiseFulfilled = 1;
        public const int JsPromiseRejected = 2;
        public static IntPtr NewRuntime() => BackendImpl.NewRuntime();
        public static void FreeRuntime(IntPtr rt) => BackendImpl.FreeRuntime(rt);
        public static IntPtr NewContext(IntPtr rt) => BackendImpl.NewContext(rt);
        public static void FreeContext(IntPtr ctx) => BackendImpl.FreeContext(ctx);
        public static IntPtr GetRuntime(IntPtr ctx) => BackendImpl.GetRuntime(ctx);
        public static void SetContextOpaque(IntPtr ctx, IntPtr opaque) => BackendImpl.SetContextOpaque(ctx, opaque);
        public static IntPtr GetContextOpaque(IntPtr ctx) => BackendImpl.GetContextOpaque(ctx);
        public static void FreeCString(IntPtr ctx, IntPtr ptr) => BackendImpl.FreeCString(ctx, ptr);
        public static void SetModuleLoaderFunc(IntPtr rt, IntPtr moduleNormalize, JsModuleLoaderFunc moduleLoader, IntPtr opaque) => BackendImpl.SetModuleLoaderFunc(rt, moduleNormalize, moduleLoader, opaque);
        public static void StdInitHandlers(IntPtr rt) => BackendImpl.StdInitHandlers(rt);
        public static void StdFreeHandlers(IntPtr rt) => BackendImpl.StdFreeHandlers(rt);
        public static void StdAddHelpers(IntPtr ctx, int argc, IntPtr argv) => BackendImpl.StdAddHelpers(ctx, argc, argv);
        public static int IsJobPending(IntPtr rt) => BackendImpl.IsJobPending(rt);
        public static int ExecutePendingJob(IntPtr rt, out IntPtr pctx) => BackendImpl.ExecutePendingJob(rt, out pctx);
        public static JsValue Eval(IntPtr ctx, byte[] input, UIntPtr inputLen, string filename, int evalFlags) => BackendImpl.Eval(ctx, input, inputLen, filename, evalFlags);
        public static JsValue Call(IntPtr ctx, JsValue funcObj, JsValue thisObj, int argc, IntPtr argv) => BackendImpl.Call(ctx, funcObj, thisObj, argc, argv);
        public static JsValue GetException(IntPtr ctx) => BackendImpl.GetException(ctx);
        public static JsValue Throw(IntPtr ctx, JsValue obj) => BackendImpl.Throw(ctx, obj);
        public static JsValue NewObject(IntPtr ctx) => BackendImpl.NewObject(ctx);
        public static JsValue GetGlobalObject(IntPtr ctx) => BackendImpl.GetGlobalObject(ctx);
        public static int SetPropertyStr(IntPtr ctx, JsValue thisObj, string prop, JsValue val) => BackendImpl.SetPropertyStr(ctx, thisObj, prop, val);
        public static JsValue GetPropertyStr(IntPtr ctx, JsValue thisObj, string prop) => BackendImpl.GetPropertyStr(ctx, thisObj, prop);
        public static int ToInt32(IntPtr ctx, out int pres, JsValue val) => BackendImpl.ToInt32(ctx, out pres, val);
        public static int ToFloat64(IntPtr ctx, out double pres, JsValue val) => BackendImpl.ToFloat64(ctx, out pres, val);
        public static int ToBool(IntPtr ctx, JsValue val) => BackendImpl.ToBool(ctx, val);
        public static IntPtr ToCStringLen2(IntPtr ctx, out UIntPtr plen, JsValue val, int cesu8) => BackendImpl.ToCStringLen2(ctx, out plen, val, cesu8);
        public static JsValue NewStringLen(IntPtr ctx, byte[] str, UIntPtr len) => BackendImpl.NewStringLen(ctx, str, len);
        public static int IsFunction(IntPtr ctx, JsValue val) => BackendImpl.IsFunction(ctx, val);
        public static JsValue EvalFunction(IntPtr ctx, JsValue funObj) => BackendImpl.EvalFunction(ctx, funObj);
        public static int ResolveModule(IntPtr ctx, JsValue funObj) => BackendImpl.ResolveModule(ctx, funObj);
        public static JsValue LoadModule(IntPtr ctx, string basename, string filename) => BackendImpl.LoadModule(ctx, basename, filename);
        public static JsValue GetModuleNamespace(IntPtr ctx, IntPtr moduleDef) => BackendImpl.GetModuleNamespace(ctx, moduleDef);
        public static int PromiseState(IntPtr ctx, JsValue promise) => BackendImpl.PromiseState(ctx, promise);
        public static JsValue PromiseResult(IntPtr ctx, JsValue promise) => BackendImpl.PromiseResult(ctx, promise);
        public static JsValue NewError(IntPtr ctx) => BackendImpl.NewError(ctx);
        public static JsValue NewObjectProto(IntPtr ctx, JsValue proto) => BackendImpl.NewObjectProto(ctx, proto);
        public static int SetPrototype(IntPtr ctx, JsValue obj, JsValue proto) => BackendImpl.SetPrototype(ctx, obj, proto);
        public static int SetConstructor(IntPtr ctx, JsValue funcObj, JsValue proto) => BackendImpl.SetConstructor(ctx, funcObj, proto);
        public static int SetConstructorBit(IntPtr ctx, JsValue funcObj, int val) => BackendImpl.SetConstructorBit(ctx, funcObj, val);
        public static int DefinePropertyGetSet(IntPtr ctx, JsValue thisObj, uint prop, JsValue getter, JsValue setter, int flags) => BackendImpl.DefinePropertyGetSet(ctx, thisObj, prop, getter, setter, flags);
        public static uint NewAtom(IntPtr ctx, string str) => BackendImpl.NewAtom(ctx, str);
        public static void FreeAtom(IntPtr ctx, uint atom) => BackendImpl.FreeAtom(ctx, atom);
        public static int DefinePropertyValueStr(IntPtr ctx, JsValue thisObj, string prop, JsValue val, int flags) => BackendImpl.DefinePropertyValueStr(ctx, thisObj, prop, val, flags);
        public static int IsArray(IntPtr ctx, JsValue val) => BackendImpl.IsArray(ctx, val);
        public static JsValue NewArray(IntPtr ctx) => BackendImpl.NewArray(ctx);
        public static int HasOwnProperty(IntPtr ctx, JsValue value, string name) => BackendImpl.HasOwnProperty(ctx, value, name);
        public static JsValue NewByteArrayCopy(IntPtr ctx, byte[] bytes) => BackendImpl.NewByteArrayCopy(ctx, bytes);
        public static IntPtr GetByteArrayData(IntPtr ctx, JsValue value, out UIntPtr length) => BackendImpl.GetByteArrayData(ctx, value, out length);
        public static int SetPropertyUint32(IntPtr ctx, JsValue thisObj, uint idx, JsValue val) => BackendImpl.SetPropertyUint32(ctx, thisObj, idx, val);
        public static JsValue GetPropertyUint32(IntPtr ctx, JsValue thisObj, uint idx) => BackendImpl.GetPropertyUint32(ctx, thisObj, idx);
        public static bool IsException(JsValue v) => BackendImpl.IsException(v);
        public static JsValue NewString(IntPtr ctx, string s) => BackendImpl.NewString(ctx, s);
    }
    internal static class JsValueUtil
    {
        public const int TagUninitialized = 4;
        public const int TagBigInt = -9;
        public const int TagSymbol = -8;
        public const int TagString = -7;
        public const int TagObject = -1;
        public const int TagInt = 0;
        public const int TagBool = 1;
        public const int TagNull = 2;
        public const int TagUndefined = 3;
        public const int TagException = 6;
        public const int TagFloat64 = 8;
        public const int CallbackErrorSentinelTag = unchecked((int)0xFFFF5A12);
        public static int GetTag(JsValue v) => EngineApi.ValueBackend.GetTag(v);
        public static int GetNormTag(JsValue v) => EngineApi.ValueBackend.GetNormTag(v);
        public static bool IsException(JsValue v) => EngineApi.ValueBackend.IsException(v);
        public static JsValue NewInt32(int val) => EngineApi.ValueBackend.NewInt32(val);
        public static JsValue NewBool(bool val) => EngineApi.ValueBackend.NewBool(val);
        public static JsValue NewFloat64(double d) => EngineApi.ValueBackend.NewFloat64(d);
        public static double GetFloat64(JsValue v) => EngineApi.ValueBackend.GetFloat64(v);
        public static IntPtr GetCompiledModule(JsValue v) => EngineApi.ValueBackend.GetCompiledModule(v);
        public static void Free(IntPtr ctx, JsValue v) => EngineApi.ValueBackend.Free(ctx, v);
        public static JsValue Dup(JsValue v) => EngineApi.ValueBackend.Dup(v);
        public static bool IsErrorSentinel(JsValue v) => EngineApi.ValueBackend.IsErrorSentinel(v);
        public static JsValue MakeErrorSentinel() => EngineApi.ValueBackend.MakeErrorSentinel();
        public static ulong GetUInt64(JsValue v) => EngineApi.ValueBackend.GetUInt64(v);
        public static IntPtr GetIdentity(JsValue v) => EngineApi.ValueBackend.GetIdentity(v);
        public static JsValue Null => EngineApi.ValueBackend.Null;
        public static JsValue Undefined => EngineApi.ValueBackend.Undefined;
        public static JsValue False => EngineApi.ValueBackend.False;
        public static JsValue True => EngineApi.ValueBackend.True;
        public static JsValue Exception => EngineApi.ValueBackend.Exception;
    }
}
