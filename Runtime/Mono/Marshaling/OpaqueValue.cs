using System;
using ZTS.Jvm;

namespace ZTS.Marshaling
{
    internal sealed class OpaqueValue
    {
        public OpaqueValue(object target) => Target = target;

        public object Target { get; set; }
        internal JSValue JsValue { get; set; }
        internal int Generation { get; set; }
    }
}
