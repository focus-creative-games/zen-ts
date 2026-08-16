#pragma once

#include "../Il2CppCompatible.h"

#include <string>
#include <vector>

namespace zts
{
class XmlOverlay
{
public:
    static void Reset();
    static void LoadFromStreamingAssets();

    static bool TryGetAlias(const std::string& typeFullName, const std::string& member, std::string& aliasOut);
    static void GetExtensionTypes(const std::string& targetTypeFullName, std::vector<Il2CppClass*>& outClasses);
};
}
