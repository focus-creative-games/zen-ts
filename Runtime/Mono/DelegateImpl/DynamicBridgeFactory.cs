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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Buffers;
using System.Reflection;
using ZenTS.Jvm;
using ZenTS.Emit;
using ZenTS.Marshaling;
using ZenTS.Utils;

namespace ZenTS.DelegateImpl
{
    internal static class DynamicBridgeFactory
    {
        private static readonly List<WeakReference<JsFuncHolder>> HeldFunctions = new List<WeakReference<JsFuncHolder>>();
        private static readonly ConcurrentDictionary<Type, Func<JsEnv, JsValue, int, Delegate>> Factories =
            new ConcurrentDictionary<Type, Func<JsEnv, JsValue, int, Delegate>>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Delegate, JsFuncHolder> BoundJsFuncs =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Delegate, JsFuncHolder>();

        private static readonly ConcurrentQueue<JsFuncHolder> PendingReleases = new ConcurrentQueue<JsFuncHolder>();

        private sealed class JsFuncHolder
        {
            public JsValue Func;
            public JsEnv Owner;
            public int Generation;
            public bool Valid = true;
            ~JsFuncHolder()
            {
                PendingReleases.Enqueue(this);
            }
            public void Release()
            {
                if (!Valid)
                    return;
                Valid = false;
                if (Owner.IsAlive && Owner.Generation == Generation)
                    JsValueUtil.Free(Owner.Context, Func);
            }
        }

        public static Delegate CreateBinding(JsEnv env, Type delegateType, string jsModule, string jsExportName)
        {
            JsValue jsFunc = env.GetModuleExport(jsModule, jsExportName);
            try
            {
                return Create(env, delegateType, jsFunc, env.Generation);
            }
            finally
            {
                JsValueUtil.Free(env.Context, jsFunc);
            }
        }

        private static readonly ConcurrentDictionary<(IntPtr identity, Type delType), WeakReference<Delegate>> FromJsIdentityCache =
            new ConcurrentDictionary<(IntPtr, Type), WeakReference<Delegate>>();

        public static Delegate Create(JsEnv env, Type delegateType, JsValue jsFunc, int generation)
        {
            if (!typeof(MulticastDelegate).IsAssignableFrom(delegateType))
            {
                throw new JsScriptException($"zents: {delegateType.FullName} is not a delegate type.");
            }

            MethodInfo invoke = delegateType.GetMethod("Invoke");
            if (invoke == null)
            {
                throw new JsScriptException($"zents: delegate {delegateType.FullName} has no Invoke.");
            }

            if (invoke.GetParameters().Any(p => p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0))
            {
                throw new JsScriptException("zents: GetFunction does not support params arrays.");
            }

            // Reuse identity so event remove_ with the same JS function matches add_.
            var cacheKey = (JsValueUtil.GetIdentity(jsFunc), delegateType);
            if (FromJsIdentityCache.TryGetValue(cacheKey, out WeakReference<Delegate> weakCached) && weakCached.TryGetTarget(out Delegate cached))
            {
                return cached;
            }

            JsValue funcDup = JsValueUtil.Dup(jsFunc);
            var holder = new JsFuncHolder { Func = funcDup, Owner = env, Generation = generation };
            lock (HeldFunctions)
            {
                HeldFunctions.Add(new WeakReference<JsFuncHolder>(holder));
            }

            Func<JsEnv, JsValue, int, Delegate> factory = Factories.GetOrAdd(delegateType, BuildFactory);
            Delegate created;
            try
            {
                created = factory(env, funcDup, generation);
            }
            catch { holder.Release(); throw; }
            BoundJsFuncs.Add(created, holder);
            FromJsIdentityCache[cacheKey] = new WeakReference<Delegate>(created);
            // Also index the Dup'd value (may differ if bits differ after Dup — usually same).
            FromJsIdentityCache[(JsValueUtil.GetIdentity(funcDup), delegateType)] = new WeakReference<Delegate>(created);
            return created;
        }

