using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace ZTS
{
    /// <summary>
    /// Optional XML overlays for <see cref="TsAliasAttribute"/> / extension types (ZLua parity).
    /// Missing files are ignored; invalid entries log and skip.
    /// </summary>
    public static class TsXmlOverlayLoader
    {
        private static readonly Dictionary<string, string> MethodAliases =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, List<Type>> ExtensionTypes =
            new Dictionary<string, List<Type>>(StringComparer.Ordinal);

        public static void Reset()
        {
            MethodAliases.Clear();
            ExtensionTypes.Clear();
        }

        public static void TryLoadFromDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(directory, "*.xml"))
            {
                try
                {
                    XDocument doc = XDocument.Load(file);
                    foreach (XElement el in doc.Descendants("alias"))
                    {
                        RegisterAliasElement(el);
                    }

                    foreach (XElement el in doc.Descendants("extension"))
                    {
                        RegisterExtensionElement(el);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"zts: skip XML overlay {file}: {ex.Message}");
                }
            }
        }

        public static bool TryGetAlias(string memberKey, out string alias) =>
            MethodAliases.TryGetValue(memberKey, out alias);

        public static IEnumerable<Type> GetExtensionTypes(Type extendedType)
        {
            if (extendedType == null)
            {
                yield break;
            }

            if (ExtensionTypes.TryGetValue(extendedType.FullName, out List<Type> types))
            {
                for (int i = 0; i < types.Count; i++)
                {
                    Type t = types[i];
                    if (t != null)
                    {
                        yield return t;
                    }
                }
            }
        }

        private static void RegisterAliasElement(XElement el)
        {
            string name = (string)el.Attribute("name");
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            string typeName = (string)el.Attribute("type");
            string member = (string)el.Attribute("member");
            if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(member))
            {
                MethodAliases[$"{typeName}.{member}"] = name;
                MethodAliases[$"{typeName}::{member}"] = name;
                return;
            }

            string legacyKey = (string)el.Attribute("member");
            if (!string.IsNullOrEmpty(legacyKey))
            {
                MethodAliases[legacyKey] = name;
            }
        }

        private static void RegisterExtensionElement(XElement el)
        {
            string targetTypeName = (string)el.Attribute("type");
            string methodsTypeName = (string)el.Attribute("methods");
            if (string.IsNullOrEmpty(targetTypeName) || string.IsNullOrEmpty(methodsTypeName))
            {
                return;
            }

            Type methodsType = ResolveType(methodsTypeName);
            if (methodsType == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"zts: XML extension methods type not found: {methodsTypeName}");
                return;
            }

            if (!ExtensionTypes.TryGetValue(targetTypeName, out List<Type> list))
            {
                list = new List<Type>();
                ExtensionTypes[targetTypeName] = list;
            }

            if (!list.Contains(methodsType))
            {
                list.Add(methodsType);
            }
        }

        private static Type ResolveType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Type direct = Type.GetType(fullName);
            if (direct != null)
            {
                return direct;
            }

            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t = assemblies[i].GetType(fullName, throwOnError: false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
