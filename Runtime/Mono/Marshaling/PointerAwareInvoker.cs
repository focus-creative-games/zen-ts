using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace ZTS.Marshaling
{
    /// <summary>
    /// Invokes methods with pointer parameters/returns. Unity Mono's MethodInfo.Invoke
    /// rejects <see cref="Pointer"/> boxes for <c>T*</c> signatures.
    /// </summary>
    internal static class PointerAwareInvoker
    {
        private static readonly Dictionary<MethodInfo, Func<object, object[], object>> Cache =
            new Dictionary<MethodInfo, Func<object, object[], object>>();

        public static object Invoke(MethodInfo method, object target, object[] args)
        {
            Func<object, object[], object> fn;
            lock (Cache)
            {
                if (!Cache.TryGetValue(method, out fn))
                {
                    fn = Build(method);
                    Cache[method] = fn;
                }
            }

            return fn(target, args ?? Array.Empty<object>());
        }

        private static Func<object, object[], object> Build(MethodInfo method)
        {
            var dm = new DynamicMethod(
                "zts_ptr_" + method.Name,
                typeof(object),
                new[] { typeof(object), typeof(object[]) },
                typeof(PointerAwareInvoker).Module,
                skipVisibility: true);

            ILGenerator il = dm.GetILGenerator();
            ParameterInfo[] ps = method.GetParameters();

            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, method.DeclaringType);
            }

            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                if (pt.IsByRef)
                {
                    throw new NotSupportedException(
                        "zts: PointerAwareInvoker does not support by-ref parameters.");
                }

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);

                if (pt.IsPointer)
                {
                    Label notNull = il.DefineLabel();
                    Label done = il.DefineLabel();
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Brtrue_S, notNull);
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Conv_I);
                    il.Emit(OpCodes.Br_S, done);
                    il.MarkLabel(notNull);
                    il.Emit(OpCodes.Call, typeof(Pointer).GetMethod(nameof(Pointer.Unbox)));
                    il.MarkLabel(done);
                }
                else if (pt.IsValueType)
                {
                    il.Emit(OpCodes.Unbox_Any, pt);
                }
                else
                {
                    il.Emit(OpCodes.Castclass, pt);
                }
            }

            il.Emit(method.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method);

            if (method.ReturnType == typeof(void))
            {
                il.Emit(OpCodes.Ldnull);
            }
            else if (method.ReturnType.IsPointer)
            {
                il.Emit(OpCodes.Ldtoken, method.ReturnType);
                il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle)));
                il.Emit(OpCodes.Call, typeof(Pointer).GetMethod(nameof(Pointer.Box)));
            }
            else if (method.ReturnType.IsValueType)
            {
                il.Emit(OpCodes.Box, method.ReturnType);
            }

            il.Emit(OpCodes.Ret);
            return (Func<object, object[], object>)dm.CreateDelegate(typeof(Func<object, object[], object>));
        }
    }
}