        private static Func<JsEnv, JsValue, int, Delegate> BuildFactory(Type delegateType)
        {
            MethodInfo invoke = delegateType.GetMethod("Invoke");
            ParameterInfo[] parameters = invoke.GetParameters();
            Type returnType = invoke.ReturnType;
            Type[] paramTypes = parameters.Select(p => p.ParameterType).ToArray();

            JsCallPlan plan = JsCallPlan.For(invoke);
            var closure = Expression.Parameter(typeof(BridgeClosure), "closure");
            ParameterExpression[] pexprs = parameters.Select(p => Expression.Parameter(p.ParameterType, p.Name ?? "arg")).ToArray();
            bool primitive = parameters.Length <= 4 && parameters.All(p => IsPrimitive(p.ParameterType))
                && (returnType == typeof(void) || IsPrimitive(returnType))
                && plan.Marshal.All(m => m == null) && plan.ReturnMarshal == null;
            Expression body;
            if (primitive)
            {
                var values = parameters.Select((p, i) => PushPrimitive(pexprs[i], p.ParameterType)).ToArray();
                MethodInfo call = typeof(BridgeClosure).GetMethod("Call" + values.Length);
                Expression result = Expression.Call(closure, call, values);
                body = returnType == typeof(void)
                    ? (Expression)Expression.Call(closure, typeof(BridgeClosure).GetMethod(nameof(BridgeClosure.Discard)), result)
                    : Expression.Call(closure, typeof(BridgeClosure).GetMethod(nameof(BridgeClosure.Read)).MakeGenericMethod(returnType), result);
            }
            else
            {
                var args = Expression.Variable(typeof(object[]), "args");
                var expressions = new List<Expression>
                {
                    Expression.Assign(args, parameters.Length == 0
                        ? (Expression)Expression.Call(typeof(Array), nameof(Array.Empty), new[] { typeof(object) })
                        : Expression.NewArrayInit(typeof(object), pexprs.Select(p => Expression.Convert(p, typeof(object)))))
                };
                MethodInfo run = typeof(BridgeClosure).GetMethod(nameof(BridgeClosure.Run));
                if (returnType == typeof(void))
                {
                    expressions.Add(Expression.Call(closure, run, args));
                    AppendByRefWritebacks(expressions, parameters, pexprs, args);
                    expressions.Add(Expression.Empty());
                    body = Expression.Block(new[] { args }, expressions);
                }
                else
                {
                    var ret = Expression.Variable(typeof(object), "ret");
                    expressions.Add(Expression.Assign(ret, Expression.Call(closure, run, args)));
                    AppendByRefWritebacks(expressions, parameters, pexprs, args);
                    expressions.Add(Expression.Convert(ret, returnType));
                    body = Expression.Block(new[] { args, ret }, expressions);
                }
            }
            var inner = Expression.Lambda(delegateType, body, pexprs);
            var bind = Expression.Lambda<Func<BridgeClosure, Delegate>>(Expression.Convert(inner, typeof(Delegate)), closure).Compile();
            return (env, func, gen) => bind(new BridgeClosure(env, func, gen, paramTypes, returnType, plan));
        }

        private static bool IsPrimitive(Type type)
        {
            if (type.IsByRef || Nullable.GetUnderlyingType(type) != null)
                return false;
            Type t = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            return t == typeof(bool) || t == typeof(char) || t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(short) || t == typeof(ushort) || t == typeof(int) || t == typeof(uint)
                || t == typeof(long) || t == typeof(ulong) || t == typeof(float) || t == typeof(double);
        }

        private static Expression PushPrimitive(Expression value, Type type)
        {
            Type t = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            if (t == typeof(bool))
                return Expression.Call(typeof(JsValueUtil), nameof(JsValueUtil.NewBool), null, value);
            if (t == typeof(sbyte) || t == typeof(byte) || t == typeof(short) || t == typeof(ushort) || t == typeof(char) || t == typeof(int))
                return Expression.Call(typeof(JsValueUtil), nameof(JsValueUtil.NewInt32), null, Expression.Convert(value, typeof(int)));
            return Expression.Call(typeof(JsValueUtil), nameof(JsValueUtil.NewFloat64), null, Expression.Convert(value, typeof(double)));
        }

        private static class PrimitiveReader<T>
        {
            internal static readonly Func<JsValue, T> Convert = Build();
            private static Func<JsValue, T> Build()
            {
                var value = Expression.Parameter(typeof(JsValue));
                Type t = typeof(T).IsEnum ? Enum.GetUnderlyingType(typeof(T)) : typeof(T);
                Expression read = t == typeof(bool)
                    ? (Expression)Expression.NotEqual(Expression.Call(typeof(JsValueUtil), nameof(JsValueUtil.GetUInt64), null, value), Expression.Constant(0UL))
                    : Expression.Convert(Expression.Call(typeof(DynamicBridgeFactory), nameof(ReadNumber), null, value), t);
                return Expression.Lambda<Func<JsValue, T>>(Expression.Convert(read, typeof(T)), value).Compile();
            }
        }

        public static double ReadNumber(JsValue value) => JsValueUtil.GetNormTag(value) == JsValueUtil.TagInt
            ? unchecked((int)JsValueUtil.GetUInt64(value)) : JsValueUtil.GetFloat64(value);

