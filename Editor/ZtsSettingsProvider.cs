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
