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
using System.Reflection;
using System.Collections.Generic;
using System.Linq.Expressions;
using ZenTS.Jvm;

namespace ZenTS.Marshaling
{
    internal static class TableMarshal
    {
        private static readonly Dictionary<(Type type, string member), MemberAccessor> Accessors = new Dictionary<(Type, string), MemberAccessor>();
        internal static void ResetCache()
        {
            lock (Accessors)
                Accessors.Clear();
        }

        public static int GetJsArgSlotCount(ParameterInfo param)
        {
            if (ZenTS.Emit.JsCallPlan.IsOut(param))
                return 0;
            JsMarshalAsAttribute mas = JsMarshalAsXmlRegistry.ResolveParameterMarshal(param);
            if (mas != null &&
                mas.JsMarshalType == JsMarshalType.UnpackedValues &&
                mas.Members != null &&
                mas.Members.Length > 0)
            {
                return mas.Members.Length;
            }

            return 1;
        }

        public static object Pop(IntPtr ctx, JsValue jsValue, Type expectedType, JsMarshalAsAttribute marshalAs)
        {
            if (marshalAs == null)
            {
                return null;
            }

            if (marshalAs.JsMarshalType == JsMarshalType.Table)
            {
                return PopTable(ctx, jsValue, expectedType, marshalAs.Members);
            }

            return null;
        }

        public static JsValue Push(IntPtr ctx, object value, Type declaredType, JsMarshalAsAttribute marshalAs)
        {
            if (marshalAs == null || marshalAs.Members == null || marshalAs.Members.Length == 0)
            {
                throw new JsScriptException("zents: Table/UnpackedValues push requires Members.");
            }

            if (value == null)
            {
                return JsValueUtil.Null;
            }

            Type underlying = Nullable.GetUnderlyingType(declaredType) ?? declaredType ?? value.GetType();
            if (marshalAs.JsMarshalType == JsMarshalType.Table)
            {
                return PushTable(ctx, value, underlying, marshalAs.Members);
            }

            if (marshalAs.JsMarshalType == JsMarshalType.UnpackedValues)
            {
                return PushUnpacked(ctx, value, underlying, marshalAs.Members);
            }

            throw new JsScriptException($"zents: unsupported TableMarshal push kind {marshalAs.JsMarshalType}.");
        }

        private static JsValue PushTable(IntPtr ctx, object value, Type underlying, string[] members)
        {
            JsValue obj = EngineApi.NewObject(ctx);
            try
            {
                for (int i = 0; i < members.Length; i++)
                {
                    ParseMemberSpec(members[i], out string memberName, out _);
                    MemberAccessor accessor = ResolveMember(underlying, memberName);
                    JsValue jsMember = accessor.Push(ctx, value);
                    EngineApi.SetPropertyStr(ctx, obj, memberName, jsMember);
                }

                return obj;
            }
            catch { JsValueUtil.Free(ctx, obj); throw; }
        }

        private static JsValue PushUnpacked(IntPtr ctx, object value, Type underlying, string[] members)
        {
            JsValue arr = EngineApi.NewArray(ctx);
            try
            {
                for (int i = 0; i < members.Length; i++)
                {
                    string spec = members[i];
                    if (spec != null && spec.EndsWith("?", StringComparison.Ordinal))
                    {
                        throw new JsScriptException("zents: UnpackedValues does not support optional member '?'.");
                    }

                    MemberAccessor accessor = ResolveMember(underlying, spec);
                    JsValue jsMember = accessor.Push(ctx, value);
                    EngineApi.SetPropertyUint32(ctx, arr, (uint)i, jsMember);
                }

                return arr;
            }
            catch { JsValueUtil.Free(ctx, arr); throw; }
        }

        internal static void PushUnpackedInto(IntPtr ctx, object value, Type type, JsMarshalAsAttribute marshal, JsValue[] destination, int offset)
        {
            if (value == null)
                throw new JsScriptException("zents: null cannot be expanded into UnpackedValues arguments");
            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            for (int i = 0; i < marshal.Members.Length; ++i)
            {
                ParseMemberSpec(marshal.Members[i], out string name, out bool optional);
                if (optional)
                    throw new JsScriptException("zents: UnpackedValues does not support optional member '?'.");
                destination[offset + i] = ResolveMember(underlying, name).Push(ctx, value);
            }
        }

