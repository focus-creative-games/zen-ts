// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZenTS
{
    /// <summary>Rebuildable Editor-only assets. The UPM package and SDK snapshots stay immutable.</summary>
    [InitializeOnLoad]
    public static class NativeEditorPlugins
    {
        public const string AssetRoot="Assets/ZenTSGenerated/Native";
        static NativeEditorPlugins()
        {
            EditorApplication.delayCall+=Bootstrap;
            EditorApplication.update+=WatchCatalog;
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state!=PlayModeStateChange.ExitingEditMode)return;
                try{Ensure();}catch(Exception e){Debug.LogError(e.Message);EditorApplication.isPlaying=false;}
            };
        }
        private static double NextCheck;
        private static string LastCatalogStamp;
        private static void WatchCatalog()
        {
            if(EditorApplication.timeSinceStartup<NextCheck||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            NextCheck=EditorApplication.timeSinceStartup+2;
            var info=new FileInfo(NativeSdkStore.CatalogPath);
            string stamp=info.Exists?info.Length+":"+info.LastWriteTimeUtc.Ticks:"missing";
            if(stamp==LastCatalogStamp)return;LastCatalogStamp=stamp;Bootstrap();
        }
        private static void Bootstrap()
        {
            if(!NativeSdkStore.HasTarget(NativeSdkStore.EditorTarget))return;
            try{Ensure();MonoHooks.GetIl2CppFolderHook.EnsureInstalled();}
            catch(Exception e){Debug.LogWarning("[ZenTS] Native SDK installation: "+e.Message);}
        }
        private static string GuidFor(string logical)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("com.code-philosophy.zen-ts/native/"+logical))).Replace("-","").Substring(0,32).ToLowerInvariant();
        }
        private static string Metadata(string logical,string target)
        {
            string os=target=="windows-x64"?"Windows":"OSX",cpu=target.EndsWith("arm64",StringComparison.Ordinal)?"ARM64":"x86_64";
            return "fileFormatVersion: 2\nguid: "+GuidFor(logical)+"\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      Any: \n    second:\n      enabled: 0\n      settings: {}\n  - first:\n      Editor: Editor\n    second:\n      enabled: 1\n      settings:\n        CPU: "+cpu+"\n        OS: "+os+"\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
        }
        private static void EnsureFolder(string assetPath)
        {
            if(assetPath=="Assets")return;
            string parent=Path.GetDirectoryName(assetPath).Replace('\\','/');EnsureFolder(parent);
            if(!Directory.Exists(assetPath))Directory.CreateDirectory(assetPath);
            if(!File.Exists(assetPath+".meta"))File.WriteAllText(assetPath+".meta","fileFormatVersion: 2\nguid: "+GuidFor("folder/"+assetPath)+"\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",new UTF8Encoding(false));
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern IntPtr dlopen(string path,int flags);
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int dlclose(IntPtr handle);
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern uint _dyld_image_count();
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern IntPtr _dyld_get_image_name(uint index);
        private static bool Loaded(string path)
        {
            if(Application.platform==RuntimePlatform.WindowsEditor)return GetModuleHandle(Path.GetFileName(path))!=IntPtr.Zero;
            if(Application.platform==RuntimePlatform.OSXEditor)
            {
                string name=Path.GetFileName(path);
                for(uint i=0;i<_dyld_image_count();i++)
                    if(Path.GetFileName(Marshal.PtrToStringAnsi(_dyld_get_image_name(i)))==name)return true;
                IntPtr handle=dlopen(Path.GetFullPath(path),0x10|0x1); // RTLD_NOLOAD | RTLD_LAZY
                if(handle!=IntPtr.Zero){dlclose(handle);return true;}
            }
            return false;
        }
        private static readonly Dictionary<string,string> PluginDigests=new Dictionary<string,string>(StringComparer.Ordinal);
        private static bool SameContent(string path,string digest)
        {
            if(!File.Exists(path))return false;
            var info=new FileInfo(path);string stamp=info.Length+":"+info.LastWriteTimeUtc.Ticks+":"+digest;
            if(PluginDigests.TryGetValue(path,out string cached)&&cached==stamp)return true;
            if(NativeSdkStore.HashFile(path)!=digest)return false;
            PluginDigests[path]=stamp;return true;
        }
        private static bool SameMetadata(string path,string target)
        {
            if(!File.Exists(path+".meta"))return false;
            var importer=AssetImporter.GetAtPath(path) as PluginImporter;
            return importer!=null&&AssetDatabase.AssetPathToGUID(path)==GuidFor(target+"/"+Path.GetFileName(path))&&
                importer.GetCompatibleWithEditor()&&!importer.GetCompatibleWithAnyPlatform()&&
                importer.GetEditorData("OS")== (target=="windows-x64"?"Windows":"OSX")&&
                importer.GetEditorData("CPU")== (target.EndsWith("arm64",StringComparison.Ordinal)?"ARM64":"x86_64")&&
                !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64)&&!importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX)&&
                !importer.GetCompatibleWithPlatform(BuildTarget.Android)&&!importer.GetCompatibleWithPlatform(BuildTarget.iOS)&&!importer.GetCompatibleWithPlatform(BuildTarget.WebGL);
        }
        private sealed class InstallFile {public string source,path,metadata,digest;public bool content,meta;}
        public static void Ensure()
        {
            string target=NativeSdkStore.EditorTarget;
            if(target==null)throw new PlatformNotSupportedException("[ZenTS] This Editor OS is not supported");
            string sdk=NativeSdkStore.Resolve(target);
            var descriptor=NativeSdkStore.Validate(sdk);
            string[] names=target=="windows-x64"?new[]{"zents_v8.dll"}:new[]{"libzents_v8.dylib","libMonoHookUtils_OSX.dylib"};
            var plan=names.Select(name=>
            {
                string logical=target+"/"+name,path=AssetRoot+"/"+logical;
                var file=new InstallFile{source=Path.Combine(sdk,"plugin",name),path=path,metadata=Metadata(logical,target),digest=descriptor.files.Single(x=>x.path=="plugin/"+name).sha256};
                file.content=!SameContent(path,file.digest);
                file.meta=!SameMetadata(path,target);
                return file;
            }).ToArray();
            // Inspect every replacement before changing any asset. Never overwrite a loaded module.
            foreach(var file in plan)
                if(file.content&&Loaded(file.path))throw new InvalidOperationException("[ZenTS] Restart the Unity Editor to activate the updated SDK. Loaded plugin was preserved: "+file.path);
            if(!plan.Any(x=>x.content||x.meta))return;
            AssetDatabase.DisallowAutoRefresh();AssetDatabase.StartAssetEditing();
            try
            {
                EnsureFolder(AssetRoot+"/"+target);
                foreach(var file in plan)
                {
                    if(file.content)
                    {
                        string stage=Path.Combine(CommonDirs.InstallRootDir,"plugin-"+Guid.NewGuid().ToString("N"));
                        File.Copy(file.source,stage);
                        try{if(File.Exists(file.path))File.Replace(stage,file.path,null);else File.Move(stage,file.path);}
                        finally{if(File.Exists(stage))File.Delete(stage);}
                    }
                    if(file.meta)File.WriteAllText(file.path+".meta",file.metadata,new UTF8Encoding(false));
                }
            }
            finally{AssetDatabase.StopAssetEditing();AssetDatabase.AllowAutoRefresh();}
            foreach(var file in plan)
            {
                AssetDatabase.ImportAsset(file.path,ImportAssetOptions.ForceSynchronousImport);
                var importer=AssetImporter.GetAtPath(file.path) as PluginImporter;
                if(importer!=null&&!SameMetadata(file.path,target))
                {
                    importer.SetCompatibleWithAnyPlatform(false);importer.SetCompatibleWithEditor(true);
                    importer.SetEditorData("OS",target=="windows-x64"?"Windows":"OSX");
                    importer.SetEditorData("CPU",target.EndsWith("arm64",StringComparison.Ordinal)?"ARM64":"x86_64");
                    foreach(var platform in new[]{BuildTarget.StandaloneWindows64,BuildTarget.StandaloneOSX,BuildTarget.Android,BuildTarget.iOS,BuildTarget.WebGL})importer.SetCompatibleWithPlatform(platform,false);
                    importer.SaveAndReimport();
                }
                if(importer==null||!importer.GetCompatibleWithEditor()||importer.GetCompatibleWithAnyPlatform()||importer.GetEditorData("OS")!=(target=="windows-x64"?"Windows":"OSX")||importer.GetEditorData("CPU")!=(target.EndsWith("arm64",StringComparison.Ordinal)?"ARM64":"x86_64"))throw new InvalidDataException("[ZenTS] Incorrect generated native plugin importer: "+file.path);
            }
        }
    }
}
