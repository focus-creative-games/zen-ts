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

namespace ZenTS
{
    /// <summary>
    /// JsAlias XML registry: load by assembly name; bind lazily to MethodInfo metadata token.
    /// </summary>
    public static class JsAliasXmlRegistry
    {
        private static readonly object s_gate = new object();
        private static List<JsAliasXmlRule> s_rules = new List<JsAliasXmlRule>();
        private static Dictionary<string, List<JsAliasXmlRule>> s_rulesByAssemblyName =
            new Dictionary<string, List<JsAliasXmlRule>>(StringComparer.Ordinal);
        private static Dictionary<Assembly, Dictionary<int, string>> s_aliasByMethodToken =
            new Dictionary<Assembly, Dictionary<int, string>>();
        private static bool s_loaded;

        public static bool IsLoaded
        {
            get
            {
                lock (s_gate)
                {
                    return s_loaded;
                }
            }
        }

        public static IReadOnlyList<JsAliasXmlRule> Rules
        {
            get
            {
                lock (s_gate)
                {
                    return s_rules;
                }
            }
        }

        public static void Clear()
        {
            lock (s_gate)
            {
                s_rules = new List<JsAliasXmlRule>();
                s_rulesByAssemblyName = new Dictionary<string, List<JsAliasXmlRule>>(StringComparer.Ordinal);
                s_aliasByMethodToken = new Dictionary<Assembly, Dictionary<int, string>>();
                s_loaded = false;
            }
        }

        public static void Load(IEnumerable<string> configuredPaths, string projectRoot)
        {
            List<JsAliasXmlRule> rules = JsAliasXmlLoader.LoadFromConfiguredPaths(configuredPaths, projectRoot);
            var byName = new Dictionary<string, List<JsAliasXmlRule>>(StringComparer.Ordinal);
            for (int i = 0; i < rules.Count; i++)
            {
                JsAliasXmlRule rule = rules[i];
                if (!byName.TryGetValue(rule.AssemblyName, out List<JsAliasXmlRule> list))
                {
                    list = new List<JsAliasXmlRule>();
                    byName[rule.AssemblyName] = list;
                }

                list.Add(rule);
            }

            lock (s_gate)
            {
                s_rules = rules;
                s_rulesByAssemblyName = byName;
                s_aliasByMethodToken = new Dictionary<Assembly, Dictionary<int, string>>();
                s_loaded = true;
            }
        }

        public static void ValidateAllLoadedAssemblies()
        {
            lock (s_gate)
            {
                if (!s_loaded)
                {
                    return;
                }

                foreach (KeyValuePair<string, List<JsAliasXmlRule>> pair in s_rulesByAssemblyName)
                {
                    Assembly assembly = FindAssembly(pair.Key);
                    if (assembly == null)
                    {
                        throw new JsAliasConfigurationException(
                            "[ZenTS] JsAlias XML Assembly '" + pair.Key + "' not loaded (validation).");
                    }

                    EnsureBoundUnlocked(assembly);
                }
            }
        }

        public static void EnsureBound(Assembly assembly)
        {
            if (assembly == null)
            {
                return;
            }

            lock (s_gate)
            {
                EnsureBoundUnlocked(assembly);
            }
        }

        public static bool TryGetAlias(MethodInfo method, out string alias)
        {
            alias = null;
            MethodBase defMethod = ToDefinitionMethod(method);
            if (defMethod == null)
            {
                return false;
            }

            Assembly assembly = defMethod.Module.Assembly;
            int token = defMethod.MetadataToken;
            if (assembly == null || token == 0)
            {
                return false;
            }

            lock (s_gate)
            {
                Dictionary<int, string> map = EnsureBoundUnlocked(assembly);
                return map.TryGetValue(token, out alias);
            }
        }

        public static string FormatMethodSignature(MethodBase method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return "()";
            }

            var parts = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                parts[i] = FormatTypeName(parameters[i].ParameterType);
            }

