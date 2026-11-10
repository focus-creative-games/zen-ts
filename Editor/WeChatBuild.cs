// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
namespace ZenTS.Editor
{
    /// <summary>Optional integration with the official WeChat Unity/Tuanjie conversion SDK.</summary>
    public static class WeChatBuild
    {
        private const string SdkPackage = "com.qq.weixin.minigame";
        private sealed class SettingsSnapshot : IDisposable
        {
            private readonly Dictionary<PropertyInfo, object> values = new Dictionary<PropertyInfo, object>();
            private readonly BuildTarget target;
            private readonly NamedBuildTarget named;
            private readonly bool background, automaticGraphics;
            private readonly UnityEngine.Rendering.GraphicsDeviceType[] graphics;
            private readonly Il2CppCodeGeneration codeGeneration;
            public SettingsSnapshot(BuildTarget target)
            {
                this.target = target;
                named = HostJsPlatform.NamedTarget(target);
                background = PlayerSettings.runInBackground;
                automaticGraphics = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
                graphics = PlayerSettings.GetGraphicsAPIs(target);
                codeGeneration = PlayerSettings.GetIl2CppCodeGeneration(named);
                var type = typeof(PlayerSettings).GetNestedType(target == BuildTarget.WebGL ? "WebGL" : "MiniGame", BindingFlags.Public) ?? typeof(PlayerSettings).GetNestedType("WeixinMiniGame", BindingFlags.Public);
                if (type == null)
                    throw new BuildFailedException("[ZenTS] Missing host platform settings: " + target);
                foreach (string name in new[] { "template", "emscriptenArgs", "threadsSupport", "dataCaching", "debugSymbolMode", "compressionFormat", "exceptionSupport", "linkerTarget" })
                {
                    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                    if (property != null && property.CanRead && property.CanWrite)
                        values.Add(property, property.GetValue(null));
                }
            }
            public void Dispose()
            {
                foreach (var pair in values)
                    pair.Key.SetValue(null, pair.Value);
                PlayerSettings.runInBackground = background;
                PlayerSettings.SetGraphicsAPIs(target, graphics);
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, automaticGraphics);
                PlayerSettings.SetIl2CppCodeGeneration(named, codeGeneration);
            }
        }

