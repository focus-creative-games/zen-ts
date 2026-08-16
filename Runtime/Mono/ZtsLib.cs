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
using System.IO;
using System.Reflection;
using ZTS.DelegateImpl;
using ZTS.Emit;
using ZTS.Jvm;
using ZTS.Marshaling;
using ZTS.Mt;
using ZTS.Utils;

namespace ZTS
{
    internal static class ZtsLib
    {
        public static void RegisterGlobals(JsEnv env)
        {
            IntPtr ctx = env.Context;

            // Native hooks on globalThis (not on zts — wrappers live in ztslib.js).
            BindGlobal(ctx, "__zts_typeof", TypeOfCallback, 1);
            BindGlobal(ctx, "__zts_get_type_from_name", GetTypeFromNameCallback, 1);
            BindGlobal(ctx, "__zts_ensure_assembly", EnsureAssemblyCallback, 1);
            BindGlobal(ctx, "__zts_resolve_type", ResolveTypeCallback, 2);
            BindGlobal(ctx, "__zts_cast", CastCallback, 2);
            BindGlobal(ctx, "__zts_box", BoxCallback, 2);
            BindGlobal(ctx, "__zts_unbox", UnboxCallback, 1);
            BindGlobal(ctx, "__zts_register_method", RegisterMethodCallback, 2);
            BindGlobal(ctx, "__zts_make_generic_type", MakeGenericTypeCallback, 2);
            BindGlobal(ctx, "__zts_make_generic_method", MakeGenericMethodCallback, 2);
            BindGlobal(ctx, "__zts_make_szarray_type", MakeSzArrayTypeCallback, 1);
            BindGlobal(ctx, "__zts_new_szarray_by_element_type", NewSzArrayCallback, 2);
            BindGlobal(ctx, "__zts_new_szarray_by_szarray_type", NewSzArrayByTypeCallback, 2);
            BindGlobal(ctx, "__zts_make_mdarray_type", MakeMdArrayTypeCallback, 2);
            BindGlobal(ctx, "__zts_new_mdarray_by_spec", NewMdArrayBySpecCallback, 3);
            BindGlobal(ctx, "__zts_new_mdarray_by_mdarray_type", NewMdArrayByTypeCallback, 3);
            BindGlobal(ctx, "__zts_to_array", ToArrayCallback, 1);
            BindGlobal(ctx, "__zts_to_bytes", ToBytesCallback, 1);
            BindGlobal(ctx, "__zts_to_delegate", ToDelegateCallback, 2);
            BindGlobal(ctx, "__zts_create_signature", CreateSignatureCallback, 1);
            BindGlobal(ctx, "__zts_get_opaquevalue", GetOpaqueCallback, 1);
            BindGlobal(ctx, "__zts_set_opaquevalue", SetOpaqueCallback, 2);
            BindGlobal(ctx, "__zts_to_user_data", ToUserDataCallback, 1);
            BindGlobal(ctx, "__zts_print", PrintCallback, 2);

            string js = LoadZtsLibJs();
            env.EvalScript(js, "ztslib.js");
        }

        private static string LoadZtsLibJs()
        {
            string beside = Path.Combine(
                Path.GetDirectoryName(typeof(ZtsLib).Assembly.Location) ?? string.Empty,
                "..", "..", "..", "ZTS~", "jslib", "ztslib.js");
            beside = Path.GetFullPath(beside);
            if (File.Exists(beside))
            {
                return File.ReadAllText(beside);
            }

            // Fallback: package path under Assets/Packages
            string[] candidates =
            {
                Path.Combine(UnityEngine.Application.dataPath, "..", "Packages", "com.code-philosophy.zts", "ZTS~", "jslib", "ztslib.js"),
                Path.GetFullPath("Packages/com.code-philosophy.zts/ZTS~/jslib/ztslib.js"),
            };
            foreach (string c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(full))
                {
                    return File.ReadAllText(full);
                }
            }

            throw new JsScriptException("zts: ztslib.js not found.");
        }

