using System;
using System.Collections.Generic;
using ZTS.Jvm;
using ZTS.Marshaling;
using ZTS.Mt;
using ZTS.Utils;

namespace ZTS.Emit
{
    /// <summary>
    /// Szarray ByObj IEO: <c>get</c>/<c>set</c>/<c>length</c> per Docs/spec/02-TYPE-SYSTEM §7.
    /// </summary>
    internal static class ArrayBinding
    {
        private const int PropConfigurable = 1 << 0;
        private const int PropEnumerable = 1 << 2;

        private static readonly Dictionary<Type, JSValue> Protos = new Dictionary<Type, JSValue>();

        public static JSValue EnsureProto(JsEnv env, Type arrayType)
        {
            if (arrayType == null || !arrayType.IsArray)
            {
                throw new ArgumentException("arrayType must be an array type.", nameof(arrayType));
            }

            lock (Protos)
            {
                if (Protos.TryGetValue(arrayType, out JSValue existing))
                {
                    return existing;
                }
            }

            IntPtr ctx = env.Context;
            JSValue proto = QuickJsDll.JS_NewObject(ctx);
            Type elementType = arrayType.GetElementType() ?? typeof(object);
            int rank = arrayType.GetArrayRank();

            QuickJsDll.JS_SetPropertyStr(ctx, proto, "get", JsCallbackGate.NewCFunction(
                ctx, (c, thisVal, argc, argv) => ArrayGet(c, thisVal, argc, argv, elementType, rank), "get", rank));
            QuickJsDll.JS_SetPropertyStr(ctx, proto, "set", JsCallbackGate.NewCFunction(
                ctx, (c, thisVal, argc, argv) => ArraySet(c, thisVal, argc, argv, elementType, rank), "set", rank + 1));

            JSValue lengthGet = JsCallbackGate.NewCFunction(ctx, ArrayLengthGet, "get_length", 0);
            JSValue lengthSet = JsCallbackGate.NewCFunction(ctx, ArrayLengthSet, "set_length", 1);
            uint atom = QuickJsDll.JS_NewAtom(ctx, "length");
            QuickJsDll.JS_DefinePropertyGetSet(
                ctx, proto, atom, lengthGet, lengthSet, PropConfigurable | PropEnumerable);
            QuickJsDll.JS_FreeAtom(ctx, atom);
            // DefinePropertyGetSet takes ownership of getter/setter.

            lock (Protos)
            {
                Protos[arrayType] = proto;
            }

            return proto;
        }

        public static void Release(JsEnv env)
        {
            if (env == null || !env.IsAlive)
            {
                lock (Protos)
                {
                    Protos.Clear();
                }

                return;
            }

            IntPtr ctx = env.Context;
            lock (Protos)
            {
                foreach (JSValue proto in Protos.Values)
                {
                    JsValueUtil.Free(ctx, proto);
                }

                Protos.Clear();
            }
        }

        public static void Reset()
        {
            lock (Protos)
            {
                Protos.Clear();
            }
        }

        private static JSValue ArrayLengthGet(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (!ObjectRegistry.TryGetObject(ctx, thisVal, out object obj) || !(obj is Array arr))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: length: not an array.");
                }

                return JsValueUtil.NewInt32(arr.Length);
            }
            catch (Exception ex)
            {
                JsCallbackBoundary.ThrowManaged(ctx, ex);
                return JsValueUtil.MakeErrorSentinel();
            }
        }

        private static JSValue ArrayLengthSet(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: array length is read-only.");
        }

        private static JSValue ArrayGet(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv, Type elementType, int rank)
        {
            try
            {
                if (!ObjectRegistry.TryGetObject(ctx, thisVal, out object obj) || !(obj is Array arr))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: get: not an array.");
                }

                if (argc < rank)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, $"zts: get expects {rank} index argument(s).");
                }

                int[] indices = ReadIndices(ctx, argv, rank);
                object value = arr.GetValue(indices);
                return TypedMarshal.Push(ctx, value, elementType);
            }
            catch (Exception ex)
            {
                JsCallbackBoundary.ThrowManaged(ctx, ex);
                return JsValueUtil.MakeErrorSentinel();
            }
        }

        private static JSValue ArraySet(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv, Type elementType, int rank)
        {
            try
            {
                if (!ObjectRegistry.TryGetObject(ctx, thisVal, out object obj) || !(obj is Array arr))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: set: not an array.");
                }

                if (argc < rank + 1)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, $"zts: set expects {rank} index(es) and a value.");
                }

                int[] indices = ReadIndices(ctx, argv, rank);
                object value = TypedMarshal.Pop(ctx, ArgReader.Read(argv, rank), elementType);
                arr.SetValue(value, indices);
                return JsValueUtil.Undefined;
            }
            catch (Exception ex)
            {
                JsCallbackBoundary.ThrowManaged(ctx, ex);
                return JsValueUtil.MakeErrorSentinel();
            }
        }

        private static int[] ReadIndices(IntPtr ctx, IntPtr argv, int rank)
        {
            var indices = new int[rank];
            for (int i = 0; i < rank; i++)
            {
                object raw = PrimitiveMarshal.Pop(ctx, ArgReader.Read(argv, i));
                indices[i] = Convert.ToInt32(raw);
            }

            return indices;
        }
    }
}
