// Copyright 2026 Code Philosophy

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZTS
{
    /// <summary>
    /// Loads Settings jsExtensionXmlPaths into <see cref="JsExtensionXmlRegistry"/> for Editor Mono.
    /// </summary>
    [InitializeOnLoad]
    internal static class JsExtensionXmlBootstrap
    {
        static JsExtensionXmlBootstrap()
        {
            TryLoad(logSuccess: false);
        }

        [MenuItem("ZTS/Reload JsExtensions XML", priority = 522)]
        private static void ReloadMenu()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            JsExtensionXmlRegistry.Load(Settings.Instance.jsExtensionXmlPaths, projectRoot);
            Debug.Log("[ZTS] JsExtensions XML loaded: " + JsExtensionXmlRegistry.Rules.Count + " rule(s).");
        }

        internal static void TryLoad(bool logSuccess)
        {
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                JsExtensionXmlRegistry.Load(Settings.Instance.jsExtensionXmlPaths, projectRoot);
                if (logSuccess)
                {
                    Debug.Log("[ZTS] JsExtensions XML loaded: " + JsExtensionXmlRegistry.Rules.Count + " rule(s).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZTS] JsExtensions XML load failed:\n" + ex.Message);
            }
        }
    }
}