        [MenuItem("ZenTS/Build WeChat Mini Game")]
        public static void BuildFromMenu() => Build(null, null);
        public static void RunBatch()
        {
            try
            {
                Build(Environment.GetEnvironmentVariable("ZENTS_WECHAT_APPID"), Environment.GetEnvironmentVariable("ZENTS_WECHAT_OUTPUT"), Environment.GetEnvironmentVariable("ZENTS_WECHAT_ACCEPTANCE") == "1");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogError("[ZenTS][WeChat] Build FAILED: " + e); EditorApplication.Exit(1); }
        }
        private static void Set(SerializedObject config, string path, object value, bool required = true)
        {
            var property = config.FindProperty(path);
            if (property == null)
            {
                if (required)
                    throw new BuildFailedException("[ZenTS] Unsupported WeChat SDK configuration field: " + path);
                return;
            }
            if (value is bool b)
                property.boolValue = b;
            else if (value is int i)
                property.intValue = i;
            else
                property.stringValue = (string)value;
        }
        public static void Build(string appid, string output, bool acceptance = false)
        {
            Type converter = Type.GetType("WeChatWASM.WXConvertCore, WxEditor", false);
            if (converter == null)
                throw new BuildFailedException("[ZenTS] Install the official com.qq.weixin.minigame SDK before building WeChat. See ZenTS~/wechat/README.md.");
            var sdk = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + SdkPackage + "/package.json");
            if (sdk == null)
                throw new BuildFailedException("[ZenTS] WeChat SDK must be installed through Package Manager");
            var target = BuildTarget.WebGL;
            if (Enum.TryParse("WeixinMiniGame", out BuildTarget wechat) && BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(wechat), wechat))
                target = wechat;
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new BuildFailedException("[ZenTS] Switch the active target to " + target + " before calling WeChatBuild. For batch mode pass -buildTarget WebGL (or the Tuanjie WeixinMiniGame target).");
            // The SDK's editable project settings belong to Assets, never PackageCache.
            string configAsset = "Assets/WX-WASM-SDK-V2/Editor/MiniGameConfig.asset";
            if (!File.Exists(configAsset))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configAsset));
                AssetDatabase.Refresh();
                if (!AssetDatabase.CopyAsset("Packages/" + SdkPackage + "/Editor/MiniGameConfig.asset", configAsset))
                    throw new BuildFailedException("[ZenTS] Cannot create project-local WeChat settings");
            }
            var config = (ScriptableObject)converter.GetProperty("config", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            if (AssetDatabase.GetAssetPath(config).StartsWith("Packages/", StringComparison.Ordinal))
                throw new BuildFailedException("[ZenTS] WeChat SDK returned read-only package settings instead of project-local settings");
            var serialized = new SerializedObject(config);
            serialized.Update();
            if (string.IsNullOrEmpty(appid))
                appid = serialized.FindProperty("ProjectConf.Appid").stringValue;
            if (string.IsNullOrEmpty(appid) || !System.Text.RegularExpressions.Regex.IsMatch(appid, "^wx[0-9a-fA-F]{16}$"))
                throw new BuildFailedException("[ZenTS] Configure a real Mini Game AppID in the WeChat SDK settings or ZENTS_WECHAT_APPID");
            if (string.IsNullOrEmpty(output))
                output = serialized.FindProperty("ProjectConf.DST").stringValue;
            if (string.IsNullOrEmpty(output))
                output = "Build-WeChat";
            string destination = Path.GetFullPath(output), package = Path.GetFullPath(CommonDirs.PackageResolvedRoot);
            string assets = Path.GetFullPath(Application.dataPath);
            if (destination == Path.GetPathRoot(destination) || destination == assets || destination.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("[ZenTS] WeChat output must not be a filesystem root or an Assets folder");
            if (destination == Path.GetFullPath(".") || package.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination == package || destination.StartsWith(package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("[ZenTS] WeChat output must be outside the source package");
            Set(serialized, "ProjectConf.Appid", appid);
            Set(serialized, "ProjectConf.DST", destination);
            Set(serialized, "ProjectConf.relativeDST", output, false);
            if (acceptance || string.IsNullOrEmpty(serialized.FindProperty("ProjectConf.projectName").stringValue))
                Set(serialized, "ProjectConf.projectName", Application.productName + " ZenTS");
            if (acceptance)
            {
                Set(serialized, "ProjectConf.assetLoadType", 1);
                Set(serialized, "ProjectConf.CDN", "");
            }
            string cover = serialized.FindProperty("ProjectConf.bgImageSrc").stringValue;
            if (acceptance || string.IsNullOrEmpty(cover) || cover == "Assets/WX-WASM-SDK-V2/Runtime/wechat-default/images/background.jpg")
                Set(serialized, "ProjectConf.bgImageSrc", "Packages/" + SdkPackage + "/Runtime/wechat-default/images/background.jpg");
            if (PlayerSettings.colorSpace == ColorSpace.Linear)
                Set(serialized, "CompileOptions.Webgl2", true);
            if (acceptance)
            {
                Set(serialized, "CompileOptions.DevelopBuild", true);
                Set(serialized, "CompileOptions.AutoProfile", false);
            }
            var renderThread = serialized.FindProperty("CompileOptions.enableRenderThread");
            if (!acceptance && renderThread != null && renderThread.boolValue)
                throw new BuildFailedException("[ZenTS] HostJs currently requires the WeChat single-thread build; disable enableRenderThread in the SDK settings");
            Set(serialized, "CompileOptions.enableRenderThread", false, false);
            if (acceptance)
            {
                Set(serialized, "CompileOptions.DeleteStreamingAssets", false);
                Set(serialized, "CompileOptions.enablePerfAnalysis", false, false);
                Set(serialized, "CompileOptions.enableProfileStats", false, false);
            }
            if (acceptance)
                Set(serialized, "CompileOptions.profilingFuncs", false);
            string node = Environment.GetEnvironmentVariable("ZENTS_NODE");
            if (!string.IsNullOrEmpty(node))
                Set(serialized, "CompileOptions.CustomNodePath", node, false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            string templateName = target == BuildTarget.WebGL ? "WXTemplate2022" : "WXTemplate2022TJ";
            string template = "Assets/WebGLTemplates/" + templateName;
            if (!Directory.Exists(template))
            {
                Directory.CreateDirectory("Assets/WebGLTemplates");
                AssetDatabase.Refresh();
                if (!AssetDatabase.CopyAsset("Packages/" + SdkPackage + "/WebGLTemplates/" + templateName, template))
                    throw new BuildFailedException("[ZenTS] Cannot install official WeChat build template");
            }
            using (var snapshot = new SettingsSnapshot(target))
            {
                HostJsPlatform.EnableFullExceptions(target);
                PlayerSettings.SetScriptingBackend(HostJsPlatform.NamedTarget(target), ScriptingImplementation.IL2CPP);
                var installer = new LocalInstaller();
                if (!installer.HasInstalledToLocal() || installer.NeedReinstallAfterUpdatePackage())
                    installer.InstallLocal();
                object result = converter.GetMethod("DoExport", new[] { typeof(bool) }).Invoke(null, new object[] { true });
                if (Convert.ToInt32(result) != 0)
                    throw new BuildFailedException("[ZenTS] Official WeChat converter returned " + result);
                string game = Path.Combine(destination, "minigame");
                foreach (string file in new[] { "game.js", "game.json", "project.config.json" })
                    if (!File.Exists(Path.Combine(game, file)))
                        throw new BuildFailedException("[ZenTS] WeChat export did not produce " + file);
                Debug.Log("[ZenTS][WeChat] Build PASS; platform host VM; project=" + game);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
