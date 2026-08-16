using System;
using System.Collections.Generic;
using ZTS.Jvm;
using ZTS.Utils;

namespace ZTS.Mt
{
    /// <summary>
    /// Strong-slot registry for CLR objects exposed to JS (ByObj handles).
    /// </summary>
    internal static class ObjectRegistry
    {
        private static readonly List<object> Slots = new List<object> { null };
        private static readonly List<Type> ViewTypes = new List<Type> { null };
        private static readonly Queue<int> PendingRelease = new Queue<int>();
        private static readonly Dictionary<object, int> Reverse = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<(int id, Type view), JSValue> ViewCache = new Dictionary<(int, Type), JSValue>();
        private static int _nextId = 1;

        public static int Register(object obj) => Register(obj, obj?.GetType());

        public static int Register(object obj, Type viewType)
        {
            if (obj == null)
            {
                return 0;
            }

            lock (Slots)
            {
                if (Reverse.TryGetValue(obj, out int existing))
                {
                    return existing;
                }

                int id = _nextId++;
                while (id >= Slots.Count)
                {
                    Slots.Add(null);
                    ViewTypes.Add(null);
                }

                Slots[id] = obj;
                ViewTypes[id] = viewType ?? obj.GetType();
                Reverse[obj] = id;
                return id;
            }
        }

        public static void QueueRelease(int id)
        {
            if (id <= 0)
            {
                return;
            }

            lock (Slots)
            {
                PendingRelease.Enqueue(id);
            }
        }

        public static void ProcessPending()
        {
            lock (Slots)
            {
                while (PendingRelease.Count > 0)
                {
                    int id = PendingRelease.Dequeue();
                    if (id <= 0 || id >= Slots.Count)
                    {
                        continue;
                    }

                    object obj = Slots[id];
                    Slots[id] = null;
                    ViewTypes[id] = null;
                    if (obj != null)
                    {
                        Reverse.Remove(obj);
                    }

                    // Drop any cached JS handles for this id (values freed on Reset).
                    var doomed = new List<(int, Type)>();
                    foreach (var key in ViewCache.Keys)
                    {
                        if (key.id == id)
                        {
                            doomed.Add(key);
                        }
                    }

                    foreach (var key in doomed)
                    {
                        ViewCache.Remove(key);
                    }
                }
            }
        }

        public static bool TryGetObject(IntPtr ctx, JSValue jsValue, out object obj)
        {
            obj = null;
            if (JsValueUtil.GetNormTag(jsValue) != JsValueUtil.TagObject)
            {
                return false;
            }

            JSValue idVal = QuickJsDll.JS_GetPropertyStr(ctx, jsValue, "__zts_id");
            if (JsValueUtil.GetNormTag(idVal) != JsValueUtil.TagInt)
            {
                JsValueUtil.Free(ctx, idVal);
                return false;
            }

            int id = unchecked((int)idVal.UInt64);
            JsValueUtil.Free(ctx, idVal);
            lock (Slots)
            {
                if (id <= 0 || id >= Slots.Count)
                {
                    return false;
                }

                obj = Slots[id];
                return obj != null;
            }
        }

        public static bool TryGetViewType(IntPtr ctx, JSValue jsValue, out Type viewType)
        {
            viewType = null;
            if (!TryGetObject(ctx, jsValue, out _) )
            {
                return false;
            }

            JSValue idVal = QuickJsDll.JS_GetPropertyStr(ctx, jsValue, "__zts_id");
            int id = unchecked((int)idVal.UInt64);
            JsValueUtil.Free(ctx, idVal);
            lock (Slots)
            {
                if (id <= 0 || id >= ViewTypes.Count)
                {
                    return false;
                }

                viewType = ViewTypes[id];
                return viewType != null;
            }
        }

        /// <summary>Replace the CLR object held by an existing ByObj handle (byref writeback).</summary>
        public static bool TryReplaceObject(IntPtr ctx, JSValue jsValue, object newValue)
        {
            if (newValue == null || JsValueUtil.GetNormTag(jsValue) != JsValueUtil.TagObject)
            {
                return false;
            }

            JSValue idVal = QuickJsDll.JS_GetPropertyStr(ctx, jsValue, "__zts_id");
            if (JsValueUtil.GetNormTag(idVal) != JsValueUtil.TagInt)
            {
                JsValueUtil.Free(ctx, idVal);
                return false;
            }

            int id = unchecked((int)idVal.UInt64);
            JsValueUtil.Free(ctx, idVal);
            lock (Slots)
            {
                if (id <= 0 || id >= Slots.Count)
                {
                    return false;
                }

                object old = Slots[id];
                if (old == null)
                {
                    return false;
                }

                if (!ReferenceEquals(old, newValue))
                {
                    Reverse.Remove(old);
                    Slots[id] = newValue;
                    Reverse[newValue] = id;
                }

                return true;
            }
        }

        public static JSValue CreateJsHandle(IntPtr ctx, int id) => CreateJsHandle(ctx, id, default, null);

        public static JSValue CreateJsHandle(IntPtr ctx, int id, JSValue proto) =>
            CreateJsHandle(ctx, id, proto, null);

        public static JSValue CreateJsHandle(IntPtr ctx, int id, JSValue proto, string udKind)
        {
            JSValue obj = JsValueUtil.GetNormTag(proto) == JsValueUtil.TagObject
                ? QuickJsDll.JS_NewObjectProto(ctx, proto)
                : QuickJsDll.JS_NewObject(ctx);
            QuickJsDll.JS_SetPropertyStr(ctx, obj, "__zts_id", JsValueUtil.NewInt32(id));
            if (!string.IsNullOrEmpty(udKind))
            {
                QuickJsDll.JS_SetPropertyStr(ctx, obj, "__zts_ud_kind", QuickJsDll.NewString(ctx, udKind));
            }

            return obj;
        }

        public static JSValue PushObject(IntPtr ctx, object obj, Type viewType, JSValue instanceProto) =>
            PushObject(ctx, obj, viewType, instanceProto, udKind: "byobj");

        public static JSValue PushObject(IntPtr ctx, object obj, Type viewType, JSValue instanceProto, string udKind)
        {
            if (obj == null)
            {
                return JsValueUtil.Null;
            }

            Type view = viewType ?? obj.GetType();
            int id = Register(obj, view);
            return CreateJsHandle(ctx, id, instanceProto, udKind);
        }

        public static void Reset()
        {
            lock (Slots)
            {
                Slots.Clear();
                Slots.Add(null);
                ViewTypes.Clear();
                ViewTypes.Add(null);
                PendingRelease.Clear();
                Reverse.Clear();
                ViewCache.Clear();
                _nextId = 1;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) =>
                obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
