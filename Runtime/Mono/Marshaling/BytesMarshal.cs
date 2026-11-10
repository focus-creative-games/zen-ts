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
    // Always copies. No UTF-8 roundtrip, no shared CLR/JS backing store.
    internal static class BytesMarshal
    {
        public static JsValue Push(IntPtr ctx, byte[] bytes) => bytes == null ? JsValueUtil.Null : EngineApi.NewByteArrayCopy(ctx, bytes);
        public static byte[] Pop(IntPtr ctx, JsValue value)
        {
            if (JsValueUtil.GetNormTag(value) == JsValueUtil.TagNull)
                return null;
            IntPtr data = EngineApi.GetByteArrayData(ctx, value, out UIntPtr length);
            if (data == IntPtr.Zero)
                throw new JsScriptException("zents: Bytes requires an attached Uint8Array");
            int count = checked((int)length.ToUInt64());
            var bytes = new byte[count];
            if (count > 0)
                Marshal.Copy(data, bytes, 0, count);
            return bytes;
        }
    }
}
