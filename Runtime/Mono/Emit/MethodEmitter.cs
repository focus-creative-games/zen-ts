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
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using ZTS.Jvm;
using ZTS.Marshaling;
using ZTS.Mt;
using ZTS.Utils;

namespace ZTS.Emit
{
    internal enum ConversionKind
    {
        Identity = 0,
        ImplicitNumeric,
        ImplicitEnumeration,
        ImplicitNullable,
        ImplicitBoxing,
        ImplicitReference,
        None,
    }

    internal static class MethodOverloadDispatcher
    {
        private readonly struct ApplicableMethod
        {
            public readonly MethodInfo Method;
            public readonly ConversionKind[] Kinds;
            public readonly int ParamCount;
            public readonly int Index;

            public ApplicableMethod(MethodInfo method, ConversionKind[] kinds, int paramCount, int index)
            {
                Method = method;
                Kinds = kinds;
                ParamCount = paramCount;
                Index = index;
            }
        }

        public static MethodInfo Select(IReadOnlyList<MethodInfo> methods, object[] args)
        {
            var applicable = new List<ApplicableMethod>();
            for (int i = 0; i < methods.Count; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] ps = GetJsFacingParameters(method);
                if (!ArityOk(ps, args.Length))
                {
                    continue;
                }

                if (!TryGetConversionKinds(ps, args, out ConversionKind[] kinds))
                {
                    continue;
                }

                applicable.Add(new ApplicableMethod(method, kinds, ps.Length, i));
            }

            if (applicable.Count == 0)
            {
                return null;
            }

            var winners = new List<ApplicableMethod> { applicable[0] };
            for (int i = 1; i < applicable.Count; i++)
            {
                ApplicableMethod candidate = applicable[i];
                bool dominated = false;
                for (int w = winners.Count - 1; w >= 0; w--)
                {
                    ApplicableMethod winner = winners[w];
                    if (IsStrictlyBetter(candidate, winner))
                    {
                        winners.RemoveAt(w);
                    }
                    else if (IsStrictlyBetter(winner, candidate))
                    {
                        dominated = true;
                        break;
                    }
                }

                if (!dominated)
                {
                    winners.Add(candidate);
                }
            }

            if (winners.Count == 1)
            {
                return winners[0].Method;
            }

            int minParamCount = int.MaxValue;
            foreach (ApplicableMethod winner in winners)
            {
                if (winner.ParamCount < minParamCount)
                {
                    minParamCount = winner.ParamCount;
                }
            }

            var tied = new List<ApplicableMethod>();
            foreach (ApplicableMethod winner in winners)
            {
                if (winner.ParamCount == minParamCount)
                {
                    tied.Add(winner);
                }
            }

            if (tied.Count == 1)
            {
                return tied[0].Method;
            }

            tied.Sort((a, b) => a.Index.CompareTo(b.Index));
            ApplicableMethod first = tied[0];
            for (int i = 1; i < tied.Count; i++)
            {
                if (!IsStrictlyBetter(first, tied[i]) && !IsStrictlyBetter(tied[i], first))
                {
                    throw new JsScriptException(
                        $"zts: ambiguous overload; candidates: {FormatCandidates(tied)}");
                }
            }

            return first.Method;
        }

