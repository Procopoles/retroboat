using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Retroboat.Downloads;
using Retroboat.Es;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Media;

namespace Retroboat.Launch
{
    /// <summary>
    /// retroboat-launch --downloads: baixa a fila em segundo plano e mostra o andamento no cabeçalho do ES
    /// (HeaderBar). Sem janela própria e um só por vez. Fecha sozinho quando a fila esvazia. Se o ES fechar, para e
    /// deixa o jogo na fila: a próxima abertura continua.
    /// </summary>
    static class DownloadHost
    {
        // O ES pode sumir por alguns segundos ao reiniciar pelo menu.
        const int ChecksWithoutEs = 5;
        const int CheckEveryMs = 3000;
        const int QueueCheckMs = 500;

        public static int Run(RetroBatInstall install)
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4)); // Por monitor: a região acompanha os pixels reais do ES.

            var queue = new DownloadQueue(install);
            while (true)
            {
                bool esClosed;
                using (var mutex = new Mutex(false, DownloadProcess.SingleInstanceName))
                {
                    try
                    {
                        if (!mutex.WaitOne(0))
                            return 0;
                    }
                    catch (AbandonedMutexException)
                    {
                        // Um serviço anterior foi encerrado à força; a vez é deste.
                    }

                    try
                    {
                        esClosed = RunSession(install, queue);
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }

                // Quem pôs um jogo na fila bem agora viu o serviço ainda aberto e não iniciou outro.
                if (esClosed || queue.IsEmpty)
                    return 0;
            }
        }

        /// <returns>true se terminou porque o ES fechou.</returns>
        static bool RunSession(RetroBatInstall install, DownloadQueue queue)
        {
            DownloadService.Reconcile(install, queue);
            if (queue.IsEmpty)
                return false;

            // O ES do Retroboat (RB-010) mostra o andamento e atualiza as listas sozinho; com o oficial, a janela
            // por cima do cabeçalho e o POST /addgames.
            bool native = EsApi.SupportsNativeDownloads();
            Log.Info("Serviço de downloads iniciado: " + queue.Items().Count + " jogo(s) na fila" + (native ? ", com o ES do Retroboat." : "."));

            using (var stop = new CancellationTokenSource())
            using (IDownloadDisplay bar = native ? new NativeStatusReporter() : (IDownloadDisplay)new HeaderBar(install))
            using (var context = new ApplicationContext())
            using (var closeCheck = new System.Windows.Forms.Timer { Interval = 250 })
            {
                var service = DownloadService.ForInstall(install, queue);
                var workerDone = false;
                var esClosed = false;

                service.Changed += bar.SetLive;
                service.Cancelled += item =>
                {
                    bar.SetLive(null);
                    if (!native)
                        Refresh(install, item);
                };
                service.Completed += status =>
                {
                    bar.SetLive(null);
                    bar.AddNotice(status);
                    if (!native)
                        Refresh(install, status.Item);
                };

                // A notificação entra depois da rotação, para não ficar atrás da região.
                if (bar is HeaderBar header)
                {
                    header.NoticeShown += status =>
                    {
                        if (status.Phase == DownloadPhase.Failed && !string.IsNullOrEmpty(status.Message))
                            ThreadPool.QueueUserWorkItem(_ => EsApi.Notify(status.Item.Title + ": " + status.Message));
                    };
                }

                var worker = new Thread(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        try { service.Run(stop.Token); }
                        catch (Exception ex) { Log.Error("Falha no serviço de downloads", ex); }

                        // Um jogo novo pode chegar enquanto a região mostra PRONTO: ele não espera a janela fechar.
                        workerDone = queue.IsEmpty;
                        stop.Token.WaitHandle.WaitOne(QueueCheckMs);
                    }
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Downloads" };

                var monitor = new Thread(() =>
                {
                    int withoutEs = 0;
                    while (!stop.IsCancellationRequested && withoutEs < ChecksWithoutEs)
                    {
                        if (stop.Token.WaitHandle.WaitOne(CheckEveryMs))
                            return;

                        if (IsEsRunning())
                        {
                            withoutEs = 0;
                            bar.EsBusy = !EsApi.IsIdle();
                        }
                        else
                        {
                            withoutEs++;
                        }
                    }

                    if (!stop.IsCancellationRequested)
                    {
                        esClosed = true;
                        Log.Info("ES fechado: o download continua na próxima abertura.");
                        stop.Cancel();
                    }
                }) { IsBackground = true, Name = "Acompanha o ES" };

                closeCheck.Tick += (s, e) =>
                {
                    if (esClosed || (workerDone && bar.IsIdle))
                        context.ExitThread();
                };

                worker.Start();
                monitor.Start();
                closeCheck.Start();

                Application.Run(context);

                stop.Cancel();
                worker.Join(TimeSpan.FromSeconds(15));
                Log.Info("Serviço de downloads encerrado.");
                return esClosed;
            }
        }

        /// <summary>
        /// O status do jogo mudou com o ES aberto: a lista dele se atualiza sem recarregar. Com jogo aberto ou
        /// scraper rodando, fica para depois: voltar de um jogo já atualiza.
        /// </summary>
        static void Refresh(RetroBatInstall install, DownloadItem item)
        {
            try
            {
                if (!EsApi.IsIdle())
                    return;

                var systems = GameMedia.SystemNamesByFolder(install);
                var systemName = systems.TryGetValue(item.System, out var name) ? name : item.System;
                if (!EsApi.RefreshGameList(systemName))
                    Log.Warn("O ES não atualizou a lista de " + systemName + ".");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                Log.Warn("Não foi possível atualizar a lista do ES: " + ex.Message);
            }
        }

        static bool IsEsRunning()
        {
            var processes = Process.GetProcessesByName("emulationstation");
            foreach (var process in processes)
                process.Dispose();

            return processes.Length > 0;
        }

        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    }
}
