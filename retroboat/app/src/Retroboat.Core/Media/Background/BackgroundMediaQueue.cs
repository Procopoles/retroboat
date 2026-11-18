using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Media.Background
{
    /// <summary>
    /// A fila das mídias secundárias (RB-041), em &lt;RetroBat&gt;\retroboat\estado\midias-em-segundo-plano.xml: a importação
    /// e a sincronização da nuvem põem aqui o vídeo, o manual e as imagens extras (<see cref="MediaPriority"/>) em vez de
    /// fazer a pessoa esperar por eles; o <c>Retroboat.exe --media</c> baixa em segundo plano. Fica no PC, nunca no Drive (os
    /// endereços das fontes levam as chaves). Um download interrompido continua na próxima abertura; a fila vazia apaga o
    /// arquivo, e o script de início do ES só abre o processo quando ele existe.
    /// </summary>
    public sealed class BackgroundMediaQueue
    {
        public const string FileName = "midias-em-segundo-plano.xml";

        /// <summary>Depois disso, a mídia desiste (a fonte recusa sempre); o jogo fica sem ela, como antes do RB-041.</summary>
        public const int MaxAttempts = 6;

        const string MutexName = @"Local\RetroboatBackgroundMediaQueue";

        static readonly TimeSpan[] Backoff =
        {
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(4)
        };

        readonly string _path;

        public BackgroundMediaQueue(RetroBatInstall install)
        {
            _path = PathFor(install);
        }

        public static string PathFor(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, FileName);

        /// <summary>Na ordem de baixar: imagens, vídeos e manuais; em cada um, a mais antiga primeiro.</summary>
        public IReadOnlyList<BackgroundMediaItem> Items() => Locked(() => Order(Read()));

        public bool IsEmpty => !File.Exists(_path) || Items().Count == 0;

        /// <summary>Põe as mídias na fila; a que já estava para o mesmo campo do mesmo jogo é trocada pela nova.</summary>
        /// <returns>Quantas entraram.</returns>
        public int Add(IEnumerable<BackgroundMediaItem> items)
        {
            var list = items.Where(i => i != null).ToList();
            if (list.Count == 0)
                return 0;

            return Locked(() =>
            {
                var current = Read();
                int added = 0;
                foreach (var item in list)
                {
                    var old = current.FirstOrDefault(i => SameKey(i.Key, item.Key));
                    if (old != null && SameSource(old, item))
                        continue; // Já está na fila, com as tentativas que já fez.

                    if (old != null)
                        current.Remove(old);

                    item.AddedUtc = item.AddedUtc == default ? DateTime.UtcNow : item.AddedUtc;
                    current.Add(item);
                    added++;
                }

                if (added > 0)
                    Write(current);
                return added;
            });
        }

        /// <summary>A mídia chegou (ou não existe mais na fonte): sai da fila, a não ser que outra já a tenha substituído.</summary>
        public void Complete(BackgroundMediaItem item)
        {
            Locked(() =>
            {
                var current = Read();
                if (current.RemoveAll(i => SameKey(i.Key, item.Key) && SameSource(i, item)) > 0)
                    Write(current);
                return true;
            });
        }

        /// <summary>Não deu agora: tenta de novo mais tarde, cada vez mais espaçado.</summary>
        /// <returns>false se desistiu (<see cref="MaxAttempts"/>) e a mídia saiu da fila.</returns>
        public bool Postpone(BackgroundMediaItem item, DateTime? nowUtc = null)
        {
            var now = nowUtc ?? DateTime.UtcNow;
            return Locked(() =>
            {
                var current = Read();
                var stored = current.FirstOrDefault(i => SameKey(i.Key, item.Key) && SameSource(i, item));
                if (stored == null)
                    return true; // Substituída por outra enquanto baixava.

                stored.Attempts++;
                bool keep = stored.Attempts < MaxAttempts;
                if (keep)
                    stored.NextTryUtc = now + Backoff[Math.Min(stored.Attempts, Backoff.Length) - 1];
                else
                    current.Remove(stored);

                Write(current);
                return keep;
            });
        }

        static IReadOnlyList<BackgroundMediaItem> Order(List<BackgroundMediaItem> items) =>
            items.OrderBy(i => MediaPriority.Order(i.Field)).ThenBy(i => i.AddedUtc).ToList();

        static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static bool SameSource(BackgroundMediaItem a, BackgroundMediaItem b) =>
            a.Url == b.Url && string.Equals(a.CloudFile, b.CloudFile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Target, b.Target, StringComparison.OrdinalIgnoreCase);

        List<BackgroundMediaItem> Read()
        {
            if (!File.Exists(_path))
                return new List<BackgroundMediaItem>();

            try
            {
                return XDocument.Load(_path).Root.Elements("midia").Select(e => new BackgroundMediaItem
                {
                    System = (string)e.Attribute("system"),
                    RomFolder = (string)e.Attribute("romFolder"),
                    Game = (string)e.Attribute("game"),
                    Fields = ((string)e.Attribute("fields") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                    Url = (string)e.Attribute("url"),
                    CloudFile = (string)e.Attribute("cloud"),
                    Target = (string)e.Attribute("target"),
                    Replaces = (string)e.Attribute("replaces"),
                    AddedUtc = ParseDate((string)e.Attribute("added")),
                    Attempts = (int?)e.Attribute("attempts") ?? 0,
                    NextTryUtc = ParseDate((string)e.Attribute("next"))
                }).Where(i => !string.IsNullOrEmpty(i.System) && !string.IsNullOrEmpty(i.Game) && i.Fields.Count > 0 &&
                              !string.IsNullOrEmpty(i.Target) && (i.Url != null || i.CloudFile != null)).ToList();
            }
            catch (Exception ex) when (ex is XmlException || ex is IOException || ex is FormatException)
            {
                // Uma fila ilegível só deixa estas mídias para a próxima importação ou sincronização; não pode travar nada.
                Log.Warn("Fila de mídias em segundo plano ilegível, começando vazia: " + ex.Message);
                return new List<BackgroundMediaItem>();
            }
        }

        void Write(List<BackgroundMediaItem> items)
        {
            if (items.Count == 0)
            {
                try { File.Delete(_path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".retroboat-part";
            new XDocument(new XElement("midias", Order(items).Select(i => new XElement("midia",
                new XAttribute("system", i.System),
                new XAttribute("romFolder", i.RomFolder ?? ""),
                new XAttribute("game", i.Game),
                new XAttribute("fields", string.Join(" ", i.Fields)),
                i.Url != null ? new XAttribute("url", i.Url) : null,
                i.CloudFile != null ? new XAttribute("cloud", i.CloudFile) : null,
                new XAttribute("target", i.Target),
                i.Replaces != null ? new XAttribute("replaces", i.Replaces) : null,
                new XAttribute("added", FormatDate(i.AddedUtc)),
                i.Attempts > 0 ? new XAttribute("attempts", i.Attempts) : null,
                i.NextTryUtc > DateTime.MinValue ? new XAttribute("next", FormatDate(i.NextTryUtc)) : null)))).Save(temp);

            if (File.Exists(_path))
                File.Delete(_path);
            File.Move(temp, _path);
        }

        static string FormatDate(DateTime date) => date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        static DateTime ParseDate(string text) =>
            string.IsNullOrEmpty(text) ? DateTime.MinValue : DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        static T Locked<T>(Func<T> action)
        {
            using (var mutex = new Mutex(false, MutexName))
            {
                try { mutex.WaitOne(); }
                catch (AbandonedMutexException) { } // Um processo foi encerrado à força; a vez é deste.

                try { return action(); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
