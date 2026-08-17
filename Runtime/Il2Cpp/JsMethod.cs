// Copyright 2026 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Runtime.CompilerServices;

namespace ZenTS
{
    /// <summary>
    /// Holds a JS function ref for Il2Cpp closed-delegate bindings.
    /// Field layout must match native <c>zents::JsMethod</c>.
    /// </summary>
    public sealed class JsMethod : IDisposable
    {
        private bool _disposed;
        private IntPtr _ctx;
        private int _funcRef;
        private IntPtr _methodMarshalCtx;

        internal JsMethod()
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~JsMethod()
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
