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
using ZenTS.Mt;

namespace ZenTS.Marshaling
{
    internal static class PrimitiveMarshal
    {
        public static JSValue Push(IntPtr ctx, object value)
        {
            if (value == null)
            {
                return JsValueUtil.Null;
            }

            switch (value)
            {
                case int i:
                    return JsValueUtil.NewInt32(i);
                case byte b:
                    return JsValueUtil.NewInt32(b);
                case sbyte sb:
                    return JsValueUtil.NewInt32(sb);
                case short s16:
                    return JsValueUtil.NewInt32(s16);
                case ushort u16:
                    return JsValueUtil.NewInt32(u16);
                case bool b:
                    return JsValueUtil.NewBool(b);
                case float f:
                    return JsValueUtil.NewFloat64(f);
                case double d:
                    return JsValueUtil.NewFloat64(d);
                case string s:
                    return QuickJsDll.NewString(ctx, s);
                case long l:
                    if (l >= int.MinValue && l <= int.MaxValue)
                    {
                        return JsValueUtil.NewInt32((int)l);
                    }

                    return JsValueUtil.NewFloat64(l);
                default:
                    if (value.GetType().IsEnum)
                    {
                        return EnumMarshal.Push(ctx, value);
                    }

                    if (value is OpaqueValue opaque)
                    {
                        return opaque.JsValue;
                    }

                    int slot = ObjectRegistry.Register(value);
                    return ObjectRegistry.CreateJsHandle(ctx, slot);
            }
        }

        public static object Pop(IntPtr ctx, JSValue jsValue)
        {
            int tag = JsValueUtil.GetNormTag(jsValue);
            switch (tag)
            {
                case JsValueUtil.TagNull:
                    return null;
                case JsValueUtil.TagUndefined:
                    return DBNull.Value;
                case JsValueUtil.TagBool:
                    return jsValue.UInt64 != 0;
                case JsValueUtil.TagInt:
                    return (int)jsValue.UInt64;
                case JsValueUtil.TagFloat64:
                    return JsValueUtil.GetFloat64(jsValue);
                case JsValueUtil.TagObject:
                    if (ObjectRegistry.TryGetObject(ctx, jsValue, out object obj))
                    {
                        return obj;
                    }

                    if (QuickJsDll.JS_IsArray(ctx, jsValue) != 0)
                    {
                        return ArrayMarshal.FromJsArray(ctx, jsValue);
                    }

                    return jsValue;
                default:
                    if (tag == JsValueUtil.TagString)
                    {
                        IntPtr cstr = QuickJsDll.JS_ToCStringLen2(ctx, out UIntPtr len, jsValue, 0);
                        if (cstr == IntPtr.Zero)
                        {
                            return string.Empty;
                        }

                        try
                        {
                            return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(cstr, (int)len) ?? string.Empty;
                        }
                        finally
                        {
                            QuickJsDll.JS_FreeCString(ctx, cstr);
                        }
                    }

                    throw new JsScriptException($"zents: unsupported JS value tag {tag}.");
            }
        }

        public static void RejectBigInt(JSValue jsValue)
        {
            if (JsValueUtil.GetTag(jsValue) == JsValueUtil.TagBigInt ||
                JsValueUtil.GetTag(jsValue) == JsValueUtil.TagFirst)
            {
                throw new JsScriptException("zents: BigInt marshal is not supported.");
            }
        }
    }
}
