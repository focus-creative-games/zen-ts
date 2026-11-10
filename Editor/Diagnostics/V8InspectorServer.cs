// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ZenTS.Editor.Diagnostics
{
    // I/O only: no Unity/V8 calls on these background threads.
    internal sealed class V8InspectorServer : IDisposable
    {
        internal sealed class Message
        {
            public Client Client;
            public byte[] Data; // null = connect or disconnect
            public bool Connected;
        }
        internal sealed class Client
        {
            internal readonly TcpClient Socket;
            internal readonly BlockingCollection<byte[]> Output = new BlockingCollection<byte[]>(512);
            private readonly V8InspectorServer _owner;
            private int _closed;
            private int _queuedBytes;
            internal bool Closed => Volatile.Read(ref _closed) != 0;
            internal Client(V8InspectorServer owner, TcpClient socket) { _owner = owner; Socket = socket; }
            internal bool Send(byte[] message)
            {
                if (message.Length > MaxFrame || _closed != 0) { Close(); return false; }
                int bytes = Interlocked.Add(ref _queuedBytes, message.Length);
                if (bytes > 16 * 1024 * 1024 || !Output.TryAdd(message)) { Interlocked.Add(ref _queuedBytes, -message.Length); Close(); return false; }
                return true;
            }
            internal void Sent(int length) => Interlocked.Add(ref _queuedBytes, -length);
            internal void Close()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;
                Output.CompleteAdding(); Socket.Close();
                _owner.Enqueue(new Message { Client = this, Connected = false });
            }
        }
        private const int MaxFrame = 8 * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly TcpListener _listener;
        private readonly Thread _accept;
        private readonly BlockingCollection<Message> _messages = new BlockingCollection<Message>(256);
        private readonly object _clientsLock = new object();
        private readonly HashSet<Thread> _workers = new HashSet<Thread>();
        private readonly HashSet<TcpClient> _sockets = new HashSet<TcpClient>();
        private readonly string _projectRoot;
        private readonly Action _signal;
        private volatile bool _stopped;
        private Client _active;
        internal int Port { get; }
        internal string TargetId { get; } = Guid.NewGuid().ToString("N");
        internal string WebSocketUrl => "ws://127.0.0.1:" + Port + "/" + TargetId;
        internal string SourceUrl => "http://127.0.0.1:" + Port + "/source/out/";
        internal V8InspectorServer(int port, string projectRoot, Action signal)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            _signal = signal;
            Port = port; _projectRoot = Path.GetFullPath(Path.Combine(projectRoot, "TsProject"));
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _accept = new Thread(Accept) { IsBackground = true, Name = "ZenTS Inspector accept" };
            _accept.Start();
        }
        private void Enqueue(Message message)
        {
            if (_stopped) return;
            if (!_messages.TryAdd(message)) message.Client.Close();
            _signal();
        }
        internal bool Take(int timeout, out Message message) => _messages.TryTake(out message, timeout);
        private void Accept()
        {
            while (!_stopped)
            {
                TcpClient socket;
                try { socket = _listener.AcceptTcpClient(); }
                catch (SocketException) { if (_stopped) return; continue; }
                catch (ObjectDisposedException) { return; }
                socket.NoDelay = true; socket.ReceiveTimeout = 5000; socket.SendTimeout = 5000;
                lock (_clientsLock)
                {
                    if (_stopped) { socket.Close(); return; }
                    if (_sockets.Count >= 16) { socket.Close(); continue; }
                    _sockets.Add(socket);
                }
                StartWorker(() => Serve(socket), "ZenTS Inspector read");
            }
        }
        private void StartWorker(ThreadStart action, string name)
        {
            Thread worker = null;
            worker = new Thread(() => { try { action(); } finally { lock (_clientsLock) _workers.Remove(worker); } }) { IsBackground = true, Name = name };
            lock (_clientsLock) { if (_stopped) return; _workers.Add(worker); worker.Start(); }
        }
        private void Serve(TcpClient socket)
        {
            Client client = null;
            try
            {
                var stream = socket.GetStream();
                string header = ReadHeader(stream);
                string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] request = lines[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    int at = lines[i].IndexOf(':');
                    if (at > 0) headers[lines[i].Substring(0, at)] = lines[i].Substring(at + 1).Trim();
                }
                if (request.Length != 3 || request[0] != "GET" || !headers.TryGetValue("Host", out string host) ||
                    (host != "127.0.0.1:" + Port && host != "localhost:" + Port)) { Reply(stream, 400, "Bad request"); return; }
                string route = request[1].Split('?')[0];
                if (route == "/" + TargetId && headers.TryGetValue("Upgrade", out string upgrade) && upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
                {
                    if (headers.TryGetValue("Origin", out string origin) && !origin.StartsWith("devtools://", StringComparison.Ordinal) && !origin.StartsWith("chrome-devtools://", StringComparison.Ordinal)) { Reply(stream, 403, "Origin rejected"); return; }
                    if (!headers.TryGetValue("Sec-WebSocket-Key", out string key) || Convert.FromBase64String(key).Length != 16 || !headers.TryGetValue("Sec-WebSocket-Version", out string version) || version != "13") { Reply(stream, 400, "Invalid WebSocket handshake"); return; }
                    lock (_clientsLock)
                    {
                        if (_active != null) { Reply(stream, 409, "Debugger already attached"); return; }
                        client = new Client(this, socket); _active = client;
                    }
                    string accept;
                    using (var sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    Write(stream, Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"));
                    socket.ReceiveTimeout = 0;
                    Enqueue(new Message { Client = client, Connected = true });
                    StartWorker(() => SendLoop(client), "ZenTS Inspector write");
                    ReadFrames(stream, client);
                }
                else if (route == "/json" || route == "/json/list")
                {
                    var target = new Target { id = TargetId, title = "ZenTS Editor", type = "node", url = SourceUrl, webSocketDebuggerUrl = WebSocketUrl,
                        devtoolsFrontendUrl = "devtools://devtools/bundled/inspector.html?v8only=true&ws=127.0.0.1:" + Port + "/" + TargetId };
                    Reply(stream, 200, "[" + JsonUtility.ToJson(target) + "]", "application/json");
                }
                else if (route == "/json/version") Reply(stream, 200, "{\"Browser\":\"ZenTS V8\",\"Protocol-Version\":\"1.3\"}", "application/json");
                else if (route.StartsWith("/source/", StringComparison.Ordinal)) ServeSource(stream, route.Substring(8));
                else Reply(stream, 404, "Not found");
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is FormatException || ex is ArgumentException) { }
            finally
            {
                if (client != null) client.Close();
                socket.Close();
                lock (_clientsLock) { _sockets.Remove(socket); if (_active == client) _active = null; }
            }
        }
        [Serializable] private sealed class Target { public string id, title, type, url, webSocketDebuggerUrl, devtoolsFrontendUrl; }
        private void ServeSource(NetworkStream stream, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(_projectRoot, Uri.UnescapeDataString(relative)));
            string prefix = _projectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string extension = Path.GetExtension(full);
            if (!full.StartsWith(prefix, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                (extension != ".map" && extension != ".ts" && extension != ".js")) { Reply(stream, 403, "Source path rejected"); return; }
            if (!File.Exists(full)) { Reply(stream, 404, "Source not found"); return; }
            Reply(stream, 200, File.ReadAllText(full), extension == ".map" ? "application/json" : "text/plain; charset=utf-8");
        }
        private static string ReadHeader(Stream stream)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 8192)
            {
                int b = stream.ReadByte(); if (b < 0) throw new EndOfStreamException(); bytes.Add((byte)b);
                int n = bytes.Count;
                if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) return Encoding.ASCII.GetString(bytes.ToArray());
            }
            throw new IOException("Inspector HTTP header too large");
        }
        private static void ReadExact(Stream stream, byte[] data)
        {
            int at = 0; while (at < data.Length) { int n = stream.Read(data, at, data.Length - at); if (n == 0) throw new EndOfStreamException(); at += n; }
        }
        private void ReadFrames(NetworkStream stream, Client client)
        {
            using (var message = new MemoryStream())
            {
                bool fragmented = false;
                while (!_stopped)
                {
                    int first = stream.ReadByte(), second = stream.ReadByte(); if (first < 0 || second < 0) return;
                    int opcode = first & 15; bool final = (first & 128) != 0;
                    if ((first & 112) != 0 || (second & 128) == 0) throw new IOException("Invalid WebSocket frame");
                    ulong length = (ulong)(second & 127);
                    if (length >= 126)
                    {
                        byte[] size = new byte[length == 126 ? 2 : 8]; ReadExact(stream, size); length = 0;
                        foreach (byte b in size) length = (length << 8) | b;
                    }
                    if (length > MaxFrame || (opcode >= 8 && (!final || length > 125))) throw new IOException("Oversized WebSocket frame");
                    byte[] mask = new byte[4]; ReadExact(stream, mask);
                    byte[] data = new byte[(int)length]; ReadExact(stream, data);
                    for (int i = 0; i < data.Length; i++) data[i] ^= mask[i & 3];
                    if (opcode == 8) { lock (stream) WriteFrame(stream, 8, data); return; }
                    if (opcode == 9) { lock (stream) WriteFrame(stream, 10, data); continue; }
                    if (opcode == 10) continue;
                    if ((opcode == 1 && fragmented) || (opcode == 0 && !fragmented) || (opcode != 0 && opcode != 1)) throw new IOException("Invalid text continuation");
                    if (message.Length + data.Length > 1024 * 1024) throw new IOException("Inspector request too large");
                    message.Write(data, 0, data.Length); fragmented = !final;
                    if (final)
                    {
                        byte[] body = message.ToArray(); Utf8.GetString(body); // strict UTF-8 validation
                        Enqueue(new Message { Client = client, Data = body }); message.SetLength(0);
                    }
                }
            }
        }
        private static void SendLoop(Client client)
        {
            try { var stream = client.Socket.GetStream(); foreach (byte[] data in client.Output.GetConsumingEnumerable()) { try { lock (stream) WriteFrame(stream, 1, data); } finally { client.Sent(data.Length); } } }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException) { }
            finally { client.Close(); }
        }
        private static void WriteFrame(Stream stream, int opcode, byte[] data)
        {
            using (var frame = new MemoryStream())
            {
                frame.WriteByte((byte)(128 | opcode));
                if (data.Length < 126) frame.WriteByte((byte)data.Length);
                else if (data.Length <= 65535) { frame.WriteByte(126); frame.WriteByte((byte)(data.Length >> 8)); frame.WriteByte((byte)data.Length); }
                else { frame.WriteByte(127); for (int shift = 56; shift >= 0; shift -= 8) frame.WriteByte((byte)((ulong)data.Length >> shift)); }
                frame.Write(data, 0, data.Length); Write(stream, frame.ToArray());
            }
        }
        private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
        private static void Reply(Stream stream, int status, string body, string type = "text/plain")
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            Write(stream, Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Error") + "\r\nContent-Type: " + type + "\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n"));
            Write(stream, bytes);
        }
        public void Dispose()
        {
            _stopped = true; _listener.Stop();
            lock (_clientsLock) { foreach (var socket in _sockets) socket.Close(); _active?.Close(); }
            _accept.Join(1000);
            Thread[] workers; lock (_clientsLock) { workers = new Thread[_workers.Count]; _workers.CopyTo(workers); }
            foreach (var worker in workers) worker.Join(1000);
        }
    }
}
