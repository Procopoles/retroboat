using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Retroboat.Saves
{
    /// <summary>
    /// Acompanha os saves enquanto o emulador roda: cada mudança vai para a nuvem poucos segundos depois que
    /// o emulador para de gravar, então uma queda de energia não perde o save. Nada local é trocado durante o
    /// jogo; o que vier de outro PC espera o jogo fechar. Também mantém a trava ("em uso neste PC") renovada.
    ///
    /// Quem chama decide o ritmo: <see cref="Poll"/> a cada ~2 s e <see cref="Finish"/> quando o emulador fecha.
    /// </summary>
    public sealed class SaveSession
    {
        public static readonly TimeSpan QuietTime = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan RetryAfterError = TimeSpan.FromSeconds(30);

        // Conteúdo "igual ao da nuvem no início do jogo", sem ter lido o arquivo.
        const string InSyncAtStart = "";

        readonly SaveSync _sync;
        readonly Func<IEnumerable<string>> _activeItems;
        readonly Func<DateTime> _utcNow;
        readonly Dictionary<string, Tracker> _trackers = new Dictionary<string, Tracker>(StringComparer.OrdinalIgnoreCase);
        DateTime _lastHeartbeat;
        bool _started;

        /// <param name="activeItems">Arquivos que o emulador está usando agora (ex.: os slots do PCSX2.ini).
        /// Esses são conferidos pelo conteúdo a cada passada; os outros, pelo tamanho e pela data.</param>
        public SaveSession(SaveSync sync, Func<IEnumerable<string>> activeItems = null, Func<DateTime> utcNow = null)
        {
            _sync = sync;
            _activeItems = activeItems;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public SaveSync Sync => _sync;

        /// <summary>Chamado depois da sincronização de antes do jogo, com tudo em dia: trava e anota o estado.</summary>
        public void Start(string game)
        {
            _sync.Lock(game);
            _lastHeartbeat = _utcNow();

            // Tudo acabou de ser sincronizado: não precisa ler cada cartão (no modo "um cartão por jogo" podem ser dezenas).
            foreach (var path in _sync.LocalItems())
            {
                var info = new FileInfo(path);
                _trackers[info.Name] = new Tracker { Length = info.Length, Modified = info.LastWriteTimeUtc, Sha1 = InSyncAtStart, Pushed = InSyncAtStart, Existed = true };
            }

            _started = true;
        }

        public SaveSyncResult Poll() => Run(final: false);

        /// <summary>O emulador fechou: envia o que faltar (sem esperar) e libera a trava.</summary>
        public SaveSyncResult Finish()
        {
            var result = Run(final: true);
            _sync.Unlock();
            return result;
        }

        SaveSyncResult Run(bool final)
        {
            var result = new SaveSyncResult();
            if (!_started)
                return result;

            var now = _utcNow();
            var active = new HashSet<string>(SafeActiveItems(), StringComparer.OrdinalIgnoreCase);
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in _sync.LocalItems())
            {
                var info = new FileInfo(path);
                present.Add(info.Name);
                if (!_trackers.TryGetValue(info.Name, out var tracker))
                {
                    // Cartão novo, criado pelo emulador durante o jogo.
                    _trackers[info.Name] = tracker = new Tracker { ChangedAt = now };
                }

                tracker.Existed = true;
                tracker.Missing = false;

                // A data basta depois que o emulador fecha o arquivo; o cartão do slot é lido sempre.
                bool metadataChanged = info.Length != tracker.Length || info.LastWriteTimeUtc != tracker.Modified;
                if (metadataChanged || active.Contains(info.Name) || tracker.Sha1 == null)
                {
                    string sha;
                    try { sha = Hash(path); }
                    catch (IOException) { continue; } // Sendo regravado agora; fica para a próxima passada.

                    tracker.Length = info.Length;
                    tracker.Modified = info.LastWriteTimeUtc;
                    if (sha != tracker.Sha1)
                    {
                        tracker.Sha1 = sha;
                        tracker.ChangedAt = now;
                    }
                }
            }

            foreach (var item in _trackers.Where(t => t.Value.Existed && !present.Contains(t.Key)))
            {
                if (!item.Value.Missing)
                {
                    item.Value.Missing = true;
                    item.Value.Sha1 = null;
                    item.Value.ChangedAt = now;
                }
            }

            // Envia o que mudou e parou de mudar (ou tudo, no fim).
            var due = _trackers.Where(t => t.Value.Sha1 != t.Value.Pushed && (final || now - t.Value.ChangedAt >= QuietTime) && (final || now >= t.Value.RetryAt))
                .Select(t => t.Key).ToList();

            if (due.Count > 0)
            {
                var sessionItems = new HashSet<string>(_trackers.Where(t => t.Value.Existed).Select(t => t.Key), StringComparer.OrdinalIgnoreCase);
                var plans = _sync.Plan(only: due, sessionItems: sessionItems, trustDates: false);
                result = _sync.Apply(plans, uploadOnly: true);

                var failed = new HashSet<string>(result.Errors.Select(e => e.Split(':')[0]), StringComparer.OrdinalIgnoreCase);
                foreach (var name in due)
                {
                    var tracker = _trackers[name];
                    if (failed.Contains(name))
                    {
                        tracker.RetryAt = now + RetryAfterError;
                        continue;
                    }

                    tracker.Pushed = tracker.Sha1;
                }
            }

            if (!final && now - _lastHeartbeat >= HeartbeatEvery)
            {
                try
                {
                    _sync.RenewLock();
                    _lastHeartbeat = now;
                }
                catch (IOException ex) { result.Errors.Add("trava: " + ex.Message); }
                catch (UnauthorizedAccessException ex) { result.Errors.Add("trava: " + ex.Message); }
            }

            return result;
        }

        IEnumerable<string> SafeActiveItems()
        {
            try { return _activeItems?.Invoke() ?? Enumerable.Empty<string>(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Enumerable.Empty<string>(); }
        }

        static string Hash(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        sealed class Tracker
        {
            public long Length;
            public DateTime Modified;

            /// <summary>Conteúdo atual (nulo se o arquivo sumiu).</summary>
            public string Sha1;

            /// <summary>Conteúdo que já está na nuvem.</summary>
            public string Pushed;

            public DateTime ChangedAt;
            public DateTime RetryAt;
            public bool Existed;
            public bool Missing;
        }
    }
}
