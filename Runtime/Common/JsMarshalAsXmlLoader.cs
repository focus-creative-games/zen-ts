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
using System.Xml;

namespace ZTS
{
    /// <summary>
    /// Loads and merges MarshalAs XML files (spec marshal/02-MARSHAL-AS §9).
    /// Duplicate target keys are a hard failure.
    /// </summary>
    public static class JsMarshalAsXmlLoader
    {
        public static List<JsMarshalAsXmlRule> LoadFromConfiguredPaths(IEnumerable<string> configuredPaths, string projectRoot)
        {
            List<string> files;
            try
            {
                files = JsXmlPathUtil.ExpandToXmlFiles(configuredPaths, projectRoot, "MarshalAs");
            }
            catch (Exception ex)
            {
                throw new JsMarshalAsConfigurationException(ex.Message, ex);
            }

            var rules = new List<JsMarshalAsXmlRule>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                LoadFile(file, rules, seen);
            }

            return rules;
        }

        private static void LoadFile(string filePath, List<JsMarshalAsXmlRule> rules, Dictionary<string, string> seen)
        {
            var doc = new XmlDocument();
            try
            {
                doc.Load(filePath);
            }
            catch (Exception ex)
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Failed to parse MarshalAs XML '" + filePath + "': " + ex.Message, ex);
            }

