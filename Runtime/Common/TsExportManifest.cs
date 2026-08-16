using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ZTS
{
    /// <summary>
    /// Optional P3 check: named exports recorded by esbuild emit
    /// (<c>TsProject/generated/js-exports.json</c>). Warn-only; never fails GetFunction.
    /// </summary>
    public static class TsExportManifest
    {
        private static Dictionary<string, HashSet<string>> s_map;
        private static DateTime s_loadedUtc = DateTime.MinValue;
        private static string s_path;

        public static void WarnIfUnknown(string canonicalModule, string exportName)
        {
#if !UNITY_EDITOR
            return;
#else
            if (string.IsNullOrEmpty(canonicalModule) || string.IsNullOrEmpty(exportName))
            {
                return;
            }

            if (!TryGetExports(canonicalModule, out HashSet<string> names))
            {
                return;
            }

            if (!names.Contains(exportName))
            {
                Debug.LogWarning(
                    $"[ZTS] GetFunction('{canonicalModule}', '{exportName}') is not in generated/js-exports.json");
            }
#endif
        }

        public static bool TryGetExports(string canonicalModule, out HashSet<string> names)
        {
            names = null;
            Dictionary<string, HashSet<string>> map = LoadIfNeeded();
            if (map == null)
            {
                return false;
            }

            return map.TryGetValue(canonicalModule, out names);
        }

        public static Dictionary<string, HashSet<string>> LoadIfNeeded()
        {
            string path = ManifestPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                s_map = null;
                return null;
            }

            DateTime write = File.GetLastWriteTimeUtc(path);
            if (s_map != null && s_path == path && write == s_loadedUtc)
            {
                return s_map;
            }

            try
            {
                s_map = Parse(File.ReadAllText(path));
                s_path = path;
                s_loadedUtc = write;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZTS] failed to parse js-exports.json: " + ex.Message);
                s_map = null;
            }

            return s_map;
        }

        private static string ManifestPath()
        {
            string root = TsFileModuleLoader.GetTsProjectRoot();
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return Path.Combine(root, "TsProject", "generated", "js-exports.json");
        }

        /// <summary>
        /// Minimal parser for <c>{"mod":["a","b"]}</c>. Not a general JSON library.
        /// </summary>
        internal static Dictionary<string, HashSet<string>> Parse(string json)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(json))
            {
                return map;
            }

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{')
            {
                return map;
            }

            i++;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == '}')
                {
                    break;
                }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ':')
                {
                    i++;
                }

                SkipWs(json, ref i);
                var set = new HashSet<string>(StringComparer.Ordinal);
                if (i < json.Length && json[i] == '[')
                {
                    i++;
                    while (i < json.Length)
                    {
                        SkipWs(json, ref i);
                        if (i < json.Length && json[i] == ']')
                        {
                            i++;
                            break;
                        }

                        set.Add(ReadString(json, ref i));
                        SkipWs(json, ref i);
                        if (i < json.Length && json[i] == ',')
                        {
                            i++;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(key))
                {
                    map[key] = set;
                }

                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++;
                }
            }

            return map;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static string ReadString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"')
            {
                return string.Empty;
            }

            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    break;
                }

                if (c == '\\' && i < s.Length)
                {
                    sb.Append(s[i++]);
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
