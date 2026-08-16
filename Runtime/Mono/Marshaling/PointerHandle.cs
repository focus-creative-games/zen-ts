using System;

namespace ZTS.Marshaling
{
    /// <summary>Opaque token for non-managed pointer passthrough (spec marshal/10-POINTER).</summary>
    internal sealed class PointerHandle
    {
        public IntPtr Address { get; }
        public Type PointerType { get; }

        public PointerHandle(IntPtr address, Type pointerType)
        {
            Address = address;
            PointerType = pointerType ?? throw new ArgumentNullException(nameof(pointerType));
        }
    }
}
