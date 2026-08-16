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

using System.Collections.Generic;

namespace ZTS.Utils
{
    internal static class JsPrintBuffer
    {
        private static readonly Queue<string> Pending = new Queue<string>();
        private static bool _flushScheduled;

        public static void Log(string message)
        {
            lock (Pending)
            {
                Pending.Enqueue(message ?? string.Empty);
                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    ScheduleFlush();
                }
            }
        }

        private static void ScheduleFlush()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += Flush;
#else
            UnityEngine.Application.logMessageReceived += OnLogOnce;
#endif
        }

#if !UNITY_EDITOR
        private static void OnLogOnce(string condition, string stackTrace, UnityEngine.LogType type)
        {
            UnityEngine.Application.logMessageReceived -= OnLogOnce;
            Flush();
        }
#endif

        public static void Flush()
        {
            string[] lines;
            lock (Pending)
            {
                lines = Pending.ToArray();
                Pending.Clear();
                _flushScheduled = false;
            }

            foreach (string line in lines)
            {
                UnityEngine.Debug.Log(line);
            }
        }
    }
}
