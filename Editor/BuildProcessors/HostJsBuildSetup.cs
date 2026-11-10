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
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZenTS.BuildProcessors
{
    /// <summary>
    /// Selects the host-JS backend for browser / minigame targets by setting the
    /// <c>ZENTS_HOST_JS</c> scripting define. Runs before <see cref="SetupIl2CppEnv"/>.
    ///
    /// Module precompilation is handled by HostJsModulesBuild after TS/test publishing.
    /// </summary>
    internal sealed class HostJsBuildSetup : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static string savedArguments;
        private static bool argumentsConfigured;
        private const string Define = "ZENTS_HOST_JS";

        public int callbackOrder => 1;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Settings.EnableForCurrentBuildTarget)
                return;
            bool hostJs = HostJsPlatform.IsTarget(report.summary.platform);
            if (hostJs && !argumentsConfigured)
            {
                savedArguments = PlayerSettings.GetAdditionalIl2CppArgs();
                argumentsConfigured = true;
                PlayerSettings.SetAdditionalIl2CppArgs(NativeBuildArguments.ForHost(savedArguments));
                EditorApplication.update += RestoreArgumentsAfterBuild;
            }
            if (hostJs)
                ApplyDefine(HostJsPlatform.NamedTarget(report.summary.platform), true);

            if (hostJs)
            {
                if (!HostJsPlatform.ExceptionSupport(report.summary.platform).ToString().StartsWith("Full", StringComparison.Ordinal))
                    throw new BuildFailedException("[ZenTS] HostJs requires WebGL Full exception support so CLR errors can cross the JS/wasm boundary.");
                Debug.Log("[ZenTS] Host-JS backend enabled for this build (ZENTS_HOST_JS).");
            }
        }

        public void OnPostprocessBuild(BuildReport report) => RestoreArguments();
        private static void RestoreArgumentsAfterBuild()
        {
            if (!BuildPipeline.isBuildingPlayer)
                RestoreArguments();
        }
        private static void RestoreArguments()
        {
            if (!argumentsConfigured)
                return;
            argumentsConfigured = false;
            EditorApplication.update -= RestoreArgumentsAfterBuild;
            PlayerSettings.SetAdditionalIl2CppArgs(savedArguments);
        }

        private static void ApplyDefine(NamedBuildTarget target, bool enable)
        {
            try
            {
                string current = PlayerSettings.GetScriptingDefineSymbols(target);
                var parts = new System.Collections.Generic.HashSet<string>(
                    current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.Ordinal);

                if (enable)
                {
                    parts.Add(Define);
                }
                else
                {
                    parts.Remove(Define);
                }

                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", parts));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ZenTS] Skip host-JS define for {target}: {ex.Message}");
            }
        }
    }
}