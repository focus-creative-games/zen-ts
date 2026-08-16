using System;
using System.IO;
using UnityEngine;

namespace ZTS
{
    /// <summary>
    /// File-backed ES module loader (Docs/spec/14-TYPESCRIPT.md §8).
    /// <c>csharp:</c> is handled by <see cref="TsAppDomain"/> before this loader runs.
    /// Editor: <c>TsProject/out</c> then <c>StreamingAssets/ZTS</c>.
    /// Player: <c>StreamingAssets/ZTS</c> only. Never reads <c>.ts</c>.
    /// </summary>
    public static class TsFileModuleLoader
    {
        public static bool TryLoadPublished(string canonical, out string source)
        {
            source = null;
            if (string.IsNullOrEmpty(canonical) || JsModuleSpecifier.IsCsharp(canonical))
            {
                return false;
            }

            string relativeJs = canonical.Replace('/', Path.DirectorySeparatorChar) + ".js";

#if UNITY_EDITOR
            string tsOut = GetTsProjectOutPath(relativeJs);
            if (tsOut != null && File.Exists(tsOut))
            {
                source = File.ReadAllText(tsOut);
                return true;
            }
#endif

            string streaming = Path.Combine(Application.streamingAssetsPath, "ZTS", relativeJs);
            if (File.Exists(streaming))
            {
                source = File.ReadAllText(streaming);
                return true;
            }

            return false;
        }

        public static string Load(string specifier)
        {
            string canonical = JsModuleSpecifier.Canonicalize(specifier);
            if (string.IsNullOrEmpty(canonical))
            {
                throw new TsScriptException("zts: empty module name");
            }

            if (canonical.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new TsScriptException($"zts: module path escapes not allowed: '{specifier}'");
            }

            if (TryLoadPublished(canonical, out string source))
            {
                return source;
            }

            throw new TsScriptException(
                $"zts: unknown module '{canonical}' (missing TsProject/out or StreamingAssets/ZTS/{canonical}.js)");
        }

        internal static string GetTsProjectOutPath(string relativeJs)
        {
            string dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath))
            {
                return null;
            }

            string projectRoot = Path.GetDirectoryName(dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return null;
            }

            return Path.Combine(projectRoot, "TsProject", "out", relativeJs);
        }

        internal static string GetTsProjectRoot()
        {
            string dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath))
            {
                return null;
            }

            return Path.GetDirectoryName(dataPath);
        }
    }
}
