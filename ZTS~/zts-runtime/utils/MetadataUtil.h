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

#pragma once

#include "../Il2CppCompatible.h"

#include <cstring>
#include <string>
#include <vector>

namespace zts
{
    class MetadataUtil
    {
    public:
        static void Initialize();
        static const Il2CppAssembly* ResolveAssembly(const char* name);
        static Il2CppClass* ResolveType(const Il2CppAssembly* assembly, const char* typeName);
        /** Reflection-style name: arrays, closed generics, optional ", Assembly" AQN. */
        static Il2CppClass* ResolveTypeByName(const char* typeName);
        static Il2CppClass* GetJsMethodClass();
        static Il2CppClass* GetJsScriptExceptionClass();
        static const char* GetTypeFullName(Il2CppClass* klass);

        static inline void EnsureMethods(Il2CppClass* klass)
        {
            IL2CPP_ASSERT(klass != nullptr);
            il2cpp::vm::Class::SetupMethods(klass);
        }

        static bool IsStaticMethod(const MethodInfo* method)
        {
            return (method->flags & METHOD_ATTRIBUTE_STATIC) != 0;
        }

        static bool IsPublicMethod(const MethodInfo* method)
        {
            return (method->flags & METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK) == METHOD_ATTRIBUTE_PUBLIC;
        }

        static bool IsVoidType(const Il2CppType* type)
        {
            return type->type == IL2CPP_TYPE_VOID;
        }

        static bool IsDelegateClass(Il2CppClass* klass)
        {
            if (klass == nullptr)
                return false;
            il2cpp::vm::Class::Init(klass);
            return il2cpp::vm::Class::IsAssignableFrom(il2cpp_defaults.delegate_class, klass) &&
                   klass != il2cpp_defaults.delegate_class &&
                   klass != il2cpp_defaults.multicastdelegate_class;
        }

        static std::string BuildTypeFullName(Il2CppClass* klass);

        static std::string FormatParameterSignature(const MethodInfo* method);
        static const MethodInfo* FindMethodByParameterSignature(Il2CppClass* klass, const char* name, const char* parameterSignature);
        static const MethodInfo* FindMethodByParameterSignature(Il2CppClass* klass, const char* name, const char* parameterSignature, bool isStatic);

        static bool IsCtorOrCCtor(const MethodInfo* method)
        {
            const char* name = method->name;
            return std::strcmp(name, ".ctor") == 0 || std::strcmp(name, ".cctor") == 0;
        }

        static bool IsCtor(const MethodInfo* method)
        {
            return std::strcmp(method->name, ".ctor") == 0;
        }

        static bool IsCCtor(const MethodInfo* method)
        {
            return std::strcmp(method->name, ".cctor") == 0;
        }

        static bool IsMethodSealed(const MethodInfo* method, bool byVal)
        {
            if (byVal)
                return true;
            const uint16_t flags = method->flags;
            if ((flags & METHOD_ATTRIBUTE_VIRTUAL) == 0)
                return true;
            if ((flags & METHOD_ATTRIBUTE_FINAL) != 0)
                return true;
            if ((method->klass->flags & TYPE_ATTRIBUTE_SEALED) != 0)
                return true;
            return false;
        }

        static inline const MethodInfo* ResolveInvokeMethod(const MethodInfo* declared, void* target, bool sealed)
        {
            if (sealed)
                return declared;
            return il2cpp::vm::Object::GetVirtualMethod(reinterpret_cast<Il2CppObject*>(target), declared);
        }

        static bool TryReadJsAlias(const MethodInfo* method, std::string& aliasOut);
        static bool IsExtensionMethod(const MethodInfo* method);
        static bool TryReadJsExtensionTypes(Il2CppClass* klass, std::vector<Il2CppClass*>& outExtensionClasses);

        /// Returns true when [JsMarshalAs(Bytes)] is present on a parameter (or return when paramIndex < 0).
        static bool ParameterHasBytesMarshal(const MethodInfo* method, int paramIndex);

        /// Reads [JsMarshalAs] or MarshalAs XML fallback; returns kind matching JsMarshalType ordinals.
        /// Members filled for Table / UnpackedValues. Attribute always wins over XML.
        static bool TryReadJsMarshalAs(
            const MethodInfo* method,
            int paramIndex,
            int32_t* outKind,
            std::vector<std::string>* outMembers);

        static bool IsParamsParameter(const MethodInfo* method, int paramIndex);
    };
}
