using System;
using UnityEditor;
using UnityEngine;
using ZTS.Editor.Typescript;

namespace ZTS.Editor
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
                Debug.LogError("[ZTS] TypeScript Play gate failed:\n" + ex.Message);
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog(
                        "ZTS TypeScript",
                        "TypeScript check failed; Play was cancelled.\n\nSee Console for tsc output.",
                        "OK");
                }
            }
        }
    }
}
