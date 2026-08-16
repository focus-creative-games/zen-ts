using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZTS.Editor.Typescript
{
    internal static class TsScaffold
    {
        public static void InitOrUpdate()
        {
            string scaffold = CommonDirs.TypesScaffoldPathInPackage;
            if (!Directory.Exists(scaffold))
            {
                throw new InvalidOperationException(
                    "[ZTS] package scaffold missing at " + scaffold);
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
            Debug.Log("[ZTS] TypeScript project ready at " + TsProjectPaths.TsProjectRoot);
        }

        private static void CopyIfMissing(string from, string to)
        {
            if (!File.Exists(from))
            {
                Debug.LogWarning("[ZTS] scaffold file missing: " + from);
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
