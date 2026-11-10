// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace ZenTS.Editor.Diagnostics
{
    /// <summary>Built-in V8 Inspector host. A single debugger attaches on loopback.</summary>
    public sealed class V8DebuggerHost : IZentsJsDebuggerHost
    {
        private readonly object _lifetime = new object();
        private IntPtr _inspector;
        private V8InspectorServer _server;
        private V8InspectorServer.Client _client;
        private SendCallback _send;
        private PumpCallback _pump;
        private int _thread;
        public static string Endpoint
        {
            get; private set;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SendCallback(IntPtr data, int length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PumpCallback(int timeout);
        private const string Library = "zents_v8";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ze_InspectorCreate(IntPtr context, SendCallback send, PumpCallback pump, [MarshalAs(UnmanagedType.LPUTF8Str)] string baseUrl);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ze_InspectorDestroy(IntPtr handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ze_InspectorConnect(IntPtr handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ze_InspectorDisconnect(IntPtr handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ze_InspectorDispatch(IntPtr handle, byte[] data, int length);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ze_InspectorWait(IntPtr handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ze_InspectorCount();
        public void Install(JSRuntimeHandle runtime, JSContextHandle context, JsDebuggerHostContext options)
        {
            if (_inspector != IntPtr.Zero)
                throw new InvalidOperationException("Inspector already installed");
            _thread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                _server = new V8InspectorServer(options.PreferredPort, options.ProjectRoot, Signal);
                _send = Send;
                _pump = Pump;
                lock (_lifetime)
                    _inspector = ze_InspectorCreate(context.Value, _send, _pump, _server.SourceUrl);
                if (_inspector == IntPtr.Zero)
                    throw new InvalidOperationException("V8 Inspector creation failed");
                Endpoint = _server.WebSocketUrl;
                Debug.Log("[ZenTS][debugger] Listening on " + Endpoint + "; discovery=http://127.0.0.1:" + options.PreferredPort + "/json/list");
                if (options.WaitForDebugger)
                {
                    Debug.Log("[ZenTS][debugger] Waiting for Runtime.runIfWaitingForDebugger before loading business modules");
                    ze_InspectorWait(_inspector);
                }
            }
            catch { Uninstall(); throw; }
        }
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ze_InspectorInterrupt(IntPtr handle);
        private void Signal()
        {
            // RequestInterrupt is the only cross-thread V8 entry; its callback
            // dispatches protocol on the owner thread, including CPU-bound JS.
            lock (_lifetime)
                if (_inspector != IntPtr.Zero)
                    ze_InspectorInterrupt(_inspector);
        }
        private void AssertThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread)
                throw new InvalidOperationException("Inspector must run on the V8 owner thread");
        }
        private void Send(IntPtr data, int length)
        {
            // Callback must never unwind a managed exception through a V8 frame.
            try
            {
                if (_client == null || length < 0 || length > 8 * 1024 * 1024)
                {
                    _client?.Close();
                    return;
                }
                byte[] bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, length);
                _client.Send(bytes);
            }
            catch (Exception) { _client?.Close(); }
        }
        private void Pump(int timeout)
        {
            try
            {
                AssertThread();
                if (_client != null && _client.Closed)
                {
                    ze_InspectorDisconnect(_inspector);
                    _client = null;
                }
                for (int i = 0; i < 64 && _server != null; i++)
                {
                    if (!_server.Take(i == 0 ? timeout : 0, out var message))
                        break;
                    if (message.Connected)
                    {
                        _client = message.Client;
                        ze_InspectorConnect(_inspector);
                    }
                    else if (message.Client == _client)
                    {
                        if (message.Data != null)
                            ze_InspectorDispatch(_inspector, message.Data, message.Data.Length);
                        else
                        {
                            ze_InspectorDisconnect(_inspector);
                            _client = null;
                        }
                    }
                }
            }
            catch (Exception error)
            {
                _client?.Close();
                _client = null;
                if (_inspector != IntPtr.Zero)
                    ze_InspectorDisconnect(_inspector);
                Debug.LogWarning("[ZenTS][debugger] Protocol dispatch: " + error.Message);
            }
        }
        public void Tick()
        {
            if (_inspector != IntPtr.Zero)
                Pump(0);
        }
        public void Uninstall()
        {
            if (_thread != 0)
                AssertThread();
            _server?.Dispose();
            _server = null;
            _client = null;
            lock (_lifetime)
                if (_inspector != IntPtr.Zero)
                {
                    ze_InspectorDestroy(_inspector);
                    _inspector = IntPtr.Zero;
                }
            _send = null;
            _pump = null;
            Endpoint = null;
        }
    }
}
