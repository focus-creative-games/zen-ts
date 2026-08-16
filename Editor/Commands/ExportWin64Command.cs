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

#if UNITY_EDITOR_WIN
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
#endif
