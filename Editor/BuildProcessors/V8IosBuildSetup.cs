// Copyright 2026 Code Philosophy. MIT License.
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
namespace ZenTS
{
    internal sealed class V8IosBuildSetup:IPostprocessBuildWithReport
    {
        public int callbackOrder=>20;
        public void OnPostprocessBuild(BuildReport report)
        {
            if(report.summary.platform!=BuildTarget.iOS||!Settings.EnableForCurrentBuildTarget)return;
            string root=report.summary.outputPath,key=V8PlatformBuildSetup.TargetKey(BuildTarget.iOS),sdk=V8PlatformBuildSetup.Validate(CommonDirs.LocalZentsPath,key);
            string folder=Path.Combine(root,"Libraries/ZenTSV8");Directory.CreateDirectory(folder);
            string file=PBXProject.GetPBXProjectPath(root);var project=new PBXProject();project.ReadFromFile(file);
            string target=project.GetUnityFrameworkTargetGuid();
            // Unity 2022 simulator libraries are x86_64, including on Apple Silicon hosts.
            string architecture=key=="ios-simulator-x64"?"x86_64":"arm64";
            foreach(string guid in new[]{target,project.GetUnityMainTargetGuid(),project.TargetGuidByName("GameAssembly")})
                if(!string.IsNullOrEmpty(guid))project.SetBuildProperty(guid,"ARCHS",architecture);
            foreach(string name in new[]{"libzents_v8_engine.a","libv8_monolith.a"})
            {
                File.Copy(Path.Combine(sdk,"lib",name),Path.Combine(folder,name),true);
                string path="Libraries/ZenTSV8/"+name;string guid=project.AddFile(path,path,PBXSourceTree.Source);project.AddFileToBuild(target,guid);
            }
            project.AddBuildProperty(target,"LIBRARY_SEARCH_PATHS","$(PROJECT_DIR)/Libraries/ZenTSV8");
            project.AddBuildProperty(target,"OTHER_LDFLAGS","-lc++");
            foreach(string framework in new[]{"Foundation.framework","CoreFoundation.framework","Security.framework"})project.AddFrameworkToProject(target,framework,false);
            project.WriteToFile(file);
            File.Copy(Path.Combine(sdk,"official-sdk.json"),Path.Combine(folder,"official-sdk.json"),true);
        }
    }
}
#endif
