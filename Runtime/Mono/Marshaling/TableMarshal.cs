using System;
using System.Reflection;
using ZTS.Jvm;

namespace ZTS.Marshaling
{
    internal static class TableMarshal
    {
        public static int GetJsArgSlotCount(ParameterInfo param)
        {
            TsMarshalAsAttribute mas = param.GetCustomAttribute<TsMarshalAsAttribute>();
            if (mas != null &&
                mas.TsMarshalType == TsMarshalType.UnpackedValues &&
                mas.Members != null &&
                mas.Members.Length > 0)
            {
                return mas.Members.Length;
            }

            return 1;
        }

        public static object Pop(IntPtr ctx, JSValue jsValue, Type expectedType, TsMarshalAsAttribute marshalAs)
        {
            if (marshalAs == null)
            {
                return null;
            }

            if (marshalAs.TsMarshalType == TsMarshalType.Table)
            {
                return PopTable(ctx, jsValue, expectedType, marshalAs.Members);
            }

            return null;
        }

        public static JSValue Push(IntPtr ctx, object value, Type declaredType, TsMarshalAsAttribute marshalAs)
        {
            if (marshalAs == null || marshalAs.Members == null || marshalAs.Members.Length == 0)
            {
                throw new TsScriptException("zts: Table/UnpackedValues push requires Members.");
            }

            if (value == null)
            {
                return JsValueUtil.Null;
            }

            Type underlying = Nullable.GetUnderlyingType(declaredType) ?? declaredType ?? value.GetType();
            if (marshalAs.TsMarshalType == TsMarshalType.Table)
            {
                return PushTable(ctx, value, underlying, marshalAs.Members);
            }

            if (marshalAs.TsMarshalType == TsMarshalType.UnpackedValues)
            {
                return PushUnpacked(ctx, value, underlying, marshalAs.Members);
            }

            throw new TsScriptException($"zts: unsupported TableMarshal push kind {marshalAs.TsMarshalType}.");
        }

        private static JSValue PushTable(IntPtr ctx, object value, Type underlying, string[] members)
        {
            JSValue obj = QuickJsDll.JS_NewObject(ctx);
            for (int i = 0; i < members.Length; i++)
            {
                ParseMemberSpec(members[i], out string memberName, out _);
                MemberAccessor accessor = ResolveMember(underlying, memberName);
                object memberValue = accessor.Get(value);
                JSValue jsMember = TypedMarshal.Push(ctx, memberValue, accessor.MemberType);
                QuickJsDll.JS_SetPropertyStr(ctx, obj, memberName, jsMember);
            }

            return obj;
        }

        private static JSValue PushUnpacked(IntPtr ctx, object value, Type underlying, string[] members)
        {
            JSValue arr = QuickJsDll.JS_NewArray(ctx);
            for (int i = 0; i < members.Length; i++)
            {
                string spec = members[i];
                if (spec != null && spec.EndsWith("?", StringComparison.Ordinal))
                {
                    throw new TsScriptException("zts: UnpackedValues does not support optional member '?'.");
                }

                MemberAccessor accessor = ResolveMember(underlying, spec);
                object memberValue = accessor.Get(value);
                JSValue jsMember = TypedMarshal.Push(ctx, memberValue, accessor.MemberType);
                QuickJsDll.JS_SetPropertyUint32(ctx, arr, (uint)i, jsMember);
            }

            return arr;
        }

        public static object PopUnpacked(
            IntPtr ctx,
            IntPtr argvPtr,
            int jsStartIndex,
            int argc,
            Type expectedType,
            TsMarshalAsAttribute marshalAs)
        {
            if (marshalAs == null ||
                marshalAs.TsMarshalType != TsMarshalType.UnpackedValues ||
                marshalAs.Members == null ||
                marshalAs.Members.Length == 0)
            {
                throw new TsScriptException("zts: UnpackedValues requires Members.");
            }

            string[] members = marshalAs.Members;
            if (jsStartIndex + members.Length > argc)
            {
                throw new TsScriptException(
                    $"zts: UnpackedValues requires {members.Length} argument(s) for {expectedType.FullName}.");
            }

            Type underlying = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
            if (!underlying.IsValueType || underlying.IsPrimitive || underlying.IsEnum)
            {
                throw new TsScriptException($"zts: UnpackedValues requires struct type, not {expectedType.FullName}.");
            }

            object boxed = Activator.CreateInstance(underlying);
            for (int i = 0; i < members.Length; i++)
            {
                string spec = members[i];
                if (spec != null && spec.EndsWith("?", StringComparison.Ordinal))
                {
                    throw new TsScriptException("zts: UnpackedValues does not support optional member '?'.");
                }

                string memberName = spec ?? throw new TsScriptException("zts: UnpackedValues member name is null.");
                MemberAccessor accessor = ResolveMember(underlying, memberName);
                JSValue jsArg = ArgReader.Read(argvPtr, jsStartIndex + i);
                object value = TypedMarshal.Pop(ctx, jsArg, accessor.MemberType);
                accessor.Set(boxed, value);
            }

            if (Nullable.GetUnderlyingType(expectedType) != null)
            {
                return boxed;
            }

            return boxed;
        }

