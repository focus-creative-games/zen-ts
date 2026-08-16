using System;
using System.IO;
using UnityEngine;

namespace ZTS.Editor.Typescript
{
    /// <summary>
    /// tsc check + esbuild 1:1 emit + copy to StreamingAssets/ZTS (Docs/spec/14-TYPESCRIPT.md §7–§8).
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
                    "[ZTS] npm install did not produce node_modules/.bin/tsc. Install Node LTS and retry.");
            }
        }

        public static void Check()
        {
            EnsureProject();
            string tsc = NodeCli.LocalBin(TsProjectPaths.TsProjectRoot, "tsc");
            if (tsc == null)
            {
                throw new InvalidOperationException(
                    "[ZTS] TypeScript is not installed in TsProject. Run menu 'ZTS/Init TypeScript Project' (npm install).");
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
                    "[ZTS] missing TsProject/scripts/emit.mjs. Run menu ZTS/Init TypeScript Project.");
            }

            NodeCli.RunOrThrow(TsProjectPaths.TsProjectRoot, "node", QuotePath(emitJs));
            CopyOutToStreamingAssets();
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

            Directory.CreateDirectory(TsProjectPaths.StreamingZtsDir);
            CopyDirectory(TsProjectPaths.OutDir, TsProjectPaths.StreamingZtsDir);
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
                    "[ZTS] TsProject not found. Run menu 'ZTS/Init TypeScript Project'.");
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
