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

using UnityEditor;
using UnityEngine;

namespace ZTS.Editor
{
    internal static class ZtsSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Project/ZTS", SettingsScope.Project)
            {
                label = "ZTS",
                guiHandler = _ =>
                {
                    Settings s = Settings.Instance;
                    EditorGUI.BeginChangeCheck();

                    EditorGUILayout.LabelField("Il2Cpp", EditorStyles.boldLabel);
                    s.enable = EditorGUILayout.Toggle("Enable ZTS", s.enable);
                    s.quickjsVersionId = EditorGUILayout.TextField("QuickJS version id", s.quickjsVersionId ?? "");

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("TypeScript", EditorStyles.boldLabel);
                    s.enableTsPlayGate = EditorGUILayout.Toggle(
                        new GUIContent("Play gate (tsc --noEmit)", "Docs/spec/14-TYPESCRIPT.md §8.1"),
                        s.enableTsPlayGate);
                    DrawStringArray("Binding assemblies", ref s.typescriptBindingAssemblies);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("JS Debugger (Editor Mono)", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "Editor Mono only. Wait for attach can block the main thread.",
                        MessageType.Info);
                    s.enableJsDebugger = EditorGUILayout.Toggle("Enable JS debugger", s.enableJsDebugger);
                    s.debuggerHostTypeName = EditorGUILayout.TextField(
                        "Host type (assembly-qualified)",
                        s.debuggerHostTypeName ?? "");
                    s.debuggerPort = EditorGUILayout.IntField("Preferred port", s.debuggerPort);
                    s.debuggerWaitForAttach = EditorGUILayout.Toggle("Wait for attach", s.debuggerWaitForAttach);
                    DrawStringArray("Extra source paths", ref s.debuggerSourcePaths);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("XML Bindings (Editor Mono + Il2Cpp codegen)", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "ZTSMarshalAs / JsAlias / JsExtensions roots. Il2Cpp Player uses build-time C++ tables — not runtime XML.",
                        MessageType.Info);
                    DrawStringArray("MarshalAs XML paths", ref s.marshalAsXmlPaths);
                    DrawStringArray("JsAlias XML paths", ref s.jsAliasXmlPaths);
                    DrawStringArray("JsExtensions XML paths", ref s.jsExtensionXmlPaths);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Settings.Save();
                    }
                }
            };
        }

        private static void DrawStringArray(string label, ref string[] values)
        {
            values = values ?? System.Array.Empty<string>();
            int count = EditorGUILayout.IntField(label + " count", values.Length);
            if (count < 0)
            {
                count = 0;
            }

            if (count != values.Length)
            {
                System.Array.Resize(ref values, count);
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = EditorGUILayout.TextField($"[{i}]", values[i] ?? "");
            }

            EditorGUI.indentLevel--;
        }
    }
}
