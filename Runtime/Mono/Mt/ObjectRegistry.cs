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
using System.Runtime.InteropServices;
using ZenTS.Jvm;

namespace ZenTS.Mt
{
    internal static class ObjectRegistry
    {
        private sealed class Slot
        {
            public object Value;
            public Type View;
            public ulong Ticket;
            public int Wrappers;
        }
        private static readonly List<Slot> Slots = new List<Slot> { new Slot() };
        private static readonly Stack<int> Free = new Stack<int>();
        private static readonly Queue<(int id, ulong ticket)> PendingRelease = new Queue<(int, ulong)>();
        private static readonly Dictionary<object, int> Reverse = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private static readonly JsObjectCollected Collected = OnCollected;
        private static ulong _nextTicket;

        public static int Register(object obj) => Register(obj, obj?.GetType());
        public static int Register(object obj, Type viewType)
        {
            if (obj == null)
                return 0;
            lock (Slots)
            {
                if (Reverse.TryGetValue(obj, out int existing))
                    return existing;
                int id = Free.Count != 0 ? Free.Pop() : Slots.Count;
                if (id == Slots.Count)
                    Slots.Add(new Slot());
                Slot slot = Slots[id];
                slot.Value = obj;
                slot.View = viewType ?? obj.GetType();
                slot.Ticket = checked(++_nextTicket);
                slot.Wrappers = 0;
                Reverse[obj] = id;
                return id;
            }
        }

        [AOT.MonoPInvokeCallback(typeof(JsObjectCollected))]
        private static void OnCollected(int id, ulong ticket)
        {
            // V8 weak callbacks may not re-enter JS. Only queue a main-thread release.
            lock (Slots)
                PendingRelease.Enqueue((id, ticket));
        }

        public static void QueueRelease(int id)
        {
            lock (Slots)
                if (id > 0 && id < Slots.Count)
                    PendingRelease.Enqueue((id, Slots[id].Ticket));
        }

        public static void ReleaseImmediately(int id)
        {
            lock (Slots)
                if (id > 0 && id < Slots.Count)
                    ReleaseSlot(id);
        }

        private static void ReleaseSlot(int id)
        {
            Slot slot = Slots[id];
            if (slot.Value == null)
                return;
            if (Reverse.TryGetValue(slot.Value, out int reverse) && reverse == id)
                Reverse.Remove(slot.Value);
            slot.Value = null;
            slot.View = null;
            slot.Wrappers = 0;
            Free.Push(id);
        }

        public static void ProcessPending()
        {
            lock (Slots)
                while (PendingRelease.Count != 0)
                {
                    var release = PendingRelease.Dequeue();
                    if (release.id <= 0 || release.id >= Slots.Count)
                        continue;
                    Slot slot = Slots[release.id];
                    if (slot.Value == null || slot.Ticket != release.ticket)
                        continue;
                    if (--slot.Wrappers <= 0)
                        ReleaseSlot(release.id);
                }
        }

        private static bool TryGetSlot(IntPtr ctx, JsValue value, out int id)
        {
            id = 0;
            if (JsValueUtil.GetNormTag(value) != JsValueUtil.TagObject)
                return false;
            JsValue index = EngineApi.GetPropertyStr(ctx, value, "__zents_id");
            JsValue lo = EngineApi.GetPropertyStr(ctx, value, "__zents_ticket_lo");
            JsValue hi = EngineApi.GetPropertyStr(ctx, value, "__zents_ticket_hi");
            try
            {
                if (JsValueUtil.GetNormTag(index) != JsValueUtil.TagInt ||
                    JsValueUtil.GetNormTag(lo) != JsValueUtil.TagInt || JsValueUtil.GetNormTag(hi) != JsValueUtil.TagInt)
                    return false;
                id = unchecked((int)JsValueUtil.GetUInt64(index));
                ulong ticket = (uint)JsValueUtil.GetUInt64(lo) | ((ulong)(uint)JsValueUtil.GetUInt64(hi) << 32);
                return id > 0 && id < Slots.Count && Slots[id].Value != null && Slots[id].Ticket == ticket;
            }
            finally
            {
                JsValueUtil.Free(ctx, index);
                JsValueUtil.Free(ctx, lo);
                JsValueUtil.Free(ctx, hi);
            }
        }

        public static bool TryGetObject(IntPtr ctx, JsValue value, out object obj)
        {
            lock (Slots)
            {
                bool found = TryGetSlot(ctx, value, out int id);
                obj = found ? Slots[id].Value : null;
                return found;
            }
        }

        public static bool TryGetViewType(IntPtr ctx, JsValue value, out Type viewType)
        {
            lock (Slots)
            {
                bool found = TryGetSlot(ctx, value, out int id);
                viewType = found ? Slots[id].View : null;
                return found;
            }
        }

        public static bool TryReplaceObject(IntPtr ctx, JsValue value, object newValue)
        {
            if (newValue == null)
                return false;
            lock (Slots)
            {
                if (!TryGetSlot(ctx, value, out int id))
                    return false;
                Slot slot = Slots[id];
                if (Reverse.TryGetValue(slot.Value, out int reverse) && reverse == id)
                    Reverse.Remove(slot.Value);
                slot.Value = newValue;
                Reverse[newValue] = id;
                return true;
            }
        }

        public static JsValue CreateJsHandle(IntPtr ctx, int id) => CreateJsHandle(ctx, id, default, null);
        public static JsValue CreateJsHandle(IntPtr ctx, int id, JsValue proto) => CreateJsHandle(ctx, id, proto, null);
        public static JsValue CreateJsHandle(IntPtr ctx, int id, JsValue proto, string udKind)
        {
            JsValue obj = JsValueUtil.GetNormTag(proto) == JsValueUtil.TagObject ? EngineApi.NewObjectProto(ctx, proto) : EngineApi.NewObject(ctx);
            try
            {
                AttachJsHandle(ctx, obj, id, udKind);
                return obj;
            }
            catch { JsValueUtil.Free(ctx, obj); throw; }
        }

        internal static void AttachJsHandle(IntPtr ctx, JsValue obj, int id, string udKind = null)
        {
            Slot slot = Slots[id];
            EngineApi.SetPropertyStr(ctx, obj, "__zents_id", JsValueUtil.NewInt32(id));
            EngineApi.SetPropertyStr(ctx, obj, "__zents_ticket_lo", JsValueUtil.NewInt32(unchecked((int)slot.Ticket)));
            EngineApi.SetPropertyStr(ctx, obj, "__zents_ticket_hi", JsValueUtil.NewInt32(unchecked((int)(slot.Ticket >> 32))));
            if (!string.IsNullOrEmpty(udKind))
                EngineApi.SetPropertyStr(ctx, obj, "__zents_ud_kind", EngineApi.NewString(ctx, udKind));
            EngineApi.TrackObject(ctx, obj, id, slot.Ticket, Collected);
            slot.Wrappers++;
        }

        public static JsValue PushObject(IntPtr ctx, object obj, Type viewType, JsValue proto) => PushObject(ctx, obj, viewType, proto, "byobj");
        public static JsValue PushObject(IntPtr ctx, object obj, Type viewType, JsValue proto, string udKind)
        {
            if (obj == null)
                return JsValueUtil.Null;
            return CreateJsHandle(ctx, Register(obj, viewType), proto, udKind);
        }

        public static void Reset()
        {
            lock (Slots)
            {
                Slots.Clear();
                Slots.Add(new Slot());
                Free.Clear();
                PendingRelease.Clear();
                Reverse.Clear();
                // Tickets never restart: late callbacks cannot release a slot in a newer runtime.
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
