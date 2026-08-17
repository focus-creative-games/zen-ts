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
using UnityEditor;
using UnityEngine;

namespace ZenTS.Editor.Typescript
{
    internal static class TsScaffold
    {
        public static void InitOrUpdate()
        {
            string scaffold = CommonDirs.TypesScaffoldPathInPackage;
            if (!Directory.Exists(scaffold))
            {
                throw new InvalidOperationException(
                    "[ZenTS] package scaffold missing at " + scaffold);
            }

            Directory.CreateDirectory(TsProjectPaths.TsProjectRoot);
            Directory.CreateDirectory(TsProjectPaths.SrcDir);
            Directory.CreateDirectory(TsProjectPaths.GeneratedDir);
            Directory.CreateDirectory(Path.Combine(TsProjectPaths.TsProjectRoot, "scripts"));

            CopyIfMissing(
                Path.Combine(scaffold, "package.json"),
                TsProjectPaths.PackageJsonPath);
            CopyIfMissing(
                Path.Combine(scaffold, "tsconfig.json"),
                TsProjectPaths.TsconfigPath);
            CopyIfMissing(
                Path.Combine(scaffold, "emit.mjs"),
                Path.Combine(TsProjectPaths.TsProjectRoot, "scripts", "emit.mjs"));
            CopyIfMissing(
                Path.Combine(scaffold, "copy-streaming.mjs"),
                Path.Combine(TsProjectPaths.TsProjectRoot, "scripts", "copy-streaming.mjs"));
            CopyIfMissing(
                Path.Combine(scaffold, "main.ts"),
                Path.Combine(TsProjectPaths.SrcDir, "main.ts"));
            CopyIfMissing(
                Path.Combine(scaffold, "gitignore"),
                Path.Combine(TsProjectPaths.TsProjectRoot, ".gitignore"));

            AssetDatabase.Refresh();
            TypescriptToolchain.EnsureDependencies();
            Debug.Log("[ZenTS] TypeScript project ready at " + TsProjectPaths.TsProjectRoot);
        }

        private static void CopyIfMissing(string from, string to)
        {
            if (!File.Exists(from))
            {
                Debug.LogWarning("[ZenTS] scaffold file missing: " + from);
                return;
            }

            if (File.Exists(to))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to) ?? TsProjectPaths.TsProjectRoot);
            File.Copy(from, to, overwrite: false);
        }
    }
}
