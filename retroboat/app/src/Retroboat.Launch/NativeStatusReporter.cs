using System;
using System.Collections.Generic;
using System.Threading;
using Retroboat.Downloads;
using Retroboat.Es;

namespace Retroboat.Launch
{
    /// <summary>Quem mostra o andamento: a janela por cima do ES oficial (<see cref="HeaderBar"/>) ou o ES do Retroboat.</summary>
    interface IDownloadDisplay : IDisposable
    {
        void SetLive(DownloadStatus status);
        void AddNotice(DownloadStatus status);

        /// <summary>Não há nada para mostrar nem por enviar: o serviço já pode fechar.</summary>
        bool IsIdle { get; }

        /// <summary>O ES está ocupado (jogo, scraper).</summary>
        bool EsBusy { get; set; }
    }

    /// <summary>
    /// Com o ES do Retroboat (RB-010), o andamento vai para o próprio ES, que desenha a região do cabeçalho, o status
    /// do jogo e a notificação de falha: nada de janela por cima. Os envios saem numa thread própria, para um ES lento
    /// nunca segurar o download. O andamento fica só no último (no máximo 4 por segundo), os fins de jogo vão todos e
    /// em ordem, e um sinal a cada 2 s diz ao ES que o serviço continua aberto.
    /// </summary>
    sealed class NativeStatusReporter : IDownloadDisplay
    {
        const int MinGapMs = 250;
        const int HeartbeatMs = 2000;

        readonly object _lock = new object();
        readonly Queue<string> _notices = new Queue<string>();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly Thread _thread;

        string _live = DownloadStatusJson.Of(null);
        bool _liveActive;
        bool _liveChanged;
        volatile bool _stopping;

        public NativeStatusReporter()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Andamento para o ES" };
            _thread.Start();
        }

        /// <summary>O ES do Retroboat cuida sozinho do canto que divide com o scraper.</summary>
        public bool EsBusy { get; set; }

        public bool IsIdle
        {
            get { lock (_lock) return _notices.Count == 0 && !_liveChanged; }
        }

        public void SetLive(DownloadStatus status)
        {
            lock (_lock)
            {
                _live = DownloadStatusJson.Of(status);
                _liveActive = status != null;
                _liveChanged = true;
            }

            _wake.Set();
        }

        public void AddNotice(DownloadStatus status)
        {
            lock (_lock)
                _notices.Enqueue(DownloadStatusJson.Of(status));

            _wake.Set();
        }

        void Run()
        {
            int lastSent = Environment.TickCount - HeartbeatMs;
            while (!_stopping)
            {
                _wake.WaitOne(HeartbeatMs);

                int gap = MinGapMs - (Environment.TickCount - lastSent);
                if (gap > 0 && !_stopping)
                    Thread.Sleep(gap);

                foreach (var json in TakePending(Environment.TickCount - lastSent >= HeartbeatMs))
                {
                    EsApi.PostDownloadStatus(json);
                    lastSent = Environment.TickCount;
                }
            }

            // O último estado (idle, posto pelo Dispose) sempre chega: o ES tira a barra e relê a fila.
            foreach (var json in TakePending(false))
                EsApi.PostDownloadStatus(json);
        }

        List<string> TakePending(bool heartbeatDue)
        {
            lock (_lock)
            {
                var pending = new List<string>(_notices);
                _notices.Clear();

                if (_liveChanged || (heartbeatDue && _liveActive))
                    pending.Add(_live);

                _liveChanged = false;
                return pending;
            }
        }

        public void Dispose()
        {
            SetLive(null);
            _stopping = true;
            _wake.Set();
            _thread.Join(TimeSpan.FromSeconds(5));
            _wake.Dispose();
        }
    }
}
