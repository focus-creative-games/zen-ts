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
using System.Text.RegularExpressions;
using UnityEngine;

namespace ZenTS.Utils
{
    public sealed class QuickJsVersionInfo
    {
        public string Id { get; set; }
        public string VersionDate { get; set; }
        /// <summary>Il2Cpp vendored tree under ZenTS~/quickjs-il2cpp (or legacy cache).</summary>
        public string SourceDir { get; set; }
    }

    public static class QuickJsVersionUtil
    {
        public const string DefaultVersionId = "quickjs-2026-06-04";

        private static readonly Regex s_id = new Regex(
            @"^quickjs-(\d{4}-\d{2}-\d{2})$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string ResolveConfiguredOrDefault(string configured, out bool wroteDefault)
        {
            wroteDefault = false;
            if (string.IsNullOrWhiteSpace(configured))
            {
                wroteDefault = true;
                return DefaultVersionId;
            }

            return configured.Trim();
        }

        /// <summary>
        /// Resolve version from Settings id, preferring the vendored Il2Cpp tree.
        /// </summary>
        public static bool TryParse(string versionId, out QuickJsVersionInfo info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(versionId))
            {
                return false;
            }

            Match m = s_id.Match(versionId.Trim());
            if (!m.Success)
            {
                return false;
            }

            string date = m.Groups[1].Value;
            string vendored = CommonDirs.QuickJsIl2CppPathInPackage;
            info = new QuickJsVersionInfo
            {
                Id = versionId.Trim(),
                VersionDate = date,
                SourceDir = Directory.Exists(vendored) ? vendored : Path.Combine(CommonDirs.QuickJsSrcCacheDir, versionId.Trim()),
            };
            return true;
        }

        /// <summary>
        /// Read <c>VERSION</c> from the vendored Il2Cpp QuickJS tree and build version info.
        /// </summary>
        public static QuickJsVersionInfo FromVendoredIl2CppTree()
        {
            string dir = CommonDirs.QuickJsIl2CppPathInPackage;
            if (!Directory.Exists(dir))
            {
                throw new InvalidOperationException(
                    $"[ZenTS] Vendored QuickJS missing: {dir}");
            }

            string versionFile = Path.Combine(dir, "VERSION");
            if (!File.Exists(versionFile))
            {
                throw new InvalidOperationException(
                    $"[ZenTS] Vendored QuickJS missing VERSION: {versionFile}");
            }

            string date = File.ReadAllText(versionFile).Trim();
            if (!Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            {
                throw new InvalidOperationException(
                    $"[ZenTS] Invalid VERSION in vendored QuickJS: '{date}'");
            }

            string quickjsC = Path.Combine(dir, "quickjs.c");
            if (!File.Exists(quickjsC))
            {
                throw new InvalidOperationException(
                    $"[ZenTS] Vendored QuickJS missing quickjs.c: {quickjsC}");
            }

            return new QuickJsVersionInfo
            {
                Id = "quickjs-" + date,
                VersionDate = date,
                SourceDir = dir,
            };
        }

        public static void EnsureAvailable(QuickJsVersionInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            if (!Directory.Exists(info.SourceDir))
            {
                throw new InvalidOperationException(
                    $"[ZenTS] QuickJS source missing: {info.SourceDir}");
            }

            string versionFile = Path.Combine(info.SourceDir, "VERSION");
            if (!File.Exists(versionFile))
            {
                throw new InvalidOperationException(
                    $"[ZenTS] QuickJS missing VERSION file: {versionFile}");
            }

            string actual = File.ReadAllText(versionFile).Trim();
            if (!string.Equals(actual, info.VersionDate, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"[ZenTS] QuickJS VERSION mismatch: expected '{info.VersionDate}', got '{actual}' in {info.SourceDir}");
            }

            if (!File.Exists(Path.Combine(info.SourceDir, "quickjs.c")))
            {
                throw new InvalidOperationException(
                    $"[ZenTS] QuickJS missing quickjs.c in {info.SourceDir}");
            }
        }
    }
}
