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
using System.Text;
using System.Threading;

namespace ZTS.Jvm
{
    /// <summary>
    /// QuickJS JSValue on Win64 (non-NAN-boxing): { union u; int64_t tag } = 16 bytes.
    /// Native calls go through zts_* pointer shims — Mono P/Invoke cannot safely
    /// pass/return 16-byte structs by value against the MSVC x64 ABI.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct JSValue
    {
        [FieldOffset(0)] public ulong UInt64;
        [FieldOffset(0)] public double Float64;
        [FieldOffset(0)] public IntPtr Ptr;
        [FieldOffset(8)] public long Tag;
    }

    /// <summary>
    /// Managed→native callback ABI used by zts_mono_gate (all JSValues via pointers).
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void JsCFunctionOut(IntPtr ctx, ref JSValue thisVal, int argc, IntPtr argv, out JSValue result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void JsCFunctionMagicOut(IntPtr ctx, ref JSValue thisVal, int argc, IntPtr argv, int magic, out JSValue result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr JsModuleLoaderFunc(IntPtr ctx, IntPtr moduleName, IntPtr opaque);

    /// <summary>High-level managed callback signature (converted at the gate boundary).</summary>
    internal delegate JSValue JsCFunction(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv);

    internal delegate JSValue JsCFunctionMagic(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv, int magic);

    internal static class QuickJsDll
    {
        public const int JsEvalTypeGlobal = 0;
        public const int JsEvalTypeModule = 1;
        public const int JsEvalFlagCompileOnly = 1 << 5;
        public const int JsEvalFlagStrict = 1 << 3;

        public const int JsCfuncGeneric = 0;
        public const int JsCfuncGenericMagic = 1;

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr JS_NewRuntime();

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern void JS_FreeRuntime(IntPtr rt);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr JS_NewContext(IntPtr rt);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern void JS_FreeContext(IntPtr ctx);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr JS_GetRuntime(IntPtr ctx);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern void JS_SetContextOpaque(IntPtr ctx, IntPtr opaque);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr JS_GetContextOpaque(IntPtr ctx);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern void JS_FreeCString(IntPtr ctx, IntPtr ptr);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern void JS_SetModuleLoaderFunc(IntPtr rt, IntPtr moduleNormalize, JsModuleLoaderFunc moduleLoader, IntPtr opaque);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern void js_std_init_handlers(IntPtr rt);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern void js_std_free_handlers(IntPtr rt);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern void js_std_add_helpers(IntPtr ctx, int argc, IntPtr argv);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_Eval")]
        private static extern void zts_JS_Eval(IntPtr ctx, IntPtr input, UIntPtr inputLen, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int evalFlags, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_Call")]
        private static extern void zts_JS_Call(IntPtr ctx, ref JSValue funcObj, ref JSValue thisObj, int argc, IntPtr argv, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_GetException")]
        private static extern void zts_JS_GetException(IntPtr ctx, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_Throw")]
        private static extern void zts_JS_Throw(IntPtr ctx, ref JSValue obj, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_NewObject")]
        private static extern void zts_JS_NewObject(IntPtr ctx, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_GetGlobalObject")]
        private static extern void zts_JS_GetGlobalObject(IntPtr ctx, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_SetPropertyStr")]
        private static extern int zts_JS_SetPropertyStr(IntPtr ctx, ref JSValue thisObj, [MarshalAs(UnmanagedType.LPUTF8Str)] string prop, ref JSValue val);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_GetPropertyStr")]
        private static extern void zts_JS_GetPropertyStr(IntPtr ctx, ref JSValue thisObj, [MarshalAs(UnmanagedType.LPUTF8Str)] string prop, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_ToInt32")]
        private static extern int zts_JS_ToInt32(IntPtr ctx, out int pres, ref JSValue val);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_ToFloat64")]
        private static extern int zts_JS_ToFloat64(IntPtr ctx, out double pres, ref JSValue val);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_ToBool")]
        private static extern int zts_JS_ToBool(IntPtr ctx, ref JSValue val);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_ToCStringLen2")]
        private static extern IntPtr zts_JS_ToCStringLen2(IntPtr ctx, out UIntPtr plen, ref JSValue val, int cesu8);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_NewStringLen")]
        private static extern void zts_JS_NewStringLen(IntPtr ctx, IntPtr str, UIntPtr len, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_IsFunction")]
        private static extern int zts_JS_IsFunction(IntPtr ctx, ref JSValue val);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_EvalFunction")]
        private static extern void zts_JS_EvalFunction(IntPtr ctx, ref JSValue funObj, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_ResolveModule")]
        private static extern int zts_JS_ResolveModule(IntPtr ctx, ref JSValue funObj);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts___JS_FreeValue")]
        private static extern void zts___JS_FreeValue(IntPtr ctx, ref JSValue v);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_NewCFunction2")]
        private static extern void zts_JS_NewCFunction2(IntPtr ctx, IntPtr func, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int length, int cproto, int magic, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_LoadModule")]
        private static extern void zts_JS_LoadModule(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string basename, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_GetModuleNamespace")]
        private static extern void zts_JS_GetModuleNamespace(IntPtr ctx, IntPtr moduleDef, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_PromiseState")]
        private static extern int zts_JS_PromiseState(IntPtr ctx, ref JSValue promise);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_PromiseResult")]
        private static extern void zts_JS_PromiseResult(IntPtr ctx, ref JSValue promise, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern int JS_IsJobPending(IntPtr rt);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl)]
        public static extern int JS_ExecutePendingJob(IntPtr rt, out IntPtr pctx);

        public const int JsPromisePending = 0;
        public const int JsPromiseFulfilled = 1;
        public const int JsPromiseRejected = 2;

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_NewError")]
        private static extern void zts_JS_NewError(IntPtr ctx, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_NewObjectProto")]
        private static extern void zts_JS_NewObjectProto(IntPtr ctx, ref JSValue proto, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_SetPrototype")]
        private static extern int zts_JS_SetPrototype(IntPtr ctx, ref JSValue obj, ref JSValue proto);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_SetConstructor")]
        private static extern int zts_JS_SetConstructor(IntPtr ctx, ref JSValue funcObj, ref JSValue proto);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_SetConstructorBit")]
        private static extern int zts_JS_SetConstructorBit(IntPtr ctx, ref JSValue funcObj, int val);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_DefinePropertyGetSet")]
        private static extern int zts_JS_DefinePropertyGetSet(IntPtr ctx, ref JSValue thisObj, uint prop, ref JSValue getter, ref JSValue setter, int flags);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_NewAtom")]
        private static extern uint zts_JS_NewAtom(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string str);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_FreeAtom")]
        private static extern void zts_JS_FreeAtom(IntPtr ctx, uint atom);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_DefinePropertyValueStr")]
        private static extern int zts_JS_DefinePropertyValueStr(IntPtr ctx, ref JSValue thisObj, [MarshalAs(UnmanagedType.LPUTF8Str)] string prop, ref JSValue val, int flags);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_IsArray")]
        private static extern int zts_JS_IsArray(IntPtr ctx, ref JSValue val);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_NewArray")]
        private static extern void zts_JS_NewArray(IntPtr ctx, out JSValue result);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_SetPropertyUint32")]
        private static extern int zts_JS_SetPropertyUint32(IntPtr ctx, ref JSValue thisObj, uint idx, ref JSValue val);

        [DllImport(QuickJsDllName.QuickJs, CallingConvention = CallingConvention.Cdecl, EntryPoint = "zts_JS_GetPropertyUint32")]
        private static extern void zts_JS_GetPropertyUint32(IntPtr ctx, ref JSValue thisObj, uint idx, out JSValue result);

        public static unsafe JSValue JS_Eval(IntPtr ctx, byte[] input, UIntPtr inputLen, string filename, int evalFlags)
        {
            if (input == null || input.Length == 0)
            {
                // QuickJS requires input[input_len] == '\0' even for empty.
                byte* emptyBuf = stackalloc byte[1];
                emptyBuf[0] = 0;
                zts_JS_Eval(ctx, (IntPtr)emptyBuf, UIntPtr.Zero, filename, evalFlags, out JSValue empty);
                return empty;
            }

            int len = (int)inputLen;
            if (len < 0 || len > input.Length)
            {
                len = input.Length;
            }

            // QuickJS requires input[input_len] == '\0' (reads past length for safety/lexer).
            byte[] terminated = new byte[len + 1];
            Buffer.BlockCopy(input, 0, terminated, 0, len);
            fixed (byte* p = terminated)
            {
                zts_JS_Eval(ctx, (IntPtr)p, (UIntPtr)len, filename, evalFlags, out JSValue result);
                return result;
            }
        }

        public static JSValue JS_Call(IntPtr ctx, JSValue funcObj, JSValue thisObj, int argc, IntPtr argv)
        {
            zts_JS_Call(ctx, ref funcObj, ref thisObj, argc, argv, out JSValue result);
            return result;
        }

        public static JSValue JS_GetException(IntPtr ctx)
        {
            zts_JS_GetException(ctx, out JSValue result);
            return result;
        }

        public static JSValue JS_Throw(IntPtr ctx, JSValue obj)
        {
            zts_JS_Throw(ctx, ref obj, out JSValue result);
            return result;
        }

        public static JSValue JS_NewObject(IntPtr ctx)
        {
            zts_JS_NewObject(ctx, out JSValue result);
            return result;
        }

        public static JSValue JS_GetGlobalObject(IntPtr ctx)
        {
            zts_JS_GetGlobalObject(ctx, out JSValue result);
            return result;
        }

        public static int JS_SetPropertyStr(IntPtr ctx, JSValue thisObj, string prop, JSValue val)
        {
            return zts_JS_SetPropertyStr(ctx, ref thisObj, prop, ref val);
        }

        public static JSValue JS_GetPropertyStr(IntPtr ctx, JSValue thisObj, string prop)
        {
            zts_JS_GetPropertyStr(ctx, ref thisObj, prop, out JSValue result);
            return result;
        }

        public static int JS_ToInt32(IntPtr ctx, out int pres, JSValue val)
        {
            return zts_JS_ToInt32(ctx, out pres, ref val);
        }

        public static int JS_ToFloat64(IntPtr ctx, out double pres, JSValue val)
        {
            return zts_JS_ToFloat64(ctx, out pres, ref val);
        }

        public static int JS_ToBool(IntPtr ctx, JSValue val)
        {
            return zts_JS_ToBool(ctx, ref val);
        }

        public static IntPtr JS_ToCStringLen2(IntPtr ctx, out UIntPtr plen, JSValue val, int cesu8)
        {
            return zts_JS_ToCStringLen2(ctx, out plen, ref val, cesu8);
        }

        public static unsafe JSValue JS_NewStringLen(IntPtr ctx, byte[] str, UIntPtr len)
        {
            if (str == null || str.Length == 0)
            {
                zts_JS_NewStringLen(ctx, IntPtr.Zero, UIntPtr.Zero, out JSValue empty);
                return empty;
            }

            fixed (byte* p = str)
            {
                zts_JS_NewStringLen(ctx, (IntPtr)p, len, out JSValue result);
                return result;
            }
        }

        public static int JS_IsFunction(IntPtr ctx, JSValue val)
        {
            return zts_JS_IsFunction(ctx, ref val);
        }

        public static JSValue JS_EvalFunction(IntPtr ctx, JSValue funObj)
        {
            zts_JS_EvalFunction(ctx, ref funObj, out JSValue result);
            return result;
        }

        public static int JS_ResolveModule(IntPtr ctx, JSValue funObj)
        {
            return zts_JS_ResolveModule(ctx, ref funObj);
        }

        public static void __JS_FreeValue(IntPtr ctx, JSValue v)
        {
            zts___JS_FreeValue(ctx, ref v);
        }

        public static JSValue JS_NewCFunction2(IntPtr ctx, IntPtr func, string name, int length, int cproto, int magic)
        {
            zts_JS_NewCFunction2(ctx, func, name, length, cproto, magic, out JSValue result);
            return result;
        }

        public static JSValue JS_LoadModule(IntPtr ctx, string basename, string filename)
        {
            zts_JS_LoadModule(ctx, basename, filename, out JSValue result);
            return result;
        }

        public static JSValue JS_GetModuleNamespace(IntPtr ctx, IntPtr moduleDef)
        {
            zts_JS_GetModuleNamespace(ctx, moduleDef, out JSValue result);
            return result;
        }

        public static int JS_PromiseState(IntPtr ctx, JSValue promise)
        {
            return zts_JS_PromiseState(ctx, ref promise);
        }

        public static JSValue JS_PromiseResult(IntPtr ctx, JSValue promise)
        {
            zts_JS_PromiseResult(ctx, ref promise, out JSValue result);
            return result;
        }

        public static JSValue JS_NewError(IntPtr ctx)
        {
            zts_JS_NewError(ctx, out JSValue result);
            return result;
        }

        public static JSValue JS_NewObjectProto(IntPtr ctx, JSValue proto)
        {
            zts_JS_NewObjectProto(ctx, ref proto, out JSValue result);
            return result;
        }

        public static int JS_SetPrototype(IntPtr ctx, JSValue obj, JSValue proto)
        {
            return zts_JS_SetPrototype(ctx, ref obj, ref proto);
        }

        public static int JS_SetConstructor(IntPtr ctx, JSValue funcObj, JSValue proto)
        {
            return zts_JS_SetConstructor(ctx, ref funcObj, ref proto);
        }

        public static int JS_SetConstructorBit(IntPtr ctx, JSValue funcObj, int val)
        {
            return zts_JS_SetConstructorBit(ctx, ref funcObj, val);
        }

        public static int JS_DefinePropertyGetSet(IntPtr ctx, JSValue thisObj, uint prop, JSValue getter, JSValue setter, int flags)
        {
            return zts_JS_DefinePropertyGetSet(ctx, ref thisObj, prop, ref getter, ref setter, flags);
        }

        public static uint JS_NewAtom(IntPtr ctx, string str) => zts_JS_NewAtom(ctx, str);

        public static void JS_FreeAtom(IntPtr ctx, uint atom) => zts_JS_FreeAtom(ctx, atom);

        public static int JS_DefinePropertyValueStr(IntPtr ctx, JSValue thisObj, string prop, JSValue val, int flags)
        {
            return zts_JS_DefinePropertyValueStr(ctx, ref thisObj, prop, ref val, flags);
        }

        public static int JS_IsArray(IntPtr ctx, JSValue val)
        {
            return zts_JS_IsArray(ctx, ref val);
        }

        public static JSValue JS_NewArray(IntPtr ctx)
        {
            zts_JS_NewArray(ctx, out JSValue result);
            return result;
        }

        public static int JS_SetPropertyUint32(IntPtr ctx, JSValue thisObj, uint idx, JSValue val)
        {
            return zts_JS_SetPropertyUint32(ctx, ref thisObj, idx, ref val);
        }

        public static JSValue JS_GetPropertyUint32(IntPtr ctx, JSValue thisObj, uint idx)
        {
            zts_JS_GetPropertyUint32(ctx, ref thisObj, idx, out JSValue result);
            return result;
        }

        public static bool IsException(JSValue v) => JsValueUtil.GetTag(v) == JsValueUtil.TagException;

        public static JSValue NewString(IntPtr ctx, string s)
        {
            if (s == null)
            {
                return JsValueUtil.Null;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(s);
            return JS_NewStringLen(ctx, bytes, (UIntPtr)bytes.Length);
        }
    }

    internal static class JsValueUtil
    {
        public const int TagFirst = -9;
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

        public static readonly JSValue Null = MkVal(TagNull, 0);
        public static readonly JSValue Undefined = MkVal(TagUndefined, 0);
        public static readonly JSValue False = MkVal(TagBool, 0);
        public static readonly JSValue True = MkVal(TagBool, 1);
        public static readonly JSValue Exception = MkVal(TagException, 0);

        public const int CallbackErrorSentinelTag = unchecked((int)0xFFFF5A12);

        public static JSValue MkVal(int tag, uint val) => new JSValue
        {
            UInt64 = val,
            Tag = tag
        };

        public static JSValue MkPtr(int tag, IntPtr ptr) => new JSValue
        {
            Ptr = ptr,
            Tag = tag
        };

        public static int GetTag(JSValue v) => (int)v.Tag;

        public static int GetNormTag(JSValue v) => GetTag(v);

        public static bool TagIsFloat64(int tag) => tag == TagFloat64;

        public static bool HasRefCount(JSValue v)
        {
            unchecked
            {
                return (uint)GetTag(v) >= (uint)TagFirst;
            }
        }

        public static bool IsException(JSValue v) => GetTag(v) == TagException;

        public static JSValue NewInt32(int val) => MkVal(TagInt, unchecked((uint)val));

        public static JSValue NewBool(bool val) => val ? True : False;

        public static JSValue NewFloat64(double d) => new JSValue
        {
            Float64 = d,
            Tag = TagFloat64
        };

        public static double GetFloat64(JSValue v) => v.Float64;

        public static IntPtr GetPtr(JSValue v) => v.Ptr;

        public static unsafe void Free(IntPtr ctx, JSValue v)
        {
            if (!HasRefCount(v))
            {
                return;
            }

            IntPtr ptr = v.Ptr;
            int* header = (int*)ptr.ToPointer() - 1;
            if (Interlocked.Decrement(ref *header) <= 0)
            {
                QuickJsDll.__JS_FreeValue(ctx, v);
            }
        }

        public static unsafe JSValue Dup(JSValue v)
        {
            if (!HasRefCount(v))
            {
                return v;
            }

            IntPtr ptr = v.Ptr;
            int* header = (int*)ptr.ToPointer() - 1;
            Interlocked.Increment(ref *header);
            return v;
        }

        public static bool IsErrorSentinel(JSValue v) => GetTag(v) == CallbackErrorSentinelTag;

        public static JSValue MakeErrorSentinel() => new JSValue
        {
            UInt64 = 0,
            Tag = CallbackErrorSentinelTag
        };
    }
}
