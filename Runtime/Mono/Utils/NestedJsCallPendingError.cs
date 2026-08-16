using System;

namespace ZTS.Utils
{
    internal static class NestedJsCallPendingError
    {
        private static Exception _pending;

        public static void Set(Exception ex) => _pending = ex;

        public static bool TryTake(out Exception ex)
        {
            ex = _pending;
            _pending = null;
            return ex != null;
        }

        public static void Clear() => _pending = null;
    }
}
