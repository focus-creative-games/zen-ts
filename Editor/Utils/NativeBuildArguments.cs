// Copyright 2026 Code Philosophy. MIT license; see package LICENSE.
using System.Text.RegularExpressions;
namespace ZenTS
{
    public static class NativeBuildArguments
    {
        // Remove only ZenTS/V8 archives. Preserve unrelated user libraries and switches.
        private const string Archive = @"(?:lib)?(?:zents_v8_engine(?:_release)?|v8_monolith|wee8-mt)\.(?:lib|a)";
        public static string StripV8Libraries(string arguments) => Regex.Replace(arguments ?? string.Empty,
            @"\s*--additional-libraries=(?:""(?:[^""]*[/\\])?" + Archive + @"""|(?:[^\s""]*[/\\])?" + Archive + @")", string.Empty, RegexOptions.IgnoreCase);
        public static string ForHost(string arguments) => StripV8Libraries(arguments)
            .Replace("--compiler-flags=-DZENTS_V8_STATIC_CRT=1", string.Empty).Trim();
    }
}
