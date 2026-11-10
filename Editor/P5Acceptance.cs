// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace ZenTS.Editor
{
    public static class P5Acceptance
    {
        public static void BuildAndroidX64(){Build(BuildTarget.Android,"Build-Android/ZenTSTest-x64.apk","x64");}
        public static void BuildAndroidArm64(){Build(BuildTarget.Android,"Build-Android/ZenTSTest-arm64.apk","arm64");}
        public static void BuildMacArm64(){Build(BuildTarget.StandaloneOSX,"Build-macOS/ZenTSTest-arm64.app","arm64");}
        public static void BuildMacX64(){Build(BuildTarget.StandaloneOSX,"Build-macOS/ZenTSTest-x64.app","x64");}
        public static void ExportIosSimulator(){Build(BuildTarget.iOS,"Build-iOS-Simulator","simulator");}
        public static void ExportIosDevice(){Build(BuildTarget.iOS,"Build-iOS-Device","device");}
        private static void Build(BuildTarget target,string output,string cpu)
        {
            int exitCode=0;
            try
            {
                var installer=new LocalInstaller();if(!installer.HasInstalledToLocal()||installer.NeedReinstallAfterUpdatePackage())installer.InstallLocal();
                NamedBuildTarget group=NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
                PlayerSettings.SetScriptingBackend(group,ScriptingImplementation.IL2CPP);
                PlayerSettings.SetApplicationIdentifier(group,"com.codephilosophy.zentstest");
                if(target==BuildTarget.Android){PlayerSettings.Android.targetArchitectures=cpu=="x64"?AndroidArchitecture.X86_64:AndroidArchitecture.ARM64;PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel23;EditorUserBuildSettings.buildAppBundle=false;}
                if(target==BuildTarget.StandaloneOSX)EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(target),"Architecture",cpu);
                if(target==BuildTarget.iOS){PlayerSettings.iOS.sdkVersion=cpu=="simulator"?iOSSdkVersion.SimulatorSDK:iOSSdkVersion.DeviceSDK;PlayerSettings.iOS.targetOSVersionString="13.0";PlayerSettings.SetArchitecture(NamedBuildTarget.iOS,1);}
                var scenes=EditorBuildSettings.scenes.Where(x=>x.enabled).Select(x=>x.path).ToArray();
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,target=target,locationPathName=output,options=BuildOptions.Development});
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("P5 build failed: "+report.summary.result);
                Debug.Log("[ZenTS][P5] Build PASS "+target+" "+cpu);
            }
            catch(Exception e){Debug.LogError(e);exitCode=1;}
            finally{V8PlatformBuildSetup.Restore();}
            EditorApplication.Exit(exitCode);
        }
    }
}
