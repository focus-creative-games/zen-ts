// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace ZenTS.Editor
{
    public static class P4Acceptance
    {
        public static void BuildWebGL()
        {
            try
            {
                var installer = new LocalInstaller();
                if (!installer.HasInstalledToLocal() || installer.NeedReinstallAfterUpdatePackage())
                    installer.InstallLocal();
                PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.WebGL.memorySize = 256;
                PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Debug);
                var scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, target = BuildTarget.WebGL, locationPathName = "Build-WebGL", options = BuildOptions.Development });
                if (result.summary.result != BuildResult.Succeeded)
                    throw new Exception("WebGL build failed: " + result.summary.result);
                Debug.Log("[ZenTS][P4] WebGL build PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
        }
    }
}
