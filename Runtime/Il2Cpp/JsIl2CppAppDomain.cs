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
    /// Player Il2Cpp backend. Invoked by <see cref="JsAppDomain"/> via reflective
    /// construction of nested <see cref="Runtime"/> (Editor uses Mono instead).
    /// </summary>
    public static class JsIl2CppAppDomain
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

        private sealed class Runtime : IJsRuntime
        {
            public void Initialize(Func<string, object> moduleLoader)
            {
                JsIl2CppAppDomain.Initialize(moduleLoader);
            }

            public void Reset(Func<string, object> moduleLoader)
            {
                JsIl2CppAppDomain.Reset(moduleLoader);
            }

            public void ProcessPendingRefReleases()
            {
                JsIl2CppAppDomain.ProcessPendingRefReleases();
            }

            public Delegate GetFunction(Type delegateType, string jsModule, string jsExportName)
            {
                return JsIl2CppAppDomain.GetFunction(delegateType, jsModule, jsExportName);
            }
        }
    }
}
