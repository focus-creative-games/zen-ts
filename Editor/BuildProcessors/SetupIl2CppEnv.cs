// Copyright 2026 Code Philosophy

using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZTS.BuildProcessors
{
    internal class SetupIl2CppEnv : IPreprocessBuildWithReport
    {
        public int callbackOrder => 2;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Settings.EnableForCurrentBuildTarget)
            {
                Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", "");
                return;
            }

            var installer = new LocalInstaller();
            if (!installer.HasInstalledToLocal())
            {
                throw new BuildFailedException(
                    "[ZTS] Please install ZTS first via menu 'ZTS/Install...'.");
            }

            string runtimeDir = CommonDirs.LocalIl2CppPath;
            Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", runtimeDir);
            Debug.Log($"[SetupIl2CppEnv] set UNITY_IL2CPP_PATH='{runtimeDir}'");
        }
    }
}
