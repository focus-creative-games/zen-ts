// Copyright 2026 Code Philosophy

using System.IO;
using UnityEngine;

namespace ZTS
{
    public static class CommonDirs
    {
        public static string PackageName => "com.code-philosophy.zts";

        public static string InstallRootDir => Path.GetFullPath("Library/ZTS");

        public static string ZtsDataPathInPackage => $"Packages/{PackageName}/ZTS~";

        public static string JsLibPathInPackage => $"{ZtsDataPathInPackage}/jslib";

        public static string GetJsLibScriptPath(string fileName) =>
            Path.GetFullPath(Path.Combine(JsLibPathInPackage, fileName));

        public static string ZtsRuntimePathInPackage =>
            Path.GetFullPath(Path.Combine(ZtsDataPathInPackage, "zts-runtime"));

        /// <summary>
        /// Vendored QuickJS sources for Il2Cpp (no Install-time patching).
        /// </summary>
        public static string QuickJsIl2CppPathInPackage =>
            Path.GetFullPath(Path.Combine(ZtsDataPathInPackage, "quickjs-il2cpp"));

        public static string QuickJsSrcCacheDir =>
            Path.GetFullPath(Path.Combine(InstallRootDir, "QuickJsSrcCache"));

        public static string Libil2cppPatchesPathInPackage =>
            Path.GetFullPath(Path.Combine(ZtsDataPathInPackage, "patches", "libil2cpp"));

        public static string LocalIl2CppDataPath => $"{InstallRootDir}/LocalIl2CppData-{Application.platform}";

        public static string LocalIl2CppPath => $"{LocalIl2CppDataPath}/il2cpp";

        public static string LocalLibil2cppPath => $"{LocalIl2CppPath}/libil2cpp";

        public static string LocalQuickJsSrcPath => $"{LocalLibil2cppPath}/quickjs";

        public static string LocalZtsPath => Path.Combine(LocalLibil2cppPath, "zts");

        public static string TypesPathInPackage =>
            Path.GetFullPath(Path.Combine(ZtsDataPathInPackage, "types"));

        public static string TypesScaffoldPathInPackage =>
            Path.GetFullPath(Path.Combine(TypesPathInPackage, "scaffold"));

        public static string GeneratedZtsPath =>
            Path.GetFullPath(Path.Combine(LocalZtsPath, "generated"));

        public static string PackageGeneratedZtsPath =>
            Path.GetFullPath(Path.Combine(ZtsRuntimePathInPackage, "generated"));

        public static string BuildWin64GeneratedZtsPath =>
            Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "Build-Win64",
                "Il2CppOutputProject",
                "IL2CPP",
                "libil2cpp",
                "zts",
                "generated"));

        public static string BuildWin64ZtsPath =>
            Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "Build-Win64",
                "Il2CppOutputProject",
                "IL2CPP",
                "libil2cpp",
                "zts"));

        public static string GetManagedStrippedDuplicatePath(UnityEditor.BuildTarget buildTarget) =>
            $"{InstallRootDir}/ManagedStripped/{buildTarget}";

        public static string GetTempAotProjectOutputDir(UnityEditor.BuildTarget target) =>
            $"Temp/TempAotProject/{target}";

        public static string PackagePluginsRoot =>
            Path.GetFullPath(Path.Combine("Packages", PackageName, "Plugins"));
    }
}