            XmlElement root = doc.DocumentElement;
            if (root == null || !string.Equals(root.Name, "ZTSMarshalAs", StringComparison.Ordinal))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] MarshalAs XML root must be <ZTSMarshalAs>: " + filePath);
            }

            string version = root.GetAttribute("version");
            if (!string.Equals(version, "1", StringComparison.Ordinal))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Unsupported MarshalAs XML version '" + version + "' in " + filePath);
            }

            foreach (XmlNode child in root.ChildNodes)
            {
                if (!(child is XmlElement assemblyEl))
                {
                    continue;
                }

                if (!string.Equals(assemblyEl.Name, "Assembly", StringComparison.Ordinal))
                {
                    throw new JsMarshalAsConfigurationException(
                        "[ZTS] Unexpected element <" + assemblyEl.Name + "> under ZTSMarshalAs in " + filePath);
                }

                string assemblyName = RequireAttr(assemblyEl, "name", filePath);
                foreach (XmlNode typeNode in assemblyEl.ChildNodes)
                {
                    if (!(typeNode is XmlElement typeEl))
                    {
                        continue;
                    }

                    if (!string.Equals(typeEl.Name, "Type", StringComparison.Ordinal))
                    {
                        throw new JsMarshalAsConfigurationException(
                            "[ZTS] Unexpected element <" + typeEl.Name + "> under Assembly in " + filePath);
                    }

                    ParseType(filePath, assemblyName, typeEl, rules, seen);
                }
            }
        }

        private static void ParseType(
            string filePath,
            string assemblyName,
            XmlElement typeEl,
            List<JsMarshalAsXmlRule> rules,
            Dictionary<string, string> seen)
        {
            string typeFullName = RequireAttr(typeEl, "fullName", filePath);
            foreach (XmlNode child in typeEl.ChildNodes)
            {
                if (!(child is XmlElement el))
                {
                    continue;
                }

                switch (el.Name)
                {
                    case "MarshalAs":
                        AddRule(rules, seen, BuildRule(
                            filePath, assemblyName, typeFullName, JsMarshalAsXmlTargetKind.Type,
                            memberName: null, methodName: null, signature: null, paramIndex: -1, el));
                        break;
                    case "Field":
                        AddRule(rules, seen, BuildRule(
                            filePath, assemblyName, typeFullName, JsMarshalAsXmlTargetKind.Field,
                            memberName: RequireAttr(el, "name", filePath), methodName: null, signature: null, paramIndex: -1,
                            RequireSingleMarshalAs(el, filePath)));
                        break;
                    case "Property":
                        AddRule(rules, seen, BuildRule(
                            filePath, assemblyName, typeFullName, JsMarshalAsXmlTargetKind.Property,
                            memberName: RequireAttr(el, "name", filePath), methodName: null, signature: null, paramIndex: -1,
                            RequireSingleMarshalAs(el, filePath)));
                        break;
                    case "Method":
                        ParseMethod(filePath, assemblyName, typeFullName, el, rules, seen);
                        break;
                    default:
                        throw new JsMarshalAsConfigurationException(
                            "[ZTS] Unexpected element <" + el.Name + "> under Type in " + filePath);
                }
            }
        }

        private static void ParseMethod(
            string filePath,
            string assemblyName,
            string typeFullName,
            XmlElement methodEl,
            List<JsMarshalAsXmlRule> rules,
            Dictionary<string, string> seen)
        {
            string methodName = RequireAttr(methodEl, "name", filePath);
            string signature = RequireAttr(methodEl, "signature", filePath);

            foreach (XmlAttribute attr in methodEl.Attributes)
            {
                if (attr.Name == "name" || attr.Name == "signature")
                {
                    continue;
                }

                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Unsupported Method attribute '@" + attr.Name + "' in " + filePath);
            }

            foreach (XmlNode child in methodEl.ChildNodes)
            {
                if (!(child is XmlElement el))
                {
                    continue;
                }

                switch (el.Name)
                {
                    case "Param":
                    {
                        if (el.HasAttribute("name"))
                        {
                            throw new JsMarshalAsConfigurationException(
                                "[ZTS] Param must use @index (not @name) in " + filePath);
                        }

                        if (!el.HasAttribute("index")
                            || !int.TryParse(el.GetAttribute("index"), out int index)
                            || index < 0)
                        {
                            throw new JsMarshalAsConfigurationException(
                                "[ZTS] Param/@index must be a non-negative integer in " + filePath);
                        }

                        AddRule(rules, seen, BuildRule(
                            filePath, assemblyName, typeFullName, JsMarshalAsXmlTargetKind.Param,
                            memberName: null, methodName: methodName, signature: signature, paramIndex: index,
                            RequireSingleMarshalAs(el, filePath)));
                        break;
                    }
                    case "Return":
                        AddRule(rules, seen, BuildRule(
                            filePath, assemblyName, typeFullName, JsMarshalAsXmlTargetKind.Return,
                            memberName: null, methodName: methodName, signature: signature, paramIndex: -1,
                            RequireSingleMarshalAs(el, filePath)));
                        break;
                    case "MarshalAs":
                        throw new JsMarshalAsConfigurationException(
                            "[ZTS] Method-level MarshalAs is not allowed; use Param/Return in " + filePath);
                    default:
                        throw new JsMarshalAsConfigurationException(
                            "[ZTS] Unexpected element <" + el.Name + "> under Method in " + filePath);
                }
            }
        }

        private static XmlElement RequireSingleMarshalAs(XmlElement parent, string filePath)
        {
            XmlElement found = null;
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (!(child is XmlElement el))
                {
                    continue;
                }

                if (!string.Equals(el.Name, "MarshalAs", StringComparison.Ordinal))
                {
                    throw new JsMarshalAsConfigurationException(
                        "[ZTS] Unexpected element <" + el.Name + "> under <" + parent.Name + "> in " + filePath);
                }

                if (found != null)
                {
                    throw new JsMarshalAsConfigurationException(
                        "[ZTS] Duplicate <MarshalAs> under <" + parent.Name + "> in " + filePath);
                }

                found = el;
            }

            if (found == null)
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Missing <MarshalAs> under <" + parent.Name + "> in " + filePath);
            }

            return found;
        }

        private static JsMarshalAsXmlRule BuildRule(
            string filePath,
            string assemblyName,
            string typeFullName,
            JsMarshalAsXmlTargetKind kind,
            string memberName,
            string methodName,
            string signature,
            int paramIndex,
            XmlElement marshalAsEl)
        {
            string typeName = RequireAttr(marshalAsEl, "type", filePath);
            if (string.Equals(typeName, "UserData", StringComparison.Ordinal))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] MarshalAs type 'UserData' is not valid in " + filePath
                    + "; use 'Object'.");
            }

            if (string.Equals(typeName, "OpaqueLightUserData", StringComparison.Ordinal))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Obsolete MarshalAs type 'OpaqueLightUserData' in " + filePath
                    + "; use 'OpaqueValue'.");
            }

            if (string.Equals(typeName, "ParamsTable", StringComparison.Ordinal))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Removed MarshalAs type 'ParamsTable' in " + filePath
                    + "; params T[] uses default szarray rules.");
            }

            if (!Enum.TryParse(typeName, ignoreCase: false, out JsMarshalType marshalType))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Unknown MarshalAs type '" + typeName + "' in " + filePath);
            }

            string membersAttr = marshalAsEl.HasAttribute("members") ? marshalAsEl.GetAttribute("members") : null;
            string[] members = SplitMembers(membersAttr);
            if ((marshalType == JsMarshalType.Table || marshalType == JsMarshalType.UnpackedValues)
                && (members == null || members.Length == 0))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] MarshalAs type '" + marshalType + "' requires @members in " + filePath);
            }

            return new JsMarshalAsXmlRule
            {
                SourcePath = filePath,
                AssemblyName = assemblyName,
                TypeFullName = typeFullName,
                Kind = kind,
                MemberName = memberName,
                MethodName = methodName,
                Signature = signature,
                ParamIndex = paramIndex,
                MarshalType = marshalType,
                Members = members,
            };
        }

        private static string[] SplitMembers(string membersAttr)
        {
            if (string.IsNullOrWhiteSpace(membersAttr))
            {
                return null;
            }

            string[] parts = membersAttr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }

            return parts;
        }

        private static void AddRule(
            List<JsMarshalAsXmlRule> rules,
            Dictionary<string, string> seen,
            JsMarshalAsXmlRule rule)
        {
            string key = rule.DuplicateKey;
            if (seen.TryGetValue(key, out string previousPath))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Duplicate MarshalAs XML rule for key '" + key + "'.\n"
                    + "  first: " + previousPath + "\n"
                    + "  conflict: " + rule.SourcePath);
            }

            seen.Add(key, rule.SourcePath);
            rules.Add(rule);
        }

        private static string RequireAttr(XmlElement el, string name, string filePath)
        {
            string value = el.GetAttribute(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new JsMarshalAsConfigurationException(
                    "[ZTS] Missing @" + name + " on <" + el.Name + "> in " + filePath);
            }

            return value.Trim();
        }
    }
}
