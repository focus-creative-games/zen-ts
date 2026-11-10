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

namespace ZenTS
{
    /// <summary>
    /// Host-JS mode (browser / minigame engine) is a **compile-time** backend switch driven
    /// by the <c>ZENTS_HOST_JS</c> scripting define. It is set by
    /// <c>ZenTS.BuildProcessors.HostJsBuildSetup</c> for the WebGL / WeixinMiniGame
    /// build target.
    ///
    /// Kept as a tiny helper so non-<c>#if</c> call sites (module loading, diagnostics) can
    /// consult one source of truth instead of re-deriving the define.
    /// </summary>
    public static class JsHostDetect
    {
        /// <summary>True when the host VM owns the JS engine (WebGL / minigame).</summary>
        public static bool IsHostJs
        {
            get
            {
#if (UNITY_WEBGL || UNITY_WEIXINMINIGAME || WEIXINMINIGAME || ZENTS_HOST_JS) && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>True when executable modules are precompiled for the host; the loader still selects source variants.</summary>
        public static bool HostOwnsModuleLoading => IsHostJs;
    }
}