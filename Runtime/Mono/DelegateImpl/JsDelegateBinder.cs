using ZTS.DelegateImpl;

namespace ZTS.DelegateImpl
{
    internal static class JsDelegateBinder
    {
        public static System.Delegate GetFunction(ZTS.JsEnv env, System.Type delegateType, string jsModule, string jsExportName)
        {
            return DynamicBridgeFactory.CreateBinding(env, delegateType, jsModule, jsExportName);
        }
    }
}
