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
using System.Linq;
using System.Reflection;
using ZTS.Jvm;
using ZTS.Marshaling;
using ZTS.Mt;
using ZTS.Utils;

namespace ZTS.Emit
{
    /// <summary>
    /// Builds a callable type object (STO) + instance prototype (IEO) and keeps them alive
    /// via <see cref="TypeRegistry"/> strong JSValue holds.
    /// </summary>
    internal static class MetaBinding
    {
        public static TypeBinding BuildTypeObject(JsEnv env, Type type)
        {
            IntPtr ctx = env.Context;
            var binding = new TypeBinding { Type = type };

            if (type.IsGenericTypeDefinition)
            {
                JSValue stub = QuickJsDll.JS_NewObject(ctx);
                QuickJsDll.JS_SetPropertyStr(ctx, stub, "__zts_type_name", QuickJsDll.NewString(ctx, type.FullName ?? type.Name));
                int typeSlot = ObjectRegistry.Register(type, typeof(Type));
                QuickJsDll.JS_SetPropertyStr(ctx, stub, "__zts_id", JsValueUtil.NewInt32(typeSlot));
                binding.TypeObject = stub;
                binding.TypeObjectRaw = JsValueUtil.Dup(stub);
                binding.InstanceProto = QuickJsDll.JS_NewObject(ctx);
                binding.HasJsValues = true;
                return binding;
            }

            Type nullableUnderlying = Nullable.GetUnderlyingType(type);
            if (nullableUnderlying != null)
            {
                return BuildNullableTypeObject(env, type, nullableUnderlying);
            }

            JSValue proto = QuickJsDll.JS_NewObject(ctx);
            bool isStruct = type.IsValueType && !type.IsEnum && type != typeof(void);
            JSValue byValProto = default;
            JSValue ctor = ConstructorEmitter.Emit(env, type, proto);
            JSValue typeObjectRaw = JsValueUtil.Dup(ctor);

            // Tag type object with Type identity for zts.typeof / cast.
            int ctorTypeSlot = ObjectRegistry.Register(type, typeof(Type));
            QuickJsDll.JS_SetPropertyStr(ctx, ctor, "__zts_id", JsValueUtil.NewInt32(ctorTypeSlot));
            QuickJsDll.JS_SetPropertyStr(ctx, ctor, "__zts_type_name", QuickJsDll.NewString(ctx, type.FullName ?? type.Name));

            QuickJsDll.JS_SetConstructorBit(ctx, ctor, 1);
            QuickJsDll.JS_SetPropertyStr(ctx, ctor, "prototype", JsValueUtil.Dup(proto));

            var seenMethods = new HashSet<string>(StringComparer.Ordinal);
            var seenFields = new HashSet<string>(StringComparer.Ordinal);
            var seenProps = new HashSet<string>(StringComparer.Ordinal);

            foreach (Type t in EnumerateTypeChain(type))
            {
                BindMethods(env, binding, type, t, ctor, proto, seenMethods, isByVal: false);
                BindFieldsAsMethods(env, binding, t, ctor, proto, seenFields, isByVal: false);
                BindPropertiesAsMethods(env, binding, t, ctor, proto, seenProps, isByVal: false);
            }

            foreach (MethodInfo ext in ExtensionMethodUtil.GetExtensionMethods(type))
            {
                string key = ResolveMemberName(ext);
                BindOrMergeExtension(env, binding, type, proto, seenMethods, key, ext, isByVal: false);
            }

            if (isStruct)
            {
                byValProto = QuickJsDll.JS_NewObject(ctx);
                var seenByValMethods = new HashSet<string>(StringComparer.Ordinal);
                var seenByValFields = new HashSet<string>(StringComparer.Ordinal);
                var seenByValProps = new HashSet<string>(StringComparer.Ordinal);

                foreach (Type t in EnumerateTypeChain(type))
                {
                    BindMethods(env, binding, type, t, ctor, byValProto, seenByValMethods, isByVal: true, instanceOnly: true);
                    BindFieldsAsMethods(env, binding, t, ctor, byValProto, seenByValFields, isByVal: true, instanceOnly: true);
                    BindPropertiesAsMethods(env, binding, t, ctor, byValProto, seenByValProps, isByVal: true, instanceOnly: true);
                }

                foreach (MethodInfo ext in ExtensionMethodUtil.GetExtensionMethods(type))
                {
                    string key = ResolveMemberName(ext);
                    BindOrMergeExtension(env, binding, type, byValProto, seenByValMethods, key, ext, isByVal: true);
                }

                QuickJsDll.JS_SetPropertyStr(ctx, ctor, "__struct", JsValueUtil.NewBool(true));
                QuickJsDll.JS_SetPropertyStr(ctx, ctor, "__byvalInstanceProto", JsValueUtil.Dup(byValProto));
                QuickJsDll.JS_SetPropertyStr(ctx, ctor, "__byobjInstanceProto", JsValueUtil.Dup(proto));
                binding.ByValInstanceProto = byValProto;
                binding.ByObjInstanceProto = proto;
            }

            // Enum numeric literals on type object
            if (type.IsEnum)
            {
                foreach (string name in Enum.GetNames(type))
                {
                    binding.MemberKeys.Add(name);
                    object value = Enum.Parse(type, name);
                    QuickJsDll.JS_SetPropertyStr(ctx, ctor, name, JsValueUtil.NewInt32(Convert.ToInt32(value)));
                }
            }

            // ValueType _default
            if (type.IsValueType && !type.IsEnum && type != typeof(void))
            {
                binding.MemberKeys.Add("_default");
                JSValue defaultFn = JsCallbackGate.NewCFunction(
                    ctx,
                    (c, thisVal, argc, argv) =>
                    {
                        try
                        {
                            object def = Activator.CreateInstance(type);
                            return StructMarshal.PushByVal(c, def);
                        }
                        catch (Exception ex)
                        {
                            return JsCallbackGate.ReturnErrorSentinel(c, ex.Message);
                        }
                    },
                    "_default",
                    0);
                QuickJsDll.JS_SetPropertyStr(ctx, ctor, "_default", defaultFn);
            }

            // Strict miss on STO (type object). Instance miss is applied in PushByObj.
            // Keep TypeObjectRaw for host APIs (register_method) that must mutate the STO tables.
            if (TryWrapMiss(env, ctor, out JSValue wrappedCtor))
            {
                JsValueUtil.Free(ctx, ctor);
                ctor = wrappedCtor;
            }

            binding.TypeObject = ctor;
            binding.TypeObjectRaw = typeObjectRaw;
            binding.InstanceProto = proto;
            binding.HasJsValues = true;
            return binding;
        }

