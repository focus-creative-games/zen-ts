using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZTS.Editor.Diagnostics
{
    public readonly struct JSRuntimeHandle
    {
        public IntPtr Value { get; }

        public JSRuntimeHandle(IntPtr value)
        {
            Value = value;
        }
    }

    public readonly struct JSContextHandle
    {
        public IntPtr Value { get; }

        public JSContextHandle(IntPtr value)
        {
            Value = value;
        }
    }

    public sealed class JsDebuggerHostContext
    {
        public string ProjectRoot { get; set; }

        public IReadOnlyList<string> SourceSearchPaths { get; set; }

        public int PreferredPort { get; set; }

        public bool WaitForDebugger { get; set; }
    }

    /// <summary>
    /// Editor optional JS debug host. Implementation lives in an extension assembly.
    /// Docs/spec/build/04-JS-DEBUGGER.md
    /// </summary>
    public interface IZtsJsDebuggerHost
    {
        void Install(JSRuntimeHandle rt, JSContextHandle ctx, JsDebuggerHostContext hostContext);

        void Uninstall();

        void Tick();
    }
}
