using System;
using System.IO;
using System.Text;

namespace Retroboat.Logging
{
    /// <summary>Log em arquivo texto. Ao passar de 1 MB, o anterior vira .old.log.</summary>
    public static class Log
    {
        const long MaxSize = 1 << 20;
        static readonly object Sync = new object();
        static string _path;

        public static void Init(string path)
        {
            _path = path;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxSize)
                {
                    var old = Path.ChangeExtension(path, ".old.log");
                    if (File.Exists(old))
                        File.Delete(old);

                    File.Move(path, old);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("AVISO", message);
        public static void Error(string message, Exception ex = null) => Write("ERRO", ex == null ? message : message + ": " + ex);

        static void Write(string level, string message)
        {
            if (_path == null)
                return;

            lock (Sync)
            {
                try
                {
                    File.AppendAllText(_path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
