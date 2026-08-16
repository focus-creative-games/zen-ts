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
using ZTS.CppCodeGen;

namespace ZTS.Editor
{
    /// <summary>
    /// Build-time MarshalAs / JsAlias / JsExtensions XML → C++ binding tables.
    /// </summary>
    public static class XmlBindingsGenerate
    {
        private static readonly string[] GeneratedFileNames =
        {
            "MarshalBindings.h",
            "MarshalBindings.cpp",
            "AliasBindings.h",
            "AliasBindings.cpp",
            "ExtensionBindings.h",
            "ExtensionBindings.cpp",
        };

        public static void Generate()
        {
            var installer = new LocalInstaller();
            if (!installer.HasInstalledToLocal())
            {
                throw new InvalidOperationException(
                    "[ZTS] Local install not found. Run menu 'ZTS/Install...' before Generate Xml Bindings.");
            }

            string outDir = CommonDirs.GeneratedZtsPath;
            Directory.CreateDirectory(outDir);

            new MarshalAsCodegen(outDir).Generate();
            new AliasCodegen(outDir).Generate();
            new ExtensionCodegen(outDir).Generate();

            // LocalIl2Cpp is the build authority (ZLua parity). Do NOT write back into the
            // package tree — that invalidates InstallFingerprint and trips CheckLocalInstall.
            CopyGeneratedFiles(outDir, CommonDirs.BuildWin64GeneratedZtsPath);

            Debug.Log("[ZTS] Generate Xml Bindings OK → " + outDir);
        }

        private static void CopyGeneratedFiles(string sourceDir, string destDir)
        {
            if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
            {
                // Parent may exist without generated/; create when package/build tree is present.
                string parent = Path.GetDirectoryName(destDir);
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                {
                    return;
                }

                Directory.CreateDirectory(destDir);
            }

            for (int i = 0; i < GeneratedFileNames.Length; i++)
            {
                string name = GeneratedFileNames[i];
                string src = Path.Combine(sourceDir, name);
                if (!File.Exists(src))
                {
                    continue;
                }

                File.Copy(src, Path.Combine(destDir, name), overwrite: true);
            }
        }
    }
}
