using System;

namespace ZTS
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class MonoJsCallbackAttribute : Attribute
    {
        public Type DelegateType { get; }

        public MonoJsCallbackAttribute(Type delegateType)
        {
            DelegateType = delegateType;
        }
    }
}
