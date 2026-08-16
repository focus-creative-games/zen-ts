// Copyright 2026 Code Philosophy

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZTS
{
    /// <summary>
    /// Loads Settings jsAliasXmlPaths into <see cref="JsAliasXmlRegistry"/> for Editor Mono.
    /// </summary>
    [InitializeOnLoad]
    internal static class JsAliasXmlBootstrap
    {
        static JsAliasXmlBootstrap()
        {
            TryLoad(logSuccess: false);
        }

        [MenuItem("ZTS/Reload JsAlias XML", priority = 521)]
        private static void ReloadMenu()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            JsAliasXmlRegistry.Load(Settings.Instance.jsAliasXmlPaths, projectRoot);
            Debug.Log("[ZTS] JsAlias XML loaded: " + JsAliasXmlRegistry.Rules.Count + " rule(s).");
        }

        internal static void TryLoad(bool logSuccess)
        {
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                JsAliasXmlRegistry.Load(Settings.Instance.jsAliasXmlPaths, projectRoot);
                if (logSuccess)
                {
                    Debug.Log("[ZTS] JsAlias XML loaded: " + JsAliasXmlRegistry.Rules.Count + " rule(s).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[ZTS] JsAlias XML load failed:\n" + ex.Message);
            }
        }
    }
}
