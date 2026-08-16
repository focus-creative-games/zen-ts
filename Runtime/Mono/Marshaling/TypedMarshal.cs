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
using System.Globalization;
using System.Reflection;
using ZTS.Jvm;
using ZTS.Mt;

namespace ZTS.Marshaling
{
    /// <summary>
    /// Typed Push/Pop with undefined≠null and default marshal matrix.
    /// </summary>
    internal static class TypedMarshal
    {
        public static JSValue Push(IntPtr ctx, object value, Type declaredType = null, JsMarshalAsAttribute marshalAs = null)
        {
            if (value == null)
            {
                return JsValueUtil.Null;
            }

            Type runtimeType = value.GetType();
            Type pushAs = declaredType ?? runtimeType;

            if (pushAs == typeof(void))
            {
                return JsValueUtil.Undefined;
            }

            if (marshalAs != null)
            {
                if (marshalAs.JsMarshalType == JsMarshalType.Bytes)
                {
                    if (value is byte[] bytes)
                    {
                        return BytesMarshal.Push(ctx, bytes);
                    }

                    throw new JsScriptException("zts: [JsMarshalAs(Bytes)] push requires byte[].");
                }

                if (marshalAs.JsMarshalType == JsMarshalType.Table ||
                    marshalAs.JsMarshalType == JsMarshalType.UnpackedValues)
                {
                    return TableMarshal.Push(ctx, value, pushAs, marshalAs);
                }

                if (marshalAs.JsMarshalType == JsMarshalType.OpaqueValue)
                {
                    return OpaqueParameterScope.Push(ctx, value);
                }
            }

            if (value is OpaqueValue opaque)
            {
                return OpaqueParameterScope.PushExisting(ctx, opaque);
            }

            if (pushAs.IsPointer)
            {
                return PointerMarshal.Push(ctx, value, pushAs);
            }

            Type underlying = Nullable.GetUnderlyingType(pushAs) ?? pushAs;

            if (underlying.IsEnum)
            {
                return JsValueUtil.NewInt32(Convert.ToInt32(value));
            }

            switch (Type.GetTypeCode(underlying))
            {
                case TypeCode.Boolean:
                    return JsValueUtil.NewBool((bool)value);
                case TypeCode.Char:
                    return JsValueUtil.NewInt32((char)value);
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                    return JsValueUtil.NewInt32(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                case TypeCode.UInt32:
                    {
                        uint u = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                        return u <= int.MaxValue
                            ? JsValueUtil.NewInt32((int)u)
                            : JsValueUtil.NewFloat64(u);
                    }
                case TypeCode.Int64:
                    {
                        long l = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                        if (l >= int.MinValue && l <= int.MaxValue)
                        {
                            return JsValueUtil.NewInt32((int)l);
                        }

                        return JsValueUtil.NewFloat64(l);
                    }
                case TypeCode.UInt64:
                    {
                        ulong ul = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                        if (ul <= int.MaxValue)
                        {
                            return JsValueUtil.NewInt32((int)ul);
                        }

                        return JsValueUtil.NewFloat64(ul);
                    }
                case TypeCode.Single:
                    return JsValueUtil.NewFloat64(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                case TypeCode.Double:
                    return JsValueUtil.NewFloat64(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                case TypeCode.Decimal:
                    throw new JsScriptException("zts: System.Decimal marshal is not supported.");
                case TypeCode.String:
                    return QuickJsDll.NewString(ctx, (string)value);
                case TypeCode.DateTime:
                    throw new JsScriptException("zts: System.DateTime marshal is not supported.");
            }

            if (underlying == typeof(IntPtr))
            {
                long bits = ((IntPtr)value).ToInt64();
                if (bits >= int.MinValue && bits <= int.MaxValue)
                {
                    return JsValueUtil.NewInt32((int)bits);
                }

                return JsValueUtil.NewFloat64(bits);
            }

            if (underlying == typeof(UIntPtr))
            {
                ulong bits = ((UIntPtr)value).ToUInt64();
                if (bits <= int.MaxValue)
                {
                    return JsValueUtil.NewInt32((int)bits);
                }

                return JsValueUtil.NewFloat64(bits);
            }

            // Spec: T[] / arrays push as ByObj exotic (not JS Array). Use zts.to_array to convert.
            if (value is Array && !(value is string))
            {
                Type arrView = underlying.IsArray ? underlying : runtimeType;
                return PushByObj(ctx, value, arrView);
            }

            // Delegate: JsMethod roundtrip → original JS function; else exotic + [[Call]].
            if (value is Delegate del)
            {
                if (DelegateImpl.DynamicBridgeFactory.TryGetBoundJsFunction(del, out JSValue boundFn))
                {
                    return JsValueUtil.Dup(boundFn);
                }

                Type delView = typeof(Delegate).IsAssignableFrom(underlying) ? underlying : runtimeType;
                JSValue delHandle = PushByObj(ctx, value, delView);
                if (JsEnvFromContext(ctx) is JsEnv delEnv &&
                    Emit.MetaBinding.TryWrapDelegateCallPublic(delEnv, delHandle, out JSValue callable))
                {
                    JsValueUtil.Free(ctx, delHandle);
                    return callable;
                }

                return delHandle;
            }

            if (underlying.IsValueType && !underlying.IsPrimitive && !underlying.IsEnum)
            {
                return StructMarshal.PushByVal(ctx, value);
            }

            // class / interface ByObj
            Type view = declaredType != null && !declaredType.IsValueType ? declaredType : runtimeType;
            return PushByObj(ctx, value, view);
        }

        public static object Pop(IntPtr ctx, JSValue jsValue, Type expectedType, JsMarshalAsAttribute marshalAs = null)
        {
            if (expectedType == null)
            {
                return PrimitiveMarshal.Pop(ctx, jsValue);
            }

            Type underlying = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
            if (marshalAs != null)
            {
                if (marshalAs.JsMarshalType == JsMarshalType.Object && underlying == typeof(object))
                {
                    return PopObjectMarshal(ctx, jsValue);
                }

                if (marshalAs.JsMarshalType == JsMarshalType.Table)
                {
                    return TableMarshal.Pop(ctx, jsValue, expectedType, marshalAs);
                }

                if (marshalAs.JsMarshalType == JsMarshalType.Bytes)
                {
                    if (underlying == typeof(byte[]))
                    {
                        return BytesMarshal.Pop(ctx, jsValue);
                    }

                    throw new JsScriptException("zts: [JsMarshalAs(Bytes)] pop requires byte[].");
                }
            }

            if (underlying.IsPointer)
            {
                return PointerMarshal.Pop(ctx, jsValue, underlying);
            }
            bool nullable = Nullable.GetUnderlyingType(expectedType) != null || !expectedType.IsValueType;

            int tag = JsValueUtil.GetNormTag(jsValue);
            if (tag == JsValueUtil.TagUndefined)
            {
                if (nullable && !expectedType.IsValueType)
                {
                    // Optional / missing — treat as error for required ref unless OptionalAttribute (v1: throw)
                    throw new JsScriptException($"zts: undefined is not assignable to {expectedType.FullName} (use null for CLR null).");
                }

                if (Nullable.GetUnderlyingType(expectedType) != null)
                {
                    return null;
                }

                throw new JsScriptException($"zts: undefined is not assignable to {expectedType.FullName}.");
            }

            if (tag == JsValueUtil.TagNull)
            {
                if (!nullable && expectedType.IsValueType && Nullable.GetUnderlyingType(expectedType) == null)
                {
                    throw new JsScriptException($"zts: null is not assignable to {expectedType.FullName}.");
                }

                return null;
            }

            PrimitiveMarshal.RejectBigInt(jsValue);

            if (typeof(Delegate).IsAssignableFrom(underlying))
            {
                // Already a CLR Delegate exotic (incl. callable Proxy wrapping it).
                if (ObjectRegistry.TryGetObject(ctx, jsValue, out object heldDel) &&
                    heldDel is Delegate existing &&
                    underlying.IsInstanceOfType(existing))
                {
                    return existing;
                }

                return DelegateMarshal.FromJsFunction(ctx, jsValue, underlying);
            }

            if (OpaqueParameterScope.TryPop(ctx, jsValue, out object opaqueTarget))
            {
                return Coerce(opaqueTarget, underlying);
            }

            if (underlying.IsEnum)
            {
                object num = PrimitiveMarshal.Pop(ctx, jsValue);
                return Enum.ToObject(underlying, Convert.ToInt32(num, CultureInfo.InvariantCulture));
            }

            if (underlying.IsArray)
            {
                if (ObjectRegistry.TryGetObject(ctx, jsValue, out object held) && held is Array clrArr)
                {
                    return Coerce(clrArr, underlying);
                }

                if (QuickJsDll.JS_IsArray(ctx, jsValue) != 0)
                {
                    object arr = ArrayMarshal.FromJsArray(ctx, jsValue);
                    return CoerceArray((object[])arr, underlying.GetElementType());
                }

                throw new JsScriptException($"zts: expected CLR array or JS Array for {underlying.FullName}.");
            }

            if (underlying.IsValueType && !underlying.IsPrimitive && !underlying.IsEnum)
            {
                if (ObjectRegistry.TryGetObject(ctx, jsValue, out object boxed))
                {
                    return Coerce(boxed, underlying);
                }
            }

            if (!underlying.IsValueType || underlying == typeof(string))
            {
                if (ObjectRegistry.TryGetObject(ctx, jsValue, out object obj))
                {
                    return Coerce(obj, underlying);
                }
            }

            object raw = PrimitiveMarshal.Pop(ctx, jsValue);
            if (ReferenceEquals(raw, DBNull.Value))
            {
                throw new JsScriptException($"zts: undefined is not assignable to {expectedType.FullName}.");
            }

            return Coerce(raw, underlying);
        }

        private static object PopObjectMarshal(IntPtr ctx, JSValue jsValue)
        {
            int tag = JsValueUtil.GetNormTag(jsValue);
            if (tag == JsValueUtil.TagUndefined)
            {
                throw new JsScriptException("zts: undefined is not assignable to System.Object (use null for CLR null).");
            }

            if (tag == JsValueUtil.TagNull)
            {
                return null;
            }

            if (ObjectRegistry.TryGetObject(ctx, jsValue, out object obj))
            {
                return obj;
            }

            throw new JsScriptException("zts: [JsMarshalAs(Object)] requires a CLR object handle.");
        }

        private static object Coerce(object value, Type target)
        {
            if (value == null)
            {
                return null;
            }

            if (target.IsInstanceOfType(value))
            {
                return value;
            }

            if (target.IsEnum)
            {
                return Enum.ToObject(target, Convert.ToInt32(value, CultureInfo.InvariantCulture));
            }

            if (target == typeof(IntPtr))
            {
                return new IntPtr(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }

            if (target == typeof(UIntPtr))
            {
                return new UIntPtr(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
            }

            if (target == typeof(long))
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            if (target == typeof(ulong))
            {
                return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            }

            try
            {
                return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new JsScriptException($"zts: cannot convert {value.GetType().FullName} to {target.FullName}: {ex.Message}");
            }
        }

        private static Array CoerceArray(object[] items, Type elementType)
        {
            Array result = Array.CreateInstance(elementType, items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                result.SetValue(items[i] == null ? null : Coerce(items[i], elementType), i);
            }

            return result;
        }

        private static JSValue PushByObj(IntPtr ctx, object value, Type view)
        {
            JSValue proto = default;
            // Avoid EnsureBinding(typeof(Type)) — reflection surface is enormous.
            if (view == typeof(Type))
            {
                return ObjectRegistry.PushObject(ctx, value, view, default);
            }

            if (view.IsArray)
            {
                if (JsEnvFromContext(ctx) is JsEnv arrEnv)
                {
                    proto = Emit.ArrayBinding.EnsureProto(arrEnv, view);
                }

                return ObjectRegistry.PushObject(ctx, value, view, proto);
            }

            if (TypeRegistry.TryGetBinding(view, out TypeBinding binding))
            {
                proto = view.IsValueType && !view.IsPrimitive && !view.IsEnum &&
                        JsValueUtil.GetNormTag(binding.ByObjInstanceProto) == JsValueUtil.TagObject
                    ? binding.ByObjInstanceProto
                    : binding.InstanceProto;
            }
            else if (JsEnvFromContext(ctx) is JsEnv env)
            {
                TypeBinding ensured = TypeRegistry.EnsureBinding(env, view);
                proto = view.IsValueType && !view.IsPrimitive && !view.IsEnum &&
                        JsValueUtil.GetNormTag(ensured.ByObjInstanceProto) == JsValueUtil.TagObject
                    ? ensured.ByObjInstanceProto
                    : ensured.InstanceProto;
            }

            JSValue handle = ObjectRegistry.PushObject(ctx, value, view, proto);
            // Instance miss Proxy (metatable/02-INDEX) — do not put Proxy on IEO itself
            // (JS_NewObjectProto(Proxy) is unreliable); wrap the instance handle instead.
            if (JsEnvFromContext(ctx) is JsEnv wrapEnv &&
                Emit.MetaBinding.TryWrapMissPublic(wrapEnv, handle, out JSValue wrapped))
            {
                JsValueUtil.Free(ctx, handle);
                return wrapped;
            }

            return handle;
        }

        private static JsEnv JsEnvFromContext(IntPtr ctx)
        {
            try
            {
                return JsEnv.FromContext(ctx);
            }
            catch
            {
                return null;
            }
        }
    }
}
