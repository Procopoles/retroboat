using System;
using System.IO;
using System.Threading;
using Retroboat.Library;

namespace Retroboat.Cloud
{
    /// <summary>
    /// A vez de sincronizar: a abertura, o CloudWatcher, o "Atualizar listas de jogos" do ES (outro processo) e as mídias de
    /// segundo plano (RB-041, outro processo) mexem nos mesmos gamelists, memory cards, configurações do Drive e índice. Um
    /// de cada vez.
    /// </summary>
    public static class SyncLock
    {
        public const string MutexName = @"Local\RetroboatSync";

        public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(2);

        /// <summary>Espera a vez (até <paramref name="wait"/>) e faz <paramref name="work"/>.</summary>
        /// <exception cref="TimeoutException">Outra sincronização não terminou a tempo.</exception>
        public static void Run(Action work, TimeSpan? wait = null)
        {
            var limit = wait ?? DefaultWait;
            if (!TryRun(work, limit))
                throw new TimeoutException("outra sincronização com a nuvem não terminou em " + limit.TotalMinutes + " minutos");
        }

        /// <returns>false se não conseguiu a vez em <paramref name="wait"/> (nada foi feito).</returns>
        public static bool TryRun(Action work, TimeSpan wait)
        {
            using (var mutex = new Mutex(false, MutexName))
            {
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(wait);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true; // Quem sincronizava foi encerrado à força; a vez é desta.
                }

                if (!acquired)
                    return false;

                try
                {
                    work();
                }
                finally
                {
                    mutex.ReleaseMutex();
                }

                return true;
            }
        }

        /// <summary>Outro processo (ou outra thread) está sincronizando agora.</summary>
        public static bool IsBusy() => !TryRun(() => { }, TimeSpan.Zero);

        /// <summary>
        /// Fim de uma sincronização com a nuvem. O ES relê as listas logo depois (o "Atualizar listas de jogos", o
        /// CloudWatcher): até ele terminar, a memória dele está atrás do disco, e nada deve pedir a ele para regravar um
        /// gamelist (<see cref="SinceLastSync"/>).
        /// </summary>
        public static void MarkSynced(RetroBatInstall install)
        {
            try
            {
                var path = MarkerPath(install);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch (IOException) { } // Só informativo.
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Quanto tempo faz que a última sincronização terminou; muito, se nunca houve uma.</summary>
        public static TimeSpan SinceLastSync(RetroBatInstall install)
        {
            var path = MarkerPath(install);
            return File.Exists(path) ? DateTime.UtcNow - File.GetLastWriteTimeUtc(path) : TimeSpan.MaxValue;
        }

        static string MarkerPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, "ultima-sincronizacao");
    }
}
