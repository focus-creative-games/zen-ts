// Copyright 2026 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#if UNITY_6000_3_OR_NEWER && UNITY_EDITOR_OSX
#define NEW_IL2CPP_PATH
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using ZenTS.Utils;
using Debug = UnityEngine.Debug;

namespace ZenTS
{
    public class LocalInstaller
    {

        private readonly UnityVersion _curVersion;

        public bool RequiresEditorRestart
        {
            get; private set;
        }

        public LocalInstaller()
        {
            _curVersion = new UnityVersion(Application.unityVersion);
        }

        public string ApplicationIl2cppPath
        {
            get
            {
#if NEW_IL2CPP_PATH
#if UNITY_IOS
                string platformDirName = "iOSSupport";
#elif UNITY_TVOS
                string platformDirName = "AppleTVSupport";
#elif UNITY_VISIONOS
                string platformDirName = "VisionOSPlayer";
#else
                string platformDirName = "iOSSupport";
#endif
                return $"{EditorApplication.applicationContentsPath}/../../PlaybackEngines/{platformDirName}/il2cpp";
#else
                return $"{EditorApplication.applicationContentsPath}/il2cpp";
#endif
            }
        }

        public void InstallLocal()
        {
            RequiresEditorRestart = false;
            try
            {
                RunInitLocalIl2CppData(ApplicationIl2cppPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZenTS] Install failed:\n{ex}");
                throw;
            }
        }

        public bool HasInstalledToLocal()
        {
            return Directory.Exists(CommonDirs.LocalLibil2cppPath)
                   && File.Exists(Path.Combine(CommonDirs.LocalZentsPath, "ZenTSCommon.h"))
                   && File.Exists(ZenTSConfWriter.LocalConfPath);
        }

