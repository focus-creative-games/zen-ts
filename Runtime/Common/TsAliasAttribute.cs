using System;

namespace ZTS
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class TsAliasAttribute : Attribute
    {
        public string Alias { get; }

        public TsAliasAttribute(string alias)
        {
            Alias = alias;
        }
    }
}
