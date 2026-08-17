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
using System.Reflection;
using ZenTS.Jvm;
using ZenTS.Mt;

namespace ZenTS.Marshaling
{
    internal static unsafe class PointerMarshal
    {
        public static JSValue Push(IntPtr ctx, object value, Type pointerType)
        {
            if (value == null)
            {
                return JsValueUtil.Null;
            }

            IntPtr addr = ExtractAddress(value);
            var handle = new PointerHandle(addr, pointerType);
            int id = ObjectRegistry.Register(handle, pointerType);
            JSValue obj = QuickJsDll.JS_NewObject(ctx);
            QuickJsDll.JS_SetPropertyStr(ctx, obj, "__zents_id", JsValueUtil.NewInt32(id));
            QuickJsDll.JS_SetPropertyStr(ctx, obj, "__zents_pointer", JsValueUtil.NewBool(true));
            return obj;
        }

        public static object Pop(IntPtr ctx, JSValue jsValue, Type expectedPointerType)
        {
            int tag = JsValueUtil.GetNormTag(jsValue);
            if (tag == JsValueUtil.TagNull)
            {
                return null;
            }

            if (tag == JsValueUtil.TagUndefined)
            {
                throw new JsScriptException(
                    $"zents: undefined is not assignable to {expectedPointerType.FullName} (use null for null pointer).");
            }

            if (tag != JsValueUtil.TagObject)
            {
                throw new JsScriptException($"zents: expected Pointer handle for {expectedPointerType.FullName}.");
            }

            JSValue ptrFlag = QuickJsDll.JS_GetPropertyStr(ctx, jsValue, "__zents_pointer");
            try
            {
                if (JsValueUtil.GetNormTag(ptrFlag) != JsValueUtil.TagBool || ptrFlag.UInt64 == 0)
                {
                    throw new JsScriptException($"zents: expected Pointer handle for {expectedPointerType.FullName}.");
                }
            }
            finally
            {
                JsValueUtil.Free(ctx, ptrFlag);
            }

            if (!ObjectRegistry.TryGetObject(ctx, jsValue, out object obj) || !(obj is PointerHandle handle))
            {
                throw new JsScriptException($"zents: expected Pointer handle for {expectedPointerType.FullName}.");
            }

            if (!PointerTypesMatch(handle.PointerType, expectedPointerType))
            {
                throw new JsScriptException(
                    $"zents: Pointer type mismatch: expected {expectedPointerType.FullName}, got {handle.PointerType.FullName}.");
            }

            return BoxPointer(handle.Address, expectedPointerType);
        }

        private static IntPtr ExtractAddress(object value)
        {
            if (value is PointerHandle ph)
            {
                return ph.Address;
            }

            if (value == null)
            {
                return IntPtr.Zero;
            }

            // Unity Mono: boxed pointers are System.Reflection.Pointer instances;
            // GetType() may report the pointer type — always Unbox when possible.
            try
            {
                return new IntPtr(Pointer.Unbox(value));
            }
            catch (ArgumentException)
            {
                return IntPtr.Zero;
            }
        }

        private static object BoxPointer(IntPtr address, Type pointerType)
        {
            void* raw = address.ToPointer();
            return Pointer.Box(raw, pointerType);
        }

        private static bool PointerTypesMatch(Type actual, Type expected)
        {
            if (actual == expected)
            {
                return true;
            }

            if (expected == typeof(void*) || actual == typeof(void*))
            {
                return true;
            }

            return false;
        }
    }
}
