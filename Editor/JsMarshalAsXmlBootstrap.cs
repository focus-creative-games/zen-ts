// Copyright 2026 Code Philosophy

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZTS
{
    /// <summary>
    /// Loads Settings marshalAsXmlPaths into <see cref="JsMarshalAsXmlRegistry"/> for Editor Mono.
    /// </summary>
    [InitializeOnLoad]
    internal static class JsMarshalAsXmlBootstrap
    {
        static JsMarshalAsXmlBootstrap()
        {
            TryLoad(logSuccess: false);
        }

        [MenuItem("ZTS/Reload MarshalAs XML", priority = 520)]
        private static void ReloadMenu()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            JsMarshalAsXmlRegistry.Load(Settings.Instance.marshalAsXmlPaths, projectRoot);
            Debug.Log("[ZTS] MarshalAs XML loaded: " + JsMarshalAsXmlRegistry.Rules.Count + " rule(s).");
        }

        internal static void TryLoad(bool logSuccess)
        {
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                JsMarshalAsXmlRegistry.Load(Settings.Instance.marshalAsXmlPaths, projectRoot);
                if (logSuccess)
                {
                    Debug.Log("[ZTS] MarshalAs XML loaded: " + JsMarshalAsXmlRegistry.Rules.Count + " rule(s).");
                }
            }
            catch (Exception ex)
            {
                // Do not rethrow from InitializeOnLoad — keep Editor usable; Generate/menu still fail hard.
                Debug.LogError("[ZTS] MarshalAs XML load failed:\n" + ex.Message);
            }
        }
    }
}
