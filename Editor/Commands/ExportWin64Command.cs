// Copyright 2026 Code Philosophy

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using ZTS;

namespace ZTS.Editor
{
    /// <summary>
    /// Export StandaloneWindows64 Il2Cpp as a VS/Il2Cpp solution under Build-Win64
    /// for fast native iteration (edit zts under Il2CppOutputProject, then sync back).
    /// </summary>
    public static class ExportWin64Command
    {
        public const string OutputDir = "Build-Win64";

        public static void ExportSolution()
        {
            var installer = new LocalInstaller();
            if (!installer.HasInstalledToLocal())
            {
                throw new InvalidOperationException(
                    "[ZTS] Run ZTS/Install... before exporting Build-Win64.");
            }

            string scene = FindFirstEnabledScene();
            if (string.IsNullOrEmpty(scene))
            {
                throw new InvalidOperationException("[ZTS] No enabled scene in Build Settings.");
            }

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 1);
            UnityEditor.WindowsStandalone.UserBuildSettings.createSolution = true;

            string outDir = Path.GetFullPath(OutputDir);
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, PlayerSettings.productName + ".exe");

            Debug.Log($"[ZTS] Export Build-Win64 start out={outPath} scene={scene}");

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary s = report.summary;
            Debug.Log($"[ZTS] Export Build-Win64 result={s.result} errors={s.totalErrors}");
            if (s.result != BuildResult.Succeeded)
            {
                throw new Exception($"Build-Win64 export failed: {s.result}");
            }

            Debug.Log(
                "[ZTS] Export OK. Edit C++ under Build-Win64/Il2CppOutputProject/IL2CPP/libil2cpp/zts ; "
                + "sync back with sync-runtime-zts.bat");
        }

        private static string FindFirstEnabledScene()
        {
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.enabled && !string.IsNullOrEmpty(s.path))
                {
                    return s.path;
                }
            }

            // Prefer project smoke scene when Build Settings has no enabled scene.
            const string fallback = "Assets/Scenes/TestScene.unity";
            if (File.Exists(fallback))
            {
                return fallback;
            }

            string[] guids = AssetDatabase.FindAssets("t:Scene");
            if (guids.Length > 0)
            {
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            }

            return null;
        }
    }
}
