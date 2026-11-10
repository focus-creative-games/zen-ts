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
using System.Reflection.Emit;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ZenTS.DelegateImpl;
using ZenTS.Emit;
using ZenTS.Jvm;
using ZenTS.Marshaling;
using ZenTS.Mt;
using ZenTS.Utils;

namespace ZenTS
{
    internal static class ZentsLib
    {
        public static void RegisterGlobals(JsEnv env)
        {
            IntPtr ctx = env.Context;

            // Native hooks on globalThis (not on zents — wrappers live in zentslib.js).
            BindGlobal(ctx, "__zents_typeof", TypeOfCallback, 1);
            BindGlobal(ctx, "__zents_get_type_from_name", GetTypeFromNameCallback, 1);
            BindGlobal(ctx, "__zents_ensure_assembly", EnsureAssemblyCallback, 1);
            BindGlobal(ctx, "__zents_resolve_type", ResolveTypeCallback, 2);
            BindGlobal(ctx, "__zents_cast", CastCallback, 2);
            BindGlobal(ctx, "__zents_box", BoxCallback, 2);
            BindGlobal(ctx, "__zents_unbox", UnboxCallback, 1);
            BindGlobal(ctx, "__zents_resolve_signature", ResolveSignatureCallback, 2);
            BindGlobal(ctx, "__zents_make_generic_type", MakeGenericTypeCallback, 2);
            BindGlobal(ctx, "__zents_inflate", InflateCallback, 3);
            BindGlobal(ctx, "__zents_make_szarray_type", MakeSzArrayTypeCallback, 1);
            BindGlobal(ctx, "__zents_new_szarray_by_element_type", NewSzArrayCallback, 2);
            BindGlobal(ctx, "__zents_new_szarray_by_szarray_type", NewSzArrayByTypeCallback, 2);
            BindGlobal(ctx, "__zents_make_mdarray_type", MakeMdArrayTypeCallback, 2);
            BindGlobal(ctx, "__zents_new_mdarray_by_spec", NewMdArrayBySpecCallback, 3);
            BindGlobal(ctx, "__zents_new_mdarray_by_mdarray_type", NewMdArrayByTypeCallback, 3);
            BindGlobal(ctx, "__zents_to_array", ToArrayCallback, 1);
            BindGlobal(ctx, "__zents_to_bytes", ToBytesCallback, 1);
            BindGlobal(ctx, "__zents_to_delegate", ToDelegateCallback, 2);
            BindGlobal(ctx, "__zents_create_signature", CreateSignatureCallback, 1);
            BindGlobal(ctx, "__zents_get_opaquevalue", GetOpaqueCallback, 1);
            BindGlobal(ctx, "__zents_set_opaquevalue", SetOpaqueCallback, 2);
            BindGlobal(ctx, "__zents_to_user_data", ToUserDataCallback, 1);
            BindGlobal(ctx, "__zents_print", PrintCallback, 2);

            string js = LoadZentsLibJs();
            env.EvalScript(js, "zentslib.js");
        }

        private static string LoadZentsLibJs()
        {
            // Prefer staged CoreLibs (Editor Ensure); then package-relative fallbacks.
            string[] candidates =
            {
                Path.GetFullPath("Packages/com.code-philosophy.zen-ts/ZenTS~/jslib/zentslib.js"),
                Path.Combine(UnityEngine.Application.dataPath, "..", "Library", "ZenTS", "CoreLibs", "jslib", "zentslib.js"),
                Path.Combine(
                    Path.GetDirectoryName(typeof(ZentsLib).Assembly.Location) ?? string.Empty,
                    "..", "..", "..", "ZenTS~", "jslib", "zentslib.js"),
                Path.Combine(UnityEngine.Application.dataPath, "..", "Packages", "com.code-philosophy.zen-ts", "ZenTS~", "jslib", "zentslib.js"),
                Path.GetFullPath("Packages/com.code-philosophy.zen-ts/ZenTS~/jslib/zentslib.js"),
            };
            foreach (string c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(full))
                {
                    return File.ReadAllText(full);
                }
            }

