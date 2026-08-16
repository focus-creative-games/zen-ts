using System;

namespace ZTS
{
    public sealed class TsScriptException : Exception
    {
        public TsScriptException(string message)
            : base(message)
        {
        }

        public TsScriptException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