            return "(" + string.Join(",", parts) + ")";
        }

        /// <summary>
        /// Type.FullName with byref '&amp;' suffix (matches ZLua FormatTypeName).
        /// </summary>
        public static string FormatTypeName(Type type)
        {
            if (type == null)
            {
                return "System.Void";
            }

            if (type.IsByRef)
            {
                return FormatTypeName(type.GetElementType()) + "&";
            }

            if (type.IsArray)
            {
                Type element = type.GetElementType();
                int rank = type.GetArrayRank();
                if (rank == 1 && type.Name.EndsWith("[]", StringComparison.Ordinal))
                {
                    return FormatTypeName(element) + "[]";
                }

                return FormatTypeName(element) + "[" + new string(',', rank - 1) + "]";
            }

            if (type.IsGenericParameter)
            {
                return type.Name;
            }

            return type.FullName ?? type.Name;
        }

        private static MethodBase ToDefinitionMethod(MethodBase method)
        {
            if (method == null)
            {
                return null;
            }

            Type declaring = method.DeclaringType;
            if (declaring != null && declaring.IsGenericType && !declaring.IsGenericTypeDefinition)
            {
                try
                {
                    return declaring.Module.ResolveMethod(method.MetadataToken);
                }
                catch (Exception)
                {
                    return method;
                }
            }

            return method;
        }

        private static Dictionary<int, string> EnsureBoundUnlocked(Assembly assembly)
        {
            if (s_aliasByMethodToken.TryGetValue(assembly, out Dictionary<int, string> existing))
            {
                return existing;
            }

            string assemblyName = assembly.GetName()?.Name;
            var map = new Dictionary<int, string>();
            if (assemblyName != null
                && s_rulesByAssemblyName.TryGetValue(assemblyName, out List<JsAliasXmlRule> rules)
                && rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    BindRule(rules[i], assembly, map);
                }
            }

            s_aliasByMethodToken[assembly] = map;
            return map;
        }

        private static void BindRule(JsAliasXmlRule rule, Assembly expectedAssembly, Dictionary<int, string> map)
        {
            Type type = expectedAssembly.GetType(rule.TypeFullName, throwOnError: false, ignoreCase: false);
            if (type == null)
            {
                throw new JsAliasConfigurationException(
                    "[ZenTS] JsAlias XML Type '" + rule.TypeFullName + "' not found in assembly '"
                    + expectedAssembly.GetName().Name + "' (" + rule.SourcePath + ")");
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                throw new JsAliasConfigurationException(
                    "[ZenTS] JsAlias XML Type must be an open generic definition or non-generic type: "
                    + rule.TypeFullName + " in " + rule.SourcePath);
            }

            MethodBase method = ResolveMethod(type, rule.MethodName, rule.Signature, rule.SourcePath);
            int token = method.MetadataToken;
            if (map.ContainsKey(token))
            {
                throw new JsAliasConfigurationException(
                    "[ZenTS] JsAlias XML duplicate method token 0x" + token.ToString("X8")
                    + " while binding " + rule.SourcePath);
            }

            map[token] = rule.Alias;
        }

        private static MethodBase ResolveMethod(Type type, string methodName, string signature, string sourcePath)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static
                                       | BindingFlags.DeclaredOnly;

            MethodInfo[] methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(FormatMethodSignature(method), signature, StringComparison.Ordinal))
                {
                    return method;
                }
            }

            if (string.Equals(methodName, ".ctor", StringComparison.Ordinal))
            {
                ConstructorInfo[] ctors = type.GetConstructors(flags);
                for (int i = 0; i < ctors.Length; i++)
                {
                    if (string.Equals(FormatMethodSignature(ctors[i]), signature, StringComparison.Ordinal))
                    {
                        return ctors[i];
                    }
                }
            }

            throw new JsAliasConfigurationException(
                "[ZenTS] JsAlias XML Method '" + methodName + signature + "' not found on "
                + type.FullName + " in " + sourcePath);
        }

        private static Assembly FindAssembly(string assemblyName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyName name = assemblies[i].GetName();
                if (name != null && string.Equals(name.Name, assemblyName, StringComparison.Ordinal))
                {
                    return assemblies[i];
                }
            }

            return null;
        }
    }
}
