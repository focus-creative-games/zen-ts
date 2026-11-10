// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using ZenTS.Editor.Typescript;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace ZenTS.Editor
{
    internal sealed class HostJsModulesBuild:IPreprocessBuildWithReport
    {
        [Serializable] private class Entry {public string name;public string source;}
        [Serializable] private class Input {public Entry[] modules;public Entry[] scripts;}
        public int callbackOrder=>100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if(!HostJsPlatform.IsTarget(report.summary.platform)||!Settings.EnableForCurrentBuildTarget)return;
            Compile();
            if(HostJsPlatform.IsWeChat(report.summary.platform))
            {
                // Packages/Plugins/WebGL is a platform-specific folder. Keep it read-only;
                // materialize a Weixin-only copy for Tuanjie's separate target instead.
                string asset="Assets/ZenTSHostGenerated/ZenTSWeChatHost.jslib";
                File.Copy(Path.Combine(CommonDirs.PackageResolvedRoot,"Plugins/WebGL/HostEngine.jslib"),asset,true);
                AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceSynchronousImport);
                var bridge=(PluginImporter)AssetImporter.GetAtPath(asset);
                bridge.SetCompatibleWithAnyPlatform(false);bridge.SetCompatibleWithEditor(false);
                bridge.SetCompatibleWithPlatform(BuildTarget.WebGL,false);
                bridge.SetCompatibleWithPlatform(report.summary.platform,true);bridge.SaveAndReimport();
                var modules=(PluginImporter)AssetImporter.GetAtPath("Assets/ZenTSHostGenerated/ZenTSModules.jslib");
                modules.SetCompatibleWithPlatform(report.summary.platform,true);modules.SaveAndReimport();
            }
        }
        internal static void Compile()
        {
            var modules=new Dictionary<string,string>(StringComparer.Ordinal);
            string project=Path.GetDirectoryName(Application.dataPath);
            Action<string,string,string> scan=(root,pattern,prefix)=>{
                if(!Directory.Exists(root))return;
                foreach(string file in Directory.GetFiles(root,pattern,SearchOption.AllDirectories)){
                    string key=file.Substring(root.Length).TrimStart('/',(char)92).Replace((char)92,'/');
                    if(key.EndsWith(".txt",StringComparison.Ordinal))key=key.Substring(0,key.Length-4);
                    key=key.Substring(0,key.Length-3);modules[prefix+key]=File.ReadAllText(file);
                }
            };
            scan(Path.Combine(Application.streamingAssetsPath,"Tests/Js"),"*.js","");
            scan(Path.Combine(Application.streamingAssetsPath,"ZenTS"),"*.js","");
            string app=Path.Combine(Application.streamingAssetsPath,"app.js");if(File.Exists(app))modules["app"]=File.ReadAllText(app);
            scan(Path.Combine(Application.dataPath,"Resources/Tests/Js"),"*.js.txt","");
            scan(Path.Combine(Application.dataPath,"Resources/ZenTS"),"*.js.txt","");
            scan(TsProjectPaths.OutDir,"*.js","");
            var imports=new HashSet<string>(StringComparer.Ordinal);
            foreach(string source in modules.Values.ToArray())foreach(Match m in Regex.Matches(source,"[\"'](csharp:[^\"']+)[\"']"))imports.Add(m.Groups[1].Value);
            foreach(string spec in imports)modules[spec]=CsharpVirtualModule.Synthesize(spec);
            string cache=Path.Combine(project,"Library/ZenTS/HostJs");Directory.CreateDirectory(cache);
            string input=Path.Combine(cache,"modules.json");File.WriteAllText(input,JsonUtility.ToJson(new Input{modules=modules.Select(x=>new Entry{name=x.Key,source=x.Value}).ToArray(),scripts=new[]{new Entry{name="zentslib.js",source=File.ReadAllText(Path.Combine(CommonDirs.JsLibPathInPackage,"zentslib.js"))}}}));
            string output=Path.Combine(Application.dataPath,"ZenTSHostGenerated/ZenTSModules.jslib");Directory.CreateDirectory(Path.GetDirectoryName(output));
            string script=Path.Combine(CommonDirs.ZentsDataPathInPackage,"host-js/build-modules.mjs");
            var start=new ProcessStartInfo("node",$"\"{script}\" \"{input}\" \"{output}\" \"{TsProjectPaths.TsProjectRoot}\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(start)){string stdout=process.StandardOutput.ReadToEnd(),stderr=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new BuildFailedException(stderr);UnityEngine.Debug.Log("[ZenTS] "+stdout.Trim());}
            AssetDatabase.ImportAsset("Assets/ZenTSHostGenerated/ZenTSModules.jslib",ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
