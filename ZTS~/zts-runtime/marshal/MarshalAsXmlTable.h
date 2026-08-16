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

#include "MarshalDefs.h"

#include <cstdint>
#include <string>
#include <vector>

struct MethodInfo;
struct Il2CppClass;
struct Il2CppImage;

namespace zts
{
/// Matches C# <c>ZTS.JsMarshalType</c> ordinal values exactly.
enum class JsMarshalType : uint8_t
{
    Default = 0,
    Object = 1,
    Bytes = 2,
    OpaqueValue = 3,
    UnpackedValues = 4,
    Table = 5,
};

enum class JsMarshalAsXmlKind : uint8_t
{
    Type = 0,
    Field = 1,
    Property = 2,
    Param = 3,
    Return = 4,
};

/// Name-based entry from MarshalAsCodegen (tokens resolved at RegisterEntries / startup).
struct JsMarshalAsXmlEntry
{
    JsMarshalAsXmlKind kind;
    const char* assemblyName;
    const char* typeFullName;
    const char* memberOrMethodName; // Field/Property/Method name; nullptr for Type
    const char* signature;          // Method signature "(T1,T2)"; nullptr unless Param/Return
    int32_t paramIndex;             // Param index; unused otherwise (-1)
    JsMarshalType marshalType;
    const char* const* members;
    uint16_t memberCount;
};

struct JsMarshalAsResolvedData
{
    JsMarshalType marshalType = JsMarshalType::Default;
    std::vector<std::string> members;
};

class MarshalAsXmlTable
{
  public:
    static void Clear();
    /// Store entries and resolve all tokens immediately (Il2Cpp has loaded assemblies).
    static void RegisterEntries(const JsMarshalAsXmlEntry* entries, size_t count);

    /// Type / Field / Property: image → memberToken → Rule
    static bool TryGet(const Il2CppImage* image, uint32_t token, JsMarshalAsResolvedData& outData);

    /// Param (index>=0) / Return (index=-1): image → (methodDefToken, index) → Rule
    static bool TryGetForMethodSlot(const MethodInfo* method, int argIndex, JsMarshalAsResolvedData& outData);

    static bool TryGetForType(Il2CppClass* klass, JsMarshalAsResolvedData& outData);
};
} // namespace zts
