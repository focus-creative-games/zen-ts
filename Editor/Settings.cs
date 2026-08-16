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

        [Tooltip("MarshalAs XML files or directories (relative to project root or absolute). Root element ZTSMarshalAs. See Docs/spec/marshal/02-MARSHAL-AS §9.")]
        public string[] marshalAsXmlPaths = { "Assets/CustomJsMarshalAsRules.xml" };

        [Tooltip("JsAlias XML files or directories (relative to project root or absolute). Root element JsAlias. See Docs/spec/04-METHOD-OVERLOAD §5.4.")]
        public string[] jsAliasXmlPaths = { "Assets/CustomJsAliasRules.xml" };

        [Tooltip("JsExtensions XML files or directories (relative to project root or absolute). Root element JsExtensions. See Docs/spec/13-EXTENSION-METHODS §2.2.")]
        public string[] jsExtensionXmlPaths = { "Assets/CustomJsExtensionRules.xml" };

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

            if (settingsVersion < 2)
            {
                if (jsAliasXmlPaths == null)
                {
                    jsAliasXmlPaths = new[] { "Assets/CustomJsAliasRules.xml" };
                }

                if (jsExtensionXmlPaths == null)
                {
                    jsExtensionXmlPaths = new[] { "Assets/CustomJsExtensionRules.xml" };
                }

                settingsVersion = 2;
                _needsSaveAfterMigrate = true;
            }

            if (settingsVersion < 3)
            {
                if (marshalAsXmlPaths == null)
                {
                    marshalAsXmlPaths = new[] { "Assets/CustomJsMarshalAsRules.xml" };
                }

                settingsVersion = 3;
                _needsSaveAfterMigrate = true;
            }
            else
            {
                if (jsAliasXmlPaths == null)
                {
                    jsAliasXmlPaths = new[] { "Assets/CustomJsAliasRules.xml" };
                    _needsSaveAfterMigrate = true;
                }

                if (jsExtensionXmlPaths == null)
                {
                    jsExtensionXmlPaths = new[] { "Assets/CustomJsExtensionRules.xml" };
                    _needsSaveAfterMigrate = true;
                }

                if (marshalAsXmlPaths == null)
                {
                    marshalAsXmlPaths = new[] { "Assets/CustomJsMarshalAsRules.xml" };
                    _needsSaveAfterMigrate = true;
                }
            }
        }
    }
}
