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
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using ZenTS.Editor.Typescript;

namespace ZenTS.Editor.Diagnostics
{
    /// <summary>
    /// Reflects an <see cref="IZentsJsDebuggerHost"/> from Settings and installs it
    /// after JsMonoAppDomain has finished core init. Missing host → log and skip.
    /// </summary>
    public static class JsDebuggerBootstrap
    {
        private static IZentsJsDebuggerHost s_active;
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
                    "[ZenTS] enableJsDebugger is on but debuggerHostTypeName is empty. Skipping debugger.");
                return;
            }

            Type hostType = Type.GetType(typeName);
            if (hostType == null)
            {
                Debug.LogError(
                    $"[ZenTS] debugger host type not found: '{typeName}'. Skipping debugger.");
                return;
            }

            if (!typeof(IZentsJsDebuggerHost).IsAssignableFrom(hostType))
            {
                Debug.LogError(
                    $"[ZenTS] '{typeName}' does not implement IZentsJsDebuggerHost. Skipping debugger.");
                return;
            }

            IZentsJsDebuggerHost host;
            try
            {
                host = (IZentsJsDebuggerHost)Activator.CreateInstance(hostType);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZenTS] failed to create debugger host: " + ex.Message);
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
                Debug.LogError("[ZenTS] debugger host Install failed: " + ex.Message);
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
                Debug.LogWarning("[ZenTS] debugger host Uninstall: " + ex.Message);
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
                Debug.LogWarning("[ZenTS] debugger host Tick: " + ex.Message);
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
