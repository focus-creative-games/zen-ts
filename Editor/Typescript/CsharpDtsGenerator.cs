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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ZenTS.Editor.Typescript
{
    /// <summary>
    /// Emits <c>declare module "csharp:…"</c> for Settings.typescriptBindingAssemblies
    /// (same type set Il2Cpp Generate will bind). Docs/spec/14-TYPESCRIPT.md §6.
    /// </summary>
    public static class CsharpDtsGenerator
    {
        public static void Generate()
        {
            string[] assemblies = Settings.Instance.typescriptBindingAssemblies ?? Array.Empty<string>();
            var types = new List<Type>();
            foreach (string name in assemblies)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                Assembly asm = FindAssembly(name.Trim());
                if (asm == null)
                {
                    Debug.Log($"[ZenTS] Generate Typings: skip missing assembly '{name}'");
                    continue;
                }

                Type[] loaded;
                try
                {
                    loaded = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    loaded = ex.Types ?? Array.Empty<Type>();
                }

                foreach (Type t in loaded)
                {
                    if (t == null || !(t.IsPublic || t.IsNestedPublic))
                    {
                        continue;
                    }

                    if (t.Name.IndexOf('<') >= 0 || t.Name.IndexOf('>') >= 0)
                    {
                        continue;
                    }

                    if (t.IsGenericType && !t.IsGenericTypeDefinition)
                    {
                        continue;
                    }

                    types.Add(t);
                }
            }

            var known = new HashSet<Type>(types);
            var modules = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            foreach (Type t in types)
            {
                string spec = SpecifierFor(t);
                if (!modules.TryGetValue(spec, out List<Type> list))
                {
                    list = new List<Type>();
                    modules[spec] = list;
                }

                list.Add(t);
            }

            string root = TsProjectPaths.GeneratedCsharpDir;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(root);

            foreach (KeyValuePair<string, List<Type>> kv in modules.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                string rel = FilePathForSpecifier(kv.Key);
                string path = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
                File.WriteAllText(path, RenderModule(kv.Key, kv.Value, known), new UTF8Encoding(false));
            }

            Debug.Log($"[ZenTS] Generate Typings: {types.Count} types → {modules.Count} modules under {root}");
        }

        private static string SpecifierFor(Type t)
        {
            string asm = t.Assembly.GetName().Name;
            if (t.IsNested)
            {
                Type decl = t.DeclaringType;
                string full = decl != null ? (decl.FullName ?? decl.Name) : t.FullName;
                return CsharpVirtualModule.ModuleSpecifier(asm, full, nestedForce: true);
            }

            return CsharpVirtualModule.ModuleSpecifier(asm, t.Namespace ?? string.Empty);
        }

        private static string FilePathForSpecifier(string specifier)
        {
            string rest = specifier.Substring(JsModuleSpecifier.CsharpScheme.Length);
            rest = rest.TrimEnd('+');
            string[] parts = rest.Split(new[] { '/' }, 2);
            string asm = SanitizePath(parts[0]);
            if (parts.Length == 1 || string.IsNullOrEmpty(parts[1]))
            {
                return Path.Combine(asm, "_global.d.ts");
            }

            return Path.Combine(asm, SanitizePath(parts[1]) + ".d.ts");
        }

        private static string SanitizePath(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }

            return s.Replace('+', '_');
        }

        private static string RenderModule(string specifier, List<Type> types, HashSet<Type> known)
        {
            var sb = new StringBuilder();
            sb.AppendLine("/* generated by ZenTS/Generate Typings — do not edit */");
            sb.Append("declare module ").Append(TsString(specifier)).AppendLine(" {");

            var exportNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type t in types.OrderBy(x => x.FullName, StringComparer.Ordinal))
            {
                RenderType(sb, t, specifier, known, exportNames);
            }

            sb.AppendLine("}");
            sb.AppendLine();
            return sb.ToString();
        }

        private static void RenderType(
            StringBuilder sb,
            Type t,
            string moduleSpecifier,
            HashSet<Type> known,
            HashSet<string> exportNames)
        {
            string encoded = CsharpVirtualModule.EncodeExportName(t.Name);
            if (!CsharpVirtualModule.IsJsIdentifier(encoded) || !exportNames.Add(encoded))
            {
                return;
            }

            if (t.IsEnum)
            {
                RenderEnum(sb, t, encoded);
                return;
            }

            if (t.IsGenericTypeDefinition)
            {
                int arity = t.GetGenericArguments().Length;
                sb.Append("  export const ").Append(encoded)
                    .Append(": ZenTS.GenericDef<").Append(arity.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(">;");
                int tick = t.Name.IndexOf('`');
                if (tick > 0)
                {
                    string sugar = t.Name.Substring(0, tick);
                    if (CsharpVirtualModule.IsJsIdentifier(sugar) && exportNames.Add(sugar))
                    {
                        sb.Append("  export const ").Append(sugar).Append(": typeof ").Append(encoded).AppendLine(";");
                    }
                }

                return;
            }

            bool isStruct = t.IsValueType && !t.IsEnum;
            bool isStatic = t.IsAbstract && t.IsSealed;
            bool isInterface = t.IsInterface;

            sb.Append("  export class ").Append(encoded).AppendLine(" {");

            if (isStatic || isInterface)
            {
                sb.AppendLine("    private constructor();");
            }
            else
            {
                RenderConstructors(sb, t, moduleSpecifier, known);
            }

            if (isStruct)
            {
                sb.Append("    static _default(): ").Append(encoded).AppendLine(";");
            }

            RenderMembers(sb, t, moduleSpecifier, known);
            sb.AppendLine("  }");
        }

        private static void RenderEnum(StringBuilder sb, Type t, string encoded)
        {
            sb.Append("  export const ").Append(encoded).AppendLine(": {");
            foreach (string name in Enum.GetNames(t))
            {
                if (!CsharpVirtualModule.IsJsIdentifier(name))
                {
                    continue;
                }

                long raw = Convert.ToInt64(Enum.Parse(t, name), CultureInfo.InvariantCulture);
                sb.Append("    readonly ").Append(name).Append(": ")
                    .Append(raw.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            }

            sb.AppendLine("  };");
            sb.Append("  export type ").Append(encoded).AppendLine(" = number;");
        }

        private static void RenderConstructors(StringBuilder sb, Type t, string moduleSpecifier, HashSet<Type> known)
        {
            ConstructorInfo[] ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0)
            {
                sb.AppendLine("    private constructor();");
                return;
            }

            foreach (ConstructorInfo ctor in ctors)
            {
                sb.Append("    constructor(");
                AppendParams(sb, ctor.GetParameters(), moduleSpecifier, known);
                sb.AppendLine(");");
            }
        }

        private static void RenderMembers(StringBuilder sb, Type t, string moduleSpecifier, HashSet<Type> known)
        {
            var seenFields = new HashSet<string>(StringComparer.Ordinal);
            var seenProps = new HashSet<string>(StringComparer.Ordinal);
            var seenMethods = new HashSet<string>(StringComparer.Ordinal);
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (Type slice in EnumerateChain(t))
            {
                foreach (FieldInfo field in slice.GetFields(flags))
                {
                    if (!seenFields.Add(field.Name))
                    {
                        continue;
                    }

                    string ts = MapType(field.FieldType, moduleSpecifier, known);
                    string mod = field.IsStatic ? "static " : "";
                    string ro = field.IsInitOnly || field.IsLiteral ? "readonly " : "";
                    sb.Append("    ").Append(mod).Append(ro).Append(Ident(field.Name)).Append(": ").Append(ts)
                        .AppendLine(";");
                }

                foreach (PropertyInfo prop in slice.GetProperties(flags))
                {
                    ParameterInfo[] idx = prop.GetIndexParameters();
                    if (idx.Length > 0)
                    {
                        if (prop.CanRead && seenProps.Add("get_Item"))
                        {
                            sb.Append("    ").Append(prop.GetGetMethod()?.IsStatic == true ? "static " : "")
                                .Append("get_Item(");
                            AppendParams(sb, idx, moduleSpecifier, known);
                            sb.Append("): ").Append(MapType(prop.PropertyType, moduleSpecifier, known))
                                .AppendLine(";");
                        }

                        if (prop.CanWrite && seenProps.Add("set_Item"))
                        {
                            sb.Append("    ").Append(prop.GetSetMethod()?.IsStatic == true ? "static " : "")
                                .Append("set_Item(");
                            AppendParams(sb, idx, moduleSpecifier, known);
                            if (idx.Length > 0)
                            {
                                sb.Append(", ");
                            }

                            sb.Append("value: ").Append(MapType(prop.PropertyType, moduleSpecifier, known))
                                .AppendLine("): void;");
                        }

                        continue;
                    }

                    if (!seenProps.Add(prop.Name))
                    {
                        continue;
                    }

                    string mod = (prop.GetMethod ?? prop.SetMethod)?.IsStatic == true ? "static " : "";
                    if (prop.CanRead && prop.CanWrite)
                    {
                        sb.Append("    ").Append(mod).Append(Ident(prop.Name)).Append(": ")
                            .Append(MapType(prop.PropertyType, moduleSpecifier, known)).AppendLine(";");
                    }
                    else if (prop.CanRead)
                    {
                        sb.Append("    ").Append(mod).Append("readonly ").Append(Ident(prop.Name)).Append(": ")
                            .Append(MapType(prop.PropertyType, moduleSpecifier, known)).AppendLine(";");
                    }
                    else if (prop.CanWrite)
                    {
                        sb.Append("    ").Append(mod).Append("set ").Append(Ident(prop.Name)).Append("(v: ")
                            .Append(MapType(prop.PropertyType, moduleSpecifier, known)).AppendLine(");");
                    }
                }

                foreach (EventInfo ev in slice.GetEvents(flags))
                {
                    string addName = "add_" + ev.Name;
                    if (!seenMethods.Add(addName))
                    {
                        continue;
                    }

                    seenMethods.Add("remove_" + ev.Name);
                    string handler = MapType(ev.EventHandlerType, moduleSpecifier, known);
                    string mod = ev.GetAddMethod()?.IsStatic == true ? "static " : "";
                    sb.Append("    ").Append(mod).Append(addName).Append("(handler: ")
                        .Append(handler).AppendLine("): void;");
                    sb.Append("    ").Append(mod).Append("remove_").Append(ev.Name).Append("(handler: ")
                        .Append(handler).AppendLine("): void;");
                }

                var groups = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
                foreach (MethodInfo method in slice.GetMethods(flags))
                {
                    if (method.IsSpecialName || method.IsGenericMethodDefinition || method.Name == "Finalize")
                    {
                        continue;
                    }

                    string key = ResolveMemberName(method);
                    if (!groups.TryGetValue(key, out List<MethodInfo> list))
                    {
                        list = new List<MethodInfo>();
                        groups[key] = list;
                    }

                    list.Add(method);
                }

                foreach (KeyValuePair<string, List<MethodInfo>> kv in groups)
                {
                    if (!seenMethods.Add(kv.Key))
                    {
                        continue;
                    }

                    foreach (MethodInfo method in kv.Value)
                    {
                        RenderMethod(sb, method, kv.Key, moduleSpecifier, known, skipFirst: false);
                    }

                    if (kv.Value.Count >= 2)
                    {
                        foreach (MethodInfo method in kv.Value)
                        {
                            string sigKey = FormatSignatureKey(method);
                            if (!seenMethods.Add(sigKey))
                            {
                                continue;
                            }

                            RenderMethod(sb, method, sigKey, moduleSpecifier, known, skipFirst: false, quotedName: true);
                        }
                    }
                }
            }

            foreach (MethodInfo ext in EnumerateExtensionMethods(t))
            {
                string key = ResolveMemberName(ext);
                if (!seenMethods.Add(key))
                {
                    continue;
                }

                RenderMethod(sb, ext, key, moduleSpecifier, known, skipFirst: true);
            }
        }

        private static IEnumerable<MethodInfo> EnumerateExtensionMethods(Type extendedType)
        {
            var seen = new HashSet<MethodInfo>();
            for (Type t = extendedType; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (JsExtensionAttribute attr in t.GetCustomAttributes<JsExtensionAttribute>(inherit: false))
                {
                    if (attr.ExtensionTypes == null)
                    {
                        continue;
                    }

                    foreach (Type extType in attr.ExtensionTypes)
                    {
                        if (extType == null)
                        {
                            continue;
                        }

                        foreach (MethodInfo method in extType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            ParameterInfo[] ps = method.GetParameters();
                            if (ps.Length == 0 || !ps[0].ParameterType.IsAssignableFrom(extendedType))
                            {
                                continue;
                            }

                            if (seen.Add(method))
                            {
                                yield return method;
                            }
                        }
                    }
                }
            }
        }

        private static void RenderMethod(
            StringBuilder sb,
            MethodInfo method,
            string jsName,
            string moduleSpecifier,
            HashSet<Type> known,
            bool skipFirst,
            bool quotedName = false)
        {
            ParameterInfo[] ps = method.GetParameters();
            int start = skipFirst ? 1 : 0;
            var slice = new ParameterInfo[Math.Max(0, ps.Length - start)];
            Array.Copy(ps, start, slice, 0, slice.Length);

            bool isStatic = method.IsStatic && !skipFirst;
            sb.Append("    ");
            if (isStatic)
            {
                sb.Append("static ");
            }

            if (quotedName || !CsharpVirtualModule.IsJsIdentifier(jsName))
            {
                sb.Append("[\"").Append(jsName.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"](");
            }
            else
            {
                sb.Append(jsName).Append("(");
            }

            AppendParams(sb, slice, moduleSpecifier, known);
            sb.Append("): ")
                .Append(method.ReturnType == typeof(void) ? "void" : MapType(method.ReturnType, moduleSpecifier, known))
                .AppendLine(";");
        }

        private static void AppendParams(
            StringBuilder sb,
            ParameterInfo[] ps,
            string moduleSpecifier,
            HashSet<Type> known)
        {
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                ParameterInfo p = ps[i];
                string name = CsharpVirtualModule.IsJsIdentifier(p.Name) ? p.Name : ("p" + i);
                bool isParams = p.GetCustomAttribute<ParamArrayAttribute>() != null;
                if (isParams)
                {
                    sb.Append("...");
                }
                else if (p.IsOptional)
                {
                    name += "?";
                }

                sb.Append(name).Append(": ").Append(MapParamType(p, moduleSpecifier, known));
            }
        }

        private static string MapParamType(ParameterInfo p, string moduleSpecifier, HashSet<Type> known)
        {
            Type t = p.ParameterType;
            if (t.IsByRef)
            {
                return "ZenTS.OpaqueHandle<" + MapType(t.GetElementType(), moduleSpecifier, known) + ">";
            }

            return MapType(t, moduleSpecifier, known);
        }

        private static string MapType(Type t, string moduleSpecifier, HashSet<Type> known)
        {
            if (t == null || t == typeof(void))
            {
                return "void";
            }

            if (t == typeof(string))
            {
                return "string";
            }

            if (t == typeof(bool))
            {
                return "boolean";
            }

            if (t == typeof(object))
            {
                return "any";
            }

            if (t.IsPrimitive || t == typeof(decimal) || t == typeof(IntPtr) || t == typeof(UIntPtr))
            {
                return "number";
            }

            if (t.IsEnum)
            {
                return "number";
            }

            if (t.IsArray && t.GetArrayRank() == 1)
            {
                string e = MapType(t.GetElementType(), moduleSpecifier, known);
                return "ZenTS.SzArray<" + e + "> | " + e + "[]";
            }

            Type nullable = Nullable.GetUnderlyingType(t);
            if (nullable != null)
            {
                return MapType(nullable, moduleSpecifier, known) + " | null";
            }

            if (typeof(Delegate).IsAssignableFrom(t) && t != typeof(Delegate) && t != typeof(MulticastDelegate))
            {
                MethodInfo invoke = t.GetMethod("Invoke");
                if (invoke != null)
                {
                    var sb = new StringBuilder();
                    sb.Append("(");
                    AppendParams(sb, invoke.GetParameters(), moduleSpecifier, known);
                    sb.Append(") => ");
                    sb.Append(invoke.ReturnType == typeof(void)
                        ? "void"
                        : MapType(invoke.ReturnType, moduleSpecifier, known));
                    return sb.ToString();
                }
            }

            if (t.IsGenericTypeDefinition)
            {
                return "ZenTS.GenericDef<" + t.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture) + ">";
            }

            if (known.Contains(t) && !t.IsGenericType && SpecifierFor(t) == moduleSpecifier)
            {
                string encoded = CsharpVirtualModule.EncodeExportName(t.Name);
                if (CsharpVirtualModule.IsJsIdentifier(encoded))
                {
                    return encoded;
                }
            }

            return "any";
        }

        private static IEnumerable<Type> EnumerateChain(Type type)
        {
            var list = new List<Type>();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                list.Add(t);
            }

            return list;
        }

        private static string ResolveMemberName(MemberInfo member)
        {
            JsAliasAttribute alias = member.GetCustomAttribute<JsAliasAttribute>();
            if (alias != null && !string.IsNullOrEmpty(alias.Alias))
            {
                return alias.Alias;
            }

            return member.Name;
        }

        private static string FormatSignatureKey(MethodInfo method)
        {
            ParameterInfo[] ps = method.GetParameters();
            var sb = new StringBuilder(method.Name);
            sb.Append('(');
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                Type pt = ps[i].ParameterType;
                sb.Append(pt.FullName ?? pt.Name);
            }

            sb.Append(')');
            return sb.ToString();
        }

        private static string Ident(string name)
        {
            return CsharpVirtualModule.IsJsIdentifier(name) ? name : ("[\"" + name + "\"]");
        }

        private static string TsString(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
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
    }
}