        internal static bool IsExtensionMethod(MethodInfo method) =>
            method != null &&
            method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);

        /// <summary>CLR parameters visible from JS (extension methods omit the leading <c>this</c>).</summary>
        internal static ParameterInfo[] GetJsFacingParameters(MethodInfo method)
        {
            ParameterInfo[] ps = method.GetParameters();
            if (!IsExtensionMethod(method) || ps.Length == 0)
            {
                return ps;
            }

            var rest = new ParameterInfo[ps.Length - 1];
            Array.Copy(ps, 1, rest, 0, rest.Length);
            return rest;
        }

        private static bool ArityOk(ParameterInfo[] ps, int argc)
        {
            int required = 0;
            int maxSlots = 0;
            bool hasParams = ps.Length > 0 && IsParams(ps[ps.Length - 1]);

            for (int i = 0; i < ps.Length; i++)
            {
                if (IsParams(ps[i]))
                {
                    continue;
                }

                int slots = TableMarshal.GetJsArgSlotCount(ps[i]);
                maxSlots += slots;
                if (!ps[i].IsOptional)
                {
                    required += slots;
                }
            }

            if (argc < required)
            {
                return false;
            }

            if (hasParams)
            {
                return true;
            }

            return argc <= maxSlots;
        }

        private static bool IsParams(ParameterInfo p) =>
            p.IsDefined(typeof(ParamArrayAttribute), false);

        private static Type UnwrapParameterType(ParameterInfo p)
        {
            Type pt = p.ParameterType;
            return pt.IsByRef ? pt.GetElementType() : pt;
        }

        private static bool TryGetConversionKinds(ParameterInfo[] ps, object[] args, out ConversionKind[] kinds)
        {
            kinds = new ConversionKind[args.Length];
            int argIndex = 0;
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = UnwrapParameterType(ps[i]);
                if (IsParams(ps[i]))
                {
                    Type elem = pt.IsArray ? pt.GetElementType() : pt;
                    for (; argIndex < args.Length; argIndex++)
                    {
                        ConversionKind kind = GetConversionKind(args[argIndex], elem);
                        if (kind == ConversionKind.None)
                        {
                            return false;
                        }

                        kinds[argIndex] = kind;
                    }

                    return true;
                }

                JsMarshalAsAttribute mas = JsMarshalAsXmlRegistry.ResolveParameterMarshal(ps[i]);
                if (mas != null &&
                    mas.JsMarshalType == JsMarshalType.UnpackedValues &&
                    mas.Members != null &&
                    mas.Members.Length > 0)
                {
                    Type underlying = Nullable.GetUnderlyingType(pt) ?? pt;
                    int memberCount = mas.Members.Length;
                    if (argIndex + memberCount > args.Length)
                    {
                        if (ps[i].IsOptional)
                        {
                            continue;
                        }

                        return false;
                    }

                    for (int m = 0; m < memberCount; m++)
                    {
                        Type memberType = TableMarshal.ResolveMemberType(underlying, mas.Members[m]);
                        ConversionKind kind = GetConversionKind(args[argIndex], memberType);
                        if (kind == ConversionKind.None)
                        {
                            return false;
                        }

                        kinds[argIndex] = kind;
                        argIndex++;
                    }

                    continue;
                }

                if (argIndex >= args.Length)
                {
                    if (ps[i].IsOptional)
                    {
                        continue;
                    }

                    return false;
                }

                ConversionKind paramKind = GetConversionKind(args[argIndex], pt);
                if (paramKind == ConversionKind.None)
                {
                    return false;
                }

                kinds[argIndex] = paramKind;
                argIndex++;
            }

            return argIndex == args.Length;
        }

        private static ConversionKind GetConversionKind(object arg, Type paramType)
        {
            Type nullableUnderlying = Nullable.GetUnderlyingType(paramType);
            Type target = nullableUnderlying ?? paramType;
            bool isNullable = nullableUnderlying != null;

            if (arg == null)
            {
                if (isNullable)
                {
                    return ConversionKind.ImplicitNullable;
                }

                return target.IsValueType ? ConversionKind.None : ConversionKind.ImplicitReference;
            }

            Type argType = arg.GetType();
            if (target == argType)
            {
                return ConversionKind.Identity;
            }

            if (target.IsEnum)
            {
                return TryEnumFromIntegral(arg, target, out ConversionKind enumKind)
                    ? enumKind
                    : ConversionKind.None;
            }

            if (argType.IsEnum && target.IsPrimitive && IsIntegralNumeric(target))
            {
                Type enumUnderlying = Enum.GetUnderlyingType(argType);
                if (target == enumUnderlying)
                {
                    return ConversionKind.ImplicitEnumeration;
                }
            }

            if (TryGetNumericConversionKind(arg, target, out ConversionKind numericKind))
            {
                return numericKind;
            }

            if (argType.IsValueType && !target.IsValueType &&
                (target == typeof(object) || target == typeof(ValueType) || target.IsInterface))
            {
                return ConversionKind.ImplicitBoxing;
            }

            if (target.IsAssignableFrom(argType))
            {
                return ConversionKind.ImplicitReference;
            }

            return ConversionKind.None;
        }

        private static bool TryEnumFromIntegral(object arg, Type enumType, out ConversionKind kind)
        {
            kind = ConversionKind.None;
            if (!enumType.IsEnum)
            {
                return false;
            }

            if (arg is int)
            {
                kind = ConversionKind.ImplicitEnumeration;
                return true;
            }

            if (arg is double d)
            {
                if (d != Math.Truncate(d))
                {
                    return false;
                }

                kind = ConversionKind.ImplicitEnumeration;
                return true;
            }

            if (IsIntegralNumeric(arg.GetType()))
            {
                kind = ConversionKind.ImplicitEnumeration;
                return true;
            }

            return false;
        }

        private static bool TryGetNumericConversionKind(object arg, Type target, out ConversionKind kind)
        {
            kind = ConversionKind.None;
            if (!IsNumeric(target))
            {
                return false;
            }

            Type from = arg.GetType();
            if (from == target)
            {
                kind = ConversionKind.Identity;
                return true;
            }

            if (from == typeof(double) && IsIntegralNumeric(target))
            {
                double d = (double)arg;
                if (d != Math.Truncate(d))
                {
                    return false;
                }
            }

            if (IsImplicitNumericWidening(from, target))
            {
                kind = ConversionKind.ImplicitNumeric;
                return true;
            }

            if (from == typeof(double) && IsIntegralNumeric(target))
            {
                try
                {
                    Convert.ChangeType(arg, target, CultureInfo.InvariantCulture);
                    kind = ConversionKind.ImplicitNumeric;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsStrictlyBetter(ApplicableMethod a, ApplicableMethod b)
        {
            bool aBetter = false;
            bool bBetter = false;
            int shared = Math.Min(a.Kinds.Length, b.Kinds.Length);
            for (int i = 0; i < shared; i++)
            {
                if (a.Kinds[i] < b.Kinds[i])
                {
                    aBetter = true;
                }
                else if (a.Kinds[i] > b.Kinds[i])
                {
                    bBetter = true;
                }
            }

            if (aBetter && !bBetter)
            {
                return true;
            }

            if (bBetter && !aBetter)
            {
                return false;
            }

            if (a.ParamCount < b.ParamCount)
            {
                return true;
            }

            if (a.ParamCount > b.ParamCount)
            {
                return false;
            }

            return false;
        }

        private static bool IsImplicitNumericWidening(Type from, Type to)
        {
            if (!IsNumeric(from) || !IsNumeric(to) || from == to)
            {
                return false;
            }

            if (from == typeof(decimal) || to == typeof(decimal))
            {
                if (to == typeof(decimal))
                {
                    return IsIntegralNumeric(from) || from == typeof(float) || from == typeof(double);
                }

                return false;
            }

            return GetNumericRank(from) < GetNumericRank(to);
        }

        private static int GetNumericRank(Type t)
        {
            if (t == typeof(sbyte) || t == typeof(byte))
            {
                return 1;
            }

            if (t == typeof(short) || t == typeof(ushort) || t == typeof(char))
            {
                return 2;
            }

            if (t == typeof(int) || t == typeof(uint))
            {
                return 3;
            }

            if (t == typeof(long) || t == typeof(ulong))
            {
                return 4;
            }

            if (t == typeof(float))
            {
                return 5;
            }

            if (t == typeof(double))
            {
                return 6;
            }

            return -1;
        }

        private static bool IsIntegralNumeric(Type t) =>
            t == typeof(sbyte) || t == typeof(byte) || t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) ||
            t == typeof(char);

        private static bool IsNumeric(Type t) =>
            IsIntegralNumeric(t) || t == typeof(float) || t == typeof(double) || t == typeof(decimal);

        private static string FormatCandidates(IReadOnlyList<ApplicableMethod> candidates)
        {
            var parts = new string[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                parts[i] = FormatSignature(candidates[i].Method);
            }

            return string.Join(", ", parts);
        }

        private static string FormatSignature(MethodInfo method)
        {
            ParameterInfo[] ps = method.GetParameters();
            if (ps.Length == 0)
            {
                return method.Name + "()";
            }

            var names = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType;
                names[i] = pt.FullName;
            }

            return method.Name + "(" + string.Join(", ", names) + ")";
        }
    }

    /// <summary>Expression-compiled method bridge (no Method.Invoke on hot path).</summary>
    internal static class MethodEmitter
    {
        private static readonly ConcurrentDictionary<MethodInfo, Func<object, object[], object>> Compiled =
            new ConcurrentDictionary<MethodInfo, Func<object, object[], object>>();

        private static readonly ConcurrentDictionary<MethodInfo, MethodArgPlan> ArgPlans =
            new ConcurrentDictionary<MethodInfo, MethodArgPlan>();

        /// <summary>Per-method marshal / default cache so hot invoke skips XML/attribute resolve.</summary>
        private sealed class MethodArgPlan
        {
            public readonly ParameterInfo[] Parameters;
            public readonly JsMarshalAsAttribute[] ParamMarshal;
            public readonly JsMarshalAsAttribute ReturnMarshal;
            public readonly object[] DefaultValues;
            public readonly bool[] HasDefault;

            public MethodArgPlan(MethodInfo method)
            {
                Parameters = method.GetParameters();
                ParamMarshal = new JsMarshalAsAttribute[Parameters.Length];
                DefaultValues = new object[Parameters.Length];
                HasDefault = new bool[Parameters.Length];
                for (int i = 0; i < Parameters.Length; i++)
                {
                    ParamMarshal[i] = JsMarshalAsXmlRegistry.ResolveParameterMarshal(Parameters[i]);
                    if (Parameters[i].IsOptional)
                    {
                        HasDefault[i] = true;
                        DefaultValues[i] = Parameters[i].DefaultValue;
                    }
                }

                ReturnMarshal = JsMarshalAsXmlRegistry.ResolveReturnMarshal(method);
            }
        }

        private static MethodArgPlan GetArgPlan(MethodInfo method) =>
            ArgPlans.GetOrAdd(method, static m => new MethodArgPlan(m));

        internal static void ResetCaches()
        {
            Compiled.Clear();
            ArgPlans.Clear();
        }

        public static JSValue EmitMethod(JsEnv env, MethodInfo method) =>
            EmitMethod(env, method, ownerType: method.DeclaringType, isByVal: false);

        public static JSValue EmitMethod(JsEnv env, MethodInfo method, Type ownerType, bool isByVal)
        {
            IntPtr ctx = env.Context;
            JSValue fn;
            if (method.IsGenericMethodDefinition)
            {
                var openCb = new JsOpenGenericMethodCallback(method);
                fn = JsCallbackGate.NewCFunction(ctx, openCb.Invoke, method.Name, method.GetParameters().Length);
            }
            else
            {
                EnsureCompiled(method);
                var callback = new JsMethodCallback(method);
                fn = JsCallbackGate.NewCFunction(ctx, callback.Invoke, method.Name, method.GetParameters().Length);
            }

            AttachTag(ctx, fn, new MethodClosureTag
            {
                Method = method,
                OwnerType = ownerType ?? method.DeclaringType,
                IsStatic = method.IsStatic,
                IsByVal = isByVal,
                IsDirect = true,
            });
            return fn;
        }

        public static JSValue EmitOverloadGroup(JsEnv env, string name, List<MethodInfo> methods) =>
            EmitOverloadGroup(env, name, methods, ownerType: methods.Count > 0 ? methods[0].DeclaringType : null, isByVal: false);

        public static JSValue EmitOverloadGroup(JsEnv env, string name, List<MethodInfo> methods, Type ownerType, bool isByVal)
        {
            foreach (MethodInfo m in methods)
            {
                if (!m.IsGenericMethodDefinition)
                {
                    EnsureCompiled(m);
                }
            }

            var callback = new JsOverloadCallback(methods);
            JSValue fn = JsCallbackGate.NewCFunction(env.Context, callback.Invoke, name, 0);
            AttachTag(env.Context, fn, new MethodClosureTag
            {
                Method = null,
                OwnerType = ownerType ?? (methods.Count > 0 ? methods[0].DeclaringType : null),
                IsStatic = methods.Count > 0 && methods.All(m => m.IsStatic),
                IsByVal = isByVal,
                IsDirect = false,
            });
            return fn;
        }

        public static bool TryGetDirectTag(IntPtr ctx, JSValue fn, out MethodClosureTag tag)
        {
            tag = null;
            if (QuickJsDll.JS_IsFunction(ctx, fn) == 0)
            {
                return false;
            }

            JSValue idVal = QuickJsDll.JS_GetPropertyStr(ctx, fn, "__zts_method_id");
            try
            {
                if (JsValueUtil.GetNormTag(idVal) != JsValueUtil.TagInt)
                {
                    return false;
                }

                int id = unchecked((int)idVal.UInt64);
                if (!MethodTagRegistry.TryGet(id, out tag) || tag == null || !tag.IsDirect || tag.Method == null)
                {
                    tag = null;
                    return false;
                }

                return true;
            }
            finally
            {
                JsValueUtil.Free(ctx, idVal);
            }
        }

        private static void AttachTag(IntPtr ctx, JSValue fn, MethodClosureTag tag)
        {
            int id = MethodTagRegistry.Register(tag);
            QuickJsDll.JS_SetPropertyStr(ctx, fn, "__zts_method_id", JsValueUtil.NewInt32(id));
            QuickJsDll.JS_SetPropertyStr(ctx, fn, "__zts_direct", JsValueUtil.NewBool(tag.IsDirect));
        }

        internal static Func<object, object[], object> EnsureCompiled(MethodInfo method)
        {
            if (Compiled.TryGetValue(method, out Func<object, object[], object> existing))
            {
                return existing;
            }

            Func<object, object[], object> fn;
            try
            {
                fn = Compile(method);
            }
            catch
            {
                MethodInfo m = method;
                fn = (target, args) => m.Invoke(target, args);
            }

            return Compiled.GetOrAdd(method, fn);
        }

        private static Func<object, object[], object> Compile(MethodInfo method)
        {
            ParameterExpression target = Expression.Parameter(typeof(object), "target");
            ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
            ParameterInfo[] ps = method.GetParameters();
            var callArgs = new Expression[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                Expression raw = Expression.ArrayIndex(args, Expression.Constant(i));
                if (pt.IsByRef)
                {
                    Type elem = pt.GetElementType();
                    callArgs[i] = Expression.Convert(raw, elem);
                }
                else
                {
                    callArgs[i] = Expression.Convert(ConvertArg(raw, pt), pt);
                }
            }

            Expression instance = method.IsStatic
                ? null
                : Expression.Convert(target, method.DeclaringType);

            Expression call = method.IsStatic
                ? Expression.Call(method, callArgs)
                : Expression.Call(instance, method, callArgs);

            Expression body;
            if (method.ReturnType == typeof(void))
            {
                body = Expression.Block(call, Expression.Constant(null, typeof(object)));
            }
            else
            {
                body = Expression.Convert(call, typeof(object));
            }

            return Expression.Lambda<Func<object, object[], object>>(body, target, args).Compile();
        }

        private static Expression ConvertArg(Expression raw, Type pt)
        {
            Type target = Nullable.GetUnderlyingType(pt) ?? pt;
            return Expression.Convert(raw, target);
        }

        private static object PopParameter(IntPtr ctx, JSValue jsArg, ParameterInfo param, JsMarshalAsAttribute mas)
        {
            Type pt = param.ParameterType;
            if (pt.IsByRef)
            {
                pt = pt.GetElementType();
            }

            return TypedMarshal.Pop(ctx, jsArg, pt, mas);
        }

        private static object[] PopArgs(IntPtr ctx, MethodInfo method, int argc, IntPtr argvPtr)
        {
            MethodArgPlan plan = GetArgPlan(method);
            ParameterInfo[] ps = plan.Parameters;
            var args = new object[ps.Length];
            int jsIndex = 0;
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                if (pt.IsByRef)
                {
                    pt = pt.GetElementType();
                }

                bool isParams = ps[i].IsDefined(typeof(ParamArrayAttribute), false);
                if (isParams)
                {
                    Type el = pt.GetElementType() ?? typeof(object);
                    if (jsIndex >= argc)
                    {
                        args[i] = Array.CreateInstance(el, 0);
                    }
                    else
                    {
                        args[i] = PopParameter(ctx, ArgReader.Read(argvPtr, jsIndex), ps[i], plan.ParamMarshal[i]);
                    }

                    continue;
                }

                JsMarshalAsAttribute mas = plan.ParamMarshal[i];
                if (mas != null &&
                    mas.JsMarshalType == JsMarshalType.UnpackedValues &&
                    mas.Members != null &&
                    mas.Members.Length > 0)
                {
                    int slots = mas.Members.Length;
                    if (jsIndex + slots <= argc)
                    {
                        args[i] = TableMarshal.PopUnpacked(ctx, argvPtr, jsIndex, argc, pt, mas);
                    }
                    else if (ps[i].IsOptional)
                    {
                        args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                    }
                    else
                    {
                        args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                    }

                    jsIndex += slots;
                    continue;
                }

                if (jsIndex < argc)
                {
                    args[i] = PopParameter(ctx, ArgReader.Read(argvPtr, jsIndex), ps[i], mas);
                    jsIndex++;
                }
                else if (plan.HasDefault[i])
                {
                    args[i] = plan.DefaultValues[i];
                }
                else
                {
                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }
            }

            return args;
        }

        private static bool HasByRefParameters(MethodInfo method)
        {
            foreach (ParameterInfo p in method.GetParameters())
            {
                if (p.ParameterType.IsByRef)
                {
                    return true;
                }
            }

            return false;
        }

        private static object InvokeMethod(MethodInfo method, object target, object[] args)
        {
            if (HasPointerSignature(method))
            {
                return PointerAwareInvoker.Invoke(method, target, args);
            }

            if (HasByRefParameters(method))
            {
                return method.Invoke(target, args);
            }

            return EnsureCompiled(method)(target, args);
        }

        private static bool HasPointerSignature(MethodInfo method)
        {
            if (method.ReturnType.IsPointer)
            {
                return true;
            }

            foreach (ParameterInfo p in method.GetParameters())
            {
                Type pt = p.ParameterType;
                if (pt.IsByRef)
                {
                    pt = pt.GetElementType();
                }

                if (pt != null && pt.IsPointer)
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteBackByRefParameters(
            IntPtr ctx, MethodInfo method, int argc, IntPtr argvPtr, object[] args, bool isExtension)
        {
            ParameterInfo[] ps = method.GetParameters();
            int jsIndex = 0;
            int start = isExtension ? 1 : 0;
            for (int i = start; i < ps.Length; i++)
            {
                int slots = TableMarshal.GetJsArgSlotCount(ps[i]);
                if (ps[i].ParameterType.IsByRef)
                {
                    if (jsIndex >= 0 && jsIndex < argc)
                    {
                        JSValue jsArg = ArgReader.Read(argvPtr, jsIndex);
                        object updated = args[i];
                        if (ObjectRegistry.TryGetObject(ctx, jsArg, out object holder) && holder is OpaqueValue opaque)
                        {
                            OpaqueParameterScope.SetTarget(opaque, updated);
                        }
                        else
                        {
                            Type elemType = ps[i].ParameterType.GetElementType();
                            if (elemType != null && elemType.IsValueType &&
                                ObjectRegistry.TryGetObject(ctx, jsArg, out _))
                            {
                                ObjectRegistry.TryReplaceObject(ctx, jsArg, updated);
                            }
                        }
                    }
                }

                if (ps[i].IsDefined(typeof(ParamArrayAttribute), false))
                {
                    break;
                }

                jsIndex += slots;
            }
        }

        private sealed class JsOpenGenericMethodCallback
        {
            private readonly MethodInfo _method;

            public JsOpenGenericMethodCallback(MethodInfo method) => _method = method;

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argvPtr)
            {
                return JsCallbackGate.ReturnErrorSentinel(
                    ctx,
                    $"zts: open generic method '{_method.Name}'; use zts.make_generic_method.");
            }
        }

        private sealed class JsMethodCallback
        {
            private readonly MethodInfo _method;
            private readonly Func<object, object[], object> _compiled;

            public JsMethodCallback(MethodInfo method)
            {
                _method = method;
                _compiled = HasPointerSignature(method) ? null : EnsureCompiled(method);
            }

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argvPtr)
            {
                try
                {
                    bool isExtension = _method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
                    object target = null;
                    if (!_method.IsStatic || isExtension)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: invalid method target.");
                        }
                    }

                    object[] args;
                    if (isExtension)
                    {
                        MethodArgPlan plan = GetArgPlan(_method);
                        ParameterInfo[] ps = plan.Parameters;
                        args = new object[ps.Length];
                        args[0] = target;
                        int jsIndex = 0;
                        for (int i = 1; i < ps.Length; i++)
                        {
                            Type pt = ps[i].ParameterType;
                            if (pt.IsByRef)
                            {
                                pt = pt.GetElementType();
                            }

                            JsMarshalAsAttribute mas = plan.ParamMarshal[i];
                            if (mas != null &&
                                mas.JsMarshalType == JsMarshalType.UnpackedValues &&
                                mas.Members != null &&
                                mas.Members.Length > 0)
                            {
                                int slots = mas.Members.Length;
                                if (jsIndex + slots <= argc)
                                {
                                    args[i] = TableMarshal.PopUnpacked(ctx, argvPtr, jsIndex, argc, pt, mas);
                                }
                                else if (ps[i].IsOptional)
                                {
                                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                                }
                                else
                                {
                                    args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                                }

                                jsIndex += slots;
                                continue;
                            }

                            if (jsIndex < argc)
                            {
                                args[i] = PopParameter(ctx, ArgReader.Read(argvPtr, jsIndex), ps[i], mas);
                                jsIndex++;
                            }
                            else if (plan.HasDefault[i])
                            {
                                args[i] = plan.DefaultValues[i];
                            }
                            else
                            {
                                args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                            }
                        }

                        object resultExt = InvokeMethod(_method, null, args);
                        WriteBackByRefParameters(ctx, _method, argc, argvPtr, args, isExtension: true);
                        if (_method.ReturnType == typeof(void))
                        {
                            return JsValueUtil.Undefined;
                        }

                        return TypedMarshal.Push(ctx, resultExt, _method.ReturnType, plan.ReturnMarshal);
                    }

                    args = PopArgs(ctx, _method, argc, argvPtr);
                    object result = InvokeMethod(_method, target, args);
                    WriteBackByRefParameters(ctx, _method, argc, argvPtr, args, isExtension: false);
                    if (_method.ReturnType == typeof(void))
                    {
                        return JsValueUtil.Undefined;
                    }

                    return TypedMarshal.Push(ctx, result, _method.ReturnType, GetArgPlan(_method).ReturnMarshal);
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }

        private sealed class JsOverloadCallback
        {
            private readonly List<MethodInfo> _methods;

            public JsOverloadCallback(List<MethodInfo> methods) => _methods = methods;

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argvPtr)
            {
                try
                {
                    var rough = new object[argc];
                    for (int i = 0; i < argc; i++)
                    {
                        rough[i] = PrimitiveMarshal.Pop(ctx, ArgReader.Read(argvPtr, i));
                        if (ReferenceEquals(rough[i], DBNull.Value))
                        {
                            rough[i] = null;
                        }
                    }

                    MethodInfo method = MethodOverloadDispatcher.Select(_methods, rough);
                    if (method == null)
                    {
                        string methodName = _methods.Count > 0 ? _methods[0].Name : "?";
                        return JsCallbackGate.ReturnErrorSentinel(ctx,
                            $"zts: no overload for {methodName} matching {argc} argument(s); candidates: {_methods.Count}.");
                    }

                    bool isExtension = MethodOverloadDispatcher.IsExtensionMethod(method);
                    object target = null;
                    if (!method.IsStatic || isExtension)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: invalid method target.");
                        }
                    }

                    object[] args;
                    object result;
                    if (isExtension)
                    {
                        MethodArgPlan plan = GetArgPlan(method);
                        ParameterInfo[] ps = plan.Parameters;
                        args = new object[ps.Length];
                        args[0] = target;
                        int jsIndex = 0;
                        for (int i = 1; i < ps.Length; i++)
                        {
                            Type pt = ps[i].ParameterType;
                            if (pt.IsByRef)
                            {
                                pt = pt.GetElementType();
                            }

                            if (jsIndex < argc)
                            {
                                args[i] = PopParameter(
                                    ctx, ArgReader.Read(argvPtr, jsIndex), ps[i], plan.ParamMarshal[i]);
                                jsIndex++;
                            }
                            else if (plan.HasDefault[i])
                            {
                                args[i] = plan.DefaultValues[i];
                            }
                            else
                            {
                                args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                            }
                        }

                        result = InvokeMethod(method, null, args);
                        WriteBackByRefParameters(ctx, method, argc, argvPtr, args, isExtension: true);
                    }
                    else
                    {
                        args = PopArgs(ctx, method, argc, argvPtr);
                        result = InvokeMethod(method, target, args);
                        WriteBackByRefParameters(ctx, method, argc, argvPtr, args, isExtension: false);
                    }

                    if (method.ReturnType == typeof(void))
                    {
                        return JsValueUtil.Undefined;
                    }

                    return TypedMarshal.Push(ctx, result, method.ReturnType, GetReturnMarshal(method));
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }

        private static JsMarshalAsAttribute GetReturnMarshal(MethodInfo method) =>
            GetArgPlan(method).ReturnMarshal;
    }

    internal static class FieldEmitter
    {
        public static JSValue EmitGetter(JsEnv env, FieldInfo field)
        {
            var cb = new FieldGetter(field);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, "get_" + field.Name, 0);
        }

        public static JSValue EmitSetter(JsEnv env, FieldInfo field)
        {
            var cb = new FieldSetter(field);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, "set_" + field.Name, 1);
        }

        private sealed class FieldGetter
        {
            private readonly FieldInfo _field;
            public FieldGetter(FieldInfo field) => _field = field;

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    object target = null;
                    if (!_field.IsStatic)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: invalid field target.");
                        }
                    }

                    return TypedMarshal.Push(ctx, _field.GetValue(target), _field.FieldType,
                        JsMarshalAsXmlRegistry.ResolveFieldMarshal(_field));
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }

        private sealed class FieldSetter
        {
            private readonly FieldInfo _field;
            public FieldSetter(FieldInfo field) => _field = field;

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    object target = null;
                    if (!_field.IsStatic)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: invalid field target.");
                        }
                    }

                    JSValue jsArg = ArgReader.Read(argv, 0);
                    object value = TypedMarshal.Pop(ctx, jsArg, _field.FieldType,
                        JsMarshalAsXmlRegistry.ResolveFieldMarshal(_field));
                    _field.SetValue(target, value);
                    return JsValueUtil.Undefined;
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }
    }

    internal static class PropertyEmitter
    {
        public static JSValue EmitGetter(JsEnv env, PropertyInfo property)
        {
            var cb = new PropGetter(property);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, "get_" + property.Name, 0);
        }

        public static JSValue EmitSetter(JsEnv env, PropertyInfo property)
        {
            var cb = new PropSetter(property);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, "set_" + property.Name, 1);
        }

        private sealed class PropGetter
        {
            private readonly PropertyInfo _property;
            public PropGetter(PropertyInfo property) => _property = property;

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    if (!_property.CanRead)
                    {
                        return JsCallbackGate.ReturnErrorSentinel(ctx,
                            $"zts: property has no getter: {_property.DeclaringType?.FullName}.{_property.Name}");
                    }

                    object target = null;
                    MethodInfo get = _property.GetMethod;
                    if (get != null && !get.IsStatic)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: invalid property target.");
                        }
                    }

                    return TypedMarshal.Push(ctx, _property.GetValue(target), _property.PropertyType,
                        JsMarshalAsXmlRegistry.ResolvePropertyMarshal(_property));
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }

        private sealed class PropSetter
        {
            private readonly PropertyInfo _property;
            public PropSetter(PropertyInfo property) => _property = property;

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    if (!_property.CanWrite)
                    {
                        return JsCallbackGate.ReturnErrorSentinel(ctx,
                            $"zts: property has no setter: {_property.DeclaringType?.FullName}.{_property.Name}");
                    }

                    object target = null;
                    MethodInfo set = _property.SetMethod;
                    if (set != null && !set.IsStatic)
                    {
                        if (!ObjectRegistry.TryGetObject(ctx, thisVal, out target))
                        {
                            return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: invalid property target.");
                        }
                    }

                    object value = TypedMarshal.Pop(ctx, ArgReader.Read(argv, 0), _property.PropertyType,
                        JsMarshalAsXmlRegistry.ResolvePropertyMarshal(_property));
                    _property.SetValue(target, value);
                    return JsValueUtil.Undefined;
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }
    }

    internal static class ConstructorEmitter
    {
        private static readonly Dictionary<ConstructorInfo, Func<object[], object>> CtorCache =
            new Dictionary<ConstructorInfo, Func<object[], object>>();

        public static JSValue Emit(JsEnv env, Type type, JSValue instanceProto)
        {
            var cb = new ConstructorCallback(type, instanceProto);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, type.Name, 0);
        }

        internal static ConstructorInfo SelectCtorPublic(ConstructorInfo[] ctors, object[] rough)
        {
            ConstructorInfo best = null;
            int bestScore = int.MinValue;
            foreach (ConstructorInfo ctor in ctors)
            {
                ParameterInfo[] ps = ctor.GetParameters();
                if (rough.Length > ps.Length)
                {
                    continue;
                }

                int score = 0;
                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i >= rough.Length)
                    {
                        if (!ps[i].IsOptional)
                        {
                            ok = false;
                            break;
                        }

                        continue;
                    }

                    if (rough[i] == null)
                    {
                        score += 5;
                        continue;
                    }

                    Type pt = ps[i].ParameterType;
                    Type at = rough[i].GetType();
                    if (pt == at)
                    {
                        score += 20;
                    }
                    else if (pt.IsAssignableFrom(at))
                    {
                        score += 10;
                    }
                    else
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok && score > bestScore)
                {
                    bestScore = score;
                    best = ctor;
                }
            }

            return best ?? ctors.FirstOrDefault(c => c.GetParameters().Length == rough.Length)
                   ?? ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
        }

        internal static Func<object[], object> CompileCtorPublic(ConstructorInfo ctor)
        {
            lock (CtorCache)
            {
                if (CtorCache.TryGetValue(ctor, out Func<object[], object> existing))
                {
                    return existing;
                }
            }

            ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
            ParameterInfo[] ps = ctor.GetParameters();
            var callArgs = new Expression[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                callArgs[i] = Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(i)), ps[i].ParameterType);
            }

            NewExpression neo = Expression.New(ctor, callArgs);
            Func<object[], object> fn = Expression.Lambda<Func<object[], object>>(
                Expression.Convert(neo, typeof(object)), args).Compile();
            lock (CtorCache)
            {
                CtorCache[ctor] = fn;
            }

            return fn;
        }

        private sealed class ConstructorCallback
        {
            private readonly Type _type;
            public JSValue Proto;

            public ConstructorCallback(Type type, JSValue instanceProto)
            {
                _type = type;
                Proto = instanceProto;
            }

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    ConstructorInfo[] ctors = _type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    if (ctors.Length == 0)
                    {
                        if (_type.IsValueType)
                        {
                            object def = Activator.CreateInstance(_type);
                            return TypedMarshal.Push(ctx, def, _type);
                        }

                        return JsCallbackGate.ReturnErrorSentinel(ctx, $"zts: no public constructor for {_type.FullName}.");
                    }

                    var rough = new object[argc];
                    for (int i = 0; i < argc; i++)
                    {
                        rough[i] = PrimitiveMarshal.Pop(ctx, ArgReader.Read(argv, i));
                        if (ReferenceEquals(rough[i], DBNull.Value))
                        {
                            rough[i] = null;
                        }
                    }

                    ConstructorInfo ctor = SelectCtorPublic(ctors, rough);
                    if (ctor == null)
                    {
                        return JsCallbackGate.ReturnErrorSentinel(ctx, $"zts: no matching constructor for {_type.FullName}.");
                    }

                    ParameterInfo[] ps = ctor.GetParameters();
                    var args = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                    {
                        Type pt = ps[i].ParameterType;
                        if (i < argc)
                        {
                            args[i] = TypedMarshal.Pop(ctx, ArgReader.Read(argv, i), pt);
                        }
                        else if (ps[i].IsOptional)
                        {
                            args[i] = ps[i].DefaultValue;
                        }
                        else
                        {
                            args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                        }
                    }

                    // Expression-compiled ctor
                    object instance = CompileCtorPublic(ctor)(args);
                    return TypedMarshal.Push(ctx, instance, _type);
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }
    }

    internal static class ExtensionMethodUtil
    {
        public static IEnumerable<MethodInfo> GetExtensionMethods(Type extendedType)
        {
            var seenMethods = new HashSet<MethodInfo>();
            var scannedExtTypes = new HashSet<Type>();

            for (Type t = extendedType; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (JsExtensionAttribute attr in t.GetCustomAttributes<JsExtensionAttribute>(inherit: false))
                {
                    if (attr.ExtensionTypes == null)
                    {
                        continue;
                    }

                    foreach (Type extType in attr.ExtensionTypes)
                    {
                        foreach (MethodInfo method in EnumerateExtensionTypeMethods(
                                     extType, extendedType, seenMethods, scannedExtTypes))
                        {
                            yield return method;
                        }
                    }
                }

                if (JsExtensionXmlRegistry.TryGetExtensionTypes(t, out Type[] xmlTypes))
                {
                    foreach (Type extType in xmlTypes)
                    {
                        foreach (MethodInfo method in EnumerateExtensionTypeMethods(
                                     extType, extendedType, seenMethods, scannedExtTypes))
                        {
                            yield return method;
                        }
                    }
                }
            }
        }

        private static IEnumerable<MethodInfo> EnumerateExtensionTypeMethods(
            Type extType,
            Type extendedType,
            HashSet<MethodInfo> seenMethods,
            HashSet<Type> scannedExtTypes)
        {
            if (extType == null || !scannedExtTypes.Add(extType))
            {
                yield break;
            }

            foreach (MethodInfo method in extType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                {
                    continue;
                }

                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length > 0 && ps[0].ParameterType.IsAssignableFrom(extendedType) &&
                    seenMethods.Add(method))
                {
                    yield return method;
                }
            }
        }
    }

    internal static class NullableConstructorEmitter
    {
        public static JSValue Emit(JsEnv env, Type nullableType, Type underlyingType)
        {
            var cb = new NullableConstructorCallback(nullableType, underlyingType);
            return JsCallbackGate.NewCFunction(env.Context, cb.Invoke, nullableType.Name, 0);
        }

        private sealed class NullableConstructorCallback
        {
            private readonly Type _underlyingType;

            public NullableConstructorCallback(Type nullableType, Type underlyingType)
            {
                _underlyingType = underlyingType;
            }

            public JSValue Invoke(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
            {
                try
                {
                    if (_underlyingType.IsEnum)
                    {
                        return JsCallbackGate.ReturnErrorSentinel(
                            ctx, $"zts: Nullable<{_underlyingType.Name}> construct is not supported for enums.");
                    }

                    if (_underlyingType.IsPrimitive || _underlyingType == typeof(string) ||
                        _underlyingType == typeof(IntPtr) || _underlyingType == typeof(UIntPtr))
                    {
                        if (argc < 1)
                        {
                            return JsCallbackGate.ReturnErrorSentinel(
                                ctx, $"zts: Nullable<{_underlyingType.Name}> requires one argument.");
                        }

                        object value = TypedMarshal.Pop(ctx, ArgReader.Read(argv, 0), _underlyingType);
                        return TypedMarshal.Push(ctx, value, _underlyingType);
                    }

                    ConstructorInfo[] ctors = _underlyingType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    if (ctors.Length == 0)
                    {
                        if (_underlyingType.IsValueType)
                        {
                            object def = Activator.CreateInstance(_underlyingType);
                            return TypedMarshal.Push(ctx, def, _underlyingType);
                        }

                        return JsCallbackGate.ReturnErrorSentinel(
                            ctx, $"zts: no public constructor for Nullable<{_underlyingType.FullName}>.");
                    }

                    var rough = new object[argc];
                    for (int i = 0; i < argc; i++)
                    {
                        rough[i] = PrimitiveMarshal.Pop(ctx, ArgReader.Read(argv, i));
                        if (ReferenceEquals(rough[i], DBNull.Value))
                        {
                            rough[i] = null;
                        }
                    }

                    ConstructorInfo ctor = ConstructorEmitter.SelectCtorPublic(ctors, rough);
                    if (ctor == null)
                    {
                        return JsCallbackGate.ReturnErrorSentinel(
                            ctx, $"zts: no matching constructor for Nullable<{_underlyingType.FullName}>.");
                    }

                    ParameterInfo[] ps = ctor.GetParameters();
                    var args = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                    {
                        Type pt = ps[i].ParameterType;
                        if (i < argc)
                        {
                            args[i] = TypedMarshal.Pop(ctx, ArgReader.Read(argv, i), pt);
                        }
                        else if (ps[i].IsOptional)
                        {
                            args[i] = ps[i].DefaultValue;
                        }
                        else
                        {
                            args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                        }
                    }

                    object instance = ConstructorEmitter.CompileCtorPublic(ctor)(args);
                    return TypedMarshal.Push(ctx, instance, _underlyingType);
                }
                catch (Exception ex)
                {
                    JsCallbackBoundary.ThrowManaged(ctx, ex);
                    return JsValueUtil.MakeErrorSentinel();
                }
            }
        }
    }
}
