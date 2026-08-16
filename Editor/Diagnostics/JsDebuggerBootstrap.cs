using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using ZTS.Editor.Typescript;

namespace ZTS.Editor.Diagnostics
{
    /// <summary>
    /// Reflects an <see cref="IZtsJsDebuggerHost"/> from Settings and installs it
    /// after TsMonoAppDomain has finished core init. Missing host → log and skip.
    /// </summary>
    public static class JsDebuggerBootstrap
    {
        private static IZtsJsDebuggerHost s_active;
        private static bool s_tickHooked;

        public static void TryStart(IntPtr runtime, IntPtr context)
        {
            TryStop();

            Settings settings = Settings.Instance;
            if (settings == null || !settings.enableJsDebugger)
            {
                return;
            }

            string typeName = settings.debuggerHostTypeName;
            if (string.IsNullOrEmpty(typeName))
            {
                Debug.LogError(
                    "[ZTS] enableJsDebugger is on but debuggerHostTypeName is empty. Skipping debugger.");
                return;
            }

            Type hostType = Type.GetType(typeName);
            if (hostType == null)
            {
                Debug.LogError(
                    $"[ZTS] debugger host type not found: '{typeName}'. Skipping debugger.");
                return;
            }

            if (!typeof(IZtsJsDebuggerHost).IsAssignableFrom(hostType))
            {
                Debug.LogError(
                    $"[ZTS] '{typeName}' does not implement IZtsJsDebuggerHost. Skipping debugger.");
                return;
            }

            IZtsJsDebuggerHost host;
            try
            {
                host = (IZtsJsDebuggerHost)Activator.CreateInstance(hostType);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZTS] failed to create debugger host: " + ex.Message);
                return;
            }

            try
            {
                host.Install(
                    new JSRuntimeHandle(runtime),
                    new JSContextHandle(context),
                    BuildContext(settings));
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZTS] debugger host Install failed: " + ex.Message);
                return;
            }

            s_active = host;
            EnsureTickHook();
        }

        public static void TryStop()
        {
            if (s_active == null)
            {
                return;
            }

            try
            {
                s_active.Uninstall();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZTS] debugger host Uninstall: " + ex.Message);
            }

            s_active = null;
        }

        private static void EnsureTickHook()
        {
            if (s_tickHooked)
            {
                return;
            }

            EditorApplication.update += Tick;
            s_tickHooked = true;
        }

        private static void Tick()
        {
            try
            {
                s_active?.Tick();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZTS] debugger host Tick: " + ex.Message);
            }
        }

        private static JsDebuggerHostContext BuildContext(Settings settings)
        {
            var paths = new List<string>();
            if (Directory.Exists(TsProjectPaths.SrcDir))
            {
                paths.Add(TsProjectPaths.SrcDir);
            }

            paths.Add(Application.dataPath);
            if (settings.debuggerSourcePaths != null)
            {
                foreach (string extra in settings.debuggerSourcePaths)
                {
                    if (!string.IsNullOrWhiteSpace(extra))
                    {
                        paths.Add(extra);
                    }
                }
            }

            return new JsDebuggerHostContext
            {
                ProjectRoot = TsProjectPaths.ProjectRoot,
                SourceSearchPaths = paths,
                PreferredPort = settings.debuggerPort,
                WaitForDebugger = settings.debuggerWaitForAttach,
            };
        }
    }
}