        private static void AppendByRefWritebacks(
            List<Expression> body,
            ParameterInfo[] parameters,
            ParameterExpression[] pexprs,
            ParameterExpression argsVar)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].ParameterType.IsByRef)
                {
                    continue;
                }

                Type elem = parameters[i].ParameterType.GetElementType();
                body.Add(Expression.Assign(
                    pexprs[i],
                    Expression.Convert(Expression.ArrayIndex(argsVar, Expression.Constant(i)), elem)));
            }
        }

        private static object InvokeCore(
            JsEnv env,
            JsValue func,
            int boundGen,
            Type[] paramTypes,
            Type returnType,
            object[] args, JsCallPlan plan, int[] offsets, int argc, bool opaque)
        {
            JsMainThread.EnsureMainThread();
            if (!env.IsAlive || boundGen != env.Generation)
            {
                throw new InvalidOperationException("zents: JS runtime was reset; re-bind GetFunction delegates.");
            }

            IntPtr ctx = env.Context;
            int clrArgc = args?.Length ?? 0;
            JsValue[] jsArgs = argc == 0 ? Array.Empty<JsValue>() : ArrayPool<JsValue>.Shared.Rent(argc);
            Array.Clear(jsArgs, 0, argc);
            JsValue ret = JsValueUtil.Undefined;
            if (opaque)
                OpaqueParameterScope.Enter();
            try
            {
                for (int i = 0; i < clrArgc; ++i)
                {
                    Type pt = paramTypes[i];
                    var mas = plan.Marshal[i];
                    int slot = offsets[i];
                    if (pt.IsByRef)
                        jsArgs[slot] = OpaqueParameterScope.Push(ctx, args[i], pt.GetElementType(), mas);
                    else if (mas?.JsMarshalType == JsMarshalType.UnpackedValues)
                    {
                        TableMarshal.PushUnpackedInto(ctx, args[i], pt, mas, jsArgs, slot);
                    }
                    else
                        jsArgs[slot] = TypedMarshal.Push(ctx, args[i], pt, mas);
                }

                unsafe
                {
                    fixed (JsValue* argvPtr = argc > 0 ? jsArgs : null)
                    {
                        ret = EngineApi.Call(
                            ctx,
                            func,
                            JsValueUtil.Undefined,
                            argc,
                            argc > 0 ? (IntPtr)argvPtr : IntPtr.Zero);
                    }
                }
                if (JsValueUtil.IsException(ret))
                {
                    if (NestedJsCallPendingError.TryTake(out Exception nested))
                    {
                        throw nested;
                    }

                    JsValue ex = EngineApi.GetException(ctx);
                    string msg = JsEnv.FormatJsValue(ctx, ex);
                    JsValueUtil.Free(ctx, ex);
                    throw new JsScriptException($"zents: {msg}");
                }

                // Write back by-ref via Opaque
                for (int i = 0; i < clrArgc; i++)
                {
                    if (!paramTypes[i].IsByRef)
                    {
                        continue;
                    }

                    if (OpaqueParameterScope.TryPop(ctx, jsArgs[offsets[i]], out object updated))
                    {
                        Type elem = paramTypes[i].GetElementType();
                        if (updated == null)
                        {
                            args[i] = null;
                        }
                        else if (elem.IsInstanceOfType(updated))
                        {
                            args[i] = updated;
                        }
                        else
                        {
                            args[i] = Convert.ChangeType(updated, Nullable.GetUnderlyingType(elem) ?? elem);
                        }
                    }
                }

                if (returnType == typeof(void))
                {
                    return null;
                }

                object managed = TypedMarshal.Pop(ctx, ret, returnType, plan.ReturnMarshal);
                return managed;
            }
            finally
            {
                JsValueUtil.Free(ctx, ret);
                for (int i = 0; i < argc; i++)
                {
                    JsValueUtil.Free(ctx, jsArgs[i]);
                }

                if (argc != 0)
                    ArrayPool<JsValue>.Shared.Return(jsArgs, clearArray: true);
                if (opaque)
                    OpaqueParameterScope.Leave(ctx);
            }
        }

        private sealed class BridgeClosure
        {
            private readonly JsEnv _env;
            private readonly JsValue _func;
            private readonly int _gen;
            private readonly Type[] _paramTypes;
            private readonly Type _returnType;
            private readonly JsCallPlan _plan;
            private readonly int[] _offsets;
            private readonly int _argc;
            private readonly bool _opaque;

            public BridgeClosure(JsEnv env, JsValue func, int gen, Type[] paramTypes, Type returnType, JsCallPlan plan)
            {
                _env = env;
                _func = func;
                _gen = gen;
                _paramTypes = paramTypes;
                _returnType = returnType;
                _plan = plan;
                _offsets = new int[paramTypes.Length];
                for (int i = 0; i < paramTypes.Length; ++i)
                {
                    _offsets[i] = _argc;
                    var marshal = plan.Marshal[i];
                    _argc += !paramTypes[i].IsByRef && marshal?.JsMarshalType == JsMarshalType.UnpackedValues ? marshal.Members.Length : 1;
                    _opaque |= paramTypes[i].IsByRef || marshal?.JsMarshalType == JsMarshalType.OpaqueValue;
                }
            }

            private unsafe JsValue Call(int count, JsValue* values)
            {
                JsMainThread.EnsureMainThread();
                if (!_env.IsAlive || _env.Generation != _gen)
                    throw new InvalidOperationException("zents: JS runtime was reset; re-bind GetFunction delegates.");
                JsValue result = EngineApi.Call(_env.Context, _func, JsValueUtil.Undefined, count, (IntPtr)values);
                if (JsValueUtil.IsException(result))
                {
                    if (NestedJsCallPendingError.TryTake(out Exception nested))
                        throw nested;
                    _env.ThrowPendingException();
                }
                return result;
            }
            public unsafe JsValue Call0() => Call(0, null);
            public unsafe JsValue Call1(JsValue a)
            {
                JsValue* values = stackalloc JsValue[1];
                values[0] = a;
                return Call(1, values);
            }
            public unsafe JsValue Call2(JsValue a, JsValue b)
            {
                JsValue* values = stackalloc JsValue[2];
                values[0] = a;
                values[1] = b;
                return Call(2, values);
            }
            public unsafe JsValue Call3(JsValue a, JsValue b, JsValue c)
            {
                JsValue* values = stackalloc JsValue[3];
                values[0] = a;
                values[1] = b;
                values[2] = c;
                return Call(3, values);
            }
            public unsafe JsValue Call4(JsValue a, JsValue b, JsValue c, JsValue d)
            {
                JsValue* values = stackalloc JsValue[4];
                values[0] = a;
                values[1] = b;
                values[2] = c;
                values[3] = d;
                return Call(4, values);
            }
            public void Discard(JsValue result) => JsValueUtil.Free(_env.Context, result);
            public T Read<T>(JsValue result)
            {
                try
                {
                    if (JsCallPlan.MatchValue(_env.Context, result, typeof(T), null) < 0)
                        throw new InvalidCastException("zents: JS result is not assignable to " + typeof(T));
                    return PrimitiveReader<T>.Convert(result);
                }
                finally { JsValueUtil.Free(_env.Context, result); }
            }

            public JsValue JsFunction => _func;

            public object Run(object[] args) =>
                InvokeCore(_env, _func, _gen, _paramTypes, _returnType, args, _plan, _offsets, _argc, _opaque);
        }

        /// <summary>
        /// If <paramref name="d"/> was created from a JS function, return that function (not Dup'd).
        /// </summary>
        public static bool TryGetBoundJsFunction(Delegate d, out JsValue jsFunc)
        {
            jsFunc = default;
            if (d == null)
            {
                return false;
            }

            Delegate[] list = d.GetInvocationList();
            if (list.Length != 1)
            {
                return false;
            }

            if (!BoundJsFuncs.TryGetValue(list[0], out JsFuncHolder holder))
            {
                return false;
            }

            if (!holder.Valid || !holder.Owner.IsAlive || holder.Generation != holder.Owner.Generation)
                throw new InvalidOperationException("zents: JS runtime was reset; re-bind GetFunction delegates.");
            jsFunc = holder.Func;
            return true;
        }

        public static void ProcessPendingReleases()
        {
            while (PendingReleases.TryDequeue(out JsFuncHolder holder))
            {
                holder.Release();
            }
            lock (HeldFunctions)
                HeldFunctions.RemoveAll(weak => !weak.TryGetTarget(out _));
            foreach (var pair in FromJsIdentityCache)
                if (!pair.Value.TryGetTarget(out _))
                    FromJsIdentityCache.TryRemove(pair.Key, out _);
        }

        public static void Release(JsEnv env)
        {
            ProcessPendingReleases();
            FromJsIdentityCache.Clear();
            lock (HeldFunctions)
            {
                foreach (var weak in HeldFunctions)
                    if (weak.TryGetTarget(out JsFuncHolder holder))
                        holder.Release();
                HeldFunctions.Clear();
            }
        }

        public static void InvalidateGeneration(int generation)
        {
        }
    }
}