        private static TypeBinding BuildNullableTypeObject(JsEnv env, Type nullableType, Type underlyingType)
        {
            IntPtr ctx = env.Context;
            var binding = new TypeBinding { Type = nullableType, IsNullable = true };

            JSValue ctor = NullableConstructorEmitter.Emit(env, nullableType, underlyingType);
            JSValue typeObjectRaw = JsValueUtil.Dup(ctor);

            int ctorTypeSlot = ObjectRegistry.Register(nullableType, typeof(Type));
            QuickJsDll.JS_SetPropertyStr(ctx, ctor, "__zts_id", JsValueUtil.NewInt32(ctorTypeSlot));
            QuickJsDll.JS_SetPropertyStr(ctx, ctor, "__zts_type_name", QuickJsDll.NewString(ctx, nullableType.FullName ?? nullableType.Name));
            QuickJsDll.JS_SetPropertyStr(ctx, ctor, "__nullable", JsValueUtil.NewBool(true));

            QuickJsDll.JS_SetConstructorBit(ctx, ctor, 1);

            if (TryWrapMiss(env, ctor, out JSValue wrappedCtor))
            {
                JsValueUtil.Free(ctx, ctor);
                ctor = wrappedCtor;
            }

            binding.TypeObject = ctor;
            binding.TypeObjectRaw = typeObjectRaw;
            binding.HasJsValues = true;
            return binding;
        }

        private static bool TryWrapMiss(JsEnv env, JSValue target, out JSValue wrapped)
        {
            return TryWrapMissPublic(env, target, out wrapped);
        }

