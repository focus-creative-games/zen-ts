using System;

namespace ZTS
{
    /// <summary>
    /// Validates <see cref="TsMarshalAsAttribute"/> combinations (Editor Mono).
    /// Illegal combos log and fall back to Default.
    /// </summary>
    public static class TsMarshalAsValidation
    {
        public static TsMarshalType Normalize(TsMarshalType kind, Type clrType, string memberName)
        {
            if (kind == TsMarshalType.Default)
            {
                return kind;
            }

            if (kind == TsMarshalType.Bytes && clrType != typeof(byte[]))
            {
                UnityEngine.Debug.LogWarning(
                    $"zts: [TsMarshalAs(Bytes)] on {memberName} requires byte[]; falling back to Default.");
                return TsMarshalType.Default;
            }

            return kind;
        }
    }
}