        private static void BindGlobal(IntPtr ctx, string name, JsCFunction callback, int length)
        {
            JSValue fn = JsCallbackGate.NewCFunction(ctx, callback, name, length);
            JSValue global = QuickJsDll.JS_GetGlobalObject(ctx);
            QuickJsDll.JS_SetPropertyStr(ctx, global, name, fn);
            JsValueUtil.Free(ctx, global);
        }

        private static string ReadStringArg(IntPtr ctx, IntPtr argv, int index)
        {
            JSValue v = ArgReader.Read(argv, index);
            IntPtr ptr = QuickJsDll.JS_ToCStringLen2(ctx, out UIntPtr len, v, 0);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr, (int)len);
            }
            finally
            {
                QuickJsDll.JS_FreeCString(ctx, ptr);
            }
        }

        private static JSValue PrintCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                int level = 0;
                if (argc >= 1)
                {
                    JSValue lv = ArgReader.Read(argv, 0);
                    if (QuickJsDll.JS_ToInt32(ctx, out level, lv) < 0)
                    {
                        level = 0;
                    }
                }

                string msg = argc >= 2 ? ReadStringArg(ctx, argv, 1) : string.Empty;
                if (msg == null)
                {
                    msg = string.Empty;
                }

                // Never Debug.Log directly from a JS→C# frame (gate / stack); buffer instead.
                if (level >= 2)
                {
                    JsPrintBuffer.Log("[JS:error] " + msg);
                }
                else if (level == 1)
                {
                    JsPrintBuffer.Log("[JS:warn] " + msg);
                }
                else
                {
                    JsPrintBuffer.Log("[JS] " + msg);
                }

                return JsValueUtil.Undefined;
            }
            catch (Exception ex)
            {
                return JsCallbackGate.ReturnErrorSentinel(ctx, ex.Message);
            }
        }

        private static JSValue TypeOfCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 1)
                {
                    return JsValueUtil.Undefined;
                }

                JSValue arg0 = ArgReader.Read(argv, 0);
                Type type = null;
                if (ObjectRegistry.TryGetObject(ctx, arg0, out object obj) && obj is Type t)
                {
                    type = t;
                }
                else
                {
                    // STO / type object tagged with __zts_id → Type
                    JSValue nameVal = QuickJsDll.JS_GetPropertyStr(ctx, arg0, "__zts_type_name");
                    bool hasName = JsValueUtil.GetNormTag(nameVal) == JsValueUtil.TagString;
                    JsValueUtil.Free(ctx, nameVal);
                    if (ObjectRegistry.TryGetObject(ctx, arg0, out object tagged) && tagged is Type typed)
                    {
                        type = typed;
                    }
                    else if (hasName && ObjectRegistry.TryGetObject(ctx, arg0, out _))
                    {
                        // fallthrough
                    }
                }

                // Type objects from TypeRegistry store Type in __zts_id slot
                if (type == null && ObjectRegistry.TryGetObject(ctx, arg0, out object any) && any is Type asType)
                {
                    type = asType;
                }

                if (type == null)
                {
                    // Resolve via __zts_type_name string when present on STO
                    JSValue nameVal = QuickJsDll.JS_GetPropertyStr(ctx, arg0, "__zts_type_name");
                    try
                    {
                        if (JsValueUtil.GetNormTag(nameVal) == JsValueUtil.TagString)
                        {
                            IntPtr ptr = QuickJsDll.JS_ToCStringLen2(ctx, out UIntPtr len, nameVal, 0);
                            if (ptr != IntPtr.Zero)
                            {
                                try
                                {
                                    string full = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr, (int)len);
                                    type = AssemblyRegistry.FindTypeByFullName(full);
                                }
                                finally
                                {
                                    QuickJsDll.JS_FreeCString(ctx, ptr);
                                }
                            }
                        }
                    }
                    finally
                    {
                        JsValueUtil.Free(ctx, nameVal);
                    }
                }

                if (type == null)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: typeof expects a type object.");
                }

                // Spec: System.Type ByObj. Do not EnsureBinding(typeof(Type)) — that pulls the
                // entire reflection surface and routinely blows the callback gate / FreeRuntime.
                JSValue handle = ObjectRegistry.PushObject(ctx, type, typeof(Type), default);
                QuickJsDll.JS_SetPropertyStr(
                    ctx,
                    handle,
                    "__zts_type_name",
                    QuickJsDll.NewString(ctx, type.FullName ?? type.Name));
                return handle;
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue GetTypeFromNameCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                string name = ReadStringArg(ctx, argv, 0);
                Type type = AssemblyRegistry.FindTypeByFullName(name);
                if (type == null)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, $"zts: type not found: {name}");
                }

                return TypeRegistry.PushTypeObject(JsEnv.FromContext(ctx), type);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue EnsureAssemblyCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                string name = ReadStringArg(ctx, argv, 0);
                AssemblyRegistry.EnsureAssemblyExists(name);
                return JsValueUtil.Undefined;
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue ResolveTypeCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                string asm = ReadStringArg(ctx, argv, 0);
                string typeName = ReadStringArg(ctx, argv, 1);
                Type type = AssemblyRegistry.ResolveType(asm, typeName);
                return TypeRegistry.PushTypeObject(JsEnv.FromContext(ctx), type);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue CastCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 2)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: cast requires value and type.");
                }

                JSValue value = ArgReader.Read(argv, 0);
                JSValue typeVal = ArgReader.Read(argv, 1);
                if (!ObjectRegistry.TryGetObject(ctx, typeVal, out object typeObj) || !(typeObj is Type type))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: cast target is not a type.");
                }

                object managed = TypedMarshal.Pop(ctx, value, type);
                return TypedMarshal.Push(ctx, managed, type);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue BoxCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 2)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: box requires type and value.");
                }

                Type type = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                if (!type.IsValueType)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: box expects a value type or enum.");
                }

                object value = TypedMarshal.Pop(ctx, ArgReader.Read(argv, 1), type);
                // Force ByObj (even for enums/structs that default to number/ByVal).
                JsEnv env = JsEnv.FromContext(ctx);
                TypeBinding typeBinding = TypeRegistry.EnsureBinding(env, type);
                JSValue proto = JsValueUtil.GetNormTag(typeBinding.ByObjInstanceProto) == JsValueUtil.TagObject
                    ? typeBinding.ByObjInstanceProto
                    : typeBinding.InstanceProto;
                JSValue handle = ObjectRegistry.PushObject(ctx, value, type, proto);
                if (Emit.MetaBinding.TryWrapMissPublic(env, handle, out JSValue wrapped))
                {
                    JsValueUtil.Free(ctx, handle);
                    return wrapped;
                }

                return handle;
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue UnboxCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 1)
                {
                    return JsValueUtil.Undefined;
                }

                if (!ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, 0), out object obj))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: unbox expects a CLR object.");
                }

                return TypedMarshal.Push(ctx, obj, obj.GetType());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue RegisterMethodCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                string name = ReadStringArg(ctx, argv, 0);
                if (string.IsNullOrEmpty(name))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: register_method requires a non-empty name.");
                }

                JSValue fnVal = ArgReader.Read(argv, 1);
                if (!MethodEmitter.TryGetDirectTag(ctx, fnVal, out MethodClosureTag tag))
                {
                    return JsCallbackGate.ReturnErrorSentinel(
                        ctx, "zts: register_method expects a direct method function (not dispatch).");
                }

                JsEnv env = JsEnv.FromContext(ctx);
                TypeBinding binding = TypeRegistry.EnsureBinding(env, tag.OwnerType);
                if (binding.MemberKeys.Contains(name))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, $"zts: register_method conflict: {name}");
                }

                JSValue target = tag.IsStatic ? binding.TypeObjectRaw : binding.InstanceProto;
                if (JsValueUtil.GetNormTag(target) != JsValueUtil.TagObject)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: register_method: type binding incomplete.");
                }

                binding.MemberKeys.Add(name);
                QuickJsDll.JS_SetPropertyStr(ctx, target, name, JsValueUtil.Dup(fnVal));
                return JsValueUtil.Undefined;
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue MakeGenericTypeCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type generic = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                if (generic == null || !generic.IsGenericTypeDefinition)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: make_generic_type: invalid generic type.");
                }

                Type[] typeArgs = argc >= 2
                    ? ReadTypeArgs(ctx, ArgReader.Read(argv, 1))
                    : Array.Empty<Type>();
                Type closed = generic.MakeGenericType(typeArgs);
                return TypeRegistry.PushTypeObject(JsEnv.FromContext(ctx), closed);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static Type[] ReadTypeArgs(IntPtr ctx, JSValue jsArray)
        {
            var list = new System.Collections.Generic.List<Type>();
            uint idx = 0;
            while (true)
            {
                JSValue item = QuickJsDll.JS_GetPropertyUint32(ctx, jsArray, idx);
                if (JsValueUtil.GetNormTag(item) == JsValueUtil.TagUndefined)
                {
                    JsValueUtil.Free(ctx, item);
                    break;
                }

                try
                {
                    list.Add(ResolveTypeArg(ctx, item));
                }
                finally
                {
                    JsValueUtil.Free(ctx, item);
                }

                idx++;
            }

            return list.ToArray();
        }

        private static Type ResolveTypeArg(IntPtr ctx, JSValue value)
        {
            if (ObjectRegistry.TryGetObject(ctx, value, out object obj) && obj is Type t)
            {
                return t;
            }

            if (JsValueUtil.GetNormTag(value) == JsValueUtil.TagString)
            {
                string name = ReadStringValue(ctx, value);
                Type byName = AssemblyRegistry.FindTypeByFullName(name);
                if (byName != null)
                {
                    return byName;
                }
            }

            throw new JsScriptException("zts: generic type argument is not a Type.");
        }

        private static string ReadStringValue(IntPtr ctx, JSValue value)
        {
            IntPtr cstr = QuickJsDll.JS_ToCStringLen2(ctx, out UIntPtr len, value, 0);
            if (cstr == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(cstr, (int)len) ?? string.Empty;
            }
            finally
            {
                QuickJsDll.JS_FreeCString(ctx, cstr);
            }
        }

        private static readonly System.Collections.Generic.Dictionary<MethodInfo, JSValue> ClosedGenericMethodCache =
            new System.Collections.Generic.Dictionary<MethodInfo, JSValue>();

        private static JSValue MakeGenericMethodCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                JSValue baseFn = ArgReader.Read(argv, 0);
                if (!MethodEmitter.TryGetDirectTag(ctx, baseFn, out MethodClosureTag tag))
                {
                    return JsCallbackGate.ReturnErrorSentinel(
                        ctx, "zts: make_generic_method expects a direct method function (not dispatch).");
                }

                MethodInfo open = tag.Method;
                if (open == null || !open.IsGenericMethodDefinition)
                {
                    return JsCallbackGate.ReturnErrorSentinel(
                        ctx, "zts: make_generic_method requires an open generic method definition.");
                }

                Type[] typeArgs = argc >= 2
                    ? ReadTypeArgs(ctx, ArgReader.Read(argv, 1))
                    : Array.Empty<Type>();

                MethodInfo closed = open.MakeGenericMethod(typeArgs);
                lock (ClosedGenericMethodCache)
                {
                    if (ClosedGenericMethodCache.TryGetValue(closed, out JSValue cached))
                    {
                        return JsValueUtil.Dup(cached);
                    }
                }

                JsEnv env = JsEnv.FromContext(ctx);
                JSValue emitted = MethodEmitter.EmitMethod(env, closed, tag.OwnerType, tag.IsByVal);
                lock (ClosedGenericMethodCache)
                {
                    ClosedGenericMethodCache[closed] = JsValueUtil.Dup(emitted);
                }

                return emitted;
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        internal static void ResetGenericMethodCache(IntPtr ctx)
        {
            lock (ClosedGenericMethodCache)
            {
                foreach (JSValue v in ClosedGenericMethodCache.Values)
                {
                    JsValueUtil.Free(ctx, v);
                }

                ClosedGenericMethodCache.Clear();
            }
        }

        private static JSValue MakeSzArrayTypeCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type el = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                return TypeRegistry.PushTypeObject(JsEnv.FromContext(ctx), el.MakeArrayType());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue NewSzArrayCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type el = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                int length = Convert.ToInt32(PrimitiveMarshal.Pop(ctx, ArgReader.Read(argv, 1)));
                Array arr = Array.CreateInstance(el, length);
                return TypedMarshal.Push(ctx, arr, arr.GetType());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue NewSzArrayByTypeCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type arrType = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                if (arrType == null || !arrType.IsArray || arrType.GetArrayRank() != 1)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: new_szarray_by_szarray_type expects a szarray type.");
                }

                Type el = arrType.GetElementType() ?? typeof(object);
                int length = Convert.ToInt32(PrimitiveMarshal.Pop(ctx, ArgReader.Read(argv, 1)));
                Array arr = Array.CreateInstance(el, length);
                return TypedMarshal.Push(ctx, arr, arrType);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue CreateSignatureCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                // argv[0] may be a JS Array of typeArgs, or we read argc args when called via JS_Call with rest.
                // ztslib passes a single JS array of typeArgs.
                Type[] types;
                if (argc >= 1 && QuickJsDll.JS_IsArray(ctx, ArgReader.Read(argv, 0)) != 0)
                {
                    types = ReadTypeArgs(ctx, ArgReader.Read(argv, 0));
                }
                else
                {
                    types = new Type[argc];
                    for (int i = 0; i < argc; i++)
                    {
                        types[i] = ResolveTypeArg(ctx, ArgReader.Read(argv, i));
                    }
                }

                var sb = new System.Text.StringBuilder();
                sb.Append('(');
                for (int i = 0; i < types.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    Type t = types[i];
                    sb.Append(t.FullName ?? t.Name);
                }

                sb.Append(')');
                return QuickJsDll.NewString(ctx, sb.ToString());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue MakeMdArrayTypeCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type el = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                int rank = Convert.ToInt32(PrimitiveMarshal.Pop(ctx, ArgReader.Read(argv, 1)));
                if (rank < 1 || rank > 32)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: make_mdarray_type rank must be in [1, 32].");
                }

                return TypeRegistry.PushTypeObject(JsEnv.FromContext(ctx), el.MakeArrayType(rank));
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue NewMdArrayBySpecCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type el = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                int[] lowBounds = ReadIntJsArray(ctx, ArgReader.Read(argv, 1), "lowbounds");
                int[] sizes = ReadIntJsArray(ctx, ArgReader.Read(argv, 2), "sizes");
                if (lowBounds.Length != sizes.Length || sizes.Length == 0)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: new_mdarray_by_spec: lowbounds/sizes rank mismatch.");
                }

                Array arr = Array.CreateInstance(el, sizes, lowBounds);
                return TypedMarshal.Push(ctx, arr, arr.GetType());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue NewMdArrayByTypeCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type mdType = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                if (!mdType.IsArray || mdType.GetArrayRank() < 1)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: new_mdarray_by_mdarray_type expects mdarray type.");
                }

                Type el = mdType.GetElementType() ?? typeof(object);
                int[] lowBounds = ReadIntJsArray(ctx, ArgReader.Read(argv, 1), "lowbounds");
                int[] sizes = ReadIntJsArray(ctx, ArgReader.Read(argv, 2), "sizes");
                if (lowBounds.Length != sizes.Length || sizes.Length != mdType.GetArrayRank())
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: new_mdarray_by_mdarray_type: lowbounds/sizes rank mismatch.");
                }

                Array arr = Array.CreateInstance(el, sizes, lowBounds);
                return TypedMarshal.Push(ctx, arr, mdType);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static int[] ReadIntJsArray(IntPtr ctx, JSValue jsArray, string label)
        {
            if (QuickJsDll.JS_IsArray(ctx, jsArray) == 0)
            {
                throw new JsScriptException($"zts: {label} must be a JS Array.");
            }

            object[] raw = (object[])ArrayMarshal.FromJsArray(ctx, jsArray);
            var result = new int[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == null || ReferenceEquals(raw[i], DBNull.Value))
                {
                    throw new JsScriptException($"zts: {label}[{i}] is missing.");
                }

                result[i] = Convert.ToInt32(raw[i]);
            }

            return result;
        }

        private static JSValue ToArrayCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (!ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, 0), out object obj) || !(obj is Array arr))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: to_array expects CLR array.");
                }

                return ArrayMarshal.ToJsArray(ctx, arr);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue ToBytesCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (!ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, 0), out object obj) || !(obj is Array arr))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: to_bytes expects CLR array.");
                }

                Type el = arr.GetType().GetElementType();
                if (el == typeof(bool) || el == typeof(char) || el == typeof(string) || (el != null && el.IsClass))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: to_bytes: element type is not blittable.");
                }

                if (arr is byte[] bytes)
                {
                    return ArrayMarshal.ToJsArray(ctx, bytes);
                }

                int byteLen = Buffer.ByteLength(arr);
                var raw = new byte[byteLen];
                Buffer.BlockCopy(arr, 0, raw, 0, byteLen);
                return ArrayMarshal.ToJsArray(ctx, raw);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue ToDelegateCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                JSValue fn = ArgReader.Read(argv, 0);
                if (!ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, 1), out object obj) || !(obj is Type delType))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: to_delegate expects delegate type.");
                }

                Delegate del = DelegateMarshal.FromJsFunction(ctx, fn, delType);
                return TypedMarshal.Push(ctx, del, delType);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue GetOpaqueCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (!OpaqueParameterScope.TryPop(ctx, ArgReader.Read(argv, 0), out object target))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: get_opaquevalue: not an opaque handle.");
                }

                return TypedMarshal.Push(ctx, target, target?.GetType());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue SetOpaqueCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                JSValue handle = ArgReader.Read(argv, 0);
                if (!ObjectRegistry.TryGetObject(ctx, handle, out object obj) || !(obj is OpaqueValue opaque))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: set_opaquevalue: not an opaque handle.");
                }

                object value = PrimitiveMarshal.Pop(ctx, ArgReader.Read(argv, 1));
                if (ReferenceEquals(value, DBNull.Value))
                {
                    value = null;
                }

                OpaqueParameterScope.SetTarget(opaque, value);
                return JsValueUtil.Undefined;
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue ToUserDataCallback(IntPtr ctx, JSValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 1)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: to_user_data requires an opaque handle.");
                }

                if (!OpaqueParameterScope.TryPop(ctx, ArgReader.Read(argv, 0), out object target))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: to_user_data: not an opaque handle.");
                }

                if (target == null)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: to_user_data: null target.");
                }

                Type runtimeType = target.GetType();
                if (!runtimeType.IsValueType || runtimeType.IsPrimitive || runtimeType.IsEnum)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zts: to_user_data expects a struct opaque value.");
                }

                return StructMarshal.PushByVal(ctx, target);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JSValue Fail(IntPtr ctx, Exception ex)
        {
            JsCallbackBoundary.ThrowManaged(ctx, ex);
            return JsValueUtil.MakeErrorSentinel();
        }
    }

    internal static class ArgReader
    {
        public static JSValue Read(IntPtr argv, int index)
        {
            unsafe
            {
                return *(JSValue*)(argv + index * sizeof(JSValue));
            }
        }
    }
}
