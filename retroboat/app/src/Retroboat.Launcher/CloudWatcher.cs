using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Es;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Settings;

namespace Retroboat.Launcher
{
    /// <summary>
    /// Acompanha o Google Drive enquanto o ES estiver aberto, sem janela. Quando o Drive conecta, sincroniza como
    /// no início e o ES recarrega com a nuvem acesa e os jogos novos; quando desconecta, o ES recarrega com o X na
    /// nuvem. A pergunta sobre abrir o Drive fica para o BAIXAR ou o jogo com memory card: daqui, uma tela por
    /// cima do ES roubaria o foco com a pessoa jogando ou navegando, e o A dela poderia abrir um jogo no ES.
    /// Com jogo aberto ou scraper rodando, a mudança espera: recarregar o ES interromperia os dois.
    /// </summary>
    static class CloudWatcher
    {
        const string SingleInstance = @"Local\RetroboatCloudWatcher";
        static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);

        // O ES pode sumir por alguns segundos ao reiniciar pelo menu.
        const int ChecksWithoutEs = 3;

        // O ES leva um instante para recarregar; notificação antes disso passa despercebida.
        static readonly TimeSpan AfterReload = TimeSpan.FromSeconds(3);

        /// <returns>true se acompanhou até o ES fechar; false se outro lançador já estava acompanhando.</returns>
        public static bool Run(RetroBatInstall install, RetroboatSettings settings)
        {
            using (var mutex = new Mutex(false, SingleInstance))
            {
                try
                {
                    if (!mutex.WaitOne(0))
                        return false;
                }
                catch (AbandonedMutexException)
                {
                    // Um lançador anterior foi encerrado à força; a vez é deste.
                }

                try
                {
                    Log.Info("Acompanhando o Google Drive enquanto o ES estiver aberto.");
                    Watch(install, settings);
                    Log.Info("ES fechado: fim do acompanhamento do Google Drive.");
                    return true;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        static void Watch(RetroBatInstall install, RetroboatSettings settings)
        {
            // O que o tema está mostrando: o ES leu este arquivo ao carregar.
            bool shown = CloudStatusFile.IsConnected(install);
            bool? changingTo = null;
            int withoutEs = 0;

            while (withoutEs < ChecksWithoutEs)
            {
                Thread.Sleep(CheckEvery);

                if (!IsEsProcessRunning())
                {
                    withoutEs++;
                    continue;
                }

                withoutEs = 0;

                try
                {
                    if (!EsApi.IsIdle())
                        continue;

                    bool connected = CloudLocator.IsAvailable(settings);
                    if (connected == shown)
                    {
                        changingTo = null;
                        continue;
                    }

                    // Confirma na volta seguinte: o Drive pode oscilar enquanto monta a unidade.
                    if (changingTo != connected)
                    {
                        changingTo = connected;
                        continue;
                    }

                    changingTo = null;
                    shown = connected;
                    if (connected)
                        OnConnected(install, settings);
                    else
                        OnDisconnected(install);
                }
                catch (Exception ex)
                {
                    // Sem o acompanhamento, o ES segue funcionando; na próxima volta tenta de novo.
                    Log.Error("Falha ao acompanhar o Google Drive", ex);
                }
            }
        }

        static void OnConnected(RetroBatInstall install, RetroboatSettings settings)
        {
            Log.Info("Google Drive conectado com o ES aberto: sincronizando.");

            // Um sistema novo na nuvem precisa do retroboat-launch antes de o ES reler os sistemas.
            Program.ApplyLibrary(install, settings.RomsPath, Program.CreateCommandWrapper(install, settings));

            var report = Program.SyncWithCloud(install, settings);
            Program.LogReport(report);

            EsApi.ReloadGames();
            Thread.Sleep(AfterReload);
            EsApi.Notify(string.Join("\n", new[] { "Google Drive conectado." }.Concat(report.Messages)));
        }

        static void OnDisconnected(RetroBatInstall install)
        {
            Log.Warn("Google Drive desconectado com o ES aberto.");

            CloudStatusFile.Write(install, false);
            EsApi.ReloadGames();
            Thread.Sleep(AfterReload);
            EsApi.Notify("Google Drive desconectado. Os jogos baixados continuam funcionando.");
        }

        static bool IsEsProcessRunning()
        {
            var processes = Process.GetProcessesByName("emulationstation");
            foreach (var process in processes)
                process.Dispose();

            return processes.Length > 0;
        }
    }
}
