using System;
using System.IO;
using System.Text;

namespace Retroboat.Logging
{
    /// <summary>
    /// Um arquivo de log texto, uma linha por mensagem. Ao passar de 1 MB, o anterior vira .old.log. Falhas de escrita são
    /// ignoradas: log nunca derruba o app.
    /// </summary>
    public sealed class LogFile
    {
        const long MaxSize = 1 << 20;
        readonly object _sync = new object();

        public LogFile(string path)
        {
            Path = path;
            Rotate();
        }

        public string Path { get; }

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("AVISO", message);
        public void Error(string message, Exception ex = null) => Write("ERRO", ex == null ? message : message + ": " + ex);

        /// <summary>Várias linhas de uma vez, sem outra mensagem no meio (ex.: um relatório).</summary>
        public void Block(string level, string text) => Write(level, text);

        void Write(string level, string message)
        {
            lock (_sync)
            {
                try
                {
                    File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        void Rotate()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                var info = new FileInfo(Path);
                if (info.Exists && info.Length > MaxSize)
                {
                    var old = System.IO.Path.ChangeExtension(Path, ".old.log");
                    if (File.Exists(old))
                        File.Delete(old);

                    File.Move(Path, old);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
