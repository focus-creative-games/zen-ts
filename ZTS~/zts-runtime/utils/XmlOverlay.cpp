#include "XmlOverlay.h"
#include "MetadataUtil.h"

#include "utils/StringUtils.h"
#include "vm/Assembly.h"
#include "vm/Class.h"
#include "vm/Runtime.h"
#include "vm/String.h"

#include <cstring>
#include <fstream>
#include <sstream>
#include <unordered_map>

#if defined(_MSC_VER)
#include <windows.h>
#endif

namespace zts
{
namespace
{
static std::unordered_map<std::string, std::string> s_aliases;
static std::unordered_map<std::string, std::vector<std::string>> s_extensionTypeNames;

static std::string AttrValue(const std::string& tag, const char* attr)
{
    std::string key = std::string(attr) + "=\"";
    size_t start = tag.find(key);
    if (start == std::string::npos)
        return {};
    start += key.size();
    size_t end = tag.find('"', start);
    if (end == std::string::npos)
        return {};
    return tag.substr(start, end - start);
}

static void ParseXmlContent(const std::string& xml)
{
    size_t pos = 0;
    while (pos < xml.size())
    {
        size_t lt = xml.find('<', pos);
        if (lt == std::string::npos)
            break;
        size_t gt = xml.find('>', lt);
        if (gt == std::string::npos)
            break;
        std::string tag = xml.substr(lt + 1, gt - lt - 1);
        pos = gt + 1;

        if (tag.compare(0, 5, "alias") == 0 && (tag.size() == 5 || tag[5] == ' ' || tag[5] == '/'))
        {
            std::string name = AttrValue(tag, "name");
            std::string typeName = AttrValue(tag, "type");
            std::string member = AttrValue(tag, "member");
            if (!name.empty() && !typeName.empty() && !member.empty())
            {
                s_aliases[typeName + "." + member] = name;
                s_aliases[typeName + "::" + member] = name;
            }
            continue;
        }

        if (tag.compare(0, 9, "extension") == 0
            && (tag.size() == 9 || tag[9] == ' ' || tag[9] == '/'))
        {
            std::string target = AttrValue(tag, "type");
            std::string methods = AttrValue(tag, "methods");
            if (!target.empty() && !methods.empty())
                s_extensionTypeNames[target].push_back(methods);
        }
    }
}

static bool ReadFileUtf8(const std::string& path, std::string& out)
{
    std::ifstream ifs(path.c_str(), std::ios::binary);
    if (!ifs)
        return false;
    std::ostringstream ss;
    ss << ifs.rdbuf();
    out = ss.str();
    return true;
}

static std::string GetStreamingAssetsPath()
{
    const Il2CppAssembly* asmUnity = MetadataUtil::ResolveAssembly("UnityEngine.CoreModule");
    if (asmUnity == nullptr)
        return {};

    Il2CppClass* klass = il2cpp::vm::Class::FromName(asmUnity->image, "UnityEngine", "Application");
    if (klass == nullptr)
        return {};
    const MethodInfo* getter = il2cpp::vm::Class::GetMethodFromName(klass, "get_streamingAssetsPath", 0);
    if (getter == nullptr)
        return {};

    Il2CppException* exc = nullptr;
    Il2CppObject* boxed = il2cpp::vm::Runtime::Invoke(getter, nullptr, nullptr, &exc);
    if (exc != nullptr || boxed == nullptr)
        return {};
    Il2CppString* s = reinterpret_cast<Il2CppString*>(boxed);
    return il2cpp::utils::StringUtils::Utf16ToUtf8(
        il2cpp::utils::StringUtils::GetChars(s),
        il2cpp::utils::StringUtils::GetLength(s));
}

static void LoadDirectory(const std::string& dir)
{
    if (dir.empty())
        return;

#if defined(_MSC_VER)
    std::string pattern = dir + "\\*.xml";
    WIN32_FIND_DATAA fd;
    HANDLE h = FindFirstFileA(pattern.c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE)
        return;
    do
    {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            continue;
        std::string path = dir + "\\" + fd.cFileName;
        std::string content;
        if (ReadFileUtf8(path, content))
            ParseXmlContent(content);
    } while (FindNextFileA(h, &fd));
    FindClose(h);
#else
    const char* known[] = { "aliases.xml" };
    for (const char* name : known)
    {
        std::string path = dir + "/" + name;
        std::string content;
        if (ReadFileUtf8(path, content))
            ParseXmlContent(content);
    }
#endif
}

static Il2CppClass* ResolveTypeByFullName(const std::string& fullName)
{
    il2cpp::vm::AssemblyVector* assemblies = il2cpp::vm::Assembly::GetAllAssemblies();
    if (assemblies == nullptr)
        return nullptr;
    for (const Il2CppAssembly* assembly : *assemblies)
    {
        Il2CppClass* klass = MetadataUtil::ResolveType(assembly, fullName.c_str());
        if (klass != nullptr)
            return klass;
    }
    return nullptr;
}
} // namespace

void XmlOverlay::Reset()
{
    s_aliases.clear();
    s_extensionTypeNames.clear();
}

void XmlOverlay::LoadFromStreamingAssets()
{
    Reset();
    std::string root = GetStreamingAssetsPath();
    if (root.empty())
    {
        LoadDirectory("ZLuaDemo_Data/StreamingAssets/Tests/Xml");
        LoadDirectory("ZLuaDemo_Data/StreamingAssets/ZTS/Xml");
        return;
    }
#if defined(_MSC_VER)
    LoadDirectory(root + "\\Tests\\Xml");
    LoadDirectory(root + "\\ZTS\\Xml");
#else
    LoadDirectory(root + "/Tests/Xml");
    LoadDirectory(root + "/ZTS/Xml");
#endif
}

bool XmlOverlay::TryGetAlias(const std::string& typeFullName, const std::string& member, std::string& aliasOut)
{
    auto it = s_aliases.find(typeFullName + "." + member);
    if (it == s_aliases.end())
        it = s_aliases.find(typeFullName + "::" + member);
    if (it == s_aliases.end())
        return false;
    aliasOut = it->second;
    return true;
}

void XmlOverlay::GetExtensionTypes(const std::string& targetTypeFullName, std::vector<Il2CppClass*>& outClasses)
{
    auto it = s_extensionTypeNames.find(targetTypeFullName);
    if (it == s_extensionTypeNames.end())
        return;
    for (const std::string& typeName : it->second)
    {
        Il2CppClass* klass = ResolveTypeByFullName(typeName);
        if (klass != nullptr)
            outClasses.push_back(klass);
    }
}
}
