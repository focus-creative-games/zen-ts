// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZenTS.Editor.Typescript;
namespace ZenTS.Editor
{
    public static class P3Acceptance
    {
        public static void ConfigureExistingExport()
        {
            try
            {
                V8BuildSetup.ConfigureExport(Path.GetFullPath("Build-Win64/ZLuaDemo.exe"));
                Debug.Log("[ZenTS][P3] Export exe-path normalization and SDK configuration PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogError(ex); EditorApplication.Exit(1); }
        }
        public static void PrepareAndExportWin64()
        {
            try
            {
                ValidateXmlAndDeclarationsCore();
                new LocalInstaller().InstallLocal();
                XmlBindingsGenerate.Generate();
#if UNITY_EDITOR_WIN
                ExportWin64Command.ExportSolution();
                Debug.Log("[ZenTS][P3] Fresh C# Win64 export PASS");
                EditorApplication.Exit(0);
#else
                throw new PlatformNotSupportedException("Win64 solution export requires a Windows Editor.");
#endif
            }
            catch (Exception ex) { Debug.LogError("[ZenTS][P3] Export FAILED: " + ex); EditorApplication.Exit(1); }
        }
        public static void ValidateXmlAndDeclarations()
        {
            try
            {
                ValidateXmlAndDeclarationsCore();
                Debug.Log("[ZenTS][P3] XML and declarations PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogError("[ZenTS][P3] XML/declarations FAILED: " + ex); EditorApplication.Exit(1); }
        }
        private static void ValidateXmlAndDeclarationsCore()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string path = Path.GetTempFileName() + ".xml";
            try
            {
                File.WriteAllText(path, "<ZenTSMarshalAs version='1'><Assembly name='ZenTS.Tests'><Type fullName='ZenTS.Tests.Fixtures.P3AlignmentFixture'><Method name='Defaults' signature='(System.Int32,System.Int32)'><Param index='0'><MarshalAs type='BigInt'/></Param></Method></Type></Assembly></ZenTSMarshalAs>");
                bool rejected = false;
                try
                {
                    JsMarshalAsXmlRegistry.Load(new[] { path }, root);
                    JsMarshalAsXmlRegistry.ValidateAllLoadedAssemblies();
                }
                catch (JsMarshalAsConfigurationException) { rejected = true; }
                if (!rejected)
                    throw new InvalidOperationException("Invalid XML BigInt rule was accepted");
                File.WriteAllText(path, "<JsAlias version='1'><Assembly name='ZenTS.Tests'><Type fullName='ZenTS.Tests.Fixtures.P3AlignmentFixture'><Method name='DefaultLong' signature='(System.Int64)' alias='a'/><Method name='DefaultLong' signature='(System.Int64)' alias='b'/></Type></Assembly></JsAlias>");
                rejected = false;
                try
                {
                    JsAliasXmlRegistry.Load(new[] { path }, root);
                    JsAliasXmlRegistry.ValidateAllLoadedAssemblies();
                }
                catch (Exception) { rejected = true; }
                if (!rejected)
                    throw new InvalidOperationException("Duplicate XML alias rule was accepted");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
                JsMarshalAsXmlRegistry.Load(Settings.Instance.EffectiveMarshalAsXmlPaths, root);
                JsAliasXmlRegistry.Load(Settings.Instance.EffectiveAliasXmlPaths, root);
                JsExtensionXmlRegistry.Load(Settings.Instance.jsExtensionXmlPaths, root);
            }
            JsMarshalAsXmlRegistry.ValidateAllLoadedAssemblies();
            JsAliasXmlRegistry.ValidateAllLoadedAssemblies();
            CsharpDtsGenerator.Generate();
            CoreLibsEnsure.Ensure();
            string declaration = Directory.GetFiles(TsProjectPaths.GeneratedCsharpDir, "*.d.ts", SearchOption.AllDirectories)
                .Select(File.ReadAllText).First(text => text.Contains("class P3AlignmentFixture"));
            foreach (string expected in new[] { "WithOut(value: number): [boolean, number]", "OnlyOut(): [string]", "Exact(value: bigint): bigint", "ExactProperty: bigint", "DefaultLong(value: number): number" })
                if (!declaration.Contains(expected))
                    throw new InvalidOperationException("Missing declaration contract: " + expected);
            Debug.Log("[ZenTS][P3] Invalid XML, duplicate slots and generated out/BigInt declarations PASS");
        }
    }
}
