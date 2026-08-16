using System;
using System.Collections.Generic;
using ZTS.Jvm;

namespace ZTS.Marshaling
{
    internal static class ArrayMarshal
    {
        public static object FromJsArray(IntPtr ctx, JSValue jsArray)
        {
            var list = new List<object>();
            uint idx = 0;
            while (true)
            {
                JSValue item = QuickJsDll.JS_GetPropertyUint32(ctx, jsArray, idx);
                if (JsValueUtil.GetNormTag(item) == JsValueUtil.TagUndefined)
                {
                    JsValueUtil.Free(ctx, item);
                    break;
                }

                list.Add(PrimitiveMarshal.Pop(ctx, item));
                JsValueUtil.Free(ctx, item);
                idx++;
            }

            return list.ToArray();
        }

        public static JSValue ToJsArray(IntPtr ctx, Array array)
        {
            JSValue jsArray = QuickJsDll.JS_NewArray(ctx);
            for (int i = 0; i < array.Length; i++)
            {
                JSValue item = PrimitiveMarshal.Push(ctx, array.GetValue(i));
                QuickJsDll.JS_SetPropertyUint32(ctx, jsArray, (uint)i, item);
                // SetPropertyUint32 takes ownership of item.
            }

            return jsArray;
        }
    }
}
