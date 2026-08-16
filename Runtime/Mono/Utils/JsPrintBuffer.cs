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