        /// <summary>Used by <see cref="Marshaling.TypedMarshal"/> to wrap instance handles.</summary>
        internal static bool TryWrapMissPublic(JsEnv env, JSValue target, out JSValue wrapped) =>
            TryCallGlobalWrap(env, "__zts_wrap_miss", target, out wrapped);

        /// <summary>Make CLR Delegate exotic callable as <c>d(...)</c> (spec marshal/09-FUNCTION §3.2).</summary>
        internal static bool TryWrapDelegateCallPublic(JsEnv env, JSValue target, out JSValue wrapped) =>
            TryCallGlobalWrap(env, "__zts_wrap_delegate_call", target, out wrapped);

        private static bool TryCallGlobalWrap(JsEnv env, string globalFnName, JSValue target, out JSValue wrapped)
        {
            wrapped = default;
            IntPtr ctx = env.Context;
            JSValue global = QuickJsDll.JS_GetGlobalObject(ctx);
            try
            {
                JSValue wrap = QuickJsDll.JS_GetPropertyStr(ctx, global, globalFnName);
                try
                {
                    if (QuickJsDll.JS_IsFunction(ctx, wrap) == 0)
                    {
                        return false;
                    }

                    unsafe
                    {
                        JSValue arg = target;
                        JSValue result = QuickJsDll.JS_Call(ctx, wrap, JsValueUtil.Undefined, 1, (IntPtr)(&arg));
                        if (JsValueUtil.IsException(result))
                        {
                            return false;
                        }

                        // Proxy / function / object are all TagObject in QuickJS.
                        if (JsValueUtil.GetNormTag(result) != JsValueUtil.TagObject)
                        {
                            JsValueUtil.Free(ctx, result);
                            return false;
                        }

                        wrapped = result;
                        return true;
                    }
                }
                finally
                {
                    JsValueUtil.Free(ctx, wrap);
                }
            }
            finally
            {
                JsValueUtil.Free(ctx, global);
            }
        }

        private static IEnumerable<Type> EnumerateTypeChain(Type type)
        {
            var list = new List<Type>();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                list.Add(t);
            }

            return list;
        }

