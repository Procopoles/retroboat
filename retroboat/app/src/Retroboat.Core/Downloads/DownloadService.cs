using System;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Settings;
using Retroboat.Transfers;

namespace Retroboat.Downloads
{
    public enum DownloadPhase
    {
        /// <summary>Lendo o jogo na nuvem; o progresso ainda não começou.</summary>
        Preparing,
        Downloading,
        /// <summary>Sem o Google Drive agora: continua sozinho quando ele voltar.</summary>
        WaitingForCloud,
        Finished,
        Failed
    }

    public sealed class DownloadStatus
    {
        public DownloadItem Item { get; set; }
        public DownloadPhase Phase { get; set; }
        public long BytesDone { get; set; }
        public long BytesTotal { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan? Remaining { get; set; }

        /// <summary>Quantos jogos esperam a vez depois deste.</summary>
        public int Waiting { get; set; }

        /// <summary>Por que não deu certo (<see cref="DownloadPhase.Failed"/>).</summary>
        public string Message { get; set; }

        public double Fraction => BytesTotal > 0 ? Math.Min(1, (double)BytesDone / BytesTotal) : 0;
    }

    /// <summary>
    /// Baixa a fila, um jogo por vez, sem interface. Quem mostra o andamento assina os eventos, que chegam na
    /// thread do serviço. Termina quando a fila esvazia; parado por <c>stop</c> (o ES fechou), o jogo atual
    /// continua na fila para a próxima abertura.
    /// </summary>
    public sealed class DownloadService
    {
        const int ProgressEveryMs = 1000;

        readonly DownloadQueue _queue;
        readonly Func<DownloadItem, IProgress<TransferProgress>, CancellationToken, OnDemandResult> _install;
        readonly Func<bool> _cloudAvailable;
        readonly TimeSpan _checkEvery;
        volatile int _waiting;

        public DownloadService(
            DownloadQueue queue,
            Func<DownloadItem, IProgress<TransferProgress>, CancellationToken, OnDemandResult> install,
            Func<bool> cloudAvailable,
            TimeSpan? checkEvery = null)
        {
            _queue = queue;
            _install = install;
            _cloudAvailable = cloudAvailable;
            _checkEvery = checkEvery ?? TimeSpan.FromMilliseconds(500);
        }

        /// <summary>
        /// Pausa antes de olhar o Drive de novo quando ele sumiu. Ao reiniciar, o Google Drive aborta a leitura
        /// meio segundo antes de desmontar o disco: sem a pausa, a nova tentativa pegaria o disco saindo.
        /// </summary>
        public TimeSpan RetryPause { get; set; } = TimeSpan.Zero;

        /// <summary>O andamento do jogo atual mudou.</summary>
        public event Action<DownloadStatus> Changed;

        /// <summary>Um jogo terminou: <see cref="DownloadPhase.Finished"/> ou <see cref="DownloadPhase.Failed"/>.</summary>
        public event Action<DownloadStatus> Completed;

        /// <summary>O jogo atual saiu da fila a pedido do usuário.</summary>
        public event Action<DownloadItem> Cancelled;

        /// <summary>Instalação de verdade: cada jogo lê o índice de novo, porque a nuvem pode ter sincronizado no meio.</summary>
        public static DownloadService ForInstall(RetroBatInstall install, DownloadQueue queue)
        {
            var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
            var service = new DownloadService(
                queue,
                (item, progress, token) =>
                {
                    var installer = new OnDemandInstaller(install);
                    var entry = installer.FindEntry(item.System, item.CloudName);
                    return entry != null
                        ? installer.Install(entry, progress, token)
                        : new OnDemandResult(OnDemandOutcome.Failed, "Este jogo não está mais na nuvem (" + item.Key + ").");
                },
                () => CloudLocator.IsAvailable(settings));
            service.RetryPause = TimeSpan.FromSeconds(5);
            return service;
        }

        /// <summary>Tira da fila o que não está mais pendente: só um marcador vazio ainda precisa de download.</summary>
        public static void Reconcile(RetroBatInstall install, DownloadQueue queue)
        {
            var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
            var romsRoot = string.IsNullOrEmpty(settings.RomsPath) ? install.DefaultRomsPath : settings.RomsPath;
            queue.Reconcile(item =>
            {
                var local = new FileInfo(Path.Combine(romsRoot, item.System, item.LocalName));
                return local.Exists && local.Length == 0;
            });
        }

        public void Run(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                var item = _queue.Items().FirstOrDefault();
                if (item == null)
                    return;

                RunOne(item, stop);
            }
        }

