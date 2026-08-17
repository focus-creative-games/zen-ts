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
    /// JsExtensions XML registry: load rules; resolve extension classes on Bind (spec 13 §2).
    /// </summary>
    public static class JsExtensionXmlRegistry
    {
        private static readonly object s_gate = new object();
        private static List<JsExtensionXmlRule> s_rules = new List<JsExtensionXmlRule>();
        private static Dictionary<string, List<JsExtensionXmlRule>> s_rulesByTargetKey =
            new Dictionary<string, List<JsExtensionXmlRule>>(StringComparer.Ordinal);
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

        public static IReadOnlyList<JsExtensionXmlRule> Rules
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
                s_rules = new List<JsExtensionXmlRule>();
                s_rulesByTargetKey = new Dictionary<string, List<JsExtensionXmlRule>>(StringComparer.Ordinal);
                s_loaded = false;
            }
        }

        public static void Load(IEnumerable<string> configuredPaths, string projectRoot)
        {
            List<JsExtensionXmlRule> rules = JsExtensionXmlLoader.LoadFromConfiguredPaths(configuredPaths, projectRoot);
            var byKey = new Dictionary<string, List<JsExtensionXmlRule>>(StringComparer.Ordinal);
            for (int i = 0; i < rules.Count; i++)
            {
                JsExtensionXmlRule rule = rules[i];
                string key = TargetKey(rule.TargetAssemblyName, rule.TargetTypeFullName);
                if (!byKey.TryGetValue(key, out List<JsExtensionXmlRule> list))
                {
                    list = new List<JsExtensionXmlRule>();
                    byKey[key] = list;
                }

                list.Add(rule);
            }

            lock (s_gate)
            {
                s_rules = rules;
                s_rulesByTargetKey = byKey;
                s_loaded = true;
            }
        }

        /// <summary>
        /// Generate-time hard validation: target + extension types must resolve (spec 13).
        /// </summary>
        public static void ValidateAllLoadedAssemblies()
        {
            List<JsExtensionXmlRule> rules;
            lock (s_gate)
            {
                if (!s_loaded)
                {
                    return;
                }

                rules = new List<JsExtensionXmlRule>(s_rules);
            }

            for (int i = 0; i < rules.Count; i++)
            {
                JsExtensionXmlRule rule = rules[i];
                ResolveTypeOnAssembly(rule.TargetAssemblyName, rule.TargetTypeFullName, "target", rule.SourcePath);
                ResolveExtensionType(rule);
            }
        }

        /// <summary>
        /// Resolves extension classes configured for <paramref name="targetType"/> (exact type only).
        /// Unresolvable extension class → hard failure.
        /// </summary>
        public static bool TryGetExtensionTypes(Type targetType, out Type[] extensionTypes)
        {
            extensionTypes = Array.Empty<Type>();
            if (targetType == null)
            {
                return false;
            }

            string assemblyName = targetType.Assembly.GetName()?.Name;
            string typeFullName = targetType.FullName;
            if (string.IsNullOrEmpty(assemblyName) || string.IsNullOrEmpty(typeFullName))
            {
                return false;
            }

            List<JsExtensionXmlRule> rules;
            lock (s_gate)
            {
                if (!s_loaded
                    || !s_rulesByTargetKey.TryGetValue(TargetKey(assemblyName, typeFullName), out rules)
                    || rules == null
                    || rules.Count == 0)
                {
                    return false;
                }

                rules = new List<JsExtensionXmlRule>(rules);
            }

            var resolved = new List<Type>(rules.Count);
            var seen = new HashSet<Type>();
            for (int i = 0; i < rules.Count; i++)
            {
                Type extType = ResolveExtensionType(rules[i]);
                if (seen.Add(extType))
                {
                    resolved.Add(extType);
                }
            }

            extensionTypes = resolved.ToArray();
            return extensionTypes.Length > 0;
        }

        private static Type ResolveTypeOnAssembly(string assemblyName, string typeFullName, string role, string sourcePath)
        {
            Assembly assembly = FindAssembly(assemblyName);
            if (assembly == null)
            {
                throw new JsExtensionConfigurationException(
                    "[ZenTS] JsExtensions XML " + role + " Assembly '" + assemblyName
                    + "' not loaded (" + sourcePath + ")");
            }

            Type type = assembly.GetType(typeFullName, throwOnError: false, ignoreCase: false);
            if (type == null)
            {
                throw new JsExtensionConfigurationException(
                    "[ZenTS] JsExtensions XML " + role + " type '" + typeFullName
                    + "' not found in assembly '" + assemblyName
                    + "' (" + sourcePath + ")");
            }

            return type;
        }

        private static Type ResolveExtensionType(JsExtensionXmlRule rule)
        {
            return ResolveTypeOnAssembly(
                rule.ExtensionAssemblyName, rule.ExtensionTypeFullName, "extension", rule.SourcePath);
        }

        private static string TargetKey(string assemblyName, string typeFullName)
        {
            return assemblyName + "|" + typeFullName;
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
