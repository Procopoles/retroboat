using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Downloads;
using Retroboat.Es;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Media;
using Retroboat.Media.Background;
using Retroboat.Settings;

namespace Retroboat.Launcher.BackgroundMedia
{
    /// <summary>
    /// Retroboat.exe --media (RB-041): baixa a fila de mídias secundárias (vídeos, manuais, imagens extras) sem janela, com
    /// prioridade baixa e sem aviso: quando cada uma chega, aparece no ES. Um por vez; fecha sozinho quando a fila esvazia.
    /// Aberto depois de cada sincronização (abertura, "Atualizar listas de jogos", CloudWatcher) e pelo script de início do
    /// ES quando sobrou fila de uma sessão anterior. Log em retroboat\logs\midias.log.
    /// </summary>
    static class BackgroundMediaHost
    {
        public static int Run(RetroBatInstall install)
        {
            try
            {
                using (var self = Process.GetCurrentProcess())
                    self.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                // Sem permissão para mudar a prioridade: baixa do mesmo jeito.
            }

            var queue = new BackgroundMediaQueue(install);
            while (true)
            {
                using (var mutex = new Mutex(false, BackgroundMediaProcess.SingleInstanceName))
                {
                    try
                    {
                        if (!mutex.WaitOne(0))
                            return 0;
                    }
                    catch (AbandonedMutexException)
                    {
                        // Um processo anterior foi encerrado à força; a vez é deste.
                    }

                    try
                    {
                        RunSession(install, queue);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Falha nas mídias em segundo plano", ex);
                        return 1;
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }

                // Quem pôs mídias na fila bem agora viu este processo ainda aberto e não abriu outro.
                if (!HasDueItems(queue))
                    return 0;
            }
        }

        static void RunSession(RetroBatInstall install, BackgroundMediaQueue queue)
        {
            var items = queue.Items();
            if (items.Count == 0)
                return;

            Log.Info("Mídias em segundo plano: " + items.Count + " na fila.");
            var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
            var pause = new PauseCheck(install);
            var worker = new BackgroundMediaWorker(install, queue,
                new BackgroundMediaFetcher(cloudAvailable: () => CloudLocator.IsAvailable(settings)),
                new EsApiGames(), pause.Reason, () => Es.Current() != null);

            bool toShare = false;
            while (true)
            {
                var clock = Stopwatch.StartNew();
                var run = worker.Run();
                Log.Info(string.Format("Mídias em segundo plano: {0} no jogo ({1:0.0} MB), {2} sem arquivo na fonte, {3} para depois, em {4:0} s.",
                    run.Applied, run.Bytes / 1048576.0, run.Gone, run.Postponed, clock.Elapsed.TotalSeconds));

                // O que veio da internet vai para a nuvem, para nenhum outro PC precisar buscar. Com o ES aberto, espera ele
                // fechar: a sincronização das mídias mexe nos gamelists, que ele regrava ao sair. Mídias novas na fila (um
                // "Atualizar listas de jogos") não esperam. Se o PC desligar antes, a próxima sincronização envia.
                toShare |= run.FromInternet;
                if (!toShare)
                    return;

                if (WaitForEsToClose(install, queue))
                    continue;

                ShareWithCloud(install, settings, queue);
                toShare = false;
                if (!HasDueItems(queue))
                    return;
            }
        }

        static void ShareWithCloud(RetroBatInstall install, RetroboatSettings settings, BackgroundMediaQueue queue)
        {
            var cloud = CloudLocator.Locate(settings);
            if (cloud == null || !cloud.Exists || string.IsNullOrEmpty(settings.RomsPath))
                return;

            try
            {
                var options = new CloudMediaOptions { Videos = settings.CloudVideos, Manuals = settings.CloudManuals, Automatic = settings.AutoMedia };
                CloudMediaSyncResult media = null;
                SyncLock.Run(() => media = StartupSync.SyncMediaAfterEs(install, cloud.RetroboatPath, settings.RomsPath, options, queue));
                Log.Info(string.Format("Mídias em segundo plano na nuvem: {0} jogo(s) enviados ({1} arquivo(s))", media.GamesShared, media.FilesUploaded));
                foreach (var problem in media.Errors) Log.Warn("Mídias em segundo plano na nuvem: " + problem);
            }
            catch (Exception ex) when (ex is TimeoutException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Mídias em segundo plano: envio para a nuvem fica para a próxima sincronização (" + ex.Message + ").");
            }
        }

        /// <returns>true se chegaram mídias novas na fila antes de o ES fechar.</returns>
        static bool WaitForEsToClose(RetroBatInstall install, BackgroundMediaQueue queue)
        {
            bool logged = false;
            Process es;
            // Outro ES pode aparecer logo depois (o ES reiniciado pelo menu): espera também.
            while ((es = Es.Current()) != null)
            {
                if (!logged)
                {
                    Log.Info("Mídias em segundo plano: enviando para a nuvem quando o ES fechar.");
                    logged = true;
                }

                // Sem gastar nada: acorda quando o ES fecha, ou a cada 10 s para ver se há fila (o arquivo só existe com ela).
                while (!es.WaitForExit(10000))
                {
                    if (File.Exists(BackgroundMediaQueue.PathFor(install)) && HasDueItems(queue))
                        return true;
                }
            }

            return false;
        }

        static bool HasDueItems(BackgroundMediaQueue queue)
        {
            var now = DateTime.UtcNow;
            foreach (var item in queue.Items())
            {
                if (item.NextTryUtc <= now)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// O processo do ES, guardado: listar os processos do Windows custa (na RB-040, 2,6% de um núcleo a cada 2 s), e numa
        /// partida longa este processo pergunta a cada poucos segundos. Com o ES conhecido, só olha se ele terminou; lista de
        /// novo no máximo a cada 3 s quando não há ES.
        /// </summary>
        static class Es
        {
            static readonly TimeSpan ListEvery = TimeSpan.FromSeconds(3);
            static readonly Stopwatch SinceListed = new Stopwatch();
            static Process _current;

            public static Process Current()
            {
                lock (SinceListed)
                {
                    if (_current != null && !_current.HasExited)
                        return _current;

                    _current?.Dispose();
                    _current = null;
                    if (SinceListed.IsRunning && SinceListed.Elapsed < ListEvery)
                        return null;

                    var running = Process.GetProcessesByName("emulationstation");
                    SinceListed.Restart();
                    for (int i = 0; i < running.Length; i++)
                    {
                        if (i == 0)
                            _current = running[0];
                        else
                            running[i].Dispose();
                    }

                    return _current;
                }
            }
        }

        /// <summary>
        /// Por que esperar agora: a pessoa está esperando uma sincronização, um jogo está baixando (vem primeiro) ou aberto
        /// (nada de download durante a partida, como na Steam). Conferido no máximo a cada 2 s, por todas as filas juntas.
        /// </summary>
        sealed class PauseCheck
        {
            static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);

            readonly RetroBatInstall _install;
            readonly Stopwatch _clock = new Stopwatch();
            string _reason;

            public PauseCheck(RetroBatInstall install) => _install = install;

            public string Reason()
            {
                lock (this)
                {
                    if (!_clock.IsRunning || _clock.Elapsed > CacheFor)
                    {
                        _reason = Compute();
                        _clock.Restart();
                    }

                    return _reason;
                }
            }

            string Compute()
            {
                if (SyncLock.IsBusy())
                    return "sincronização com a nuvem";

                var downloads = new DownloadQueue(_install).Items(out bool paused);
                if (downloads.Count > 0 && !paused)
                    return "download de jogo";

                if (EsApi.IsGameRunning())
                    return "jogo aberto";

                return null;
            }
        }
    }
}
