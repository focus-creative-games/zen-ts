using System;
using System.Runtime.CompilerServices;

namespace ZTS
{
    /// <summary>
    /// Holds a JS function ref for Il2Cpp closed-delegate bindings.
    /// Field layout must match native <c>zts::TsMethod</c>.
    /// </summary>
    public sealed class TsMethod : IDisposable
    {
        private bool _disposed;
        private IntPtr _ctx;
        private int _funcRef;
        private IntPtr _methodMarshalCtx;

        internal TsMethod()
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~TsMethod()
        {
            Dispose(false);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void AddPendingRef(IntPtr ctx, int refIndex);

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            AddPendingRef(_ctx, _funcRef);
            _disposed = true;
        }
    }
}
