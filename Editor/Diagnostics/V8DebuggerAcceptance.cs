// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZenTS.Editor.Diagnostics
{
    /// <summary>Batch entrypoints driven by the real CDP client in debugger/run-inspector.mjs.</summary>
    public static class V8DebuggerAcceptance
    {
        private const string ReloadKey = "ZenTS.InspectorAcceptance.Reload";
        [Serializable] private sealed class Saved { public bool enabled, wait; public int port; public string host; }
        private static Saved Save() => new Saved { enabled=Settings.Instance.enableJsDebugger, wait=Settings.Instance.debuggerWaitForAttach, port=Settings.Instance.debuggerPort, host=Settings.Instance.debuggerHostTypeName };
        private static void Restore(Saved saved)
        {
            var settings=Settings.Instance;settings.enableJsDebugger=saved.enabled;settings.debuggerWaitForAttach=saved.wait;settings.debuggerPort=saved.port;settings.debuggerHostTypeName=saved.host;
        }
        private static void Configure(bool wait, int port=9231)
        {
            var settings=Settings.Instance;settings.enableJsDebugger=true;settings.debuggerWaitForAttach=wait;settings.debuggerPort=port;settings.debuggerHostTypeName="";
        }
        private static string Load(string module)
        {
            if(module!="debugger-acceptance")throw new InvalidOperationException("Unexpected debug module: "+module);
            return File.ReadAllText(Path.GetFullPath("TsProject/out/debugger-acceptance.js"));
        }
        private static void Reset() { JsAppDomain.Reset(Load); JsAppDomain.ApplyPendingResetNow(); }
        private static void Require(bool value,string reason) { if(!value)throw new InvalidOperationException(reason); }
        public static void Run()
        {
            Saved saved=Save();
            try
            {
                Configure(true);Reset();
                Require(V8DebuggerHost.ze_InspectorCount()==1,"Inspector not installed");
                var compute=JsAppDomain.GetFunction<Func<int,int>>("debugger-acceptance","compute");
                Require(compute(20)==42,"Breakpoint/step changed result");
                var nested=JsAppDomain.GetFunction<Func<int>>("debugger-acceptance","nested");
                Require(nested()==12,"Pause within managed callback failed");
                bool caught=false;
                try { JsAppDomain.GetFunction<Action>("debugger-acceptance","thrower")(); }
                catch(JsScriptException e) { caught=e.Message.Contains("debugger-exception-marker"); }
                Require(caught&&compute(2)==6,"Exception pause poisoned callback state");
                Require(JsAppDomain.GetFunction<Func<int>>("debugger-acceptance","spin")()==42,"Busy-loop interruption failed");
                Debug.Log("[ZenTS][debugger-test] Breakpoint, step, watch, managed callback and exception PASS");
                Reset(); // closes old target, reuses port, waits for a fresh client
                Require(V8DebuggerHost.ze_InspectorCount()==1,"Reset leaked Inspector");
                bool stale=false;try{compute(1);}catch(InvalidOperationException){stale=true;}
                Require(stale,"Old delegate survived Reset");
                Require(JsAppDomain.GetFunction<Func<int>>("debugger-acceptance","nested")()==12,"Disconnect while paused did not resume");
                Reset(); // frontend will detach while its own Runtime.evaluate is paused
                Require(V8DebuggerHost.ze_InspectorCount()==1,"Inspector eval disconnect leaked session");
                Require(JsAppDomain.GetFunction<Func<int>>("debugger-acceptance","nested")()==12,"Inspector evaluation disconnect poisoned VM");
                Settings.Instance.enableJsDebugger=false;Settings.Instance.debuggerWaitForAttach=false;
                Reset();Require(V8DebuggerHost.ze_InspectorCount()==0,"Disabled debugger retained session");
                Require(JsAppDomain.GetFunction<Func<int,int>>("debugger-acceptance","compute")(4)==10,"Disabled debugger affected execution");
                Debug.Log("[ZenTS][debugger-test] Reset, stale delegate, disconnect while paused and disabled debugger PASS");
                Debug.Log("[ZenTS][debugger-test] ALL OK");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogError("[ZenTS][debugger-test] FAILED: "+e);EditorApplication.Exit(1);}
            finally{JsDebuggerBootstrap.TryStop();Restore(saved);}
        }
        public static void RunDomainReload()
        {
            SessionState.SetString(ReloadKey+".saved",JsonUtility.ToJson(Save()));
            try
            {
                Configure(false,9232);Reset();Require(V8DebuggerHost.ze_InspectorCount()==1,"Missing initial Inspector");
                SessionState.SetBool(ReloadKey,true);EditorUtility.RequestScriptReload();
            }
            catch(Exception e){SessionState.SetBool(ReloadKey,false);Restore(JsonUtility.FromJson<Saved>(SessionState.GetString(ReloadKey+".saved","")));Debug.LogError(e);EditorApplication.Exit(1);}
        }
        [InitializeOnLoadMethod] private static void ResumeReload()
        {
            if(SessionState.GetBool(ReloadKey,false))EditorApplication.delayCall+=FinishReload;
        }
        private static void FinishReload()
        {
            SessionState.SetBool(ReloadKey,false);
            try
            {
                Require(V8DebuggerHost.ze_InspectorCount()==0,"Inspector survived Domain Reload");
                Configure(false,9232);Reset();Require(V8DebuggerHost.ze_InspectorCount()==1,"Inspector did not restart on same port");
                Require(JsAppDomain.GetFunction<Func<int,int>>("debugger-acceptance","compute")(4)==10,"Reload affected script execution");
                JsDebuggerBootstrap.TryStop();Require(V8DebuggerHost.ze_InspectorCount()==0,"Inspector failed to uninstall");
                Debug.Log("[ZenTS][debugger-test] Domain Reload PASS: Inspector count 1 -> 0 -> 1 -> 0, same port reused");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogError("[ZenTS][debugger-test] Domain Reload FAILED: "+e);EditorApplication.Exit(1);}
            finally{JsDebuggerBootstrap.TryStop();Restore(JsonUtility.FromJson<Saved>(SessionState.GetString(ReloadKey+".saved","")));}
        }
    }
}
