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
using System.Collections.Generic;
using ZenTS.Jvm;

namespace ZenTS.Marshaling
{
    internal static class ArrayMarshal
    {
        public static object FromJsArray(IntPtr ctx, JsValue jsArray)
        {
            JsValue lengthValue = EngineApi.GetPropertyStr(ctx, jsArray, "length");
            int length;
            try
            {
                length = Convert.ToInt32(PrimitiveMarshal.Pop(ctx, lengthValue));
            }
            finally { JsValueUtil.Free(ctx, lengthValue); }
            var values = new object[length];
            for (int i = 0; i < length; ++i)
            {
                if (EngineApi.HasOwnProperty(ctx, jsArray, i.ToString(System.Globalization.CultureInfo.InvariantCulture)) != 1)
                    throw new JsScriptException("zents: array holes are not supported");
                JsValue item = EngineApi.GetPropertyUint32(ctx, jsArray, (uint)i);
                try
                {
                    values[i] = PrimitiveMarshal.Pop(ctx, item);
                }
                finally { JsValueUtil.Free(ctx, item); }
            }
            return values;
        }

        public static JsValue ToJsArray(IntPtr ctx, Array array)
        {
            JsValue jsArray = EngineApi.NewArray(ctx);
            for (int i = 0; i < array.Length; i++)
            {
                JsValue item = PrimitiveMarshal.Push(ctx, array.GetValue(i));
                EngineApi.SetPropertyUint32(ctx, jsArray, (uint)i, item);
                // SetPropertyUint32 takes ownership of item.
            }

            return jsArray;
        }
    }
}
