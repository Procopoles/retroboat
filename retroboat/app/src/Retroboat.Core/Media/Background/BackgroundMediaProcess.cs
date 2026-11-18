using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Media.Background
{
    /// <summary>
    /// As mídias de segundo plano (RB-041) são baixadas pelo próprio Retroboat.exe com <see cref="Argument"/>, sem janela e
    /// com prioridade baixa. Roda à parte porque o ES espera o "Atualizar listas de jogos" terminar: ele volta na hora, e os
    /// vídeos seguem. Só existe um, garantido por <see cref="SingleInstanceName"/>; ele fecha sozinho quando a fila esvazia.
    /// </summary>
    public static class BackgroundMediaProcess
    {
        public const string Argument = "--media";
        public const string SingleInstanceName = @"Local\RetroboatBackgroundMedia";
        public const string ExeName = "Retroboat.exe";

        public static bool IsRunning()
        {
            if (!Mutex.TryOpenExisting(SingleInstanceName, out var mutex))
                return false;

            mutex.Dispose();
            return true;
        }

        /// <summary>Se há mídias na fila e o processo não está rodando, abre. Falha aqui nunca impede de jogar.</summary>
        /// <returns>true se abriu agora.</returns>
        public static bool EnsureRunning(RetroBatInstall install, string exeFolder)
        {
            try
            {
                var exe = Path.Combine(exeFolder, ExeName);
                if (!File.Exists(exe) || IsRunning() || new BackgroundMediaQueue(install).IsEmpty)
                    return false;

                // Sem barra no fim: \" fecharia a aspa errada.
                var root = install.Root.TrimEnd('\\', '/');
                Process.Start(new ProcessStartInfo(exe, Argument + " --retrobat \"" + root + "\"")
                {
                    UseShellExecute = true, // Não herda nada do ES nem do script: ninguém espera por ele.
                    WorkingDirectory = exeFolder
                })?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao abrir as mídias em segundo plano", ex);
                return false;
            }
        }
    }
}
