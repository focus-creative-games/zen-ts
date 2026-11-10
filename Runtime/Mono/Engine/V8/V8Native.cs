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
using System.Runtime.InteropServices;
namespace ZenTS.Jvm
{
    internal static class V8Native
    {
        internal const string Library = "zents_v8";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_TrackObject(IntPtr ctx, in JsValue value, int id, ulong ticket, JsObjectCollected callback);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_HasOwnProperty(IntPtr ctx, in JsValue value, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_NewByteArrayCopy(IntPtr ctx, byte[] data, UIntPtr length, out JsValue value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ze_GetByteArrayData(IntPtr ctx, in JsValue value, out UIntPtr length);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_GetLiveRuntimeCount();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ze_NewRuntime();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_FreeRuntime(IntPtr rt);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ze_NewContext(IntPtr rt);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_FreeContext(IntPtr ctx);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ze_GetRuntime(IntPtr ctx);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_SetContextOpaque(IntPtr ctx, IntPtr opaque);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ze_GetContextOpaque(IntPtr ctx);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_FreeCString(IntPtr ctx, IntPtr ptr);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_SetModuleLoaderFunc(IntPtr rt, IntPtr moduleNormalize,
                                                           JsModuleLoaderFunc moduleLoader, IntPtr opaque);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_StdInitHandlers(IntPtr rt);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_StdFreeHandlers(IntPtr rt);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_StdAddHelpers(IntPtr ctx, int argc, IntPtr argv);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_IsJobPending(IntPtr rt);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_ExecutePendingJob(IntPtr rt, out IntPtr pctx);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_Eval(IntPtr ctx, byte[] input, UIntPtr inputLen,
                                            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int evalFlags,
                                            out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_Call(IntPtr ctx, ref JsValue funcObj, ref JsValue thisObj, int argc, IntPtr argv,
                                            out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_GetException(IntPtr ctx, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_Throw(IntPtr ctx, ref JsValue obj, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_NewObject(IntPtr ctx, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_GetGlobalObject(IntPtr ctx, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_SetPropertyStr(IntPtr ctx, ref JsValue thisObj,
                                                     [MarshalAs(UnmanagedType.LPUTF8Str)] string prop, ref JsValue val);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_GetPropertyStr(IntPtr ctx, ref JsValue thisObj,
                                                      [MarshalAs(UnmanagedType.LPUTF8Str)] string prop,
                                                      out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_ToInt32(IntPtr ctx, out int pres, ref JsValue val);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_ToFloat64(IntPtr ctx, out double pres, ref JsValue val);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_ToBool(IntPtr ctx, ref JsValue val);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ze_ToCStringLen2(IntPtr ctx, out UIntPtr plen, ref JsValue val, int cesu8);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_NewStringLen(IntPtr ctx, byte[] str, UIntPtr len, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_IsFunction(IntPtr ctx, ref JsValue val);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_EvalFunction(IntPtr ctx, ref JsValue funObj, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_ResolveModule(IntPtr ctx, ref JsValue funObj);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_LoadModule(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string basename,
                                                  [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
                                                  out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_GetModuleNamespace(IntPtr ctx, IntPtr moduleDef, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_PromiseState(IntPtr ctx, ref JsValue promise);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_PromiseResult(IntPtr ctx, ref JsValue promise, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_NewError(IntPtr ctx, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_NewObjectProto(IntPtr ctx, ref JsValue proto, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_SetPrototype(IntPtr ctx, ref JsValue obj, ref JsValue proto);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_SetConstructor(IntPtr ctx, ref JsValue funcObj, ref JsValue proto);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_SetConstructorBit(IntPtr ctx, ref JsValue funcObj, int val);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_DefinePropertyGetSet(IntPtr ctx, ref JsValue thisObj, uint prop,
                                                           ref JsValue getter, ref JsValue setter, int flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint ze_NewAtom(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string str);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_FreeAtom(IntPtr ctx, uint atom);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_DefinePropertyValueStr(IntPtr ctx, ref JsValue thisObj,
                                                             [MarshalAs(UnmanagedType.LPUTF8Str)] string prop,
                                                             ref JsValue val, int flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_IsArray(IntPtr ctx, ref JsValue val);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_NewArray(IntPtr ctx, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ze_SetPropertyUint32(IntPtr ctx, ref JsValue thisObj, uint idx, ref JsValue val);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_GetPropertyUint32(IntPtr ctx, ref JsValue thisObj, uint idx, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_NewManaged(IntPtr ctx, IntPtr fn, int magic, int length,
                                                  [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_Dup(ref JsValue value, out JsValue result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ze_Free(IntPtr ctx, ref JsValue value);
    }
}
