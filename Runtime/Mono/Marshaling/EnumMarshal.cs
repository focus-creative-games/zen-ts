using System;
using ZTS.Jvm;

namespace ZTS.Marshaling
{
    internal static class EnumMarshal
    {
        public static JSValue Push(IntPtr ctx, object enumValue)
        {
            return JsValueUtil.NewInt32(Convert.ToInt32(enumValue));
        }
    }
}
