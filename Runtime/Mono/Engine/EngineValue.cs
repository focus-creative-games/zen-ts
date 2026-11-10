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
using System.Runtime.InteropServices;
namespace ZenTS.Jvm
{
    /// <summary>ZenTS Mono/plugin value envelope. Payload interpretation and lifetime belong to the active adapter.</summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct JsValue
    {
        [FieldOffset(0)] public ulong UInt64;
        [FieldOffset(0)] public double Float64;
        [FieldOffset(0)] public IntPtr Ptr;
        [FieldOffset(8)] public long Tag;
    }

    /// <summary>
    /// Managed→native callback ABI used by the V8 Mono plugin (all JsValues via pointers).
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void JsCFunctionOut(IntPtr ctx, ref JsValue thisVal, int argc, IntPtr argv, out JsValue result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void JsCFunctionMagicOut(IntPtr ctx, ref JsValue thisVal, int argc, IntPtr argv, int magic, out JsValue result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr JsModuleLoaderFunc(IntPtr ctx, IntPtr moduleName, IntPtr opaque);

    /// <summary>High-level managed callback signature (converted at the gate boundary).</summary>
    internal delegate JsValue JsCFunction(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv);

    internal delegate JsValue JsCFunctionMagic(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv, int magic);

}
