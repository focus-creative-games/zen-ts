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
using System.Text;

namespace ZTS
{
    /// <summary>
    /// Synthesizes <c>csharp:</c> ES modules as source that re-exports
    /// <c>CSharp[assembly][typeFullName]</c> (Docs/spec/02-TYPE-SYSTEM.md §2.11).
    /// </summary>
    public static class CsharpVirtualModule
    {
        public static bool TrySynthesize(string specifier, out string source)
        {
            source = null;
            if (!JsModuleSpecifier.IsCsharp(specifier))
            {
                return false;
            }

            source = Synthesize(specifier);
            return true;
        }

        public static string Synthesize(string specifier)
        {
            if (!JsModuleSpecifier.IsCsharp(specifier))
            {
                throw new JsScriptException($"zts: invalid csharp module specifier: {specifier}");
            }

            string rest = specifier.Substring(JsModuleSpecifier.CsharpScheme.Length);
            if (string.IsNullOrEmpty(rest) || rest[0] == '/')
            {
                throw new JsScriptException($"zts: invalid csharp module specifier: {specifier}");
            }

            if (rest.IndexOf('/') != rest.LastIndexOf('/'))
            {
                throw new JsScriptException($"zts: invalid csharp module specifier: {specifier}");
            }

            string assemblyName;
            string path;
            int slash = rest.IndexOf('/');
            if (slash < 0)
            {
                assemblyName = rest;
                path = string.Empty;
            }
            else
            {
                assemblyName = rest.Substring(0, slash);
                path = rest.Substring(slash + 1);
            }

            if (string.IsNullOrEmpty(assemblyName))
            {
                throw new JsScriptException($"zts: invalid csharp module specifier: {specifier}");
            }

            Assembly asm = FindAssembly(assemblyName);
            if (asm == null)
            {
                throw new JsScriptException($"zts: assembly not found: {assemblyName}");
            }

            bool nestedModule = path.EndsWith("+", StringComparison.Ordinal);
            if (nestedModule)
            {
                path = path.Substring(0, path.Length - 1);
            }

            Type[] types = GetAssemblyTypes(asm);
            var exports = new List<(string ExportName, string FullName)>();

            if (nestedModule)
            {
                Type declaring = asm.GetType(path) ?? FindTypeByFullName(types, path);
                if (declaring == null)
                {
                    throw new JsScriptException($"zts: type not found: {path}");
                }

                foreach (Type nested in declaring.GetNestedTypes(BindingFlags.Public))
                {
                    TryAddExport(exports, nested);
                }
            }
            else
            {
                string ns = path;
                bool namespaceHasTypes = false;
                foreach (Type t in types)
                {
                    if (t == null || !t.IsPublic || t.IsNested)
                    {
                        continue;
                    }

                    string typeNs = t.Namespace ?? string.Empty;
                    if (typeNs == ns)
                    {
                        namespaceHasTypes = true;
                        TryAddExport(exports, t);
                    }
                }

                if (!namespaceHasTypes && !string.IsNullOrEmpty(ns))
                {
                    Type declaring = FindTypeByFullName(types, ns);
                    if (declaring != null)
                    {
                        foreach (Type nested in declaring.GetNestedTypes(BindingFlags.Public))
                        {
                            TryAddExport(exports, nested);
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"/* synthesized {specifier} */");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string exportName, string fullName) in exports)
            {
                if (!seen.Add(exportName))
                {
                    throw new JsScriptException(
                        $"zts: csharp export name conflict: {exportName} in {specifier}");
                }

                sb.Append("export const ");
                sb.Append(exportName);
                sb.Append(" = CSharp[");
                sb.Append(JsString(assemblyName));
                sb.Append("][");
                sb.Append(JsString(fullName));
                sb.AppendLine("];");
            }

            return sb.ToString();
        }

        public static string EncodeExportName(string clrName)
        {
            if (string.IsNullOrEmpty(clrName))
            {
                return clrName;
            }

            int tick = clrName.IndexOf('`');
            if (tick < 0)
            {
                return clrName;
            }

            return clrName.Substring(0, tick) + "$" + clrName.Substring(tick + 1);
        }

        public static bool IsJsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            char c0 = name[0];
            if (!(c0 == '_' || c0 == '$' || char.IsLetter(c0)))
            {
                return false;
            }

            for (int i = 1; i < name.Length; i++)
            {
                char c = name[i];
                if (!(c == '_' || c == '$' || char.IsLetterOrDigit(c)))
                {
                    return false;
                }
            }

            return true;
        }

        public static string ModuleSpecifier(string assemblySimpleName, string namespaceOrDeclaring, bool nestedForce = false)
        {
            if (string.IsNullOrEmpty(namespaceOrDeclaring))
            {
                return JsModuleSpecifier.CsharpScheme + assemblySimpleName;
            }

            string suffix = nestedForce ? "+" : string.Empty;
            return JsModuleSpecifier.CsharpScheme + assemblySimpleName + "/" + namespaceOrDeclaring + suffix;
        }

        private static void TryAddExport(List<(string ExportName, string FullName)> exports, Type t)
        {
            if (t == null || t.Name.IndexOf('<') >= 0 || t.Name.IndexOf('>') >= 0)
            {
                return;
            }

            string encoded = EncodeExportName(t.Name);
            if (!IsJsIdentifier(encoded))
            {
                return;
            }

            string fullName = t.FullName ?? t.Name;
            exports.Add((encoded, fullName));

            int tick = t.Name.IndexOf('`');
            if (tick > 0)
            {
                string sugar = t.Name.Substring(0, tick);
                if (IsJsIdentifier(sugar))
                {
                    bool taken = false;
                    foreach ((string n, string _) in exports)
                    {
                        if (n == sugar)
                        {
                            taken = true;
                            break;
                        }
                    }

                    if (!taken)
                    {
                        exports.Add((sugar, fullName));
                    }
                }
            }
        }

        private static Type[] GetAssemblyTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types ?? Array.Empty<Type>();
            }
        }

        private static Type FindTypeByFullName(Type[] types, string fullName)
        {
            foreach (Type t in types)
            {
                if (t != null && t.FullName == fullName)
                {
                    return t;
                }
            }

            return null;
        }

        private static Assembly FindAssembly(string simpleName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == simpleName)
                {
                    return asm;
                }
            }

            return null;
        }

        private static string JsString(string s)
        {
            if (s == null)
            {
                s = string.Empty;
            }

            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
