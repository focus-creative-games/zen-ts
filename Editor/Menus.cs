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

using System;
using UnityEditor;
using UnityEngine;
using ZTS.Editor.Typescript;

namespace ZTS
{
    public static class Menus
    {
        [MenuItem("ZTS/Install...", priority = 1)]
        public static void Install()
        {
            var installer = new LocalInstaller();
            installer.InstallLocal();
            if (installer.RequiresEditorRestart)
            {
                EditorUtility.DisplayDialog(
                    "ZTS Install",
                    "Install finished. Restart the Unity Editor if scripting defines changed.",
                    "OK");
            }
        }

        [MenuItem("ZTS/Export Build-Win64...", priority = 20)]
        public static void ExportBuildWin64()
        {
#if UNITY_EDITOR_WIN
            ZTS.Editor.ExportWin64Command.ExportSolution();
#else
            EditorUtility.DisplayDialog(
                "ZTS",
                "Export Build-Win64 is only available on Windows Editor.",
                "OK");
#endif
        }

        [MenuItem("ZTS/Generate Xml Bindings", priority = 30)]
        public static void GenerateXmlBindings()
        {
            Run("Generate Xml Bindings", () => ZTS.Editor.XmlBindingsGenerate.Generate());
        }

        [MenuItem("ZTS/Init TypeScript Project", priority = 40)]
        public static void InitTypeScriptProject()
        {
            Run("Init TypeScript Project", () => TsScaffold.InitOrUpdate());
        }

        [MenuItem("ZTS/Generate Typings", priority = 41)]
        public static void GenerateTypings()
        {
            Run("Generate Typings", () =>
            {
                if (!TsProjectPaths.Exists)
                {
                    throw new InvalidOperationException(
                        "TsProject not found. Run 'ZTS/Init TypeScript Project' first.");
                }

                CsharpDtsGenerator.Generate();
            });
        }

        [MenuItem("ZTS/Compile TypeScript", priority = 42)]
        public static void CompileTypeScript()
        {
            Run("Compile TypeScript", () =>
            {
                TypescriptToolchain.Check();
                TypescriptToolchain.Emit();
                var map = TsExportManifest.LoadIfNeeded();
                int modules = map != null ? map.Count : 0;
                Debug.Log($"[ZTS] Compile TypeScript OK ({modules} module(s) in js-exports.json)");
            });
        }

        [MenuItem("ZTS/About", priority = 100)]
        public static void About()
        {
            Debug.Log("ZTS — Mono Editor + Il2Cpp Player (Install → Build-Win64). See Docs/spec.");
        }

        private static void Run(string title, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZTS] {title} failed:\n{ex.Message}");
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("ZTS " + title, ex.Message, "OK");
                }
            }
        }
    }
}
