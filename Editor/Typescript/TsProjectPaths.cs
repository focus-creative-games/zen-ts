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

using System.IO;
using UnityEngine;

namespace ZTS.Editor.Typescript
{
    internal static class TsProjectPaths
    {
        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string TsProjectRoot => Path.Combine(ProjectRoot, "TsProject");

        public static string SrcDir => Path.Combine(TsProjectRoot, "src");

        public static string OutDir => Path.Combine(TsProjectRoot, "out");

        public static string GeneratedDir => Path.Combine(TsProjectRoot, "generated");

        public static string GeneratedCsharpDir => Path.Combine(GeneratedDir, "csharp");

        public static string ExportManifestPath => Path.Combine(GeneratedDir, "js-exports.json");

        public static string TsconfigPath => Path.Combine(TsProjectRoot, "tsconfig.json");

        public static string PackageJsonPath => Path.Combine(TsProjectRoot, "package.json");

        public static string StreamingZtsDir =>
            Path.Combine(Application.dataPath, "StreamingAssets", "ZTS");

        /// <summary>
        /// Android/WebGL TextAsset root: <c>Assets/Resources/ZTS/**/*.js.txt</c>
        /// loads as <c>Resources.Load("ZTS/…/*.js")</c>.
        /// </summary>
        public static string ResourcesZtsDir =>
            Path.Combine(Application.dataPath, "Resources", "ZTS");

        public static bool Exists => File.Exists(TsconfigPath) && Directory.Exists(SrcDir);
    }
}
