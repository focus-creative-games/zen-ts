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
using ZTS.Jvm;
using ZTS.Utils;

namespace ZTS.Mt
{
    /// <summary>
    /// Lazy <c>CSharp[assembly][type]</c> resolution (Proxy installed by ztslib.js).
    /// </summary>
    internal static class AssemblyRegistry
    {
        private static readonly Dictionary<string, Assembly> AssembliesByName =
            new Dictionary<string, Assembly>(StringComparer.Ordinal);

        private static bool _installed;

        public static void EnsureCSharpRoot(JsEnv env)
        {
            // Proxy root is installed by ztslib.js after native hooks are bound.
            _installed = true;
        }

        public static void EnsureAssemblyExists(string assemblyName)
        {
            if (ResolveAssembly(assemblyName) == null)
            {
                throw new JsScriptException($"zts: assembly not found: {assemblyName}");
            }
        }

        public static Type ResolveType(string assemblyName, string typeName)
        {
            Assembly assembly = ResolveAssembly(assemblyName);
            if (assembly == null)
            {
                throw new JsScriptException($"zts: assembly not found: {assemblyName}");
            }

            Type type = FindTypeInAssembly(assembly, typeName);
            if (type == null)
            {
                throw new JsScriptException($"zts: type not found: {typeName}");
            }

            return type;
        }

        public static Assembly ResolveAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return null;
            }

            lock (AssembliesByName)
            {
                if (AssembliesByName.TryGetValue(assemblyName, out Assembly cached))
                {
                    return cached;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (string.Equals(name, assemblyName, StringComparison.Ordinal))
                {
                    lock (AssembliesByName)
                    {
                        AssembliesByName[assemblyName] = assembly;
                    }

                    return assembly;
                }
            }

            return null;
        }

        public static Type FindTypeInAssembly(Assembly assembly, string typeName)
        {
            Type direct = assembly.GetType(typeName, throwOnError: false);
            if (direct != null)
            {
                return direct;
            }

            string[] parts = typeName.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            for (int nestStart = parts.Length - 1; nestStart >= 1; nestStart--)
            {
                string nsAndOuter = string.Join(".", parts, 0, nestStart);
                string nested = string.Join("+", parts, nestStart, parts.Length - nestStart);
                string candidate = nsAndOuter + "+" + nested;
                direct = assembly.GetType(candidate, throwOnError: false);
                if (direct != null)
                {
                    return direct;
                }
            }

            return null;
        }

        public static Type FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Type t = Type.GetType(fullName, throwOnError: false);
            if (t != null)
            {
                return t;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = assembly.GetType(fullName, throwOnError: false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        public static void Release(JsEnv env)
        {
            Reset();
        }

        public static void Reset()
        {
            lock (AssembliesByName)
            {
                AssembliesByName.Clear();
            }

            _installed = false;
        }
    }
}
