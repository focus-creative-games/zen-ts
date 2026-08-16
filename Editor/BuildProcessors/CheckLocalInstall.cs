// Copyright 2026 Code Philosophy

using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ZTS.BuildProcessors
{
    internal sealed class CheckLocalInstall : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Settings.EnableForCurrentBuildTarget)
            {
                return;
            }

            var installer = new LocalInstaller();
            if (!installer.HasInstalledToLocal())
            {
                throw new BuildFailedException(
                    "[ZTS] Local install not found. Run menu 'ZTS/Install...' before building.");
            }

            if (installer.NeedReinstallAfterUpdatePackage())
            {
                throw new BuildFailedException(
                    "[ZTS] Local install is outdated. Re-run 'ZTS/Install...' before building.");
            }
        }
    }
}
