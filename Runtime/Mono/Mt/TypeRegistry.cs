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
using System.Reflection;
using ZenTS.Emit;
using ZenTS.Jvm;
using ZenTS.Utils;

namespace ZenTS.Mt
{
    internal sealed class TypeBinding
    {
        public Type Type;
        public bool IsNullable;
        /// <summary>JS-visible STO (may be miss-Proxy wrapped).</summary>
        public JSValue TypeObject;
        /// <summary>Unwrapped STO for host writes (<c>register_method</c>).</summary>
        public JSValue TypeObjectRaw;
        /// <summary>IEO prototype (not miss-wrapped). Classes: ByObj; structs: alias for <see cref="ByObjInstanceProto"/>.</summary>
        public JSValue InstanceProto;
        /// <summary>Struct ByVal instance dispatch proto; unused for reference types.</summary>
        public JSValue ByValInstanceProto;
        /// <summary>Struct ByObj instance dispatch proto; unused for reference types.</summary>
        public JSValue ByObjInstanceProto;
        public bool HasJsValues;
        public readonly HashSet<string> MemberKeys = new HashSet<string>(StringComparer.Ordinal);
    }

    internal static class TypeRegistry
    {
        private static readonly Dictionary<Type, TypeBinding> Bindings = new Dictionary<Type, TypeBinding>();

        public static TypeBinding EnsureBinding(JsEnv env, Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (Bindings)
            {
                if (Bindings.TryGetValue(type, out TypeBinding existing) && existing.HasJsValues)
                {
                    return existing;
                }
            }

            TypeBinding binding = MetaBinding.BuildTypeObject(env, type);
            lock (Bindings)
            {
                Bindings[type] = binding;
            }

            return binding;
        }

        public static JSValue PushTypeObject(JsEnv env, Type type)
        {
            TypeBinding binding = EnsureBinding(env, type);
            return JsValueUtil.Dup(binding.TypeObject);
        }

        public static bool TryGetBinding(Type type, out TypeBinding binding)
        {
            lock (Bindings)
            {
                return Bindings.TryGetValue(type, out binding) && binding.HasJsValues;
            }
        }

        public static JSValue GetInstanceProto(JsEnv env, Type type)
        {
            return JsValueUtil.Dup(EnsureBinding(env, type).InstanceProto);
        }

        public static void Release(JsEnv env)
        {
            if (env == null || !env.IsAlive)
            {
                Reset();
                return;
            }

            IntPtr ctx = env.Context;
            lock (Bindings)
            {
                foreach (TypeBinding b in Bindings.Values)
                {
                    if (!b.HasJsValues)
                    {
                        continue;
                    }

                    JsValueUtil.Free(ctx, b.TypeObject);
                    JsValueUtil.Free(ctx, b.TypeObjectRaw);
                    JsValueUtil.Free(ctx, b.InstanceProto);
                    if (IsDistinctJsObject(b.ByValInstanceProto, b.InstanceProto) &&
                        IsDistinctJsObject(b.ByValInstanceProto, b.ByObjInstanceProto))
                    {
                        JsValueUtil.Free(ctx, b.ByValInstanceProto);
                    }

                    if (IsDistinctJsObject(b.ByObjInstanceProto, b.InstanceProto))
                    {
                        JsValueUtil.Free(ctx, b.ByObjInstanceProto);
                    }

                    b.HasJsValues = false;
                }

                Bindings.Clear();
            }
        }

        public static void Reset()
        {
            lock (Bindings)
            {
                Bindings.Clear();
            }
        }

        private static bool IsDistinctJsObject(JSValue candidate, JSValue reference)
        {
            if (JsValueUtil.GetNormTag(candidate) != JsValueUtil.TagObject)
            {
                return false;
            }

            return JsValueUtil.GetNormTag(reference) != JsValueUtil.TagObject ||
                   candidate.Ptr != reference.Ptr;
        }
    }
}
