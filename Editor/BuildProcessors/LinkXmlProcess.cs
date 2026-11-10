// Copyright 2026 Code Philosophy. MIT License.
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
namespace ZenTS.Editor
{
    // Packages do not participate in the Assets-only link.xml discovery.
    internal sealed class LinkXmlProcess : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;
        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            return Settings.EnableForCurrentBuildTarget
                ? Path.Combine(CommonDirs.PackageResolvedRoot, "link.xml") : null;
        }
    }
}
