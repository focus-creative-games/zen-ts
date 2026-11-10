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
using System.Reflection;
using ZenTS.Jvm;
using ZenTS.Emit;
using ZenTS.Marshaling;
using ZenTS.Utils;

namespace ZenTS.DelegateImpl
{
    internal static class DynamicBridgeFactory
    {
        private static readonly List<JsValue> HeldFunctions = new List<JsValue>();
        private static readonly ConcurrentDictionary<Type, Func<JsEnv, JsValue, int, Delegate>> Factories =
            new ConcurrentDictionary<Type, Func<JsEnv, JsValue, int, Delegate>>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Delegate, JsFuncHolder> BoundJsFuncs =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Delegate, JsFuncHolder>();

        private sealed class JsFuncHolder
        {
            public JsValue Func;
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

        private static readonly ConcurrentDictionary<(IntPtr identity, Type delType), Delegate> FromJsIdentityCache =
            new ConcurrentDictionary<(IntPtr, Type), Delegate>();

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
            if (FromJsIdentityCache.TryGetValue(cacheKey, out Delegate cached) && cached != null)
            {
                return cached;
            }

            JsValue funcDup = JsValueUtil.Dup(jsFunc);
            lock (HeldFunctions)
            {
                HeldFunctions.Add(funcDup);
            }

            Func<JsEnv, JsValue, int, Delegate> factory = Factories.GetOrAdd(delegateType, BuildFactory);
            Delegate created = factory(env, funcDup, generation);
            BoundJsFuncs.Add(created, new JsFuncHolder { Func = funcDup });
            FromJsIdentityCache[cacheKey] = created;
            // Also index the Dup'd value (may differ if bits differ after Dup — usually same).
            FromJsIdentityCache[(JsValueUtil.GetIdentity(funcDup), delegateType)] = created;
            return created;
        }

        private static Func<JsEnv, JsValue, int, Delegate> BuildFactory(Type delegateType)
        {
            MethodInfo invoke = delegateType.GetMethod("Invoke");
            ParameterInfo[] parameters = invoke.GetParameters();
            Type returnType = invoke.ReturnType;
            Type[] paramTypes = parameters.Select(p => p.ParameterType).ToArray();

            return (env, func, gen) =>
            {
                var closure = new BridgeClosure(env, func, gen, paramTypes, returnType, JsCallPlan.For(invoke));
                ParameterExpression[] pexprs = parameters
                    .Select(p => Expression.Parameter(p.ParameterType, p.Name ?? "arg"))
                    .ToArray();
                ParameterExpression argsVar = Expression.Variable(typeof(object[]), "args");
                MethodInfo run = typeof(BridgeClosure).GetMethod(nameof(BridgeClosure.Run));

                var initializers = new Expression[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type pt = parameters[i].ParameterType;
                    if (pt.IsByRef || pt.IsValueType)
                    {
                        initializers[i] = Expression.Convert(pexprs[i], typeof(object));
                    }
                    else
                    {
                        initializers[i] = pexprs[i];
                    }
                }

                var body = new List<Expression>
                {
                    Expression.Assign(argsVar, Expression.NewArrayInit(typeof(object), initializers))
                };

                if (returnType == typeof(void))
                {
                    body.Add(Expression.Call(Expression.Constant(closure), run, argsVar));
                    AppendByRefWritebacks(body, parameters, pexprs, argsVar);
                    return Expression.Lambda(delegateType, Expression.Block(new[] { argsVar }, body), pexprs).Compile();
                }

                ParameterExpression retVar = Expression.Variable(typeof(object), "ret");
                body.Add(Expression.Assign(retVar, Expression.Call(Expression.Constant(closure), run, argsVar)));
                AppendByRefWritebacks(body, parameters, pexprs, argsVar);
                body.Add(Expression.Convert(retVar, returnType));
                return Expression.Lambda(delegateType, Expression.Block(new[] { argsVar, retVar }, body), pexprs).Compile();
            };
        }

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
            object[] args, JsCallPlan plan)
        {
            JsMainThread.EnsureMainThread();
            if (!env.IsAlive || boundGen != env.Generation)
            {
                throw new InvalidOperationException("zents: JS runtime was reset; re-bind GetFunction delegates.");
            }

            IntPtr ctx = env.Context;
            int clrArgc=args?.Length??0;
            var offsets=new int[clrArgc];int argc=0;
            for(int i=0;i<clrArgc;++i){offsets[i]=argc;var mas=plan.Marshal[i];argc+=!paramTypes[i].IsByRef && mas?.JsMarshalType==JsMarshalType.UnpackedValues?mas.Members.Length:1;}
            var jsArgs=new JsValue[argc];
            OpaqueParameterScope.Enter();
            try
            {
                for(int i=0;i<clrArgc;++i)
                {
                    Type pt=paramTypes[i];var mas=plan.Marshal[i];int slot=offsets[i];
                    if(pt.IsByRef)jsArgs[slot]=OpaqueParameterScope.Push(ctx,args[i],pt.GetElementType(),mas);
                    else if(mas?.JsMarshalType==JsMarshalType.UnpackedValues)
                    {
                        JsValue packed=TypedMarshal.Push(ctx,args[i],pt,mas);
                        try {for(int k=0;k<mas.Members.Length;++k)jsArgs[slot+k]=EngineApi.GetPropertyUint32(ctx,packed,(uint)k);}
                        finally {JsValueUtil.Free(ctx,packed);}
                    }
                    else jsArgs[slot]=TypedMarshal.Push(ctx,args[i],pt,mas);
                }

                JsValue ret;
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
                    JsValueUtil.Free(ctx, ret);
                    return null;
                }

                object managed = TypedMarshal.Pop(ctx, ret, returnType, plan.ReturnMarshal);
                JsValueUtil.Free(ctx, ret);
                return managed;
            }
            finally
            {
                for (int i = 0; i < argc; i++)
                {
                    JsValueUtil.Free(ctx, jsArgs[i]);
                }

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

            public BridgeClosure(JsEnv env, JsValue func, int gen, Type[] paramTypes, Type returnType, JsCallPlan plan)
            {
                _env = env;
                _func = func;
                _gen = gen;
                _paramTypes = paramTypes;
                _returnType = returnType;
                _plan = plan;
            }

            public JsValue JsFunction => _func;

            public object Run(object[] args) =>
                InvokeCore(_env, _func, _gen, _paramTypes, _returnType, args, _plan);
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

            jsFunc = holder.Func;
            return true;
        }

        public static void Release(JsEnv env)
        {
            FromJsIdentityCache.Clear();
            if (env == null || !env.IsAlive)
            {
                lock (HeldFunctions)
                {
                    HeldFunctions.Clear();
                }

                return;
            }

            IntPtr ctx = env.Context;
            lock (HeldFunctions)
            {
                foreach (JsValue fn in HeldFunctions)
                {
                    JsValueUtil.Free(ctx, fn);
                }

                HeldFunctions.Clear();
            }
        }

        public static void InvalidateGeneration(int generation)
        {
        }
    }
}