        private static void BindOrMergeExtension(
            JsEnv env,
            TypeBinding binding,
            Type ownerType,
            JSValue proto,
            HashSet<string> seenMethods,
            string key,
            MethodInfo ext,
            bool isByVal)
        {
            IntPtr ctx = env.Context;
            if (seenMethods.Add(key))
            {
                if (!isByVal)
                {
                    binding.MemberKeys.Add(key);
                }

                JSValue fn = MethodEmitter.EmitMethod(env, ext, ownerType, isByVal);
                QuickJsDll.JS_SetPropertyStr(ctx, proto, key, fn);
                return;
            }

            // Spec: extension + instance same JS name → one overload group (merge-compete).
            var merged = new List<MethodInfo>();
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (Type t in EnumerateTypeChain(ownerType))
            {
                foreach (MethodInfo method in t.GetMethods(flags))
                {
                    if (method.IsSpecialName &&
                        !method.Name.StartsWith("add_", StringComparison.Ordinal) &&
                        !method.Name.StartsWith("remove_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (method.Name == "Finalize")
                    {
                        continue;
                    }

                    if (ResolveMemberName(method) == key)
                    {
                        merged.Add(method);
                    }
                }
            }

            foreach (MethodInfo other in ExtensionMethodUtil.GetExtensionMethods(ownerType))
            {
                if (ResolveMemberName(other) == key && !merged.Contains(other))
                {
                    merged.Add(other);
                }
            }

            if (merged.Count == 0)
            {
                merged.Add(ext);
            }

            JSValue group = merged.Count == 1
                ? MethodEmitter.EmitMethod(env, merged[0], ownerType, isByVal)
                : MethodEmitter.EmitOverloadGroup(env, key, merged, ownerType, isByVal);
            QuickJsDll.JS_SetPropertyStr(ctx, proto, key, group);
        }

        private static void BindMethods(
            JsEnv env,
            TypeBinding binding,
            Type ownerType,
            Type t,
            JSValue ctor,
            JSValue proto,
            HashSet<string> seen,
            bool isByVal,
            bool instanceOnly = false)
        {
            IntPtr ctx = env.Context;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            MethodInfo[] methods = t.GetMethods(flags);

            // Group overloads by resolved name
            var groups = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
            foreach (MethodInfo method in methods)
            {
                if (method.IsSpecialName)
                {
                    // keep add_/remove_ for events; skip property accessors
                    if (!method.Name.StartsWith("add_", StringComparison.Ordinal) &&
                        !method.Name.StartsWith("remove_", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                if (method.Name == "Finalize")
                {
                    continue;
                }

                string key = ResolveMemberName(method);
                if (!groups.TryGetValue(key, out List<MethodInfo> list))
                {
                    list = new List<MethodInfo>();
                    groups[key] = list;
                }

                list.Add(method);
            }

            foreach (KeyValuePair<string, List<MethodInfo>> kv in groups)
            {
                bool isStatic = kv.Value.All(m => m.IsStatic);
                if (instanceOnly && isStatic)
                {
                    continue;
                }

                if (!seen.Add(kv.Key))
                {
                    continue;
                }

                if (!isByVal)
                {
                    binding.MemberKeys.Add(kv.Key);
                }

                JSValue fn = kv.Value.Count == 1
                    ? MethodEmitter.EmitMethod(env, kv.Value[0], ownerType, isByVal)
                    : MethodEmitter.EmitOverloadGroup(env, kv.Key, kv.Value, ownerType, isByVal);

                JSValue target = isStatic ? ctor : proto;
                QuickJsDll.JS_SetPropertyStr(ctx, target, kv.Key, fn);

                // Docs/spec/04-METHOD-OVERLOAD §3.7: conflict → also bind full-signature direct keys.
                if (kv.Value.Count >= 2)
                {
                    foreach (MethodInfo method in kv.Value)
                    {
                        string sigKey = FormatFullSignatureKey(method);
                        if (!seen.Add(sigKey))
                        {
                            continue;
                        }

                        if (!isByVal)
                        {
                            binding.MemberKeys.Add(sigKey);
                        }

                        QuickJsDll.JS_SetPropertyStr(
                            ctx, target, sigKey, MethodEmitter.EmitMethod(env, method, ownerType, isByVal));
                    }
                }
            }
        }

        /// <summary>
        /// <c>MethodName(Type0.FullName,Type1.FullName,…)</c> — no return type (spec §3.7 / §4.2).
        /// </summary>
        internal static string FormatFullSignatureKey(MethodInfo method)
        {
            ParameterInfo[] ps = method.GetParameters();
            if (ps.Length == 0)
            {
                return method.Name + "()";
            }

            var sb = new System.Text.StringBuilder(method.Name.Length + 16 + ps.Length * 24);
            sb.Append(method.Name);
            sb.Append('(');
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                Type pt = ps[i].ParameterType;
                sb.Append(pt.FullName ?? pt.Name);
            }

            sb.Append(')');
            return sb.ToString();
        }

        private static void BindFieldsAsMethods(
            JsEnv env, TypeBinding binding, Type t, JSValue ctor, JSValue proto, HashSet<string> seen,
            bool isByVal, bool instanceOnly = false)
        {
            IntPtr ctx = env.Context;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in t.GetFields(flags))
            {
                if (instanceOnly && field.IsStatic)
                {
                    continue;
                }

                if (!seen.Add(field.Name))
                {
                    continue;
                }

                if (!isByVal)
                {
                    binding.MemberKeys.Add(field.Name);
                }

                JSValue target = field.IsStatic ? ctor : proto;
                if (field.IsLiteral)
                {
                    if (!isByVal)
                    {
                        object lit = field.GetRawConstantValue();
                        QuickJsDll.JS_SetPropertyStr(ctx, ctor, field.Name, Marshaling.PrimitiveMarshal.Push(ctx, lit));
                    }

                    continue;
                }

                JSValue getter = FieldEmitter.EmitGetter(env, field);
                JSValue setter = field.IsInitOnly || field.IsLiteral
                    ? JsCallbackGate.NewCFunction(
                        ctx,
                        (c, thisVal, argc, argv) => JsCallbackGate.ReturnErrorSentinel(c, $"zts: field '{field.Name}' is read-only."),
                        "set_" + field.Name,
                        1)
                    : FieldEmitter.EmitSetter(env, field);
                DefineGetSet(ctx, target, field.Name, getter, setter);
            }
        }

        private static void BindPropertiesAsMethods(
            JsEnv env, TypeBinding binding, Type t, JSValue ctor, JSValue proto, HashSet<string> seen,
            bool isByVal, bool instanceOnly = false)
        {
            IntPtr ctx = env.Context;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (PropertyInfo property in t.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    if (property.CanRead)
                    {
                        MethodInfo get = property.GetGetMethod();
                        if (get != null && seen.Add("get_Item"))
                        {
                            if (!isByVal)
                            {
                                binding.MemberKeys.Add("get_Item");
                            }

                            if (!(instanceOnly && get.IsStatic))
                            {
                                QuickJsDll.JS_SetPropertyStr(
                                    ctx, get.IsStatic ? ctor : proto, "get_Item", MethodEmitter.EmitMethod(env, get, t, isByVal));
                            }
                        }
                    }

                    if (property.CanWrite)
                    {
                        MethodInfo set = property.GetSetMethod();
                        if (set != null && seen.Add("set_Item"))
                        {
                            if (!isByVal)
                            {
                                binding.MemberKeys.Add("set_Item");
                            }

                            if (!(instanceOnly && set.IsStatic))
                            {
                                QuickJsDll.JS_SetPropertyStr(
                                    ctx, set.IsStatic ? ctor : proto, "set_Item", MethodEmitter.EmitMethod(env, set, t, isByVal));
                            }
                        }
                    }

                    continue;
                }

                if (!seen.Add(property.Name))
                {
                    continue;
                }

                if (!isByVal)
                {
                    binding.MemberKeys.Add(property.Name);
                }

                bool isStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic == true;
                if (instanceOnly && isStatic)
                {
                    continue;
                }

                JSValue target = isStatic ? ctor : proto;
                JSValue getter = property.CanRead
                    ? PropertyEmitter.EmitGetter(env, property)
                    : JsCallbackGate.NewCFunction(
                        ctx,
                        (c, thisVal, argc, argv) => JsCallbackGate.ReturnErrorSentinel(c, $"zts: property '{property.Name}' has no getter."),
                        "get_" + property.Name,
                        0);
                JSValue setter = property.CanWrite
                    ? PropertyEmitter.EmitSetter(env, property)
                    : JsCallbackGate.NewCFunction(
                        ctx,
                        (c, thisVal, argc, argv) => JsCallbackGate.ReturnErrorSentinel(c, $"zts: property '{property.Name}' is read-only."),
                        "set_" + property.Name,
                        1);
                DefineGetSet(ctx, target, property.Name, getter, setter);
            }
        }

        private const int PropConfigurable = 1 << 0;
        private const int PropEnumerable = 1 << 2;

        private static void DefineGetSet(IntPtr ctx, JSValue target, string name, JSValue getter, JSValue setter)
        {
            uint atom = QuickJsDll.JS_NewAtom(ctx, name);
            QuickJsDll.JS_DefinePropertyGetSet(
                ctx, target, atom, getter, setter, PropConfigurable | PropEnumerable);
            QuickJsDll.JS_FreeAtom(ctx, atom);
        }

        internal static string ResolveMemberName(MemberInfo member)
        {
            JsAliasAttribute alias = member.GetCustomAttribute<JsAliasAttribute>();
            if (alias != null && !string.IsNullOrEmpty(alias.Alias))
            {
                return alias.Alias;
            }

            if (member is MethodInfo method)
            {
                if (JsAliasXmlRegistry.TryGetAlias(method, out string xmlAlias)
                    && !string.IsNullOrEmpty(xmlAlias))
                {
                    return xmlAlias;
                }
            }

            return member.Name;
        }
    }
}
