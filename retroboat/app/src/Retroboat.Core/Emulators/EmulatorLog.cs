using System;
using System.Collections.Generic;
using System.IO;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Emulators
{
    /// <summary>
    /// Log dos emuladores, num arquivo só para eles: &lt;RetroBat&gt;\retroboat\logs\emuladores.log. Tudo de instalar, atualizar
    /// e conferir compatibilidade vai para lá, com o processo que escreveu (launch, downloads, cli); o log do processo recebe a
    /// mesma linha, para a sequência dele continuar completa.
    /// </summary>
    public static class EmulatorLog
    {
        public const string FileName = "emuladores.log";

        static readonly Dictionary<string, LogFile> Files = new Dictionary<string, LogFile>(StringComparer.OrdinalIgnoreCase);

        public static string PathFor(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, "logs", FileName);

        public static void Info(RetroBatInstall install, string message)
        {
            FileFor(install).Info(Tagged(message));
            Log.Info(message);
        }

        public static void Warn(RetroBatInstall install, string message)
        {
            FileFor(install).Warn(Tagged(message));
            Log.Warn(message);
        }

        public static void Error(RetroBatInstall install, string message, Exception ex = null)
        {
            FileFor(install).Error(Tagged(message), ex);
            Log.Error(message, ex);
        }

        /// <summary>Um relatório de várias linhas, inteiro, só no log dos emuladores; o do processo recebe o resumo.</summary>
        public static void Report(RetroBatInstall install, string summary, string body)
        {
            FileFor(install).Block("AVISO", Tagged(summary) + Environment.NewLine + body);
            Log.Warn(summary + " (detalhes em logs\\" + FileName + ")");
        }

        static string Tagged(string message) => "(" + ProcessName + ") " + message;

        static string ProcessName
        {
            get
            {
                try { return System.Diagnostics.Process.GetCurrentProcess().ProcessName; }
                catch (InvalidOperationException) { return "?"; }
            }
        }

        static LogFile FileFor(RetroBatInstall install)
        {
            var path = PathFor(install);
            lock (Files)
            {
                if (!Files.TryGetValue(path, out var file))
                    Files[path] = file = new LogFile(path);

                return file;
            }
        }
    }
}
