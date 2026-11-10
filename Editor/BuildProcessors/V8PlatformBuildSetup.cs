// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace ZenTS
{
    internal sealed class V8PlatformBuildSetup : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        [Serializable]
        internal sealed class Identity
        {
            public string provider; public string source_commit; public string target; public bool source_pristine; public bool deps_pristine; public bool jitless; public bool android_standard_vtable_abi; public string[] upstream_patches;
        }
        private static string previousArgs; private static bool configured;
        public int callbackOrder => 4;
        internal static string Validate(string runtime, string key) => NativeSdkStore.Resolve(key);
        internal static string EnsureInstalled(string key)
        {
            string sdk = NativeSdkStore.Resolve(key);
            NativeSdkStore.Validate(sdk, true);
            return sdk;
        }
        internal static string TargetKey(BuildTarget target)
        {
            if (target == BuildTarget.StandaloneOSX)
            {
                string architecture = EditorUserBuildSettings.GetPlatformSettings(BuildPipeline.GetBuildTargetName(target), "Architecture");
                return architecture == "arm64" ? "macos-arm64" : architecture == "x64arm64" ? "macos-universal" : "macos-x64";
            }
            if (target == BuildTarget.Android)
            {
                var cpu = PlayerSettings.Android.targetArchitectures;
                if (cpu == AndroidArchitecture.ARM64)
                    return "android-arm64";
                if (cpu == AndroidArchitecture.X86_64)
                    return "android-x64";
                throw new BuildFailedException("[ZenTS] Select one imported V8 Android ABI per build (ARM64 or x86_64).");
            }
            if (target == BuildTarget.iOS)
                return PlayerSettings.iOS.sdkVersion == iOSSdkVersion.SimulatorSDK ? "ios-simulator-x64" : "ios-arm64";
            return null;
        }
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Settings.EnableForCurrentBuildTarget)
                return;
            if (NativeSdkStore.HasTarget(NativeSdkStore.EditorTarget))
                NativeEditorPlugins.Ensure();
            string clean = Regex.Replace(PlayerSettings.GetAdditionalIl2CppArgs() ?? "", @"\s*--additional-libraries=""[^""]*engine[/\\]v8[/\\]sdks[^""]*\.a""", "");
            PlayerSettings.SetAdditionalIl2CppArgs(clean.Trim());
            string key = TargetKey(report.summary.platform);
            if (key == null)
                return;
            string sdk = EnsureInstalled(key);
            if (report.summary.platform == BuildTarget.iOS)
                return; // Xcode links the copied static libraries.
            previousArgs = PlayerSettings.GetAdditionalIl2CppArgs();
            configured = true;
            EditorApplication.update += RestoreAfterBuild;
            AssemblyReloadEvents.beforeAssemblyReload += Restore;
            EditorApplication.quitting += Restore;
            string args = previousArgs ?? "";
            foreach (string name in new[] { "libzents_v8_engine.a", "libv8_monolith.a" })
                args += " --additional-libraries=\"" + Path.Combine(sdk, "lib", name) + "\"";
            PlayerSettings.SetAdditionalIl2CppArgs(args.Trim());
        }
        private static void RestoreAfterBuild()
        {
            if (!BuildPipeline.isBuildingPlayer)
                Restore();
        }
        internal static void Restore()
        {
            if (!configured)
                return;
            configured = false;
            PlayerSettings.SetAdditionalIl2CppArgs(previousArgs ?? "");
            EditorApplication.update -= RestoreAfterBuild;
            AssemblyReloadEvents.beforeAssemblyReload -= Restore;
            EditorApplication.quitting -= Restore;
        }
        public void OnPostprocessBuild(BuildReport report)
        {
            Restore();
        }
    }
}