        public static object PopUnpacked(
            IntPtr ctx,
            IntPtr argvPtr,
            int jsStartIndex,
            int argc,
            Type expectedType,
            JsMarshalAsAttribute marshalAs)
        {
            if (marshalAs == null ||
                marshalAs.JsMarshalType != JsMarshalType.UnpackedValues ||
                marshalAs.Members == null ||
                marshalAs.Members.Length == 0)
            {
                throw new JsScriptException("zents: UnpackedValues requires Members.");
            }

            string[] members = marshalAs.Members;
            if (jsStartIndex + members.Length > argc)
            {
                throw new JsScriptException(
                    $"zents: UnpackedValues requires {members.Length} argument(s) for {expectedType.FullName}.");
            }

            Type underlying = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
            if (!underlying.IsValueType || underlying.IsPrimitive || underlying.IsEnum)
            {
                throw new JsScriptException($"zents: UnpackedValues requires struct type, not {expectedType.FullName}.");
            }

            object boxed = Activator.CreateInstance(underlying);
            for (int i = 0; i < members.Length; i++)
            {
                string spec = members[i];
                if (spec != null && spec.EndsWith("?", StringComparison.Ordinal))
                {
                    throw new JsScriptException("zents: UnpackedValues does not support optional member '?'.");
                }

                string memberName = spec ?? throw new JsScriptException("zents: UnpackedValues member name is null.");
                MemberAccessor accessor = ResolveMember(underlying, memberName);
                JsValue jsArg = ArgReader.Read(argvPtr, jsStartIndex + i);
                object value = TypedMarshal.Pop(ctx, jsArg, accessor.MemberType, accessor.Marshal);
                accessor.Set(boxed, value);
            }

            if (Nullable.GetUnderlyingType(expectedType) != null)
            {
                return boxed;
            }

            return boxed;
        }

        internal static object PopPacked(IntPtr ctx, JsValue value, Type type, JsMarshalAsAttribute marshal)
        {
            if (EngineApi.IsArray(ctx, value) == 0)
                throw new JsScriptException("zents: UnpackedValues result requires an Array");
            JsValue length = EngineApi.GetPropertyStr(ctx, value, "length");
            try
            {
                if (Convert.ToInt32(PrimitiveMarshal.Pop(ctx, length)) != marshal.Members.Length)
                    throw new JsScriptException("zents: UnpackedValues result length mismatch");
            }
            finally { JsValueUtil.Free(ctx, length); }
            var args = new JsValue[marshal.Members.Length];
            try
            {
                for (int i = 0; i < args.Length; ++i)
                    args[i] = EngineApi.GetPropertyUint32(ctx, value, (uint)i);
                unsafe
                {
                    fixed (JsValue* ptr = args)
                        return PopUnpacked(ctx, (IntPtr)ptr, 0, args.Length, type, marshal);
                }
            }
            finally { foreach (var arg in args) JsValueUtil.Free(ctx, arg); }
        }

        public static Type ResolveMemberType(Type structType, string memberSpec)
        {
            ParseMemberSpec(memberSpec, out string memberName, out _);
            return ResolveMember(structType, memberName).MemberType;
        }

        private static object PopTable(IntPtr ctx, JsValue jsValue, Type expectedType, string[] members)
        {
            if (members == null || members.Length == 0)
            {
                throw new JsScriptException("zents: Table marshal requires Members.");
            }

            Type nullableUnderlying = Nullable.GetUnderlyingType(expectedType);
            bool isNullable = nullableUnderlying != null;
            Type underlying = nullableUnderlying ?? expectedType;

            if (!underlying.IsValueType || underlying.IsPrimitive || underlying.IsEnum)
            {
                throw new JsScriptException($"zents: Table marshal requires struct type, not {expectedType.FullName}.");
            }

            int tag = JsValueUtil.GetNormTag(jsValue);
            if (tag == JsValueUtil.TagUndefined || tag == JsValueUtil.TagNull)
            {
                if (isNullable)
                {
                    return null;
                }

                throw new JsScriptException($"zents: null is not assignable to {expectedType.FullName}.");
            }

            if (tag != JsValueUtil.TagObject)
            {
                throw new JsScriptException($"zents: Table marshal requires plain object for {expectedType.FullName}.");
            }

            RejectClrExotic(ctx, jsValue, expectedType);

            object boxed = Activator.CreateInstance(underlying);
            for (int i = 0; i < members.Length; i++)
            {
                ParseMemberSpec(members[i], out string memberName, out bool optional);
                MemberAccessor accessor = ResolveMember(underlying, memberName);
                int own = EngineApi.HasOwnProperty(ctx, jsValue, memberName);
                if (own != 1)
                {
                    if (optional && own == 0)
                        continue;
                    throw new JsScriptException("zents: Table missing member " + memberName);
                }
                JsValue propVal = EngineApi.GetPropertyStr(ctx, jsValue, memberName);
                try
                {

                    object value = TypedMarshal.Pop(ctx, propVal, accessor.MemberType, accessor.Marshal);
                    accessor.Set(boxed, value);
                }
                finally
                {
                    JsValueUtil.Free(ctx, propVal);
                }
            }

            return boxed;
        }

