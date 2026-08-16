// Copyright 2026 Code Philosophy

using UnityEditor;
using UnityEngine;

namespace ZTS
{
    [FilePath("ProjectSettings/ZTS.asset", FilePathAttribute.Location.ProjectFolder)]
    public class Settings : ScriptableSingleton<Settings>, ISerializationCallbackReceiver
    {
        [SerializeField] int settingsVersion;

        [System.NonSerialized] bool _needsSaveAfterMigrate;

        [Tooltip("Enable ZTS Il2Cpp install/build hooks")]
        public bool enable = true;

        [Tooltip("QuickJS pin id, e.g. quickjs-2026-06-04. Empty = default.")]
        public string quickjsVersionId = "quickjs-2026-06-04";

        [Tooltip("Run tsc --noEmit (and emit if stale) before entering Play. Docs/spec/14-TYPESCRIPT.md")]
        public bool enableTsPlayGate = true;

        [Tooltip("Assemblies whose public types are emitted as csharp: .d.ts (same set as Il2Cpp Generate).")]
        public string[] typescriptBindingAssemblies = { "Assembly-CSharp", "ZTS.Tests" };

        [Tooltip("Editor Mono JS debugger host. Default off.")]
        public bool enableJsDebugger = false;

        [Tooltip("IZtsJsDebuggerHost implementation type (assembly-qualified).")]
        public string debuggerHostTypeName = "";

        public int debuggerPort = 9230;

        public bool debuggerWaitForAttach = false;

        [Tooltip("Extra source search paths for the JS debugger (in addition to TsProject/src).")]
        public string[] debuggerSourcePaths = { };

        public static Settings Instance => instance;

        public static bool EnableForCurrentBuildTarget => Instance.enable;

        public static void Save()
        {
            if (!instance)
            {
                return;
            }

            instance.Save(true);
        }

        private void OnEnable()
        {
            if (_needsSaveAfterMigrate)
            {
                _needsSaveAfterMigrate = false;
                Save(true);
            }
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            // Existing ProjectSettings/ZTS.asset lacks new fields → Unity zeros them.
            if (settingsVersion < 1)
            {
                enableTsPlayGate = true;
                if (typescriptBindingAssemblies == null || typescriptBindingAssemblies.Length == 0)
                {
                    typescriptBindingAssemblies = new[] { "Assembly-CSharp", "ZTS.Tests" };
                }

                if (debuggerPort <= 0)
                {
                    debuggerPort = 9230;
                }

                if (debuggerSourcePaths == null)
                {
                    debuggerSourcePaths = System.Array.Empty<string>();
                }

                settingsVersion = 1;
                _needsSaveAfterMigrate = true;
            }
        }
    }
}
