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

        public static bool Exists => File.Exists(TsconfigPath) && Directory.Exists(SrcDir);
    }
}
