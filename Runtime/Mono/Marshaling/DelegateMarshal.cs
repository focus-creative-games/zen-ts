using System;
using ZTS.DelegateImpl;
using ZTS.Jvm;

namespace ZTS.Marshaling
{
    internal static class DelegateMarshal
    {
        public static Delegate FromJsFunction(IntPtr ctx, JSValue jsFunc, Type delegateType)
        {
            JsEnv env = JsEnv.FromContext(ctx);
            return DynamicBridgeFactory.Create(env, delegateType, jsFunc, env.Generation);
        }
    }
}