        private static void RejectClrExotic(IntPtr ctx, JsValue jsValue, Type expectedType)
        {
            JsValue ptrFlag = EngineApi.GetPropertyStr(ctx, jsValue, "__zents_pointer");
            try
            {
                if (JsValueUtil.GetNormTag(ptrFlag) == JsValueUtil.TagBool && JsValueUtil.GetUInt64(ptrFlag) != 0)
                {
                    throw new JsScriptException(
                        $"zents: Table marshal requires plain object for {expectedType.FullName}, not Pointer handle.");
                }
            }
            finally
            {
                JsValueUtil.Free(ctx, ptrFlag);
            }

            JsValue idVal = EngineApi.GetPropertyStr(ctx, jsValue, "__zents_id");
            try
            {
                if (JsValueUtil.GetNormTag(idVal) == JsValueUtil.TagInt)
                {
                    throw new JsScriptException(
                        $"zents: Table marshal requires plain object for {expectedType.FullName}, not CLR handle.");
                }
            }
            finally
            {
                JsValueUtil.Free(ctx, idVal);
            }
        }

        private static void ParseMemberSpec(string spec, out string memberName, out bool optional)
        {
            if (string.IsNullOrEmpty(spec))
            {
                throw new JsScriptException("zents: Table/UnpackedValues member name is empty.");
            }

            if (spec.EndsWith("?", StringComparison.Ordinal))
            {
                memberName = spec.Substring(0, spec.Length - 1);
                optional = true;
                return;
            }

            memberName = spec;
            optional = false;
        }

        private static MemberAccessor ResolveMember(Type structType, string memberName)
        {
            lock (Accessors)
            {
                var key = (structType, memberName);
                if (!Accessors.TryGetValue(key, out MemberAccessor accessor))
                    Accessors[key] = accessor = BuildMember(structType, memberName);
                return accessor;
            }
        }

        private static MemberAccessor BuildMember(Type structType, string memberName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            FieldInfo field = structType.GetField(memberName, flags);
            if (field != null)
            {
                return new MemberAccessor(field.FieldType, field.SetValue, field.GetValue, JsMarshalAsXmlRegistry.ResolveFieldMarshal(field), field);
            }

            PropertyInfo prop = structType.GetProperty(memberName, flags);
            if (prop != null && prop.CanRead)
            {
                Action<object, object> setter = prop.CanWrite ? prop.SetValue : null;
                return new MemberAccessor(prop.PropertyType, setter, prop.GetValue, JsMarshalAsXmlRegistry.ResolvePropertyMarshal(prop), prop);
            }

            throw new JsScriptException(
                $"zents: struct {structType.FullName} has no public field or property '{memberName}'.");
        }

        private readonly struct MemberAccessor
        {
            public Type MemberType
            {
                get;
            }
            public JsMarshalAsAttribute Marshal
            {
                get;
            }
            private readonly Action<object, object> _set;
            private readonly Func<object, object> _get;
            private readonly Func<IntPtr, object, JsValue> _push;

            public MemberAccessor(Type memberType, Action<object, object> set, Func<object, object> get, JsMarshalAsAttribute marshal, MemberInfo member)
            {
                MemberType = memberType;
                Marshal = marshal;
                _set = set;
                _get = get;
                _push = marshal == null ? CompilePush(member) : null;
            }

            public JsValue Push(IntPtr ctx, object target) => _push != null
                ? _push(ctx, target) : TypedMarshal.Push(ctx, _get(target), MemberType, Marshal);

            private static Func<IntPtr, object, JsValue> CompilePush(MemberInfo member)
            {
                var ctx = Expression.Parameter(typeof(IntPtr));
                var target = Expression.Parameter(typeof(object));
                Type owner = member.DeclaringType;
                Expression receiver = owner.IsValueType ? Expression.Unbox(target, owner) : Expression.Convert(target, owner);
                Expression value = member is FieldInfo field ? (Expression)Expression.Field(receiver, field) : Expression.Property(receiver, (PropertyInfo)member);
                Type type = value.Type.IsEnum ? Enum.GetUnderlyingType(value.Type) : value.Type;
                Expression push;
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Boolean:
                        push = Expression.Call(typeof(JsValueUtil), nameof(JsValueUtil.NewBool), null, value);
                        break;
                    case TypeCode.SByte:
                    case TypeCode.Byte:
                    case TypeCode.Int16:
                    case TypeCode.UInt16:
                    case TypeCode.Char:
                    case TypeCode.Int32:
                        push = Expression.Call(typeof(JsValueUtil), nameof(JsValueUtil.NewInt32), null, Expression.Convert(value, typeof(int)));
                        break;
                    case TypeCode.UInt32:
                    case TypeCode.Int64:
                    case TypeCode.UInt64:
                    case TypeCode.Single:
                    case TypeCode.Double:
                        push = Expression.Call(typeof(JsValueUtil), nameof(JsValueUtil.NewFloat64), null, Expression.Convert(value, typeof(double)));
                        break;
                    case TypeCode.String:
                        push = Expression.Call(typeof(EngineApi), nameof(EngineApi.NewString), null, ctx, value);
                        break;
                    default:
                        return null;
                }
                return Expression.Lambda<Func<IntPtr, object, JsValue>>(push, ctx, target).Compile();
            }

            public void Set(object target, object value)
            {
                if (_set == null)
                {
                    throw new JsScriptException($"zents: member is read-only.");
                }

                _set(target, value);
            }

            public object Get(object target) => _get(target);
        }
    }
}
