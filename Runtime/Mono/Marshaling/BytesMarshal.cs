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
using ZenTS.Jvm;

namespace ZenTS.Marshaling
{
    /// <summary>[JsMarshalAs(Bytes)] : byte[] ↔ JS string (raw octets).</summary>
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
                throw new JsScriptException("zents: undefined is not assignable to byte[] (Bytes).");
            }

            if (tag != JsValueUtil.TagString)
            {
                throw new JsScriptException("zents: [JsMarshalAs(Bytes)] requires a JS string (raw octets).");
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
