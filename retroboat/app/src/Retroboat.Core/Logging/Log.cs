using System;

namespace Retroboat.Logging
{
    /// <summary>O log do processo (launch.log, downloads.log, retroboat.log...). Ao passar de 1 MB, o anterior vira .old.log.</summary>
    public static class Log
    {
        static LogFile _file;

        public static void Init(string path) => _file = new LogFile(path);

        public static void Info(string message) => _file?.Info(message);
        public static void Warn(string message) => _file?.Warn(message);
        public static void Error(string message, Exception ex = null) => _file?.Error(message, ex);
    }
}
