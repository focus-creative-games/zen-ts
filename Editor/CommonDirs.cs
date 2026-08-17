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

namespace ZenTS
{
    public static class CommonDirs
    {
        public static string PackageName => "com.code-philosophy.zen-ts";

        public static string InstallRootDir => Path.GetFullPath("Library/ZenTS");

        /// <summary>
        /// Physical package root (embedded <c>Packages/…</c> or <c>Library/PackageCache/…</c>).
        /// Prefer this over assuming <c>Packages/{name}</c> exists on disk.
        /// </summary>
        public static string PackageResolvedRoot
        {
            get
            {
                try
                {
                    var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                        $"Packages/{PackageName}/package.json");
                    if (info != null
                        && !string.IsNullOrEmpty(info.resolvedPath)
                        && Directory.Exists(info.resolvedPath))
                    {
                        return Path.GetFullPath(info.resolvedPath);
                    }
                }
                catch (Exception)
                {
                    // Fall through to embedded path.
                }

                string embedded = Path.GetFullPath(Path.Combine("Packages", PackageName));
                if (Directory.Exists(embedded))
                {
                    return embedded;
                }

                throw new InvalidOperationException(
                    $"[ZenTS] package not resolved: {PackageName}");
            }
        }

        public static string ZentsDataPathInPackage =>
            Path.Combine(PackageResolvedRoot, "ZenTS~");

        public static string JsLibPathInPackage =>
            Path.Combine(ZentsDataPathInPackage, "jslib");

        public static string GetJsLibScriptPath(string fileName) =>
            Path.GetFullPath(Path.Combine(JsLibPathInPackage, fileName));

        public static string ZentsRuntimePathInPackage =>
            Path.GetFullPath(Path.Combine(ZentsDataPathInPackage, "zents-runtime"));

        /// <summary>
        /// Vendored QuickJS sources for Il2Cpp (no Install-time patching).
        /// </summary>
        public static string QuickJsIl2CppPathInPackage =>
            Path.GetFullPath(Path.Combine(ZentsDataPathInPackage, "quickjs-il2cpp"));

        public static string QuickJsSrcCacheDir =>
            Path.GetFullPath(Path.Combine(InstallRootDir, "QuickJsSrcCache"));

        /// <summary>
        /// Staged TS/JS toolchain files for tools outside Unity (tsc, IDE, Node).
        /// Layout: <c>types/</c> + <c>jslib/</c>.
        /// </summary>
        public static string CoreLibsRoot =>
            Path.GetFullPath(Path.Combine(InstallRootDir, "CoreLibs"));

        public static string CoreLibsTypesPath =>
            Path.Combine(CoreLibsRoot, "types");

        public static string CoreLibsJsLibPath =>
            Path.Combine(CoreLibsRoot, "jslib");

        public static string CoreLibsStampPath =>
            Path.Combine(CoreLibsRoot, ".stamp");

        public static string Libil2cppPatchesPathInPackage =>
            Path.GetFullPath(Path.Combine(ZentsDataPathInPackage, "patches", "libil2cpp"));

        public static string LocalIl2CppDataPath => $"{InstallRootDir}/LocalIl2CppData-{Application.platform}";

        public static string LocalIl2CppPath => $"{LocalIl2CppDataPath}/il2cpp";

        public static string LocalLibil2cppPath => $"{LocalIl2CppPath}/libil2cpp";

        public static string LocalQuickJsSrcPath => $"{LocalLibil2cppPath}/quickjs";

        public static string LocalZentsPath => Path.Combine(LocalLibil2cppPath, "zents");

        public static string TypesPathInPackage =>
            Path.GetFullPath(Path.Combine(ZentsDataPathInPackage, "types"));

        public static string TypesScaffoldPathInPackage =>
            Path.GetFullPath(Path.Combine(TypesPathInPackage, "scaffold"));

        public static string GeneratedZentsPath =>
            Path.GetFullPath(Path.Combine(LocalZentsPath, "generated"));

        public static string PackageGeneratedZentsPath =>
            Path.GetFullPath(Path.Combine(ZentsRuntimePathInPackage, "generated"));

        public static string BuildWin64GeneratedZentsPath =>
            Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "Build-Win64",
                "Il2CppOutputProject",
                "IL2CPP",
                "libil2cpp",
                "zents",
                "generated"));

        public static string BuildWin64ZentsPath =>
            Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "Build-Win64",
                "Il2CppOutputProject",
                "IL2CPP",
                "libil2cpp",
                "zents"));

        public static string GetManagedStrippedDuplicatePath(UnityEditor.BuildTarget buildTarget) =>
            $"{InstallRootDir}/ManagedStripped/{buildTarget}";

        public static string GetTempAotProjectOutputDir(UnityEditor.BuildTarget target) =>
            $"Temp/TempAotProject/{target}";

        public static string PackagePluginsRoot =>
            Path.GetFullPath(Path.Combine(PackageResolvedRoot, "Plugins"));
    }
}
