using System;
using System.Threading;
using Retroboat.Es;
using Retroboat.Integrations;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Launcher
{
    /// <summary>
    /// RB-037: verifica as integrações em segundo plano, sem atrasar a abertura do RetroBat: na abertura e, com o ES aberto,
    /// de tempos em tempos (pela volta do <see cref="CloudWatcher"/>). Uma chave que parou é trocada sozinha
    /// (<see cref="IntegrationMonitor"/>); o ES só ganha um aviso quando algo mudou: chave trocada, ou integração que parou
    /// sem chave nova. O resto fica na tela INTEGRAÇÕES do menu NUVEM.
    /// </summary>
    static class IntegrationWatch
    {
        /// <summary>Com o ES aberto por horas, verifica de novo depois disso.</summary>
        static readonly TimeSpan Every = TimeSpan.FromHours(6);

        /// <summary>O aviso espera o ES ficar livre (sem jogo nem scraper) até isso; depois, só a tela mostra.</summary>
        static readonly TimeSpan NotifyWithin = TimeSpan.FromMinutes(15);

        static DateTime _lastStart = DateTime.MinValue;
        static int _running;

        /// <summary>Na abertura: verifica (se a última não foi há menos de uma hora) numa thread de fundo.</summary>
        public static void StartInBackground(RetroBatInstall install) => Start(install);

        /// <summary>A cada volta do CloudWatcher: dispara a verificação quando já passou o intervalo.</summary>
        public static void Tick(RetroBatInstall install)
        {
            if (DateTime.UtcNow - _lastStart >= Every)
                Start(install);
        }

        static void Start(RetroBatInstall install)
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                return;

            _lastStart = DateTime.UtcNow;
            new Thread(() =>
            {
                try
                {
                    Run(install);
                }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                }
            }) { IsBackground = true, Name = "Retroboat integrações" }.Start();
        }

        static void Run(RetroBatInstall install)
        {
            IntegrationReport report;
            try
            {
                report = new IntegrationMonitor(install).Run();
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao verificar as integrações", ex);
                return;
            }

            foreach (var line in report.Log)
                Log.Info("Integrações: " + line);

            if (report.Messages.Count == 0)
                return;

            foreach (var message in report.Messages)
                Log.Warn("Integrações: " + message);

            var waitUntil = DateTime.UtcNow + NotifyWithin;
            while (DateTime.UtcNow < waitUntil)
            {
                if (EsApi.IsRunning() && EsApi.IsIdle())
                {
                    EsApi.Notify(string.Join("\n", report.Messages));
                    return;
                }

                Thread.Sleep(TimeSpan.FromSeconds(10));
            }
        }
    }
}
