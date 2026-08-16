using System;

namespace ZTS
{
    public enum TsMarshalType
    {
        Default,
        Object,
        Bytes,
        OpaqueValue,
        UnpackedValues,
        Table,
    }

    [AttributeUsage(
        AttributeTargets.Parameter
        | AttributeTargets.ReturnValue
        | AttributeTargets.Field
        | AttributeTargets.Property
        | AttributeTargets.Class
        | AttributeTargets.Struct)]
    public sealed class TsMarshalAsAttribute : Attribute
    {
        public TsMarshalType TsMarshalType { get; }

        /// <summary>
        /// Required for <see cref="TsMarshalType.Table"/> / <see cref="TsMarshalType.UnpackedValues"/>.
        /// Elements are CLR field or property names on the underlying struct (Nullable unwraps to T).
        /// Trailing '?' marks optional Table keys (JS→C#). UnpackedValues does not support '?'.
        /// </summary>
        public string[] Members { get; set; }

        public TsMarshalAsAttribute(TsMarshalType tsMarshalType = TsMarshalType.Default)
        {
            TsMarshalType = tsMarshalType;
        }
    }
}
