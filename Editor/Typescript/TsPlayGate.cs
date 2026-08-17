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
using ZenTS.Editor.Typescript;

namespace ZenTS.Editor
{
    /// <summary>
    /// Docs/spec/14-TYPESCRIPT.md §8.1: tsc --noEmit (and emit if stale) before Play.
    /// No TsProject → skip. Settings.enableTsPlayGate can disable.
    /// </summary>
    [InitializeOnLoad]
    internal static class TsPlayGate
    {
        static TsPlayGate()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            if (!Settings.Instance.enableTsPlayGate || !TsProjectPaths.Exists)
            {
                return;
            }

            try
            {
                TypescriptToolchain.CheckAndEmitIfStale();
            }
            catch (Exception ex)
            {
                EditorApplication.isPlaying = false;
                Debug.LogError("[ZenTS] TypeScript Play gate failed:\n" + ex.Message);
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog(
                        "ZenTS TypeScript",
                        "TypeScript check failed; Play was cancelled.\n\nSee Console for tsc output.",
                        "OK");
                }
            }
        }
    }
}
