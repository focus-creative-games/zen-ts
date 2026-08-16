// Copyright 2026 Code Philosophy

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using ZTS.Utils;
using Debug = UnityEngine.Debug;

namespace ZTS
{
    public class LocalInstaller
    {
        private static readonly string[] s_ztsDefinePrefixes =
        {
            "ZTS_QUICKJS",
            "ZTS_QUICKJS_VERSION_",
        };

        private readonly UnityVersion _curVersion;

        public bool RequiresEditorRestart { get; private set; }

        public LocalInstaller()
        {
            _curVersion = new UnityVersion(Application.unityVersion);
        }

        public string ApplicationIl2cppPath =>
            $"{EditorApplication.applicationContentsPath}/il2cpp";

        public void InstallLocal()
        {
            RequiresEditorRestart = false;
            try
            {
                RunInitLocalIl2CppData(ApplicationIl2cppPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZTS] Install failed:\n{ex}");
                throw;
            }
        }

        public bool HasInstalledToLocal()
        {
            return Directory.Exists(CommonDirs.LocalLibil2cppPath)
                   && Directory.Exists(CommonDirs.LocalZtsPath)
                   && Directory.Exists(CommonDirs.LocalQuickJsSrcPath);
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

            string qjsId = QuickJsVersionUtil.FromVendoredIl2CppTree().Id;
            if (!string.Equals(saved.quickjsVersionId, qjsId, StringComparison.Ordinal)
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

            if (!Directory.Exists(CommonDirs.ZtsRuntimePathInPackage))
            {
                throw new InvalidOperationException(
                    $"zts-runtime missing: {CommonDirs.ZtsRuntimePathInPackage}");
            }

            QuickJsVersionInfo qjsInfo = QuickJsVersionUtil.FromVendoredIl2CppTree();
            QuickJsVersionUtil.EnsureAvailable(qjsInfo);
            if (!string.Equals(Settings.Instance.quickjsVersionId, qjsInfo.Id, StringComparison.Ordinal))
            {
                Settings.Instance.quickjsVersionId = qjsInfo.Id;
                Settings.Save();
                Debug.Log($"[ZTS] Settings.quickjsVersionId synced to vendored {qjsInfo.Id}");
            }

            WarnIfEditorPluginMissing();

            Directory.CreateDirectory(CommonDirs.InstallRootDir);
            string localIl2CppDataDir = CommonDirs.LocalIl2CppDataPath;
            DirectoryUtil.RecreateDir(localIl2CppDataDir);

            DirectoryUtil.CopyDir(
                $"{Directory.GetParent(editorIl2cppPath)}/MonoBleedingEdge",
                $"{localIl2CppDataDir}/MonoBleedingEdge",
                true);
            DirectoryUtil.CopyDir(editorIl2cppPath, CommonDirs.LocalIl2CppPath, true);

            if (!UnityIl2CppPatchUtil.TryResolvePatchFile(
                    Application.unityVersion, out string il2cppPatchFile, out string il2cppPatchKey))
            {
                string series = UnityIl2CppPatchUtil.GetSeriesKey(Application.unityVersion);
                throw new InvalidOperationException(
                    $"[ZTS] No libil2cpp patch for Unity {Application.unityVersion}. "
                    + $"Expected a floor patch under "
                    + Path.Combine(CommonDirs.Libil2cppPatchesPathInPackage, series ?? "?"));
            }

            Debug.Log($"[ZTS] Applying libil2cpp patch {il2cppPatchKey}: {il2cppPatchFile}");
            PatchApplier.Apply(il2cppPatchFile, CommonDirs.LocalIl2CppPath, stripComponents: 1);

            DirectoryUtil.CopyDir(CommonDirs.ZtsRuntimePathInPackage, CommonDirs.LocalZtsPath, true);

            const string quickjsSourceKey = "vendored";
            InstallQuickJsSources(qjsInfo);

            string defines = ApplyScriptingDefines();
            ZTSConfWriter.WriteLocal(qjsInfo, Application.unityVersion);
            EnsureMinimalGeneratedStubs();
            ValidateLocalTree();

            InstallFingerprint.Write(new InstallFingerprintData
            {
                unityVersion = Application.unityVersion,
                quickjsVersionId = qjsInfo.Id,
                libil2cppPatchKey = il2cppPatchKey,
                quickjsPatchKey = quickjsSourceKey,
                packageContentStamp = ComputePackageContentStamp(),
                defines = defines,
            });

            DirectoryUtil.RemoveDir("Library/Il2cppBuildCache", true);
            DirectoryUtil.RemoveDir("Library/Bee", true);

            if (!HasInstalledToLocal())
            {
                throw new InvalidOperationException("[ZTS] Installation failed: local tree incomplete.");
            }

            RequiresEditorRestart = true;
            Debug.Log(
                $"[ZTS] Install succeeded. unity={Application.unityVersion} quickjs={qjsInfo.Id} "
                + $"libil2cppPatch={il2cppPatchKey} quickjsSource={quickjsSourceKey} defines={defines}. "
                + "Restart the Unity Editor if scripting defines changed.");
        }

        /// <summary>
        /// Copy vendored <c>ZTS~/quickjs-il2cpp</c> as-is. No Install-time QuickJS patches.
        /// </summary>
        private static void InstallQuickJsSources(QuickJsVersionInfo qjsInfo)
        {
            string src = qjsInfo.SourceDir;
            string dest = CommonDirs.LocalQuickJsSrcPath;
            if (!Directory.Exists(src))
            {
                throw new InvalidOperationException($"[ZTS] Vendored QuickJS missing: {src}");
            }

            DirectoryUtil.CopyDir(src, dest, true);

            // Docs stay in package only.
            foreach (string name in new[] { "README.md" })
            {
                string doc = Path.Combine(dest, name);
                if (File.Exists(doc))
                {
                    File.Delete(doc);
                }
            }

            // Never ship POSIX libc or CLI entry points into Il2Cpp.
            foreach (string name in new[] { "quickjs-libc.c", "qjs.c", "qjsc.c", "run-test262.c" })
            {
                string bad = Path.Combine(dest, name);
                if (File.Exists(bad))
                {
                    File.Delete(bad);
                }
            }

            if (!File.Exists(Path.Combine(dest, "quickjs.c"))
                || !File.Exists(Path.Combine(dest, "zts_qjs_std_stubs.c")))
            {
                throw new InvalidOperationException(
                    "[ZTS] Vendored QuickJS incomplete after copy (need quickjs.c + zts_qjs_std_stubs.c).");
            }

            Debug.Log($"[ZTS] Installed vendored QuickJS {qjsInfo.Id} → {dest}");
        }

        private static void EnsureMinimalGeneratedStubs()
        {
            string dir = CommonDirs.GeneratedZtsPath;
            Directory.CreateDirectory(dir);

            WriteIfMissing(Path.Combine(dir, "BuiltinScripts.inc"),
                "/* M0 stub — Generate/All will replace. */\n");
            WriteIfMissing(Path.Combine(dir, "MethodBridgeStub.h"),
                "#pragma once\nnamespace zts { void MethodBridge_Initialize(); }\n");
            WriteIfMissing(Path.Combine(dir, "MethodBridgeStub.cpp"),
                "#include \"MethodBridgeStub.h\"\nnamespace zts { void MethodBridge_Initialize() {} }\n");
            WriteIfMissing(Path.Combine(dir, "PropertyBridgeStub.h"),
                "#pragma once\nnamespace zts { void PropertyBridge_Initialize(); }\n");
            WriteIfMissing(Path.Combine(dir, "PropertyBridgeStub.cpp"),
                "#include \"PropertyBridgeStub.h\"\nnamespace zts { void PropertyBridge_Initialize() {} }\n");
            WriteIfMissing(Path.Combine(dir, "DelegateBridgeStub.h"),
                "#pragma once\nnamespace zts { void DelegateBridge_Initialize(); }\n");
            WriteIfMissing(Path.Combine(dir, "DelegateBridgeStub.cpp"),
                "#include \"DelegateBridgeStub.h\"\nnamespace zts { void DelegateBridge_Initialize() {} }\n");
            WriteIfMissing(Path.Combine(dir, "MarshalBindings.h"),
                "#pragma once\nnamespace zts { void RegisterMarshalBindingTables(); }\n");
            WriteIfMissing(Path.Combine(dir, "MarshalBindings.cpp"),
                "#include \"MarshalBindings.h\"\nnamespace zts { void RegisterMarshalBindingTables() {} }\n");
            WriteIfMissing(Path.Combine(dir, "AliasBindings.h"),
                "#pragma once\nnamespace zts { void RegisterAliasBindingTables(); }\n");
            WriteIfMissing(Path.Combine(dir, "AliasBindings.cpp"),
                "#include \"AliasBindings.h\"\nnamespace zts { void RegisterAliasBindingTables() {} }\n");
            WriteIfMissing(Path.Combine(dir, "ExtensionBindings.h"),
                "#pragma once\nnamespace zts { void RegisterExtensionBindingTables(); }\n");
            WriteIfMissing(Path.Combine(dir, "ExtensionBindings.cpp"),
                "#include \"ExtensionBindings.h\"\nnamespace zts { void RegisterExtensionBindingTables() {} }\n");
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
            if (!File.Exists(Path.Combine(CommonDirs.LocalQuickJsSrcPath, "quickjs.c")))
            {
                throw new InvalidOperationException("[ZTS] quickjs.c missing after Install.");
            }

            if (!File.Exists(Path.Combine(CommonDirs.LocalQuickJsSrcPath, "zts_qjs_std_stubs.c")))
            {
                throw new InvalidOperationException("[ZTS] zts_qjs_std_stubs.c missing after Install.");
            }

            if (!File.Exists(Path.Combine(CommonDirs.LocalZtsPath, "ZTSCommon.h")))
            {
                throw new InvalidOperationException("[ZTS] ZTSCommon.h missing after Install.");
            }

            if (!File.Exists(ZTSConfWriter.LocalConfPath))
            {
                throw new InvalidOperationException("[ZTS] ZTSConf.inc missing after Install.");
            }
        }

        private static void WarnIfEditorPluginMissing()
        {
            string dll = Path.Combine(CommonDirs.PackagePluginsRoot, "quickjs", "win32-x64", "quickjs.dll");
            if (!File.Exists(dll))
            {
                Debug.LogWarning($"[ZTS] Editor QuickJS plugin missing (non-fatal for Install): {dll}");
            }
        }

        private string ApplyScriptingDefines()
        {
            var targets = new[]
            {
                NamedBuildTarget.Standalone,
                NamedBuildTarget.Android,
                NamedBuildTarget.iOS,
                NamedBuildTarget.WebGL,
            };

            const string define = "ZTS_QUICKJS";
            foreach (NamedBuildTarget t in targets)
            {
                try
                {
                    string current = PlayerSettings.GetScriptingDefineSymbols(t);
                    var parts = new HashSet<string>(
                        current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                        StringComparer.Ordinal);
                    foreach (string p in parts.Where(x => s_ztsDefinePrefixes.Any(pref => x.StartsWith(pref, StringComparison.Ordinal))).ToList())
                    {
                        // keep ZTS_QUICKJS; strip version macros we don't use yet
                        if (p != "ZTS_QUICKJS")
                        {
                            parts.Remove(p);
                        }
                    }

                    parts.Add(define);
                    PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", parts.OrderBy(x => x)));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ZTS] Skip scripting defines for {t}: {ex.Message}");
                }
            }

            return define;
        }

        private static string ComputePackageContentStamp()
        {
            string runtime = CommonDirs.ZtsRuntimePathInPackage;
            string patches = CommonDirs.Libil2cppPatchesPathInPackage;
            string quickjs = CommonDirs.QuickJsIl2CppPathInPackage;
            long max = 0;
            foreach (string root in new[] { runtime, patches, quickjs })
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
