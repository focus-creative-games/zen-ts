// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
namespace ZenTS.Editor
{
    public static class ReadOnlyAcceptance
    {
        public static void Run()
        {
            try
            {
                var info=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/"+CommonDirs.PackageName+"/package.json");
                if(info==null||info.source!=PackageSource.Git||!CommonDirs.PackageResolvedRoot.Replace('\\','/').Contains("/Library/PackageCache/"))throw new Exception("Expected a Git dependency resolved in PackageCache");
                bool denied=false;string probe=Path.Combine(CommonDirs.PackageResolvedRoot,"write-probe.tmp");
                try{File.WriteAllText(probe,"must fail");File.Delete(probe);}catch(UnauthorizedAccessException){denied=true;}
                if(!denied)throw new Exception("PackageCache write probe was not denied");
                NativeEditorPlugins.Ensure();
                string plugin=NativeEditorPlugins.AssetRoot+"/windows-x64/zents_v8.dll";
                string before=File.ReadAllText(plugin+".meta");string guid=AssetDatabase.AssetPathToGUID(plugin);
                NativeEditorPlugins.Ensure();
                if(File.ReadAllText(plugin+".meta")!=before||AssetDatabase.AssetPathToGUID(plugin)!=guid)throw new Exception("Non-idempotent generated plugin metadata");
                var importer=(PluginImporter)AssetImporter.GetAtPath(plugin);
                if(!importer.GetCompatibleWithEditor()||importer.GetCompatibleWithAnyPlatform()||importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64))throw new Exception("Expected an Editor-only plugin");
                if(!NativeSdkStore.Resolve("windows-x64").StartsWith(CommonDirs.InstallRootDir,StringComparison.OrdinalIgnoreCase))throw new Exception("SDK is not in Library");
                var installer=new LocalInstaller();installer.InstallLocal();
                if(!installer.HasInstalledToLocal())throw new Exception("Local install missing");
                XmlBindingsGenerate.Generate();
                Debug.Log("[ZenTS][ReadOnly] Git PackageCache write denied; SDK Library install; generated Editor-only plugin/meta; idempotent repair; Install/XML generation PASS");
                V8Acceptance.RunEditorTests();
            }
            catch(Exception e){Debug.LogError("[ZenTS][ReadOnly] FAILED: "+e);EditorApplication.Exit(1);}
        }
        // Run in a separate Editor process with a prepared alternate SDK supplied by the harness.
        public static void RunSdkSafetyTests()
        {
            string catalog=null;int exitCode=0;
            try
            {
                NativeEditorPlugins.Ensure();
                string original=NativeSdkStore.Resolve("windows-x64");
                string alternate=Environment.GetEnvironmentVariable("ZENTS_TEST_SDK");
                if(string.IsNullOrEmpty(alternate))throw new Exception("ZENTS_TEST_SDK is required");
                catalog=File.ReadAllText(NativeSdkStore.CatalogPath);
                string plugin=NativeEditorPlugins.AssetRoot+"/windows-x64/zents_v8.dll";
                string digest=NativeSdkStore.HashFile(plugin);
                JsAppDomain.Reset(_=>"export function value(){return 42;}");
                if(JsAppDomain.GetFunction<Func<int>>("safety","value")()!=42)throw new Exception("V8 did not load");
                Debug.Log("[ZenTS][ReadOnly] Safety runtime loaded");
                string descriptor=Path.Combine(alternate,"installation.json");
                string json=File.ReadAllText(descriptor);
                try
                {
                    var bad=JsonUtility.FromJson<NativeSdkStore.Descriptor>(json);bad.abi=999;
                    File.WriteAllText(descriptor,JsonUtility.ToJson(bad));
                    bool rejected=false;
                    try{NativeSdkStore.Import(alternate);}catch(InvalidDataException){rejected=true;}
                    if(!rejected||File.ReadAllText(NativeSdkStore.CatalogPath)!=catalog)throw new Exception("Invalid ABI changed catalog");
                }
                finally{File.WriteAllText(descriptor,json);}
                Debug.Log("[ZenTS][ReadOnly] Safety ABI rejection checked");
                var valid=NativeSdkStore.Validate(alternate,true);
                string victim=Path.Combine(alternate,valid.files.First(x=>x.path.StartsWith("licenses/",StringComparison.Ordinal)).path);
                byte[] bytes=File.ReadAllBytes(victim);
                try
                {
                    File.AppendAllText(victim,"corruption");bool rejected=false;
                    try{NativeSdkStore.Import(alternate);}catch(InvalidDataException){rejected=true;}
                    if(!rejected||File.ReadAllText(NativeSdkStore.CatalogPath)!=catalog)throw new Exception("Corrupt SDK changed catalog");
                }
                finally{File.WriteAllBytes(victim,bytes);}
                Debug.Log("[ZenTS][ReadOnly] Safety corruption rejection checked");
                NativeSdkStore.Import(alternate);
                Debug.Log("[ZenTS][ReadOnly] Safety alternate SDK imported");
                bool restart=false;
                try{NativeEditorPlugins.Ensure();}catch(InvalidOperationException e){restart=e.Message.Contains("Restart");}
                if(!restart||NativeSdkStore.HashFile(plugin)!=digest)throw new Exception("Loaded library replacement was not blocked");
                Debug.Log("[ZenTS][ReadOnly] Invalid ABI rejected; corrupt SDK rejected; catalog preserved; loaded DLL preserved; restart required PASS");
            }
            catch(Exception e){Debug.LogError("[ZenTS][ReadOnly] Safety FAILED: "+e);exitCode=1;}
            finally{if(catalog!=null)File.WriteAllText(NativeSdkStore.CatalogPath,catalog);}
            EditorApplication.Exit(exitCode);
        }
        public static void RepairAndRun()=>Run();
    }
}
