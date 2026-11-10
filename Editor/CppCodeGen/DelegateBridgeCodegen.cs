// Copyright 2026 Code Philosophy. MIT license; see package LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace ZenTS.CppCodeGen
{
    // AOT thunks use real C++ parameter/return types, so ARM64 HFA and platform
    // aggregate return conventions are lowered by the target compiler.
    public static class DelegateBridgeCodegen
    {
        private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields()
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
            .ToDictionary(o => o.Value);

        public static void Generate(string directory)
        {
            var delegates = new HashSet<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;
                string name = assembly.GetName().Name;
                if (name == "mscorlib" || name == "netstandard" || name.StartsWith("System") || name.StartsWith("UnityEditor"))
                    continue;
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (Type type in types)
                {
                    AddType(type, delegates);
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    foreach (FieldInfo field in type.GetFields(flags))
                        AddType(field.FieldType, delegates);
                    foreach (MethodBase method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
                    {
                        if (method is MethodInfo info)
                            AddType(info.ReturnType, delegates);
                        foreach (ParameterInfo parameter in method.GetParameters())
                            AddType(parameter.ParameterType, delegates);
                        ScanCalls(method, delegates);
                    }
                }
            }
            Write(directory, delegates);
        }

        private static void AddType(Type type, HashSet<Type> delegates)
        {
            if (type == null || type.ContainsGenericParameters)
                return;
            if (type.HasElementType)
            {
                AddType(type.GetElementType(), delegates);
                return;
            }
            if (typeof(MulticastDelegate).IsAssignableFrom(type) && type != typeof(MulticastDelegate))
                delegates.Add(type);
            if (type.IsGenericType)
                foreach (Type arg in type.GetGenericArguments())
                    AddType(arg, delegates);
        }

        private static void ScanCalls(MethodBase method, HashSet<Type> delegates)
        {
            byte[] il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (InvalidOperationException) { return; }
            if (il == null)
                return;
            for (int offset = 0; offset < il.Length;)
            {
                short code = il[offset++];
                if (code == 0xfe)
                    code = (short)(0xfe00 | il[offset++]);
                if (!Opcodes.TryGetValue(code, out OpCode op))
                    throw new InvalidOperationException("Invalid IL opcode in " + method);
                int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        size = 0;
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        size = 1;
                        break;
                    case OperandType.InlineVar:
                        size = 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        size = 8;
                        break;
                    case OperandType.InlineSwitch:
                        size = 4 + 4 * BitConverter.ToInt32(il, offset);
                        break;
                    default:
                        size = 4;
                        break;
                }
                if (op.OperandType == OperandType.InlineMethod)
                {
                    try
                    {
                        MethodBase call = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                        AddType(call.DeclaringType, delegates);
                        if (call is MethodInfo info)
                        {
                            AddType(info.ReturnType, delegates);
                            if (info.IsGenericMethod)
                                foreach (Type arg in info.GetGenericArguments())
                                    AddType(arg, delegates);
                        }
                    }
                    catch (ArgumentException) { } // Open generic bodies have no concrete AOT shape yet.
                }
                offset += size;
            }
        }

        private static string Key(Type type)
        {
            if (type.IsByRef)
                return "&" + Key(type.GetElementType());
            if (type.IsPointer)
                return "p";
            if (type.IsEnum)
                return Key(Enum.GetUnderlyingType(type));
            if (!type.IsValueType)
                return "o";
            if (type == typeof(void))
                return "v";
            if (type == typeof(float))
                return "f";
            if (type == typeof(double))
                return "d";
            if (type == typeof(long) || type == typeof(ulong))
                return "l";
            if (type == typeof(IntPtr) || type == typeof(UIntPtr))
                return "n";
            if (type.IsPrimitive)
                return "i";
            return "{" + type + "}";
        }

        private static string Cpp(Type type, StringBuilder definitions, Dictionary<Type, string> structs)
        {
            if (type.IsByRef || type.IsPointer)
                return "void*";
            if (type.IsEnum)
                type = Enum.GetUnderlyingType(type);
            if (!type.IsValueType)
                return "Il2CppObject*";
            if (type == typeof(void))
                return "void";
            if (type == typeof(bool))
                return "bool";
            if (type == typeof(byte))
                return "uint8_t";
            if (type == typeof(sbyte))
                return "int8_t";
            if (type == typeof(char) || type == typeof(ushort))
                return "uint16_t";
            if (type == typeof(short))
                return "int16_t";
            if (type == typeof(int))
                return "int32_t";
            if (type == typeof(uint))
                return "uint32_t";
            if (type == typeof(long))
                return "int64_t";
            if (type == typeof(ulong))
                return "uint64_t";
            if (type == typeof(IntPtr))
                return "intptr_t";
            if (type == typeof(UIntPtr))
                return "uintptr_t";
            if (type == typeof(float))
                return "float";
            if (type == typeof(double))
                return "double";
            if (structs.TryGetValue(type, out string existing))
                return existing;
            if (type.IsExplicitLayout || type.IsAutoLayout || type.StructLayoutAttribute.Size != 0)
                throw new NotSupportedException("Delegate AOT layout requires a sequential, naturally sized struct: " + type);
            string name = "DelegateValue" + structs.Count;
            structs[type] = name;
            var fields = new StringBuilder();
            int index = 0;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => f.MetadataToken))
                fields.Append("    ").Append(Cpp(field.FieldType, definitions, structs)).Append(" f").Append(index++).AppendLine(";");
            int pack = type.StructLayoutAttribute.Pack;
            if (pack != 0)
                definitions.AppendLine("#pragma pack(push," + pack + ")");
            definitions.AppendLine("struct " + name + " {").Append(fields).AppendLine("};");
            if (pack != 0)
                definitions.AppendLine("#pragma pack(pop)");
            return name;
        }

        public static void Write(string directory, IEnumerable<Type> delegates)
        {
            var definitions = new StringBuilder();
            var functions = new StringBuilder();
            var entries = new SortedDictionary<string, string>();
            var structs = new Dictionary<Type, string>();
            foreach (Type type in delegates.OrderBy(t => t.ToString(), StringComparer.Ordinal))
            {
                MethodInfo invoke = type.GetMethod("Invoke");
                if (invoke == null || invoke.ReturnType.IsByRef)
                    continue;
                ParameterInfo[] parameters = invoke.GetParameters();
                string key = Key(invoke.ReturnType) + "(" + string.Join(",", parameters.Select(p => Key(p.ParameterType))) + ")";
                // A key groups ABI-compatible small integers. Keep their exact widths
                // separate so writers never overrun a narrow parameter or return slot.
                key = (type.FullName ?? type.ToString()) + ":" + key;
                if (entries.ContainsKey(key))
                    continue;
                int definitionLength = definitions.Length;
                var previousTypes = new HashSet<Type>(structs.Keys);
                string ret;
                string[] args;
                try
                {
                    ret = Cpp(invoke.ReturnType, definitions, structs);
                    args = parameters.Select(p => Cpp(p.ParameterType, definitions, structs)).ToArray();
                }
                catch (NotSupportedException)
                {
                    definitions.Length = definitionLength;
                    foreach (Type added in structs.Keys.Where(t => !previousTypes.Contains(t)).ToArray())
                        structs.Remove(added);
                    continue;
                }
                string fn = "GeneratedDelegate" + entries.Count;
                functions.Append("static ").Append(ret).Append(' ').Append(fn).Append("(Il2CppObject* target");
                for (int i = 0; i < args.Length; ++i)
                    functions.Append(',').Append(args[i]).Append(" a").Append(i);
                functions.AppendLine(",const MethodInfo* method) {");
                functions.Append("    void* args[] = {");
                for (int i = 0; i < args.Length; ++i)
                    functions.Append(args[i].EndsWith("*") ? "(void*)a" : "(void*)&a").Append(i).Append(',');
                functions.AppendLine("nullptr};");
                functions.AppendLine(ret == "void" ? "    InvokeJsDelegate(method,target,args,nullptr);" : "    " + ret + " result{}; InvokeJsDelegate(method,target,args,&result); return result;");
                functions.AppendLine("}");
                entries[key] = fn;
            }
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "DelegateBridgeStub.h"), "#pragma once\n#define ZENTS_HAS_GENERATED_DELEGATE_BRIDGES 1\n#include \"../ZenTSCommon.h\"\nnamespace zents { Il2CppMethodPointer ResolveGeneratedDelegate(const MethodInfo*); void DelegateBridge_Initialize(); }\n", new UTF8Encoding(false));
            var output = new StringBuilder("// Generated target-ABI delegate thunks.\n#include \"DelegateBridgeStub.h\"\n#include \"../marshal/DelegateMarshal.h\"\n#include \"../utils/MetadataUtil.h\"\nnamespace zents {\n");
            output.Append(definitions).Append(functions).AppendLine("void DelegateBridge_Initialize() {}\nIl2CppMethodPointer ResolveGeneratedDelegate(const MethodInfo* method) {\n    std::string name = MetadataUtil::BuildTypeFullName(method->klass);");
            foreach (var entry in entries)
            {
                string name = entry.Key.Substring(0, entry.Key.LastIndexOf(':'));
                output.Append("    if(name == \"").Append(name.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\") return (Il2CppMethodPointer)").Append(entry.Value).AppendLine(";");
            }
            output.AppendLine("    return nullptr;\n}\n}");
            File.WriteAllText(Path.Combine(directory, "DelegateBridgeStub.cpp"), output.ToString(), new UTF8Encoding(false));
        }
    }
}