        public static Type ResolveMemberType(Type structType, string memberSpec)
        {
            ParseMemberSpec(memberSpec, out string memberName, out _);
            return ResolveMember(structType, memberName).MemberType;
        }

        private static object PopTable(IntPtr ctx, JSValue jsValue, Type expectedType, string[] members)
        {
            if (members == null || members.Length == 0)
            {
                throw new TsScriptException("zts: Table marshal requires Members.");
            }

            Type nullableUnderlying = Nullable.GetUnderlyingType(expectedType);
            bool isNullable = nullableUnderlying != null;
            Type underlying = nullableUnderlying ?? expectedType;

            if (!underlying.IsValueType || underlying.IsPrimitive || underlying.IsEnum)
            {
                throw new TsScriptException($"zts: Table marshal requires struct type, not {expectedType.FullName}.");
            }

            int tag = JsValueUtil.GetNormTag(jsValue);
            if (tag == JsValueUtil.TagUndefined || tag == JsValueUtil.TagNull)
            {
                if (isNullable)
                {
                    return null;
                }

                throw new TsScriptException($"zts: null is not assignable to {expectedType.FullName}.");
            }

            if (tag != JsValueUtil.TagObject)
            {
                throw new TsScriptException($"zts: Table marshal requires plain object for {expectedType.FullName}.");
            }

            RejectClrExotic(ctx, jsValue, expectedType);

            object boxed = Activator.CreateInstance(underlying);
            for (int i = 0; i < members.Length; i++)
            {
                ParseMemberSpec(members[i], out string memberName, out bool optional);
                MemberAccessor accessor = ResolveMember(underlying, memberName);
                JSValue propVal = QuickJsDll.JS_GetPropertyStr(ctx, jsValue, memberName);
                try
                {
                    int propTag = JsValueUtil.GetNormTag(propVal);
                    if (propTag == JsValueUtil.TagUndefined)
                    {
                        if (optional)
                        {
                            continue;
                        }

                        throw new TsScriptException(
                            $"zts: Table marshal missing required member '{memberName}' for {expectedType.FullName}.");
                    }

                    object value = TypedMarshal.Pop(ctx, propVal, accessor.MemberType);
                    accessor.Set(boxed, value);
                }
                finally
                {
                    JsValueUtil.Free(ctx, propVal);
                }
            }

            return boxed;
        }

        private static void RejectClrExotic(IntPtr ctx, JSValue jsValue, Type expectedType)
        {
            JSValue ptrFlag = QuickJsDll.JS_GetPropertyStr(ctx, jsValue, "__zts_pointer");
            try
            {
                if (JsValueUtil.GetNormTag(ptrFlag) == JsValueUtil.TagBool && ptrFlag.UInt64 != 0)
                {
                    throw new TsScriptException(
                        $"zts: Table marshal requires plain object for {expectedType.FullName}, not Pointer handle.");
                }
            }
            finally
            {
                JsValueUtil.Free(ctx, ptrFlag);
            }

            JSValue idVal = QuickJsDll.JS_GetPropertyStr(ctx, jsValue, "__zts_id");
            try
            {
                if (JsValueUtil.GetNormTag(idVal) == JsValueUtil.TagInt)
                {
                    throw new TsScriptException(
                        $"zts: Table marshal requires plain object for {expectedType.FullName}, not CLR handle.");
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
                throw new TsScriptException("zts: Table/UnpackedValues member name is empty.");
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
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            FieldInfo field = structType.GetField(memberName, flags);
            if (field != null)
            {
                return new MemberAccessor(field.FieldType, field.SetValue, field.GetValue);
            }

            PropertyInfo prop = structType.GetProperty(memberName, flags);
            if (prop != null && prop.CanRead)
            {
                Action<object, object> setter = prop.CanWrite ? prop.SetValue : null;
                return new MemberAccessor(prop.PropertyType, setter, prop.GetValue);
            }

            throw new TsScriptException(
                $"zts: struct {structType.FullName} has no public field or property '{memberName}'.");
        }

        private readonly struct MemberAccessor
        {
            public Type MemberType { get; }
            private readonly Action<object, object> _set;
            private readonly Func<object, object> _get;

            public MemberAccessor(Type memberType, Action<object, object> set, Func<object, object> get)
            {
                MemberType = memberType;
                _set = set;
                _get = get;
            }

            public void Set(object target, object value)
            {
                if (_set == null)
                {
                    throw new TsScriptException($"zts: member is read-only.");
                }

                _set(target, value);
            }

            public object Get(object target) => _get(target);
        }
    }
}
