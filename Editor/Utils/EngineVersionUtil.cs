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

using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ZenTS.Utils
{
    /// <summary>
    /// Unity / Tuanjie version encoding for <c>ZenTSConf.inc</c> (spec 11-MULTI-VERSION §12).
    /// </summary>
    public static class EngineVersionUtil
    {
        private static readonly Regex s_semVer = new Regex(
            @"(\d+)\.(\d+)\.(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsTuanjieEngine(string unityVersionStr = null)
        {
#if TUANJIE_2022_3_OR_NEWER
            return true;
#else
            if (TryGetTuanjieVersionString(out _))
            {
                return true;
            }

            string v = unityVersionStr ?? Application.unityVersion;
            return Regex.IsMatch(v ?? string.Empty, @"\d+t\d", RegexOptions.CultureInvariant);
#endif
        }

        public static bool TryGetTuanjieVersionString(out string version)
        {
            version = null;
            PropertyInfo prop = typeof(Application).GetProperty(
                "tuanjieVersion", BindingFlags.Public | BindingFlags.Static);
            if (prop == null || prop.PropertyType != typeof(string))
            {
                return false;
            }

            try
            {
                version = prop.GetValue(null, null) as string;
                return !string.IsNullOrWhiteSpace(version);
            }
            catch
            {
                return false;
            }
        }

        public static int EncodeUnityVersion(UnityVersion uv)
        {
            return uv.major * 10000 + uv.minor1 * 100 + uv.minor2;
        }

        public static int EncodeUnityVersion(string unityVersionStr)
        {
            return EncodeUnityVersion(new UnityVersion(unityVersionStr));
        }

        public static int EncodeSemVerTriplet(string versionStr)
        {
            if (string.IsNullOrWhiteSpace(versionStr))
            {
                return 0;
            }

            Match m = s_semVer.Match(versionStr.Trim());
            if (!m.Success)
            {
                return 0;
            }

            int major = int.Parse(m.Groups[1].Value);
            int minor = int.Parse(m.Groups[2].Value);
            int patch = int.Parse(m.Groups[3].Value);
            return major * 10000 + minor * 100 + patch;
        }

        public static int EncodeQuickJsVersionDate(string versionDate)
        {
            if (string.IsNullOrWhiteSpace(versionDate))
            {
                return 0;
            }

            string digits = versionDate.Trim().Replace("-", string.Empty);
            if (digits.Length != 8 || !int.TryParse(digits, out int value))
            {
                throw new System.InvalidOperationException(
                    $"Cannot encode QuickJS VERSION date '{versionDate}'.");
            }

            return value;
        }

        public static string BuildConfId(string quickjsVersionId, UnityVersion unityLine, string tuanjieVersionLabel)
        {
            string tj = string.IsNullOrEmpty(tuanjieVersionLabel) ? "0" : tuanjieVersionLabel;
            return $"{quickjsVersionId}|unity-{unityLine}|tuanjie-{tj}";
        }

        public static void ResolveTuanjieFields(
            string unityVersionStr,
            out int tuanjieEngineFlag,
            out int tuanjieVersionCode,
            out string tuanjieLabel)
        {
            if (!IsTuanjieEngine(unityVersionStr))
            {
                tuanjieEngineFlag = 0;
                tuanjieVersionCode = 0;
                tuanjieLabel = "0";
                return;
            }

            tuanjieEngineFlag = 1;
            if (TryGetTuanjieVersionString(out string raw) && !string.IsNullOrWhiteSpace(raw))
            {
                tuanjieLabel = raw.Trim();
                tuanjieVersionCode = EncodeSemVerTriplet(tuanjieLabel);
                return;
            }

            tuanjieLabel = "unknown";
            tuanjieVersionCode = 0;
        }
    }
}