        public bool NeedReinstallAfterUpdatePackage()
        {
            if (!HasInstalledToLocal())
            {
                return false;
            }

            if (!InstallFingerprint.TryRead(out InstallFingerprintData saved))
            {
                return true;
            }

            string engineId = ZenTSConfWriter.ConfValues.EngineId;
            if (!string.Equals(saved.engineVersionId, engineId, StringComparison.Ordinal)
                || !string.Equals(saved.unityVersion, Application.unityVersion, StringComparison.Ordinal)
                || !string.Equals(saved.packageContentStamp, ComputePackageContentStamp(), StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private void RunInitLocalIl2CppData(string editorIl2cppPath)
        {
            if (!Directory.Exists(editorIl2cppPath))
            {
                throw new InvalidOperationException($"Editor il2cpp path not found: {editorIl2cppPath}");
            }

            if (!Directory.Exists(CommonDirs.ZentsRuntimePathInPackage))
            {
                throw new InvalidOperationException(
                    $"zents-runtime missing: {CommonDirs.ZentsRuntimePathInPackage}");
            }

            if (NativeSdkStore.HasTarget(NativeSdkStore.EditorTarget))
                NativeEditorPlugins.Ensure();
            MonoHooks.GetIl2CppFolderHook.EnsureInstalled();

            Directory.CreateDirectory(CommonDirs.InstallRootDir);
            string localIl2CppDataDir = CommonDirs.LocalIl2CppDataPath;
            DirectoryUtil.RecreateDir(localIl2CppDataDir);

#if !NEW_IL2CPP_PATH
            DirectoryUtil.CopyDir(
                $"{Directory.GetParent(editorIl2cppPath)}/MonoBleedingEdge",
                $"{localIl2CppDataDir}/MonoBleedingEdge",
                true);
#endif
            DirectoryUtil.CopyDir(editorIl2cppPath, CommonDirs.LocalIl2CppPath, true);
#if NEW_IL2CPP_PATH
            string buildDir = $"{CommonDirs.LocalIl2CppPath}/build";
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm
                || RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                DirectoryUtil.CopyDir($"{buildDir}/deploy_arm64", $"{buildDir}/deploy", false);
            }
            else
            {
                DirectoryUtil.CopyDir($"{buildDir}/deploy_x86_64", $"{buildDir}/deploy", false);
            }
#endif

            if (!UnityIl2CppPatchUtil.TryResolvePatchFile(
                    Application.unityVersion, out string il2cppPatchFile, out string il2cppPatchKey))
            {
                string series = UnityIl2CppPatchUtil.GetSeriesKey(Application.unityVersion);
                throw new InvalidOperationException(
                    $"[ZenTS] No libil2cpp patch for Unity {Application.unityVersion}. "
                    + $"Expected a floor patch under "
                    + Path.Combine(CommonDirs.Libil2cppPatchesPathInPackage, series ?? "?"));
            }

            Debug.Log($"[ZenTS] Applying libil2cpp patch {il2cppPatchKey}: {il2cppPatchFile}");
            PatchApplier.Apply(il2cppPatchFile, CommonDirs.LocalIl2CppPath, stripComponents: 1);

            DirectoryUtil.CopyDir(CommonDirs.ZentsRuntimePathInPackage, CommonDirs.LocalZentsPath, true);
            // Retired M0 JSON/ccall prototype. HostJs now shares the CLR bridge through EngineApi.
            foreach (string relative in new[] { "hostjs/HostRegistry.h", "hostjs/HostRegistry.cpp", "hostjs/HostDispatch.h", "hostjs/HostDispatch.cpp", "hostjs/HostInternalCalls.h", "hostjs/HostInternalCalls.cpp" })
            {
                string retired = Path.Combine(CommonDirs.LocalZentsPath, relative);
                if (File.Exists(retired))
                    File.Delete(retired);
            }
            if (NativeSdkStore.HasTarget("windows-x64"))
                V8BuildSetup.ConfigureStaticLibraries(CommonDirs.LocalZentsPath);
            string facade = File.ReadAllText(Path.Combine(CommonDirs.JsLibPathInPackage, "zentslib.js"));
            if (facade.Contains(")zents\""))
                throw new InvalidOperationException("[ZenTS] JS facade raw-string delimiter collision.");
            string includePath = Path.Combine(CommonDirs.LocalZentsPath, "jvm", "ZentsLibScript.inc");
            string embedded = File.ReadAllText(includePath);
            int marker = embedded.IndexOf("R\"zents(", StringComparison.Ordinal);
            if (marker < 0)
                throw new InvalidOperationException("[ZenTS] Invalid embedded facade template.");
            File.WriteAllText(includePath, embedded.Substring(0, marker) + "R\"zents(" + facade + ")zents\";\n", new UTF8Encoding(false));


            string defines = ApplyScriptingDefines();
            ZenTSConfWriter.WriteLocal(Application.unityVersion);
            EnsureMinimalGeneratedStubs();
            try
            {
                ZenTS.Editor.XmlBindingsGenerate.Generate();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[ZenTS] XmlBindingsGenerate after Install failed (stubs remain until Generate Xml Bindings):\n"
                    + ex.Message);
            }
            ValidateLocalTree();

            InstallFingerprint.Write(new InstallFingerprintData
            {
                unityVersion = Application.unityVersion,
                engineVersionId = ZenTSConfWriter.ConfValues.EngineId,
                libil2cppPatchKey = il2cppPatchKey,
                packageContentStamp = ComputePackageContentStamp(),
                defines = defines,
            });

            DirectoryUtil.RemoveDir("Library/Il2cppBuildCache", true);
            DirectoryUtil.RemoveDir("Library/Bee", true);

            if (!HasInstalledToLocal())
            {
                throw new InvalidOperationException("[ZenTS] Installation failed: local tree incomplete.");
            }

            CoreLibsEnsure.Ensure();

            RequiresEditorRestart = true;
            Debug.Log(
                $"[ZenTS] Install succeeded. unity={Application.unityVersion} engine={ZenTSConfWriter.ConfValues.EngineId} "
                + $"libil2cppPatch={il2cppPatchKey} defines={defines}. "
                + "Restart the Unity Editor if scripting defines changed.");
        }

        private static void EnsureMinimalGeneratedStubs()
        {
            string dir = CommonDirs.GeneratedZentsPath;
            Directory.CreateDirectory(dir);

            WriteIfMissing(Path.Combine(dir, "BuiltinScripts.inc"),
                "/* M0 stub � Generate/All will replace. */\n");
            WriteIfMissing(Path.Combine(dir, "MethodBridgeStub.h"),
                "#pragma once\nnamespace zents { void MethodBridge_Initialize(); }\n");
            WriteIfMissing(Path.Combine(dir, "MethodBridgeStub.cpp"),
                "#include \"MethodBridgeStub.h\"\nnamespace zents { void MethodBridge_Initialize() {} }\n");
            WriteIfMissing(Path.Combine(dir, "PropertyBridgeStub.h"),
                "#pragma once\nnamespace zents { void PropertyBridge_Initialize(); }\n");
            WriteIfMissing(Path.Combine(dir, "PropertyBridgeStub.cpp"),
                "#include \"PropertyBridgeStub.h\"\nnamespace zents { void PropertyBridge_Initialize() {} }\n");
            WriteIfMissing(Path.Combine(dir, "DelegateBridgeStub.h"),
                "#pragma once\nnamespace zents { void DelegateBridge_Initialize(); }\n");
            WriteIfMissing(Path.Combine(dir, "DelegateBridgeStub.cpp"),
                "#include \"DelegateBridgeStub.h\"\nnamespace zents { void DelegateBridge_Initialize() {} }\n");
            WriteIfMissing(Path.Combine(dir, "MarshalBindings.h"),
                "// Generated by MarshalAsCodegen. Do not edit.\n#pragma once\n\n#define ZENTS_HAS_MARSHAL_BINDINGS 1\n\nnamespace zents\n{\n    void RegisterMarshalBindingTables();\n}\n");
            WriteIfMissing(Path.Combine(dir, "MarshalBindings.cpp"),
                "// Generated by MarshalAsCodegen. Do not edit.\n#include \"MarshalBindings.h\"\n#include \"../marshal/MarshalAsXmlTable.h\"\n\nnamespace zents\n{\nnamespace marshal_as_bindings\n{\n} // namespace marshal_as_bindings\n\nvoid RegisterMarshalBindingTables()\n{\n    MarshalAsXmlTable::Clear();\n}\n} // namespace zents\n");
            WriteIfMissing(Path.Combine(dir, "AliasBindings.h"),
                "#pragma once\nnamespace zents { void RegisterAliasBindingTables(); }\n");
            WriteIfMissing(Path.Combine(dir, "AliasBindings.cpp"),
                "#include \"AliasBindings.h\"\nnamespace zents { void RegisterAliasBindingTables() {} }\n");
            WriteIfMissing(Path.Combine(dir, "ExtensionBindings.h"),
                "#pragma once\nnamespace zents { void RegisterExtensionBindingTables(); }\n");
            WriteIfMissing(Path.Combine(dir, "ExtensionBindings.cpp"),
                "#include \"ExtensionBindings.h\"\nnamespace zents { void RegisterExtensionBindingTables() {} }\n");
        }

        private static void WriteIfMissing(string path, string contents)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, contents, new UTF8Encoding(false));
            }
        }

        private static void ValidateLocalTree()
        {
            if (!File.Exists(Path.Combine(CommonDirs.LocalZentsPath, "ZenTSCommon.h")))
            {
                throw new InvalidOperationException("[ZenTS] ZenTSCommon.h missing after Install.");
            }

            if (!File.Exists(ZenTSConfWriter.LocalConfPath))
            {
                throw new InvalidOperationException("[ZenTS] ZenTSConf.inc missing after Install.");
            }
        }

        private string ApplyScriptingDefines()
        {
            var targets = new List<NamedBuildTarget>
            {
                NamedBuildTarget.Standalone,
                NamedBuildTarget.Android,
                NamedBuildTarget.iOS,
                NamedBuildTarget.WebGL,
            };

            if (Enum.TryParse("WeixinMiniGame", out BuildTargetGroup wechatGroup))
                targets.Add(NamedBuildTarget.FromBuildTargetGroup(wechatGroup));

            const string define = "ZENTS_V8";
            foreach (NamedBuildTarget t in targets)
            {
                try
                {
                    string current = PlayerSettings.GetScriptingDefineSymbols(t);
                    var parts = new HashSet<string>(
                        current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                        StringComparer.Ordinal);
                    // ZenTS owns its reserved scripting-symbol namespace.
                    parts.RemoveWhere(x => x.StartsWith("ZENTS_", StringComparison.Ordinal) || x.StartsWith("ZTS_", StringComparison.Ordinal));
                    parts.Add((t == NamedBuildTarget.WebGL || t.TargetName == "WeixinMiniGame") ? "ZENTS_HOST_JS" : define);
                    PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", parts.OrderBy(x => x)));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ZenTS] Skip scripting defines for {t}: {ex.Message}");
                }
            }

            return define;
        }

        private static string ComputePackageContentStamp()
        {
            string runtime = CommonDirs.ZentsRuntimePathInPackage;
            string patches = CommonDirs.Libil2cppPatchesPathInPackage;
            long max = 0;
            foreach (string root in new[] { runtime, patches })
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    long ticks = File.GetLastWriteTimeUtc(file).Ticks;
                    if (ticks > max)
                    {
                        max = ticks;
                    }
                }
            }

            return max.ToString();
        }
    }
}
