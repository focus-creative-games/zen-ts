// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ZenTS.Jvm;
using ZenTS.Marshaling;
using ZenTS.Mt;

namespace ZenTS.Emit
{
    // One bind-time CLR slot plan for direct, overload, constructor and generic calls.
    // Matching never boxes primitives; only the selected plan performs conversions.
    internal sealed class JsCallPlan
    {
        private static readonly ConcurrentDictionary<MethodBase, JsCallPlan> Cache = new ConcurrentDictionary<MethodBase, JsCallPlan>();
        internal readonly MethodBase Method;
        internal readonly ParameterInfo[] Parameters;
        internal readonly JsMarshalAsAttribute[] Marshal;
        internal readonly object[] Defaults;
        internal readonly bool[] Optional;
        internal readonly int Start;
        internal readonly bool Params;
        internal readonly JsMarshalAsAttribute ReturnMarshal;
        internal static JsCallPlan For(MethodBase method) => Cache.GetOrAdd(method, m => new JsCallPlan(m));
        internal static void Reset() => Cache.Clear();
        private JsCallPlan(MethodBase method)
        {
            Method = method;
            Parameters = method.GetParameters();
            Start = method is MethodInfo mi && MethodOverloadDispatcher.IsExtensionMethod(mi) ? 1 : 0;
            int n = Parameters.Length;
            Marshal = new JsMarshalAsAttribute[n];
            Defaults = new object[n];
            Optional = new bool[n];
            for (int i = 0; i < n; ++i)
            {
                Marshal[i] = JsMarshalAsXmlRegistry.ResolveParameterMarshal(Parameters[i], method);
                Optional[i] = Parameters[i].IsOptional;
                if (Optional[i])
                    Defaults[i] = Parameters[i].DefaultValue;
            }
            Params = n > Start && Parameters[n - 1].IsDefined(typeof(ParamArrayAttribute), false);
            if (method is MethodInfo info)
                ReturnMarshal = JsMarshalAsXmlRegistry.ResolveReturnMarshal(info);
        }
        internal static bool IsOut(ParameterInfo p) => p.ParameterType.IsByRef && p.IsOut && !p.IsIn;
        private static Type Element(ParameterInfo p) => p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
        private static bool WholeArray(IntPtr ctx, JsValue value, Type arrayType) => ObjectRegistry.TryGetObject(ctx, value, out object obj) && obj != null && arrayType.IsInstanceOfType(obj);
        internal bool Match(IntPtr ctx, int argc, IntPtr argv, out int[] kinds, out int optional, out int expanded)
        {
            kinds = new int[argc];
            optional = 0;
            expanded = 0;
            int slot = 0;
            for (int i = Start; i < Parameters.Length; ++i)
            {
                ParameterInfo p = Parameters[i];
                if (IsOut(p))
                    continue;
                Type type = Element(p);
                var mas = Marshal[i];
                if (Params && i == Parameters.Length - 1)
                {
                    int available = argc - slot;
                    if (available == 0)
                    {
                        optional++;
                        return true;
                    }
                    if (available == 1 && WholeArray(ctx, ArgReader.Read(argv, slot), type))
                    {
                        kinds[slot] = 0;
                        return true;
                    }
                    expanded = 1;
                    Type element = type.GetElementType();
                    while (slot < argc)
                    {
                        int k = MatchValue(ctx, ArgReader.Read(argv, slot), element, null);
                        if (k < 0)
                            return false;
                        kinds[slot++] = k;
                    }
                    return true;
                }
                if (p.ParameterType.IsByRef && slot < argc && ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, slot), out object held))
                {
                    if (held is OpaqueValue opaque)
                    {
                        if (!OpaqueParameterScope.TryPop(ctx, ArgReader.Read(argv, slot), out _) || opaque.DeclaredType != type)
                            return false;
                        kinds[slot++] = 0;
                        continue;
                    }
                    if (held != null && held.GetType() == type)
                    {
                        kinds[slot++] = 0;
                        continue;
                    }
                }
                int need = mas?.JsMarshalType == JsMarshalType.UnpackedValues ? mas.Members.Length : 1;
                if (Optional[i] && (slot == argc || (need == 1 && JsValueUtil.GetNormTag(ArgReader.Read(argv, slot)) == JsValueUtil.TagUndefined)))
                {
                    optional++;
                    if (slot < argc)
                        slot++;
                    continue;
                }
                if (argc - slot < need)
                    return false;
                if (need > 1 || mas?.JsMarshalType == JsMarshalType.UnpackedValues)
                {
                    for (int m = 0; m < need; ++m)
                    {
                        Type mt = TableMarshal.ResolveMemberType(type, mas.Members[m]);
                        int k = MatchValue(ctx, ArgReader.Read(argv, slot), mt, null);
                        if (k < 0)
                            return false;
                        kinds[slot++] = k;
                    }
                }
                else
                {
                    int k = MatchValue(ctx, ArgReader.Read(argv, slot), type, mas);
                    if (k < 0)
                        return false;
                    kinds[slot++] = k;
                }
            }
            return slot == argc;
        }
        internal static unsafe int MatchValue(IntPtr ctx, JsValue value, Type type, JsMarshalAsAttribute mas)
        {
            Type nullable = Nullable.GetUnderlyingType(type);
            Type target = nullable ?? type;
            int tag = JsValueUtil.GetNormTag(value);
            if (mas?.JsMarshalType == JsMarshalType.BigInt)
            {
                if (tag != JsValueUtil.TagBigInt)
                    return -1;
                string text = ReadText(ctx, value);
                return target == typeof(long) ? (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? 0 : -1) : target == typeof(ulong) && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? 0 : -1;
            }
            if (tag == JsValueUtil.TagBigInt)
                return -1;
            if (tag == JsValueUtil.TagUndefined)
                return nullable != null ? 3 : -1;
            if (tag == JsValueUtil.TagNull)
                return nullable != null || !target.IsValueType ? 3 : -1;
            if (ObjectRegistry.TryGetObject(ctx, value, out object obj))
            {
                if (obj is OpaqueValue opaque)
                {
                    try
                    {
                        if (!OpaqueParameterScope.TryPop(ctx, value, out obj))
                            return -1;
                    }
                    catch { return -1; }
                }
                if (!target.IsPointer && obj is PointerHandle)
                    return -1;
                if (target.IsPointer && obj is PointerHandle pointer)
                    return pointer.PointerType == target || pointer.PointerType == typeof(void*) || target == typeof(void*) ? 0 : -1;
                if (obj == null)
                    return nullable != null || !target.IsValueType ? 3 : -1;
                if (obj.GetType() == target)
                    return 0;
                return target.IsInstanceOfType(obj) ? 4 : -1;
            }
            if (mas?.JsMarshalType == JsMarshalType.Table)
                return tag == JsValueUtil.TagObject && EngineApi.IsArray(ctx, value) == 0 && EngineApi.IsFunction(ctx, value) == 0 ? 0 : -1;
            if (mas?.JsMarshalType == JsMarshalType.Bytes)
                return EngineApi.GetByteArrayData(ctx, value, out _) != IntPtr.Zero ? 0 : -1;
            if (target == typeof(object))
                return tag == JsValueUtil.TagString ? 4 : 5;
            if (typeof(Delegate).IsAssignableFrom(target))
                return EngineApi.IsFunction(ctx, value) != 0 ? 0 : -1;
            if (target.IsArray)
                return EngineApi.IsArray(ctx, value) != 0 ? 0 : -1;
            if (target == typeof(string))
                return mas?.JsMarshalType != JsMarshalType.Object && tag == JsValueUtil.TagString ? 0 : -1;
            if (target == typeof(bool))
                return tag == JsValueUtil.TagBool ? 0 : -1;
            if (target.IsPointer)
                return tag == JsValueUtil.TagObject ? 0 : -1;
            bool en = target.IsEnum;
            if (en)
                target = Enum.GetUnderlyingType(target);
            if (tag != JsValueUtil.TagInt && tag != JsValueUtil.TagFloat64)
                return -1;
            double d = tag == JsValueUtil.TagInt ? unchecked((int)JsValueUtil.GetUInt64(value)) : JsValueUtil.GetFloat64(value);
            if (target == typeof(double))
                return en ? 2 : Math.Floor(d) == d ? 1 : 0;
            if (target == typeof(float))
                return double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) <= float.MaxValue ? (en ? 2 : Math.Floor(d) == d ? 1 : 0) : -1;
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d)
                return -1;
            bool valid = target == typeof(sbyte) ? d >= sbyte.MinValue && d <= sbyte.MaxValue : target == typeof(byte) ? d >= 0 && d <= byte.MaxValue : target == typeof(short) ? d >= short.MinValue && d <= short.MaxValue : target == typeof(ushort) || target == typeof(char) ? d >= 0 && d <= ushort.MaxValue : target == typeof(int) ? d >= int.MinValue && d <= int.MaxValue : target == typeof(uint) ? d >= 0 && d <= uint.MaxValue : target == typeof(long) || target == typeof(IntPtr) ? d >= -9223372036854775808d && d < 9223372036854775808d : target == typeof(ulong) || target == typeof(UIntPtr) ? d >= 0 && d < 18446744073709551616d : false;
            return !valid ? -1 : en ? 2 : target == typeof(long) || target == typeof(ulong) || target == typeof(IntPtr) || target == typeof(UIntPtr) ? 1 : 0;
        }
        internal static string ReadText(IntPtr ctx, JsValue value)
        {
            IntPtr text = EngineApi.ToCStringLen2(ctx, out UIntPtr length, value, 0);
            if (text == IntPtr.Zero)
                throw new JsScriptException("zents: value cannot be converted to text");
            try
            {
                return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(text, (int)length);
            }
            finally { EngineApi.FreeCString(ctx, text); }
        }
        internal object[] Pop(IntPtr ctx, object receiver, int argc, IntPtr argv)
        {
            if (!Match(ctx, argc, argv, out _, out _, out _))
                throw new JsScriptException("zents: argument mismatch for " + Method.Name);
            var args = new object[Parameters.Length];
            if (Start == 1)
                args[0] = receiver;
            int slot = 0;
            for (int i = Start; i < Parameters.Length; ++i)
            {
                ParameterInfo p = Parameters[i];
                Type type = Element(p);
                var mas = Marshal[i];
                if (IsOut(p))
                {
                    args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
                    continue;
                }
                if (Params && i == Parameters.Length - 1)
                {
                    if (argc - slot == 1 && WholeArray(ctx, ArgReader.Read(argv, slot), type))
                    {
                        ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, slot), out args[i]);
                        slot++;
                    }
                    else
                    {
                        Type element = type.GetElementType();
                        Array a = Array.CreateInstance(element, argc - slot);
                        for (int k = 0; slot < argc; ++k, ++slot)
                            a.SetValue(TypedMarshal.Pop(ctx, ArgReader.Read(argv, slot), element), k);
                        args[i] = a;
                    }
                    continue;
                }
                if (Optional[i] && (slot == argc || JsValueUtil.GetNormTag(ArgReader.Read(argv, slot)) == JsValueUtil.TagUndefined))
                {
                    args[i] = Defaults[i];
                    if (slot < argc)
                        slot++;
                    continue;
                }
                if (mas?.JsMarshalType == JsMarshalType.UnpackedValues)
                {
                    args[i] = TableMarshal.PopUnpacked(ctx, argv, slot, argc, type, mas);
                    slot += mas.Members.Length;
                }
                else
                {
                    JsValue value = ArgReader.Read(argv, slot++);
                    if (p.ParameterType.IsByRef && ObjectRegistry.TryGetObject(ctx, value, out object held) && held != null && held.GetType() == type)
                        args[i] = held;
                    else
                        args[i] = TypedMarshal.Pop(ctx, value, type, mas);
                }
            }
            return args;
        }
        internal JsValue PushResult(IntPtr ctx, object result, object[] args)
        {
            var info = Method as MethodInfo;
            bool returns = info != null && info.ReturnType != typeof(void);
            bool hasOut = false;
            foreach (var p in Parameters)
                if (IsOut(p))
                {
                    hasOut = true;
                    break;
                }
            if (!hasOut)
                return returns ? TypedMarshal.Push(ctx, result, info.ReturnType, ReturnMarshal) : JsValueUtil.Undefined;
            JsValue tuple = EngineApi.NewArray(ctx);
            uint index = 0;
            try
            {
                if (returns)
                    EngineApi.SetPropertyUint32(ctx, tuple, index++, TypedMarshal.Push(ctx, result, info.ReturnType, ReturnMarshal));
                for (int i = Start; i < Parameters.Length; ++i)
                    if (IsOut(Parameters[i]))
                        EngineApi.SetPropertyUint32(ctx, tuple, index++, TypedMarshal.Push(ctx, args[i], Element(Parameters[i]), Marshal[i]));
                return tuple;
            }
            catch { JsValueUtil.Free(ctx, tuple); throw; }
        }
        internal static MethodBase Select(IntPtr ctx, MethodBase[] methods, int argc, IntPtr argv)
        {
            var matches = new List<(MethodBase method, int[] kinds, int optional, int expanded, int param)>();
            foreach (MethodBase method in methods)
            {
                if (method.ContainsGenericParameters)
                    continue;
                var plan = For(method);
                if (plan.Match(ctx, argc, argv, out int[] kinds, out int optional, out int expanded))
                    matches.Add((method, kinds, optional, expanded, plan.Params ? 1 : 0));
            }
            MethodBase winner = null;
            foreach (var a in matches)
            {
                bool beaten = false;
                foreach (var b in matches)
                {
                    if (a.method == b.method)
                        continue;
                    bool better = false, worse = false;
                    for (int i = 0; i < a.kinds.Length; ++i)
                    {
                        if (b.kinds[i] < a.kinds[i])
                            better = true;
                        else if (b.kinds[i] > a.kinds[i])
                            worse = true;
                    }
                    if (worse)
                        continue;
                    if (better || (b.optional < a.optional) || (b.optional == a.optional && (b.expanded < a.expanded || (b.expanded == a.expanded && b.param < a.param))))
                    {
                        beaten = true;
                        break;
                    }
                }
                if (!beaten)
                {
                    if (winner != null)
                        throw new JsScriptException("zents: ambiguous overload");
                    winner = a.method;
                }
            }
            return winner;
        }
    }
}