            throw new JsScriptException("zents: zentslib.js not found.");
        }

        private static void BindGlobal(IntPtr ctx, string name, JsCFunction callback, int length)
        {
            JsValue fn = JsCallbackGate.NewCFunction(ctx, callback, name, length);
            JsValue global = EngineApi.GetGlobalObject(ctx);
            EngineApi.SetPropertyStr(ctx, global, name, fn);
            JsValueUtil.Free(ctx, global);
        }

        private static string ReadStringArg(IntPtr ctx, IntPtr argv, int index)
        {
            JsValue v = ArgReader.Read(argv, index);
            IntPtr ptr = EngineApi.ToCStringLen2(ctx, out UIntPtr len, v, 0);
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
                EngineApi.FreeCString(ctx, ptr);
            }
        }

        private static JsValue PrintCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                int level = 0;
                if (argc >= 1)
                {
                    JsValue lv = ArgReader.Read(argv, 0);
                    if (EngineApi.ToInt32(ctx, out level, lv) < 0)
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

        private static JsValue TypeOfCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 1)
                {
                    return JsValueUtil.Undefined;
                }

                JsValue arg0 = ArgReader.Read(argv, 0);
                Type type = null;
                if (ObjectRegistry.TryGetObject(ctx, arg0, out object obj) && obj is Type t)
                {
                    type = t;
                }
                else
                {
                    // STO / type object tagged with __zents_id → Type
                    JsValue nameVal = EngineApi.GetPropertyStr(ctx, arg0, "__zents_type_name");
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

                // Type objects from TypeRegistry store Type in __zents_id slot
                if (type == null && ObjectRegistry.TryGetObject(ctx, arg0, out object any) && any is Type asType)
                {
                    type = asType;
                }

                if (type == null)
                {
                    // Resolve via __zents_type_name string when present on STO
                    JsValue nameVal = EngineApi.GetPropertyStr(ctx, arg0, "__zents_type_name");
                    try
                    {
                        if (JsValueUtil.GetNormTag(nameVal) == JsValueUtil.TagString)
                        {
                            IntPtr ptr = EngineApi.ToCStringLen2(ctx, out UIntPtr len, nameVal, 0);
                            if (ptr != IntPtr.Zero)
                            {
                                try
                                {
                                    string full = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr, (int)len);
                                    type = AssemblyRegistry.FindTypeByFullName(full);
                                }
                                finally
                                {
                                    EngineApi.FreeCString(ctx, ptr);
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
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: typeof expects a type object.");
                }

                // Spec: System.Type ByObj. Do not EnsureBinding(typeof(Type)) — that pulls the
                // entire reflection surface and routinely blows the callback gate / FreeRuntime.
                JsValue handle = ObjectRegistry.PushObject(ctx, type, typeof(Type), default);
                EngineApi.SetPropertyStr(
                    ctx,
                    handle,
                    "__zents_type_name",
                    EngineApi.NewString(ctx, type.FullName ?? type.Name));
                return handle;
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue GetTypeFromNameCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                string name = ReadStringArg(ctx, argv, 0);
                Type type = AssemblyRegistry.FindTypeByFullName(name);
                if (type == null)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, $"zents: type not found: {name}");
                }

                return TypeRegistry.PushTypeObject(JsEnv.FromContext(ctx), type);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue EnsureAssemblyCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
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

        private static JsValue ResolveTypeCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
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

        private static JsValue CastCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 2)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: cast requires value and type.");
                }

                JsValue value = ArgReader.Read(argv, 0);
                JsValue typeVal = ArgReader.Read(argv, 1);
                if (!ObjectRegistry.TryGetObject(ctx, typeVal, out object typeObj) || !(typeObj is Type type))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: cast target is not a type.");
                }

                object managed = TypedMarshal.Pop(ctx, value, type);
                return TypedMarshal.Push(ctx, managed, type);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue BoxCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 2)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: box requires type and value.");
                }

                Type type = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                if (!type.IsValueType)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: box expects a value type or enum.");
                }

                object value = TypedMarshal.Pop(ctx, ArgReader.Read(argv, 1), type);
                // Force ByObj (even for enums/structs that default to number/ByVal).
                JsEnv env = JsEnv.FromContext(ctx);
                TypeBinding typeBinding = TypeRegistry.EnsureBinding(env, type);
                JsValue proto = JsValueUtil.GetNormTag(typeBinding.ByObjInstanceProto) == JsValueUtil.TagObject
                    ? typeBinding.ByObjInstanceProto
                    : typeBinding.InstanceProto;
                JsValue handle = ObjectRegistry.PushObject(ctx, value, type, proto);
                if (Emit.MetaBinding.TryWrapMissPublic(env, handle, out JsValue wrapped))
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

        private static JsValue UnboxCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 1)
                {
                    return JsValueUtil.Undefined;
                }

                if (!ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, 0), out object obj))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: unbox expects a CLR object.");
                }

                return TypedMarshal.Push(ctx, obj, obj.GetType());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }



        private static JsValue MakeGenericTypeCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type generic = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                if (generic == null || !generic.IsGenericTypeDefinition)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: make_generic_type: invalid generic type.");
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

        private static Type[] ReadTypeArgs(IntPtr ctx, JsValue jsArray)
        {
            if(EngineApi.IsArray(ctx,jsArray)==0)throw new JsScriptException("zents: generic type arguments require an Array");
            JsValue lengthValue=EngineApi.GetPropertyStr(ctx,jsArray,"length");int length;
            try{length=Convert.ToInt32(PrimitiveMarshal.Pop(ctx,lengthValue));}finally{JsValueUtil.Free(ctx,lengthValue);}
            var types=new Type[length];
            for(int i=0;i<length;++i)
            {
                if(EngineApi.HasOwnProperty(ctx,jsArray,i.ToString(System.Globalization.CultureInfo.InvariantCulture))!=1)throw new JsScriptException("zents: generic type argument array has holes");
                JsValue item=EngineApi.GetPropertyUint32(ctx,jsArray,(uint)i);
                try{types[i]=ResolveTypeArg(ctx,item);}finally{JsValueUtil.Free(ctx,item);}
            }
            return types;
        }

        private static Type ResolveTypeArg(IntPtr ctx, JsValue value)
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

            throw new JsScriptException("zents: generic type argument is not a Type.");
        }

        private static string ReadStringValue(IntPtr ctx, JsValue value)
        {
            IntPtr cstr = EngineApi.ToCStringLen2(ctx, out UIntPtr len, value, 0);
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
                EngineApi.FreeCString(ctx, cstr);
            }
        }

        private static MethodInfo FindPublicMethod(Type owner, string key, bool? isStatic, int genericArity = -1)
        {
            MethodInfo found = null;
            var methods = new System.Collections.Generic.List<MethodInfo>();
            for (Type t = owner; t != null; t = t.BaseType)
                methods.AddRange(t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
            if (isStatic != true) methods.AddRange(ExtensionMethodUtil.GetExtensionMethods(owner));
            foreach (MethodInfo m in methods)
            {
                bool ext = MethodOverloadDispatcher.IsExtensionMethod(m);
                if ((m.IsSpecialName && !m.Name.StartsWith("add_",StringComparison.Ordinal) && !m.Name.StartsWith("remove_",StringComparison.Ordinal)) || (isStatic.HasValue && (m.IsStatic && !ext) != isStatic.Value)) continue;
                if (genericArity >= 0 && (!m.IsGenericMethodDefinition || m.GetGenericArguments().Length != genericArity)) continue;
                if (key.Contains("(") ? MetaBinding.FormatFullSignatureKey(m) != key : MetaBinding.ResolveMemberName(m) != key) continue;
                // Derived declarations hide identical inherited CLR signatures.
                if (found != null && MetaBinding.FormatFullSignatureKey(found) != MetaBinding.FormatFullSignatureKey(m))
                    throw new JsScriptException("zents: ambiguous generic method: " + key);
                if (found == null) found = m;
            }
            return found;
        }

        private static bool IsByValReceiver(IntPtr ctx,JsValue receiver)
        {
            JsValue kind=EngineApi.GetPropertyStr(ctx,receiver,"__zents_ud_kind");
            try{return JsValueUtil.GetNormTag(kind)==JsValueUtil.TagString&&JsCallPlan.ReadText(ctx,kind)=="byval";}
            finally{JsValueUtil.Free(ctx,kind);}
        }

        private static JsValue ResolveSignatureCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc != 2) return JsValueUtil.Undefined;
                JsValue receiver = ArgReader.Read(argv, 0);
                string key = ReadStringArg(ctx, argv, 1);
                if (!key.Contains("(")) return JsValueUtil.Undefined;
                if (!ObjectRegistry.TryGetObject(ctx, receiver, out object obj)) return JsValueUtil.Undefined;
                bool isStatic = obj is Type && EngineApi.IsFunction(ctx, receiver) != 0;
                Type owner = isStatic ? (Type)obj : ObjectRegistry.TryGetViewType(ctx, receiver, out Type view) ? view : obj.GetType();
                MethodInfo method = FindPublicMethod(owner, key, isStatic);
                return method == null ? JsValueUtil.Undefined : MethodEmitter.EmitMethod(JsEnv.FromContext(ctx), method, owner, IsByValReceiver(ctx,receiver));
            }
            catch (Exception ex) { return Fail(ctx, ex); }
        }

        private static JsValue InflateCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc != 3) throw new JsScriptException("zents: inflate requires type, method name/signature and type arguments.");
                Type owner = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                string key = ReadStringArg(ctx, argv, 1);
                Type[] types = ReadTypeArgs(ctx, ArgReader.Read(argv, 2));
                MethodInfo open = FindPublicMethod(owner, key, null, types.Length);
                if (open == null) throw new JsScriptException("zents: generic method not found: " + key);
                return GetClosedGenericFunction(ctx, open.MakeGenericMethod(types), owner, false);
            }
            catch (Exception ex) { return Fail(ctx, ex); }
        }

        internal static JsValue GetClosedGenericFunction(IntPtr ctx, MethodInfo closed, Type owner, bool isByVal)
        {
            // The callable does not capture a receiver; JS this supplies it on every call.
            var key = (closed, owner, isByVal);
            if (ClosedGenericFunctions.TryGetValue(key, out JsValue cached)) return JsValueUtil.Dup(cached);
            JsValue fn = MethodEmitter.EmitMethod(JsEnv.FromContext(ctx), closed, owner, isByVal);
            ClosedGenericFunctions[key] = JsValueUtil.Dup(fn);
            return fn;
        }
        private static readonly System.Collections.Generic.Dictionary<(MethodInfo, Type, bool), JsValue> ClosedGenericFunctions =
            new System.Collections.Generic.Dictionary<(MethodInfo, Type, bool), JsValue>();





        internal static void ResetGenericMethodCache(IntPtr ctx)
        {
            lock (ClosedGenericFunctions)
            {
                foreach (JsValue v in ClosedGenericFunctions.Values)
                {
                    JsValueUtil.Free(ctx, v);
                }

                ClosedGenericFunctions.Clear();
            }
        }

        private static JsValue MakeSzArrayTypeCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
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

        private static JsValue NewSzArrayCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
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

        private static JsValue NewSzArrayByTypeCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type arrType = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                if (arrType == null || !arrType.IsArray || arrType.GetArrayRank() != 1)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: new_szarray_by_szarray_type expects a szarray type.");
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

        private static JsValue CreateSignatureCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                // argv[0] may be a JS Array of typeArgs, or we read argc args when called via JS_Call with rest.
                // zentslib passes a single JS array of typeArgs.
                Type[] types;
                if (argc >= 1 && EngineApi.IsArray(ctx, ArgReader.Read(argv, 0)) != 0)
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
                return EngineApi.NewString(ctx, sb.ToString());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue MakeMdArrayTypeCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type el = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                int rank = Convert.ToInt32(PrimitiveMarshal.Pop(ctx, ArgReader.Read(argv, 1)));
                if (rank < 1 || rank > 32)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: make_mdarray_type rank must be in [1, 32].");
                }

                return TypeRegistry.PushTypeObject(JsEnv.FromContext(ctx), el.MakeArrayType(rank));
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue NewMdArrayBySpecCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type el = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                int[] lowBounds = ReadIntJsArray(ctx, ArgReader.Read(argv, 1), "lowbounds");
                int[] sizes = ReadIntJsArray(ctx, ArgReader.Read(argv, 2), "sizes");
                if (lowBounds.Length != sizes.Length || sizes.Length == 0)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: new_mdarray_by_spec: lowbounds/sizes rank mismatch.");
                }

                Array arr = Array.CreateInstance(el, sizes, lowBounds);
                return TypedMarshal.Push(ctx, arr, arr.GetType());
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue NewMdArrayByTypeCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                Type mdType = ResolveTypeArg(ctx, ArgReader.Read(argv, 0));
                if (!mdType.IsArray || mdType.GetArrayRank() < 1)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: new_mdarray_by_mdarray_type expects mdarray type.");
                }

                Type el = mdType.GetElementType() ?? typeof(object);
                int[] lowBounds = ReadIntJsArray(ctx, ArgReader.Read(argv, 1), "lowbounds");
                int[] sizes = ReadIntJsArray(ctx, ArgReader.Read(argv, 2), "sizes");
                if (lowBounds.Length != sizes.Length || sizes.Length != mdType.GetArrayRank())
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: new_mdarray_by_mdarray_type: lowbounds/sizes rank mismatch.");
                }

                Array arr = Array.CreateInstance(el, sizes, lowBounds);
                return TypedMarshal.Push(ctx, arr, mdType);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static int[] ReadIntJsArray(IntPtr ctx, JsValue jsArray, string label)
        {
            if (EngineApi.IsArray(ctx, jsArray) == 0)
            {
                throw new JsScriptException($"zents: {label} must be a JS Array.");
            }

            object[] raw = (object[])ArrayMarshal.FromJsArray(ctx, jsArray);
            var result = new int[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == null || ReferenceEquals(raw[i], DBNull.Value))
                {
                    throw new JsScriptException($"zents: {label}[{i}] is missing.");
                }

                result[i] = Convert.ToInt32(raw[i]);
            }

            return result;
        }

        private static JsValue ToArrayCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (!ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, 0), out object obj) || !(obj is Array arr))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_array expects CLR array.");
                }

                return ArrayMarshal.ToJsArray(ctx, arr);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static readonly ConcurrentDictionary<Type, int> ManagedElementSizes = new ConcurrentDictionary<Type, int>();

        private static int ManagedElementSize(Type type) => ManagedElementSizes.GetOrAdd(type, element =>
        {
            // sizeof follows managed array layout. Marshal.SizeOf can instead
            // follow interop attributes and must not size a raw managed copy.
            var method = new DynamicMethod("ManagedSize", typeof(int), Type.EmptyTypes, typeof(ZentsLib).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Sizeof, element);
            il.Emit(OpCodes.Ret);
            return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
        });

        private static bool IsBlittableElement(Type type)
        {
            if (type == null || !type.IsValueType || type == typeof(bool) || type == typeof(char)) return false;
            if (type.IsPrimitive || type.IsEnum || type == typeof(IntPtr) || type == typeof(UIntPtr)) return true;
            if (!type.IsLayoutSequential && !type.IsExplicitLayout) return false;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (!field.FieldType.IsPointer && !IsBlittableElement(field.FieldType)) return false;
            return true;
        }

        private static JsValue ToBytesCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (!ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, 0), out object obj) || !(obj is Array arr))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_bytes expects CLR array.");
                }

                Type el = arr.GetType().GetElementType();
                if (el == typeof(bool) || el == typeof(char) || el == typeof(string) || (el != null && el.IsClass))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_bytes: element type is not blittable.");
                }

                if (arr is byte[] bytes)
                {
                    return ArrayMarshal.ToJsArray(ctx, bytes);
                }

                if (!IsBlittableElement(el))
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_bytes: element type is not blittable.");
                // Buffer.BlockCopy only accepts primitive arrays, not blittable structs.
                int size = ManagedElementSize(el);
                var raw = new byte[checked(arr.Length * size)];
                if (raw.Length != 0)
                {
                    GCHandle pin = GCHandle.Alloc(arr, GCHandleType.Pinned);
                    try { Marshal.Copy(pin.AddrOfPinnedObject(), raw, 0, raw.Length); }
                    finally { pin.Free(); }
                }
                return ArrayMarshal.ToJsArray(ctx, raw);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue ToDelegateCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                JsValue fn = ArgReader.Read(argv, 0);
                if (!ObjectRegistry.TryGetObject(ctx, ArgReader.Read(argv, 1), out object obj) || !(obj is Type delType))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_delegate expects delegate type.");
                }

                Delegate del = DelegateMarshal.FromJsFunction(ctx, fn, delType);
                return TypedMarshal.Push(ctx, del, delType);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue GetOpaqueCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (!OpaqueParameterScope.TryPop(ctx, ArgReader.Read(argv, 0), out object target))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: get_opaquevalue: not an opaque handle.");
                }

                ObjectRegistry.TryGetObject(ctx,ArgReader.Read(argv,0),out object held);
                var opaque=held as OpaqueValue;
                return TypedMarshal.Push(ctx,target,opaque?.DeclaredType??target?.GetType(),opaque?.Marshal);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue SetOpaqueCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                JsValue handle = ArgReader.Read(argv, 0);
                if (!ObjectRegistry.TryGetObject(ctx, handle, out object obj) || !(obj is OpaqueValue opaque))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: set_opaquevalue: not an opaque handle.");
                }

                // Validate scope first; an expired handle cannot mutate any later frame.
                OpaqueParameterScope.TryPop(ctx,handle,out _);
                object value=opaque.DeclaredType!=null?TypedMarshal.Pop(ctx,ArgReader.Read(argv,1),opaque.DeclaredType,opaque.Marshal):PrimitiveMarshal.Pop(ctx,ArgReader.Read(argv,1));
                OpaqueParameterScope.SetTarget(opaque, value);
                return JsValueUtil.Undefined;
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue ToUserDataCallback(IntPtr ctx, JsValue thisVal, int argc, IntPtr argv)
        {
            try
            {
                if (argc < 1)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_user_data requires an opaque handle.");
                }

                if (!OpaqueParameterScope.TryPop(ctx, ArgReader.Read(argv, 0), out object target))
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_user_data: not an opaque handle.");
                }

                if (target == null)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_user_data: null target.");
                }

                Type runtimeType = target.GetType();
                if (!runtimeType.IsValueType || runtimeType.IsPrimitive || runtimeType.IsEnum)
                {
                    return JsCallbackGate.ReturnErrorSentinel(ctx, "zents: to_user_data expects a struct opaque value.");
                }

                return StructMarshal.PushByVal(ctx, target);
            }
            catch (Exception ex)
            {
                return Fail(ctx, ex);
            }
        }

        private static JsValue Fail(IntPtr ctx, Exception ex)
        {
            JsCallbackBoundary.ThrowManaged(ctx, ex);
            return JsValueUtil.MakeErrorSentinel();
        }
    }

    internal static class ArgReader
    {
        public static JsValue Read(IntPtr argv, int index)
        {
            unsafe
            {
                return *(JsValue*)(argv + index * sizeof(JsValue));
            }
        }
    }
}
