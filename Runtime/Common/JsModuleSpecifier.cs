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

namespace ZTS
{
    /// <summary>
    /// Canonical ES module specifiers (Docs/spec/14-TYPESCRIPT.md §4).
    /// </summary>
    public static class JsModuleSpecifier
    {
        public const string CsharpScheme = "csharp:";

        public static bool IsCsharp(string specifier)
        {
            return !string.IsNullOrEmpty(specifier) &&
                   specifier.StartsWith(CsharpScheme, StringComparison.Ordinal);
        }

        /// <summary>
        /// Strip <c>./</c> and trailing <c>.js</c>/<c>.mjs</c>/<c>.ts</c>.
        /// <c>csharp:</c> specifiers are returned unchanged.
        /// </summary>
        public static string Canonicalize(string specifier)
        {
            if (string.IsNullOrEmpty(specifier))
            {
                return string.Empty;
            }

            if (IsCsharp(specifier))
            {
                return specifier;
            }

            string key = specifier.Replace('\\', '/').Trim();
            while (key.StartsWith("./", StringComparison.Ordinal))
            {
                key = key.Substring(2);
            }

            if (key.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
            {
                return key.Substring(0, key.Length - 4);
            }

            if (key.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            {
                return key.Substring(0, key.Length - 3);
            }

            return key;
        }
    }
}
