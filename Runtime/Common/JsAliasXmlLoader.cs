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
    /// Loads and merges JsAlias XML files (spec 04-METHOD-OVERLOAD §5.4).
    /// Duplicate (assembly, type, method, signature) keys are a hard failure.
    /// </summary>
    public static class JsAliasXmlLoader
    {
        public static List<JsAliasXmlRule> LoadFromConfiguredPaths(IEnumerable<string> configuredPaths, string projectRoot)
        {
            List<string> files;
            try
            {
                files = JsXmlPathUtil.ExpandToXmlFiles(configuredPaths, projectRoot, "JsAlias");
            }
            catch (Exception ex)
            {
                throw new JsAliasConfigurationException(ex.Message, ex);
            }

            var rules = new List<JsAliasXmlRule>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                LoadFile(file, rules, seen);
            }

            return rules;
        }

        private static void LoadFile(string filePath, List<JsAliasXmlRule> rules, Dictionary<string, string> seen)
        {
            var doc = new XmlDocument();
            try
            {
                doc.Load(filePath);
            }
            catch (Exception ex)
            {
                throw new JsAliasConfigurationException(
                    "[ZTS] Failed to parse JsAlias XML '" + filePath + "': " + ex.Message, ex);
            }

            XmlElement root = doc.DocumentElement;
            if (root == null || !string.Equals(root.Name, "JsAlias", StringComparison.Ordinal))
            {
                throw new JsAliasConfigurationException(
                    "[ZTS] JsAlias XML root must be <JsAlias>: " + filePath);
            }

            string version = root.GetAttribute("version");
            if (!string.Equals(version, "1", StringComparison.Ordinal))
            {
                throw new JsAliasConfigurationException(
                    "[ZTS] Unsupported JsAlias XML version '" + version + "' in " + filePath);
            }

            foreach (XmlNode child in root.ChildNodes)
            {
                if (!(child is XmlElement assemblyEl))
                {
                    continue;
                }

                if (!string.Equals(assemblyEl.Name, "Assembly", StringComparison.Ordinal))
                {
                    throw new JsAliasConfigurationException(
                        "[ZTS] Unexpected element <" + assemblyEl.Name + "> under JsAlias in " + filePath);
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
                        throw new JsAliasConfigurationException(
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
            List<JsAliasXmlRule> rules,
            Dictionary<string, string> seen)
        {
            string typeFullName = RequireAttr(typeEl, "fullName", filePath);
            foreach (XmlNode child in typeEl.ChildNodes)
            {
                if (!(child is XmlElement el))
                {
                    continue;
                }

                if (!string.Equals(el.Name, "Method", StringComparison.Ordinal))
                {
                    throw new JsAliasConfigurationException(
                        "[ZTS] Unexpected element <" + el.Name + "> under Type in JsAlias XML " + filePath
                        + "; only <Method alias=\"...\"> is allowed.");
                }

                string methodName = RequireAttr(el, "name", filePath);
                string signature = RequireAttr(el, "signature", filePath);
                string alias = RequireAttr(el, "alias", filePath);
                if (string.IsNullOrWhiteSpace(alias))
                {
                    throw new JsAliasConfigurationException(
                        "[ZTS] Method/@alias must be non-empty in " + filePath);
                }

                foreach (XmlNode nested in el.ChildNodes)
                {
                    if (nested is XmlElement nestedEl)
                    {
                        throw new JsAliasConfigurationException(
                            "[ZTS] JsAlias Method must not contain child elements (found <"
                            + nestedEl.Name + ">) in " + filePath);
                    }
                }

                var rule = new JsAliasXmlRule
                {
                    SourcePath = filePath,
                    AssemblyName = assemblyName,
                    TypeFullName = typeFullName,
                    MethodName = methodName,
                    Signature = signature,
                    Alias = alias.Trim(),
                };

                string key = rule.DuplicateKey;
                if (seen.TryGetValue(key, out string previousPath))
                {
                    throw new JsAliasConfigurationException(
                        "[ZTS] Duplicate JsAlias XML rule for " + key
                        + "\n  first: " + previousPath
                        + "\n  again: " + filePath);
                }

                seen[key] = filePath;
                rules.Add(rule);
            }
        }

        private static string RequireAttr(XmlElement el, string attrName, string filePath)
        {
            if (!el.HasAttribute(attrName) || string.IsNullOrWhiteSpace(el.GetAttribute(attrName)))
            {
                throw new JsAliasConfigurationException(
                    "[ZTS] Missing or empty @" + attrName + " on <" + el.Name + "> in " + filePath);
            }

            return el.GetAttribute(attrName).Trim();
        }
    }
}
