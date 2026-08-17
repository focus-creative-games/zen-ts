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
using System.IO;
using UnityEngine;

namespace ZenTS.Editor.Typescript
{
    /// <summary>
    /// tsc check + esbuild 1:1 emit + copy to StreamingAssets/ZenTS (Docs/spec/14-TYPESCRIPT.md §7–§8).
    /// </summary>
    public static class TypescriptToolchain
    {
        public static void EnsureDependencies()
        {
            EnsureProject();
            if (NodeCli.LocalBin(TsProjectPaths.TsProjectRoot, "tsc") != null &&
                NodeCli.LocalBin(TsProjectPaths.TsProjectRoot, "esbuild") != null)
            {
                return;
            }

            NodeCli.RunOrThrow(TsProjectPaths.TsProjectRoot, "npm", "install");
            if (NodeCli.LocalBin(TsProjectPaths.TsProjectRoot, "tsc") == null)
            {
                throw new InvalidOperationException(
                    "[ZenTS] npm install did not produce node_modules/.bin/tsc. Install Node LTS and retry.");
            }
        }

        public static void Check()
        {
            EnsureProject();
            string tsc = NodeCli.LocalBin(TsProjectPaths.TsProjectRoot, "tsc");
            if (tsc == null)
            {
                throw new InvalidOperationException(
                    "[ZenTS] TypeScript is not installed in TsProject. Run menu 'ZenTS/Init TypeScript Project' (npm install).");
            }

            NodeCli.RunOrThrow(TsProjectPaths.TsProjectRoot, tsc, "--noEmit -p tsconfig.json");
        }

        public static void Emit()
        {
            EnsureProject();
            string emitJs = Path.Combine(TsProjectPaths.TsProjectRoot, "scripts", "emit.mjs");
            if (!File.Exists(emitJs))
            {
                throw new InvalidOperationException(
                    "[ZenTS] missing TsProject/scripts/emit.mjs. Run menu ZenTS/Init TypeScript Project.");
            }

            NodeCli.RunOrThrow(TsProjectPaths.TsProjectRoot, "node", QuotePath(emitJs));
            // Default emit path for Editor / desktop; Android/WebGL preprocess remaps to Resources.
            CopyOutToStreamingAssets();
        }

        /// <summary>
        /// Publish emitted JS for the given Player target.
        /// Android / WebGL cannot File.Read StreamingAssets → Resources TextAssets (<c>*.js.txt</c>).
        /// </summary>
        public static void PublishForBuildTarget(UnityEditor.BuildTarget target)
        {
            if (target == UnityEditor.BuildTarget.Android || target == UnityEditor.BuildTarget.WebGL)
            {
                ClearDirectory(TsProjectPaths.StreamingZentsDir);
                CopyOutToResources();
            }
            else
            {
                ClearDirectory(TsProjectPaths.ResourcesZentsDir);
                DeleteEmptyParents(TsProjectPaths.ResourcesZentsDir);
                CopyOutToStreamingAssets();
            }
        }

        public static void CheckAndEmitIfStale()
        {
            EnsureProject();
            Check();
            if (IsOutStale())
            {
                Emit();
            }
        }

        public static void CopyOutToStreamingAssets()
        {
            string copyJs = Path.Combine(TsProjectPaths.TsProjectRoot, "scripts", "copy-streaming.mjs");
            if (File.Exists(copyJs))
            {
                NodeCli.RunOrThrow(TsProjectPaths.TsProjectRoot, "node", QuotePath(copyJs));
                return;
            }

            if (!Directory.Exists(TsProjectPaths.OutDir))
            {
                return;
            }

            Directory.CreateDirectory(TsProjectPaths.StreamingZentsDir);
            CopyDirectory(TsProjectPaths.OutDir, TsProjectPaths.StreamingZentsDir);
        }

        /// <summary>
        /// Copy <c>out/**/*.js</c> → <c>Assets/Resources/ZenTS/**/*.js.txt</c> for Android/WebGL.
        /// </summary>
        public static void CopyOutToResources()
        {
            if (!Directory.Exists(TsProjectPaths.OutDir))
            {
                Debug.LogWarning("[ZenTS] TsProject/out missing; skip Resources JS publish.");
                return;
            }

            ClearDirectory(TsProjectPaths.ResourcesZentsDir);
            Directory.CreateDirectory(TsProjectPaths.ResourcesZentsDir);

            foreach (string src in Directory.GetFiles(TsProjectPaths.OutDir, "*.js", SearchOption.AllDirectories))
            {
                string rel = src.Substring(TsProjectPaths.OutDir.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dest = Path.Combine(TsProjectPaths.ResourcesZentsDir, rel + ".txt");
                string destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(src, dest, overwrite: true);
            }

            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[ZenTS] Published TsProject/out → Resources/ZenTS (*.js.txt)");
        }

        private static void ClearDirectory(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            Directory.Delete(dir, true);
            string meta = dir + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }
        }

        private static void DeleteEmptyParents(string leafDir)
        {
            // Remove Resources/ZenTS then empty Resources if empty.
            string resourcesRoot = Path.Combine(Application.dataPath, "Resources");
            if (Directory.Exists(resourcesRoot) &&
                Directory.GetFileSystemEntries(resourcesRoot).Length == 0)
            {
                Directory.Delete(resourcesRoot, true);
                string meta = resourcesRoot + ".meta";
                if (File.Exists(meta))
                {
                    File.Delete(meta);
                }

                UnityEditor.AssetDatabase.Refresh();
            }
            else if (leafDir != null)
            {
                UnityEditor.AssetDatabase.Refresh();
            }
        }

        public static bool IsOutStale()
        {
            if (!Directory.Exists(TsProjectPaths.OutDir))
            {
                return true;
            }

            DateTime newestSrc = NewestWrite(TsProjectPaths.SrcDir, "*.ts");
            DateTime newestOut = NewestWrite(TsProjectPaths.OutDir, "*.js");
            return newestSrc > newestOut;
        }

        private static void EnsureProject()
        {
            if (!TsProjectPaths.Exists)
            {
                throw new InvalidOperationException(
                    "[ZenTS] TsProject not found. Run menu 'ZenTS/Init TypeScript Project'.");
            }
        }

        private static DateTime NewestWrite(string dir, string pattern)
        {
            if (!Directory.Exists(dir))
            {
                return DateTime.MinValue;
            }

            DateTime newest = DateTime.MinValue;
            foreach (string file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
            {
                DateTime w = File.GetLastWriteTimeUtc(file);
                if (w > newest)
                {
                    newest = w;
                }
            }

            return newest;
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(src, dst));
            }

            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, file.Replace(src, dst), overwrite: true);
            }
        }

        private static string QuotePath(string path)
        {
            if (path.IndexOf(' ') < 0)
            {
                return path;
            }

            return "\"" + path + "\"";
        }
    }
}
