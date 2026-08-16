using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using ZTS.Editor.Typescript;

namespace ZTS.BuildProcessors
{
    /// <summary>
    /// Docs/spec/14-TYPESCRIPT.md §8.3: tsc check, 1:1 emit, copy out/ → StreamingAssets/ZTS.
    /// </summary>
    internal sealed class CopyTypescriptStreaming : IPreprocessBuildWithReport
    {
        public int callbackOrder => 50;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!TsProjectPaths.Exists)
            {
                return;
            }

            try
            {
                TypescriptToolchain.Check();
                TypescriptToolchain.Emit();
            }
            catch (Exception ex)
            {
                throw new BuildFailedException("[ZTS] TypeScript build step failed:\n" + ex.Message);
            }
        }
    }
}
