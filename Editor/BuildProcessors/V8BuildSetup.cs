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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ZenTS
{
    // V8's build is external. Unity links the pinned SDK + ZenTS static adapter.
    internal sealed class V8BuildSetup : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        [Serializable]
        private sealed class OfficialSdkIdentity
        {
            public string provider;
            public bool source_pristine;
            public bool deps_pristine;
            public string[] upstream_patches;
        }

        internal const string CacheMarker = "--compiler-flags=-DZENTS_V8_STATIC_CRT=1";
        private static bool environmentConfigured;
        private static string previousCompilerOptions;
        private static string previousIl2CppArgs;
        public int callbackOrder => 3;

        internal static void ConfigureStaticLibraries(string runtimeRoot)
        {
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
                return;
            string full=Path.GetFullPath(runtimeRoot);
            if(full.StartsWith(CommonDirs.PackageResolvedRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("[ZenTS] Never generate link settings inside the resolved package");
            string folder=Path.Combine(runtimeRoot,"engine","v8");Directory.CreateDirectory(folder);
            string sdkRoot=NativeSdkStore.Resolve("windows-x64");NativeSdkStore.Validate(sdkRoot,true);
            string libraries;
            if(full==Path.GetFullPath(CommonDirs.LocalZentsPath))libraries=Path.Combine(sdkRoot,"lib");
            else
            {
                // Exported VS projects are self-contained and do not depend on PackageCache/Library.
                string portable=Path.Combine(folder,"sdk");libraries=Path.Combine(portable,"lib");Directory.CreateDirectory(libraries);
                foreach(string name in new[]{"zents_v8_engine.lib","v8_monolith.lib"})File.Copy(Path.Combine(sdkRoot,"lib",name),Path.Combine(libraries,name),true);
                File.Copy(Path.Combine(sdkRoot,"official-sdk.json"),Path.Combine(portable,"official-sdk.json"),true);
            }
            string adapter = Path.Combine(libraries, "zents_v8_engine.lib");
            string sdk = Path.Combine(libraries, "v8_monolith.lib");
            foreach (string file in new[] { adapter, sdk })
                if (!File.Exists(file))
                    throw new BuildFailedException(
                        "[ZenTS] Missing pinned V8 static library: " + file +
                        ". Run build_official.py, then build_win64.py --sdk <exported SDK> before Install/build.");
            var lines = new StringBuilder("// Generated for this build location. Do not edit.\n#pragma once\n");
            lines.Append("#if defined(_DEBUG) || !defined(ZENTS_V8_STATIC_CRT)\n")
                .Append(
                    "#error \"V8 requires the configured /MT CRT. Use the ZenTS Win64 build/export setup.\"\n#endif\n");
            foreach (string file in new[] { adapter, sdk })
                lines.Append("#pragma comment(lib, \"")
                    .Append(Path.GetFullPath(file).Replace('\\', '/'))
                    .Append("\")\n");
            foreach (string file in new[] { "winmm.lib", "dbghelp.lib", "shlwapi.lib", "advapi32.lib" })
                lines.Append("#pragma comment(lib, \"").Append(file).Append("\")\n");
            File.WriteAllText(Path.Combine(folder, "V8Libraries.h"), lines.ToString(), new UTF8Encoding(false));
        }

        private static string StaticLibraryArguments(string runtimeRoot)
        {
            string folder=Path.GetFullPath(runtimeRoot)==Path.GetFullPath(CommonDirs.LocalZentsPath)
                ? Path.Combine(NativeSdkStore.Resolve("windows-x64"),"lib")
                : Path.GetFullPath(Path.Combine(runtimeRoot,"engine","v8","sdk","lib"));
            return " --additional-libraries=\"" + Path.Combine(folder, "zents_v8_engine.lib") +
                   "\" --additional-libraries=\"" + Path.Combine(folder, "v8_monolith.lib") + "\"";
        }

        // MSVC appends _CL_ after generated arguments; Bee prepends --compiler-flags,
        // so those flags cannot override its /MTd. Preserve Debug optimization/symbols,
        // while selecting the release static CRT required by the pinned V8 SDK.
        internal static void ConfigureBuildEnvironment()
        {
            if (environmentConfigured)
                return;
            previousCompilerOptions = Environment.GetEnvironmentVariable("_CL_");
            previousIl2CppArgs = PlayerSettings.GetAdditionalIl2CppArgs();
            environmentConfigured = true;
            EditorApplication.update += RestoreAfterBuild;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreBuildEnvironment;
            try
            {
                Environment.SetEnvironmentVariable("_CL_", (previousCompilerOptions + " /MT /U_DEBUG").Trim());
                string args = previousIl2CppArgs ?? string.Empty;
                if (!args.Contains(CacheMarker))
                    args += " " + CacheMarker;
                PlayerSettings.SetAdditionalIl2CppArgs(
                    (args + StaticLibraryArguments(CommonDirs.LocalZentsPath)).Trim());
            }
            catch
            {
                RestoreBuildEnvironment();
                throw;
            }
        }

        private static void RestoreAfterBuild()
        {
            if (!BuildPipeline.isBuildingPlayer)
                RestoreBuildEnvironment();
        }

        internal static void RestoreBuildEnvironment()
        {
            if (!environmentConfigured)
                return;
            try
            {
                Environment.SetEnvironmentVariable("_CL_", previousCompilerOptions);
                PlayerSettings.SetAdditionalIl2CppArgs(previousIl2CppArgs ?? string.Empty);
            }
            finally
            {
                environmentConfigured = false;
                EditorApplication.update -= RestoreAfterBuild;
                AssemblyReloadEvents.beforeAssemblyReload -= RestoreBuildEnvironment;
            }
        }

        internal static void ConfigureExport(string exportRoot)
        {
            if (string.Equals(Path.GetExtension(exportRoot), ".exe", StringComparison.OrdinalIgnoreCase))
                exportRoot = Path.GetDirectoryName(exportRoot);
            exportRoot = Path.GetFullPath(exportRoot);
            string runtime = Path.Combine(exportRoot, "Il2CppOutputProject", "IL2CPP", "libil2cpp", "zents");
            if (!Directory.Exists(runtime))
                return;
            ConfigureStaticLibraries(runtime);
            string project = Path.Combine(exportRoot, "Il2CppOutputProject", "Il2CppOutputProject.vcxproj");
            var document = XDocument.Load(project);
            XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
            foreach (var command in document.Descendants(ns + "NMakeBuildCommandLine"))
            {
                string value = Regex.Replace(
                    command.Value, @" --additional-libraries=""[^""]*(?:zents_v8_engine(?:_release)?|wee8-mt|v8_monolith)\.lib""",
                    string.Empty);
                value += StaticLibraryArguments(runtime);
                // Separate exported VS cache from Unity's initial build. _CL_ is not
                // represented in Bee's command-line cache, so never reuse MTd objects.
                value = Regex.Replace(value, @"--compiler-flags=-DZENTS_V8_STATIC_CRT=\d+", "--compiler-flags=-DZENTS_V8_STATIC_CRT=2");
                if (!value.Contains("--compiler-flags=-DZENTS_V8_STATIC_CRT=2")) value += " --compiler-flags=-DZENTS_V8_STATIC_CRT=2";
                const string prefix = "set \"_CL_=%_CL_% /MT /U_DEBUG\" && ";
                if (!value.StartsWith(prefix, StringComparison.Ordinal))
                    value = prefix + value;
                command.Value = value;
            }
            document.Save(project);
            // Unity 2022 exports an obsolete SDK pin even when a newer SDK is installed.
            string kits = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots", "KitsRoot10", null) as string;
            if (string.IsNullOrEmpty(kits)) kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10");
            string lib = Path.Combine(kits, "Lib");
            string sdkVersion = Directory.Exists(lib) ? Directory.GetDirectories(lib)
                .Select(Path.GetFileName).Where(v => Version.TryParse(v, out _) &&
                    File.Exists(Path.Combine(kits, "Include", v, "um", "Windows.h")) &&
                    File.Exists(Path.Combine(lib, v, "um", "x64", "kernel32.lib")))
                .OrderByDescending(v => Version.Parse(v)).FirstOrDefault() : null;
            if (sdkVersion == null) throw new BuildFailedException("[ZenTS] No complete Windows 10 SDK is installed.");
            foreach (string file in Directory.GetFiles(exportRoot, "*.vcxproj", SearchOption.AllDirectories))
            {
                var doc = XDocument.Load(file);bool changed=false;
                foreach (var version in doc.Descendants(ns + "WindowsTargetPlatformVersion"))
                    if (version.Value != sdkVersion) { version.Value = sdkVersion; changed=true; }
                foreach (var toolset in doc.Descendants(ns + "PlatformToolset"))
                    if (toolset.Value == "v140" || toolset.Value == "v141" || toolset.Value == "v142") { toolset.Value = "v143"; changed=true; }
                if (changed) doc.Save(file);
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Settings.EnableForCurrentBuildTarget)
                return;
            if (HostJsPlatform.IsTarget(report.summary.platform) || report.summary.platform == BuildTarget.StandaloneOSX || report.summary.platform == BuildTarget.Android || report.summary.platform == BuildTarget.iOS) return;
            if (report.summary.platform != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("[ZenTS] This package currently validates Windows x64 V8 only. Supply the target V8 SDK or complete the host-JS backend before building this platform.");
            NativeEditorPlugins.Ensure();
            ConfigureStaticLibraries(CommonDirs.LocalZentsPath);
            ConfigureBuildEnvironment();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!Settings.EnableForCurrentBuildTarget || report.summary.platform != BuildTarget.StandaloneWindows64)
                return;
            try
            {
                ConfigureExport(report.summary.outputPath);
            }
            finally
            {
                RestoreBuildEnvironment();
            }
        }
    }
}