        void RunOne(DownloadItem item, CancellationToken stop)
        {
            _waiting = Math.Max(0, _queue.Items().Count - 1);

            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop))
            using (new Timer(_ => Watch(item, cancel), null, _checkEvery, _checkEvery))
            {
                var progress = new ItemProgress(this, item);
                var result = InstallUntilDone(item, progress, cancel.Token);

                if (result.Outcome == OnDemandOutcome.Cancelled)
                {
                    if (stop.IsCancellationRequested)
                        return; // O ES fechou: continua na próxima abertura.

                    _queue.Remove(item.Key);
                    Log.Info("Download cancelado: " + item.Key);
                    Cancelled?.Invoke(item);
                    return;
                }

                _queue.Remove(item.Key);
                _waiting = Math.Max(0, _queue.Items().Count);

                bool installed = result.Outcome == OnDemandOutcome.Installed;
                if (installed)
                    Log.Info("Download concluído: " + item.Key);
                else
                    Log.Warn("Download não concluído: " + item.Key + " (" + result.Message + ")");

                Completed?.Invoke(new DownloadStatus
                {
                    Item = item,
                    Phase = installed ? DownloadPhase.Finished : DownloadPhase.Failed,
                    BytesDone = progress.LastDone,
                    BytesTotal = progress.LastTotal,
                    Waiting = _waiting,
                    Message = result.Message
                });
            }
        }

        /// <summary>Sem o Drive, espera ele voltar e tenta de novo: o usuário não precisa apertar nada.</summary>
        OnDemandResult InstallUntilDone(DownloadItem item, IProgress<TransferProgress> progress, CancellationToken cancel)
        {
            while (true)
            {
                Publish(new DownloadStatus { Item = item, Phase = DownloadPhase.Preparing });

                OnDemandResult result;
                try
                {
                    result = _install(item, progress, cancel);
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    result = new OnDemandResult(OnDemandOutcome.Cancelled);
                }
                catch (OperationCanceledException)
                {
                    result = Interrupted();
                }
                catch (Exception ex)
                {
                    Log.Error("Falha ao baixar " + item.Key, ex);
                    result = new OnDemandResult(OnDemandOutcome.Failed, "Erro inesperado: " + ex.Message);
                }

                // Cancelado é só quando o jogo saiu da fila ou o ES fechou, que é quando o token dispara. Qualquer
                // outro "cancelado" veio do sistema e não pode tirar o jogo da fila.
                if (result.Outcome == OnDemandOutcome.Cancelled && !cancel.IsCancellationRequested)
                    result = Interrupted();

                if (result.Outcome != OnDemandOutcome.CloudUnavailable)
                    return result;

                Log.Info("Google Drive indisponível" + (string.IsNullOrEmpty(result.Message) ? "" : " (" + result.Message + ")") + "; aguardando para continuar: " + item.Key);
                Publish(new DownloadStatus { Item = item, Phase = DownloadPhase.WaitingForCloud });

                if (RetryPause > TimeSpan.Zero && cancel.WaitHandle.WaitOne(RetryPause))
                    return new OnDemandResult(OnDemandOutcome.Cancelled);

                while (!_cloudAvailable())
                {
                    if (cancel.WaitHandle.WaitOne(_checkEvery))
                        return new OnDemandResult(OnDemandOutcome.Cancelled);
                }

                if (cancel.IsCancellationRequested)
                    return new OnDemandResult(OnDemandOutcome.Cancelled);
            }
        }

        static OnDemandResult Interrupted() => new OnDemandResult(OnDemandOutcome.CloudUnavailable, "a leitura do Google Drive foi interrompida");

        void Watch(DownloadItem item, CancellationTokenSource cancel)
        {
            try
            {
                var items = _queue.Items();
                _waiting = Math.Max(0, items.Count - 1);
                if (!items.Any(i => string.Equals(i.Key, item.Key, StringComparison.OrdinalIgnoreCase)))
                    cancel.Cancel();
            }
            catch (ObjectDisposedException) { } // O jogo acabou enquanto o relógio disparava.
            catch (IOException) { }
        }

        void Publish(DownloadStatus status)
        {
            status.Waiting = _waiting;
            Changed?.Invoke(status);
        }

        sealed class ItemProgress : IProgress<TransferProgress>
        {
            readonly DownloadService _service;
            readonly DownloadItem _item;
            int _lastWrite = Environment.TickCount - ProgressEveryMs;

            public ItemProgress(DownloadService service, DownloadItem item)
            {
                _service = service;
                _item = item;
            }

            public long LastDone { get; private set; }
            public long LastTotal { get; private set; }

            public void Report(TransferProgress value)
            {
                LastDone = value.BytesDone;
                LastTotal = value.BytesTotal;

                if (Environment.TickCount - _lastWrite >= ProgressEveryMs)
                {
                    _lastWrite = Environment.TickCount;
                    _service._queue.WriteProgress(_item, value.BytesDone, value.BytesTotal);
                }

                _service.Publish(new DownloadStatus
                {
                    Item = _item,
                    Phase = DownloadPhase.Downloading,
                    BytesDone = value.BytesDone,
                    BytesTotal = value.BytesTotal,
                    BytesPerSecond = value.BytesPerSecond,
                    Remaining = value.Remaining
                });
            }
        }
    }
}
