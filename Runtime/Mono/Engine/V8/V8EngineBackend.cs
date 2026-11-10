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
    internal sealed class V8EngineBackend : IMonoEngineApi
    {
        public EngineBackend Backend => EngineBackend.V8;
        public int HasOwnProperty(IntPtr ctx, JsValue value, string name) => V8Native.ze_HasOwnProperty(ctx,in value,name);
        public JsValue NewByteArrayCopy(IntPtr ctx, byte[] bytes) { V8Native.ze_NewByteArrayCopy(ctx, bytes, (UIntPtr)bytes.Length, out JsValue value); return value; }
        public IntPtr GetByteArrayData(IntPtr ctx, JsValue value, out UIntPtr length) => V8Native.ze_GetByteArrayData(ctx, in value, out length);
        public IMonoValueApi Values { get; } = new V8ValueBackend();
        public IMonoCallbacks Callbacks { get; } = new V8Callbacks();
        private JsModuleLoaderFunc loaderRoot;
        public IntPtr NewRuntime()
        {
            return V8Native.ze_NewRuntime();
        }
        public void FreeRuntime(IntPtr rt)
        {
            V8Native.ze_FreeRuntime(rt);
        }
        public IntPtr NewContext(IntPtr rt)
        {
            return V8Native.ze_NewContext(rt);
        }
        public void FreeContext(IntPtr ctx)
        {
            V8Native.ze_FreeContext(ctx);
        }
        public IntPtr GetRuntime(IntPtr ctx)
        {
            return V8Native.ze_GetRuntime(ctx);
        }
        public void SetContextOpaque(IntPtr ctx, IntPtr opaque)
        {
            V8Native.ze_SetContextOpaque(ctx, opaque);
        }
        public IntPtr GetContextOpaque(IntPtr ctx)
        {
            return V8Native.ze_GetContextOpaque(ctx);
        }
        public void FreeCString(IntPtr ctx, IntPtr ptr)
        {
            V8Native.ze_FreeCString(ctx, ptr);
        }
        public void SetModuleLoaderFunc(IntPtr rt, IntPtr moduleNormalize, JsModuleLoaderFunc moduleLoader,
                                        IntPtr opaque)
        {
            loaderRoot = moduleLoader;
            V8Native.ze_SetModuleLoaderFunc(rt, moduleNormalize, moduleLoader, opaque);
        }
        public void StdInitHandlers(IntPtr rt)
        {
            V8Native.ze_StdInitHandlers(rt);
        }
        public void StdFreeHandlers(IntPtr rt)
        {
            V8Native.ze_StdFreeHandlers(rt);
        }
        public void StdAddHelpers(IntPtr ctx, int argc, IntPtr argv)
        {
            V8Native.ze_StdAddHelpers(ctx, argc, argv);
        }
        public int IsJobPending(IntPtr rt)
        {
            return V8Native.ze_IsJobPending(rt);
        }
        public int ExecutePendingJob(IntPtr rt, out IntPtr pctx)
        {
            return V8Native.ze_ExecutePendingJob(rt, out pctx);
        }
        public JsValue Eval(IntPtr ctx, byte[] input, UIntPtr inputLen, string filename, int evalFlags)
        {
            V8Native.ze_Eval(ctx, input, inputLen, filename, evalFlags, out JsValue result);
            return result;
        }
        public JsValue Call(IntPtr ctx, JsValue funcObj, JsValue thisObj, int argc, IntPtr argv)
        {
            V8Native.ze_Call(ctx, ref funcObj, ref thisObj, argc, argv, out JsValue result);
            return result;
        }
        public JsValue GetException(IntPtr ctx)
        {
            V8Native.ze_GetException(ctx, out JsValue result);
            return result;
        }
        public JsValue Throw(IntPtr ctx, JsValue obj)
        {
            V8Native.ze_Throw(ctx, ref obj, out JsValue result);
            return result;
        }
        public JsValue NewObject(IntPtr ctx)
        {
            V8Native.ze_NewObject(ctx, out JsValue result);
            return result;
        }
        public JsValue GetGlobalObject(IntPtr ctx)
        {
            V8Native.ze_GetGlobalObject(ctx, out JsValue result);
            return result;
        }
        public int SetPropertyStr(IntPtr ctx, JsValue thisObj, string prop, JsValue val)
        {
            return V8Native.ze_SetPropertyStr(ctx, ref thisObj, prop, ref val);
        }
        public JsValue GetPropertyStr(IntPtr ctx, JsValue thisObj, string prop)
        {
            V8Native.ze_GetPropertyStr(ctx, ref thisObj, prop, out JsValue result);
            return result;
        }
        public int ToInt32(IntPtr ctx, out int pres, JsValue val)
        {
            return V8Native.ze_ToInt32(ctx, out pres, ref val);
        }
        public int ToFloat64(IntPtr ctx, out double pres, JsValue val)
        {
            return V8Native.ze_ToFloat64(ctx, out pres, ref val);
        }
        public int ToBool(IntPtr ctx, JsValue val)
        {
            return V8Native.ze_ToBool(ctx, ref val);
        }
        public IntPtr ToCStringLen2(IntPtr ctx, out UIntPtr plen, JsValue val, int cesu8)
        {
            return V8Native.ze_ToCStringLen2(ctx, out plen, ref val, cesu8);
        }
        public JsValue NewStringLen(IntPtr ctx, byte[] str, UIntPtr len)
        {
            V8Native.ze_NewStringLen(ctx, str, len, out JsValue result);
            return result;
        }
        public int IsFunction(IntPtr ctx, JsValue val)
        {
            return V8Native.ze_IsFunction(ctx, ref val);
        }
        public JsValue EvalFunction(IntPtr ctx, JsValue funObj)
        {
            V8Native.ze_EvalFunction(ctx, ref funObj, out JsValue result);
            return result;
        }
        public int ResolveModule(IntPtr ctx, JsValue funObj)
        {
            return V8Native.ze_ResolveModule(ctx, ref funObj);
        }
        public JsValue LoadModule(IntPtr ctx, string basename, string filename)
        {
            V8Native.ze_LoadModule(ctx, basename, filename, out JsValue result);
            return result;
        }
        public JsValue GetModuleNamespace(IntPtr ctx, IntPtr moduleDef)
        {
            V8Native.ze_GetModuleNamespace(ctx, moduleDef, out JsValue result);
            return result;
        }
        public int PromiseState(IntPtr ctx, JsValue promise)
        {
            return V8Native.ze_PromiseState(ctx, ref promise);
        }
        public JsValue PromiseResult(IntPtr ctx, JsValue promise)
        {
            V8Native.ze_PromiseResult(ctx, ref promise, out JsValue result);
            return result;
        }
        public JsValue NewError(IntPtr ctx)
        {
            V8Native.ze_NewError(ctx, out JsValue result);
            return result;
        }
        public JsValue NewObjectProto(IntPtr ctx, JsValue proto)
        {
            V8Native.ze_NewObjectProto(ctx, ref proto, out JsValue result);
            return result;
        }
        public int SetPrototype(IntPtr ctx, JsValue obj, JsValue proto)
        {
            return V8Native.ze_SetPrototype(ctx, ref obj, ref proto);
        }
        public int SetConstructor(IntPtr ctx, JsValue funcObj, JsValue proto)
        {
            return V8Native.ze_SetConstructor(ctx, ref funcObj, ref proto);
        }
        public int SetConstructorBit(IntPtr ctx, JsValue funcObj, int val)
        {
            return V8Native.ze_SetConstructorBit(ctx, ref funcObj, val);
        }
        public int DefinePropertyGetSet(IntPtr ctx, JsValue thisObj, uint prop, JsValue getter, JsValue setter,
                                        int flags)
        {
            return V8Native.ze_DefinePropertyGetSet(ctx, ref thisObj, prop, ref getter, ref setter, flags);
        }
        public uint NewAtom(IntPtr ctx, string str)
        {
            return V8Native.ze_NewAtom(ctx, str);
        }
        public void FreeAtom(IntPtr ctx, uint atom)
        {
            V8Native.ze_FreeAtom(ctx, atom);
        }
        public int DefinePropertyValueStr(IntPtr ctx, JsValue thisObj, string prop, JsValue val, int flags)
        {
            return V8Native.ze_DefinePropertyValueStr(ctx, ref thisObj, prop, ref val, flags);
        }
        public int IsArray(IntPtr ctx, JsValue val)
        {
            return V8Native.ze_IsArray(ctx, ref val);
        }
        public JsValue NewArray(IntPtr ctx)
        {
            V8Native.ze_NewArray(ctx, out JsValue result);
            return result;
        }
        public int SetPropertyUint32(IntPtr ctx, JsValue thisObj, uint idx, JsValue val)
        {
            return V8Native.ze_SetPropertyUint32(ctx, ref thisObj, idx, ref val);
        }
        public JsValue GetPropertyUint32(IntPtr ctx, JsValue thisObj, uint idx)
        {
            V8Native.ze_GetPropertyUint32(ctx, ref thisObj, idx, out JsValue result);
            return result;
        }
        public bool IsException(JsValue v) => v.Tag == 6;
        public JsValue NewString(IntPtr ctx, string s)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s ?? "");
            return NewStringLen(ctx, bytes, (UIntPtr)bytes.Length);
        }
    }

    internal sealed class V8ValueBackend : IMonoValueApi
    {
        public int GetTag(JsValue v) => (int)v.Tag;
        public int GetNormTag(JsValue v) => (int)v.Tag;
        public bool IsException(JsValue v) => v.Tag == 6;
        public JsValue NewInt32(int v) => new JsValue { UInt64 = unchecked((uint)v), Tag = 0 };
        public JsValue NewBool(bool v) => new JsValue { UInt64 = v ? 1UL : 0, Tag = 1 };
        public JsValue NewFloat64(double v) => new JsValue { Float64 = v, Tag = 8 };
        public double GetFloat64(JsValue v) => v.Float64;
        public IntPtr GetCompiledModule(JsValue v) => v.Tag == -3 ? v.Ptr : IntPtr.Zero;
        public void Free(IntPtr ctx, JsValue v) => V8Native.ze_Free(ctx, ref v);
        public JsValue Dup(JsValue v)
        {
            V8Native.ze_Dup(ref v, out JsValue result);
            return result;
        }
        public bool IsErrorSentinel(JsValue v) => v.Tag == JsValueUtil.CallbackErrorSentinelTag;
        public JsValue MakeErrorSentinel() => new JsValue { Tag = JsValueUtil.CallbackErrorSentinelTag };
        public ulong GetUInt64(JsValue v) => v.UInt64;
        public IntPtr GetIdentity(JsValue v) => v.Tag < 0 ? v.Ptr : IntPtr.Zero;
        public JsValue Null => new JsValue { Tag = 2 };
        public JsValue Undefined => new JsValue { Tag = 3 };
        public JsValue False => NewBool(false);
        public JsValue True => NewBool(true);
        public JsValue Exception => new JsValue { Tag = 6 };
    }
}
