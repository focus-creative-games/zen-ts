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
