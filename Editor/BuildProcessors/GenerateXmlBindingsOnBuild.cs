// Copyright 2026 Code Philosophy

using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using ZTS.Editor;

namespace ZTS.BuildProcessors
{
    /// <summary>
    /// Ensure Alias/Extension C++ tables are regenerated before Player builds.
    /// </summary>
    internal sealed class GenerateXmlBindingsOnBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Settings.EnableForCurrentBuildTarget)
            {
                return;
            }

            XmlBindingsGenerate.Generate();
        }
    }
}
