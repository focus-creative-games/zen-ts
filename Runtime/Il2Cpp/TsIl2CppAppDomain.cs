using System;
using System.Runtime.CompilerServices;

namespace ZTS
{
    /// <summary>
    /// Player Il2Cpp backend. Invoked by <see cref="TsAppDomain"/> via reflective
    /// construction of nested <see cref="Runtime"/> (Editor uses Mono instead).
    /// </summary>
    public static class TsIl2CppAppDomain
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void InitializeInternal(Func<string, object> moduleLoader);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void ResetInternal(Func<string, object> moduleLoader);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void ProcessPendingRefReleases();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern Delegate GetFunctionInternal(Type delegateType, string jsModule, string jsExportName);

        public static void Initialize(Func<string, object> moduleLoader)
        {
            InitializeInternal(moduleLoader);
        }

        public static void Reset(Func<string, object> moduleLoader)
        {
            ResetInternal(moduleLoader);
        }

        private static Delegate GetFunction(Type delegateType, string jsModule, string jsExportName)
        {
            if (delegateType == null)
            {
                throw new ArgumentNullException(nameof(delegateType));
            }

            if (!typeof(MulticastDelegate).IsAssignableFrom(delegateType))
            {
                throw new ArgumentException(
                    $"Type '{delegateType.FullName}' is not a MulticastDelegate.", nameof(delegateType));
            }

            return GetFunctionInternal(delegateType, jsModule, jsExportName);
        }

        private sealed class Runtime : ITsRuntime
        {
            public void Initialize(Func<string, object> moduleLoader)
            {
                TsIl2CppAppDomain.Initialize(moduleLoader);
            }

            public void Reset(Func<string, object> moduleLoader)
            {
                TsIl2CppAppDomain.Reset(moduleLoader);
            }

            public void ProcessPendingRefReleases()
            {
                TsIl2CppAppDomain.ProcessPendingRefReleases();
            }

            public Delegate GetFunction(Type delegateType, string jsModule, string jsExportName)
            {
                return TsIl2CppAppDomain.GetFunction(delegateType, jsModule, jsExportName);
            }
        }
    }
}
