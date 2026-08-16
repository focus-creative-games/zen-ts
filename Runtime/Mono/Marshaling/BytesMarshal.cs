using System;
using System.Runtime.InteropServices;
using ZTS.Jvm;

namespace ZTS.Marshaling
{
    /// <summary>[TsMarshalAs(Bytes)] : byte[] ↔ JS string (raw octets).</summary>
    internal static class BytesMarshal
    {
        public static JSValue Push(IntPtr ctx, byte[] bytes)
        {
            if (bytes == null)
            {
                return JsValueUtil.Null;
            }

            return QuickJsDll.JS_NewStringLen(ctx, bytes, (UIntPtr)bytes.Length);
        }

        public static byte[] Pop(IntPtr ctx, JSValue jsValue)
        {
            int tag = JsValueUtil.GetNormTag(jsValue);
            if (tag == JsValueUtil.TagNull)
            {
                return null;
            }

            if (tag == JsValueUtil.TagUndefined)
            {
                throw new TsScriptException("zts: undefined is not assignable to byte[] (Bytes).");
            }

            if (tag != JsValueUtil.TagString)
            {
                throw new TsScriptException("zts: [TsMarshalAs(Bytes)] requires a JS string (raw octets).");
            }

            IntPtr cstr = QuickJsDll.JS_ToCStringLen2(ctx, out UIntPtr len, jsValue, 0);
            if (cstr == IntPtr.Zero)
            {
                return Array.Empty<byte>();
            }

            try
            {
                int n = (int)len;
                if (n <= 0)
                {
                    return Array.Empty<byte>();
                }

                var bytes = new byte[n];
                Marshal.Copy(cstr, bytes, 0, n);
                return bytes;
            }
            finally
            {
                QuickJsDll.JS_FreeCString(ctx, cstr);
            }
        }
    }
}
