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
using System.IO;
using UnityEngine;

namespace ZTS
{
    /// <summary>
    /// File-backed ES module loader (Docs/spec/14-TYPESCRIPT.md §8).
    /// <c>csharp:</c> is handled by <see cref="JsAppDomain"/> before this loader runs.
    /// Editor: <c>TsProject/out</c> then <c>StreamingAssets/ZTS</c>.
    /// Standalone Player: <c>StreamingAssets/ZTS</c>.
    /// Android / WebGL Player: <c>Resources/ZTS/**.js.txt</c> via <see cref="Resources.Load{T}(string)"/>.
    /// Never reads <c>.ts</c>.
    /// </summary>
    public static class JsFileModuleLoader
    {
        public static bool TryLoadPublished(string canonical, out string source)
        {
            source = null;
            if (string.IsNullOrEmpty(canonical) || JsModuleSpecifier.IsCsharp(canonical))
            {
                return false;
            }

            string relativeJs = canonical.Replace('/', Path.DirectorySeparatorChar) + ".js";
            string resourcePath = "ZTS/" + canonical.Replace('\\', '/') + ".js";

#if UNITY_EDITOR
            string tsOut = GetTsProjectOutPath(relativeJs);
            if (tsOut != null && File.Exists(tsOut))
            {
                source = File.ReadAllText(tsOut);
                return true;
            }
#endif

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_WEBGL)
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset != null)
            {
                source = asset.text;
                return true;
            }

            return false;
#else
            string streaming = Path.Combine(Application.streamingAssetsPath, "ZTS", relativeJs);
            if (File.Exists(streaming))
            {
                source = File.ReadAllText(streaming);
                return true;
            }

            return false;
#endif
        }

        public static string Load(string specifier)
        {
            string canonical = JsModuleSpecifier.Canonicalize(specifier);
            if (string.IsNullOrEmpty(canonical))
            {
                throw new JsScriptException("zts: empty module name");
            }

            if (canonical.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new JsScriptException($"zts: module path escapes not allowed: '{specifier}'");
            }

            if (TryLoadPublished(canonical, out string source))
            {
                return source;
            }

            throw new JsScriptException(
                $"zts: unknown module '{canonical}' (missing TsProject/out, StreamingAssets/ZTS, or Resources/ZTS/{canonical}.js)");
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
