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
using System.Collections.Generic;
using System.Reflection;
using ZTS.Jvm;
using ZTS.Utils;

namespace ZTS.Emit
{
    /// <summary>
    /// Metadata for a JS method function (direct vs dispatch) used by
    /// <c>zts.register_method</c> / <c>zts.make_generic_method</c>.
    /// </summary>
    internal sealed class MethodClosureTag
    {
        public MethodInfo Method;
        public Type OwnerType;
        public bool IsStatic;
        public bool IsByVal;
        public bool IsDirect;
    }

    internal static class MethodTagRegistry
    {
        private static readonly List<MethodClosureTag> Slots = new List<MethodClosureTag> { null };
        private static int _nextId = 1;

        public static int Register(MethodClosureTag tag)
        {
            if (tag == null)
            {
                throw new ArgumentNullException(nameof(tag));
            }

            lock (Slots)
            {
                int id = _nextId++;
                while (id >= Slots.Count)
                {
                    Slots.Add(null);
                }

                Slots[id] = tag;
                return id;
            }
        }

        public static bool TryGet(int id, out MethodClosureTag tag)
        {
            lock (Slots)
            {
                if (id <= 0 || id >= Slots.Count)
                {
                    tag = null;
                    return false;
                }

                tag = Slots[id];
                return tag != null;
            }
        }

        public static void Reset()
        {
            lock (Slots)
            {
                Slots.Clear();
                Slots.Add(null);
                _nextId = 1;
            }
        }
    }
}
