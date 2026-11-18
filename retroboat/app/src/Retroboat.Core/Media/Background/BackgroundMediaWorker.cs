using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Retroboat.Cloud;
using Retroboat.Es;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Media.Background
{
    public sealed class BackgroundMediaRun
    {
        /// <summary>Mídias que chegaram e entraram no gamelist.</summary>
        public int Applied { get; set; }

        /// <summary>Mídias que a fonte não tem mais.</summary>
        public int Gone { get; set; }

        /// <summary>Mídias que ficaram para mais tarde (sem internet, fonte fora do ar).</summary>
        public int Postponed { get; set; }

        public long Bytes { get; set; }

        /// <summary>Alguma mídia veio da internet: ela ainda não está na nuvem.</summary>
        public bool FromInternet { get; set; }
    }

    /// <summary>
    /// Esvazia a fila de mídias secundárias (RB-041), sem atrapalhar nada:
    /// <list type="bullet">
    /// <item>espera enquanto há uma sincronização com a nuvem (a pessoa está esperando por ela), um jogo baixando (o jogo vem
    /// primeiro) ou um jogo aberto (como a Steam, nada de download durante a partida);</item>
    /// <item>um download por vez de cada servidor (o ScreenScraper limita os pedidos simultâneos) e o Drive como um servidor
    /// só, até <see cref="Lanes"/> ao mesmo tempo; um vídeo vai direto para o disco e continua de onde parou;</item>
    /// <item>a cada poucos segundos, o que chegou entra no gamelist e, com o ES aberto e livre, aparece nele na hora, sem aviso
    /// (<see cref="EsLiveMedia"/>). Se o ES fechar antes, o que ele não tinha na memória volta ao gamelist depois que ele o
    /// regrava.</item>
    /// </list>
    /// </summary>
    public sealed class BackgroundMediaWorker
    {
        readonly RetroBatInstall _install;
        readonly BackgroundMediaQueue _queue;
        readonly BackgroundMediaFetcher _fetcher;
        readonly IEsGames _es;
        readonly Func<string> _pauseReason;
        readonly Func<bool> _esOpen;

        /// <param name="pauseReason">Por que esperar agora (nulo = pode baixar). Os testes trocam.</param>
        /// <param name="esOpen">O processo do ES existe (mesmo carregando, sem a API).</param>
        public BackgroundMediaWorker(RetroBatInstall install, BackgroundMediaQueue queue, BackgroundMediaFetcher fetcher, IEsGames es,
                                     Func<string> pauseReason, Func<bool> esOpen)
        {
            _install = install;
            _queue = queue;
            _fetcher = fetcher;
            _es = es;
            _pauseReason = pauseReason;
            _esOpen = esOpen;
        }

        /// <summary>Servidores (e o Drive) baixando ao mesmo tempo.</summary>
        public int Lanes { get; set; } = 3;

        /// <summary>De quanto em quanto tempo o que chegou entra no gamelist e no ES.</summary>
        public TimeSpan SettleEvery { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>Depois de uma sincronização o ES relê as listas: até lá a memória dele está atrás do disco.</summary>
        public TimeSpan WaitAfterSync { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Uma mídia que vai tentar de novo daqui a pouco é esperada; mais que isso, fica para a próxima abertura.</summary>
        public TimeSpan WaitForRetry { get; set; } = TimeSpan.FromMinutes(5);

        public BackgroundMediaRun Run(CancellationToken stop = default)
        {
            var run = new BackgroundMediaRun();
            var incoming = new ConcurrentQueue<BackgroundMediaItem>();
            var ready = new List<BackgroundMediaItem>();
            var unpushed = new List<AppliedMedia>();

            while (!stop.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var waiting = _queue.Items().Where(i => !ready.Any(r => SameItem(r, i))).ToList();
                var due = waiting.Where(i => i.NextTryUtc <= now).ToList();

                if (due.Count > 0)
                {
                    var download = Task.Run(() => Download(due, incoming, run, stop));
                    while (!download.Wait(SettleEvery))
                        Settle(incoming, ready, ref unpushed, run);

                    Settle(incoming, ready, ref unpushed, run);
                    continue;
                }

                Settle(incoming, ready, ref unpushed, run);

                bool retrySoon = waiting.Any(i => i.NextTryUtc - now <= WaitForRetry);
                if (ready.Count == 0 && unpushed.Count == 0 && !retrySoon)
                    break;

                // Esperando o ES ficar livre, a vez de mexer no gamelist ou uma nova tentativa.
                if (stop.WaitHandle.WaitOne(SettleEvery))
                    break;
            }

            Settle(incoming, ready, ref unpushed, run);
            return run;
        }

        void Download(List<BackgroundMediaItem> due, ConcurrentQueue<BackgroundMediaItem> incoming, BackgroundMediaRun run, CancellationToken stop)
        {
            var lanes = due.GroupBy(i => i.Lane, StringComparer.OrdinalIgnoreCase).ToList();
            Parallel.ForEach(lanes, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Lanes) }, lane =>
            {
                foreach (var item in lane)
                {
                    if (!WaitWhilePaused(stop))
                        return;

                    var outcome = _fetcher.Fetch(item, out var error);
                    lock (run)
                    {
                        switch (outcome)
                        {
                            case BackgroundFetchOutcome.Ready:
                                run.Bytes += SizeOf(item.Target);
                                run.FromInternet |= item.Url != null;
                                incoming.Enqueue(item);
                                break;

                            case BackgroundFetchOutcome.Gone:
                                run.Gone++;
                                _queue.Complete(item);
                                Log.Info("Mídia em segundo plano não existe mais na fonte: " + Describe(item));
                                break;

                            default:
                                run.Postponed++;
                                bool keep = _queue.Postpone(item);
                                Log.Warn("Mídia em segundo plano " + (keep ? "fica para depois" : "desistida") + ": " + Describe(item) + " (" + error + ")");
                                break;
                        }
                    }

                    // Sem internet, o resto deste servidor falharia igual: fica para a próxima rodada.
                    if (outcome == BackgroundFetchOutcome.Retry)
                        return;
                }
            });
        }

        /// <returns>false se mandaram parar.</returns>
        bool WaitWhilePaused(CancellationToken stop)
        {
            string logged = null;
            while (!stop.IsCancellationRequested)
            {
                var reason = _pauseReason();
                if (reason == null)
                    return true;

                if (reason != logged)
                {
                    Log.Info("Mídias em segundo plano esperando: " + reason + ".");
                    logged = reason;
                }

                if (stop.WaitHandle.WaitOne(SettleEvery))
                    return false;
            }

            return false;
        }

        /// <summary>O que chegou entra no gamelist; com o ES aberto e livre, nele também.</summary>
        void Settle(ConcurrentQueue<BackgroundMediaItem> incoming, List<BackgroundMediaItem> ready, ref List<AppliedMedia> unpushed, BackgroundMediaRun run)
        {
            while (incoming.TryDequeue(out var item))
                ready.Add(item);

            bool esOpen = _esOpen();
            if (ready.Count > 0)
            {
                List<AppliedMedia> applied = null;
                var batch = ready.ToList();
                if (TryWithSyncLock(() => applied = BackgroundMediaGamelists.Apply(_install, batch)))
                {
                    foreach (var done in batch)
                        _queue.Complete(done);

                    run.Applied += batch.Count;
                    ready.Clear();
                    foreach (var media in applied)
                        Log.Info("Mídia em segundo plano no jogo: " + media.System + "/" + media.GamePath + " (" + string.Join(", ", media.Fields.Keys) + ")");

                    if (esOpen)
                        unpushed.AddRange(applied);
                }
            }

            if (unpushed.Count == 0)
                return;

            if (!esOpen)
            {
                // O ES fechou antes de receber: ao sair ele regravou o gamelist com o que tinha na memória. O que ele não tinha
                // volta agora (só onde o campo ainda está como antes).
                var items = unpushed.SelectMany(a => a.Items).ToList();
                if (TryWithSyncLock(() => BackgroundMediaGamelists.Apply(_install, items)))
                    unpushed = new List<AppliedMedia>();
                return;
            }

            if (SyncLock.SinceLastSync(_install) < WaitAfterSync || !_es.IsIdle())
                return;

            var pending = unpushed;
            List<AppliedMedia> left = null;
            if (TryWithSyncLock(() => left = EsLiveMedia.Push(_install, pending, _es)))
                unpushed = left;
        }

        static bool TryWithSyncLock(Action work)
        {
            try
            {
                return SyncLock.TryRun(work, TimeSpan.FromMilliseconds(500));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                Log.Warn("Mídias em segundo plano: não foi possível atualizar o gamelist agora (" + ex.Message + ").");
                return false;
            }
        }

        static bool SameItem(BackgroundMediaItem a, BackgroundMediaItem b) =>
            string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Target, b.Target, StringComparison.OrdinalIgnoreCase);

        static long SizeOf(string file)
        {
            try { return new FileInfo(file).Length; }
            catch (IOException) { return 0; }
        }

        static string Describe(BackgroundMediaItem item) => item.System + "/" + item.Game + " " + item.Field;
    }
}
