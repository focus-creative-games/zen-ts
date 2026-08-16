using System;
using ZTS.Jvm;
using ZTS.Mt;

namespace ZTS.Marshaling
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

                    throw new TsScriptException($"zts: unsupported JS value tag {tag}.");
            }
        }

        public static void RejectBigInt(JSValue jsValue)
        {
            if (JsValueUtil.GetTag(jsValue) == JsValueUtil.TagBigInt ||
                JsValueUtil.GetTag(jsValue) == JsValueUtil.TagFirst)
            {
                throw new TsScriptException("zts: BigInt marshal is not supported.");
            }
        }
    }
}
