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
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZenTS
{
    /// <summary>
    /// Stages package TS/JS toolchain files under <c>Library/ZenTS/CoreLibs</c>
    /// so <c>tsc</c> / Node / IDE can resolve them without relying on Unity's virtual
    /// <c>Packages/{name}</c> path (broken for git UPM → PackageCache).
    /// </summary>
    [InitializeOnLoad]
    public static class CoreLibsEnsure
    {
        static CoreLibsEnsure()
        {
            // Cheap stamp check after domain reload / package refresh.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Ensure();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ZenTS] CoreLibs ensure skipped: " + ex.Message);
                }
            };
        }

        /// <summary>
        /// Copy <c>ZenTS~/types/{tsconfig.base.json,zents.d.ts}</c> and <c>ZenTS~/jslib/</c>
        /// into <c>Library/ZenTS/CoreLibs/</c> when missing or stale.
        /// </summary>
        public static void Ensure()
        {
            string typesSrc = CommonDirs.TypesPathInPackage;
            string jsSrc = CommonDirs.JsLibPathInPackage;
            string baseJson = Path.Combine(typesSrc, "tsconfig.base.json");
            string dts = Path.Combine(typesSrc, "zents.d.ts");
            string zentslib = Path.Combine(jsSrc, "zentslib.js");

            if (!File.Exists(baseJson) || !File.Exists(dts))
            {
                throw new InvalidOperationException(
                    "[ZenTS] package types missing under " + typesSrc);
            }

            if (!File.Exists(zentslib))
            {
                throw new InvalidOperationException(
                    "[ZenTS] package jslib missing: " + zentslib);
            }

            string stamp = ComputeStamp(baseJson, dts, zentslib);
            string stampPath = CommonDirs.CoreLibsStampPath;
            string typesDst = CommonDirs.CoreLibsTypesPath;
            string jsDst = CommonDirs.CoreLibsJsLibPath;
            string dstBase = Path.Combine(typesDst, "tsconfig.base.json");
            string dstDts = Path.Combine(typesDst, "zents.d.ts");
            string dstJs = Path.Combine(jsDst, "zentslib.js");

            if (File.Exists(stampPath)
                && string.Equals(File.ReadAllText(stampPath).Trim(), stamp, StringComparison.Ordinal)
                && File.Exists(dstBase)
                && File.Exists(dstDts)
                && File.Exists(dstJs))
            {
                return;
            }

            Directory.CreateDirectory(typesDst);
            Directory.CreateDirectory(jsDst);
            File.Copy(baseJson, dstBase, overwrite: true);
            File.Copy(dts, dstDts, overwrite: true);

            foreach (string file in Directory.GetFiles(jsSrc, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(file, Path.Combine(jsDst, name), overwrite: true);
            }

            Directory.CreateDirectory(CommonDirs.CoreLibsRoot);
            File.WriteAllText(stampPath, stamp, Encoding.UTF8);
            Debug.Log("[ZenTS] CoreLibs refreshed → " + CommonDirs.CoreLibsRoot);
        }

        private static string ComputeStamp(params string[] files)
        {
            var sb = new StringBuilder(256);
            sb.Append(CommonDirs.PackageResolvedRoot).Append('|');
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                    $"Packages/{CommonDirs.PackageName}/package.json");
                if (info != null)
                {
                    sb.Append(info.version).Append('|').Append(info.source).Append('|');
                }
            }
            catch
            {
                // ignore — path + file hashes still gate refresh
            }

            using (var sha = SHA256.Create())
            {
                foreach (string file in files)
                {
                    sb.Append(file).Append('|');
                    sb.Append(File.GetLastWriteTimeUtc(file).Ticks).Append('|');
                    sb.Append(new FileInfo(file).Length).Append('|');
                    byte[] bytes = File.ReadAllBytes(file);
                    sb.Append(ToHex(sha.ComputeHash(bytes))).Append(';');
                }
            }

            return sb.ToString();
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
