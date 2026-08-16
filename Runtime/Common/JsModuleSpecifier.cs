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
