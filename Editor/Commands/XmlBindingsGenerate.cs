// Copyright 2026 Code Philosophy

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
