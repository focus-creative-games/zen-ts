using System;

namespace ZTS
{
    /// <summary>
    /// Marks an extended type with one or more extension classes.
    /// Place on the <em>extended</em> type, not on the extension class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface,
        AllowMultiple = true, Inherited = false)]
    public sealed class TsExtensionAttribute : Attribute
    {
        public Type[] ExtensionTypes { get; }

        public TsExtensionAttribute(params Type[] extensionTypes)
        {
            ExtensionTypes = extensionTypes ?? Array.Empty<Type>();
        }
    }
}
