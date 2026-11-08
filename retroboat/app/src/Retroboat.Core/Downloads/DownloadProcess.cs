using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Downloads
{
    /// <summary>
    /// O serviço de downloads é o próprio retroboat-launch.exe com <see cref="Argument"/>. Roda à parte porque o
    /// ES espera o retroboat-launch que abriu para continuar: ele volta na hora, e o download segue. Só existe um,
    /// garantido por <see cref="SingleInstanceName"/>.
    /// </summary>
    public static class DownloadProcess
    {
        public const string Argument = "--downloads";
        public const string SingleInstanceName = @"Local\RetroboatDownloads";
        public const string ExeName = "retroboat-launch.exe";

        public static bool IsRunning()
        {
            if (!Mutex.TryOpenExisting(SingleInstanceName, out var mutex))
                return false;

            mutex.Dispose();
            return true;
        }

        /// <summary>Se há jogos na fila e o serviço não está rodando, inicia. Falha aqui nunca impede de jogar.</summary>
        public static void EnsureRunning(RetroBatInstall install, string exeFolder)
        {
            try
            {
                if (new DownloadQueue(install).IsEmpty || IsRunning())
                    return;

                Process.Start(new ProcessStartInfo(Path.Combine(exeFolder, ExeName), Argument)
                {
                    UseShellExecute = true,
                    WorkingDirectory = exeFolder
                })?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao iniciar o serviço de downloads", ex);
            }
        }
    }
}
