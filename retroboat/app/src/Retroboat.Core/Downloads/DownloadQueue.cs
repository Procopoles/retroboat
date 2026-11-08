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

namespace Retroboat.Downloads
{
    public sealed class DownloadItem
    {
        public string System { get; set; }
        public string CloudName { get; set; }
        public string LocalName { get; set; }

        /// <summary>Nome do jogo para mostrar na tela.</summary>
        public string Title { get; set; }

        public DateTime AddedUtc { get; set; }

        public string Key => System + "/" + CloudName;
    }

    /// <summary>
    /// Fila de downloads, compartilhada entre o retroboat-launch que recebe o BAIXAR e o serviço que baixa.
    /// Fica em &lt;RetroBat&gt;\retroboat\estado\downloads.xml, então um download interrompido continua na
    /// próxima abertura. Tirar um jogo da fila é o pedido para cancelar: o serviço percebe e para.
    ///
    /// O tema só sabe perguntar se um arquivo existe. Cada jogo da fila tem um arquivo
    /// estado\baixando\&lt;nome do arquivo do jogo&gt;, que o status do tema usa para mostrar BAIXANDO.
    /// </summary>
    public sealed class DownloadQueue
    {
        public const string FileName = "downloads.xml";
        public const string MarkersFolder = "baixando";

        const string MutexName = @"Local\RetroboatDownloadQueue";

        readonly string _path;
        readonly string _markers;

        public DownloadQueue(RetroBatInstall install)
        {
            var folder = Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder);
            _path = Path.Combine(folder, FileName);
            _markers = Path.Combine(folder, MarkersFolder);
        }

        /// <summary>Na ordem em que entraram; o primeiro é o que está baixando.</summary>
        public IReadOnlyList<DownloadItem> Items() => Locked(Read);

        public bool IsEmpty => Items().Count == 0;

        public DownloadItem Find(string key) => Items().FirstOrDefault(i => SameKey(i.Key, key));

        public bool Contains(string key) => Find(key) != null;

        /// <returns>false se o jogo já estava na fila.</returns>
        public bool Add(DownloadItem item)
        {
            return Locked(() =>
            {
                var items = Read();
                if (items.Any(i => SameKey(i.Key, item.Key)))
                    return false;

                item.AddedUtc = item.AddedUtc == default ? DateTime.UtcNow : item.AddedUtc;
                items.Add(item);
                Write(items);

                Directory.CreateDirectory(_markers);
                File.WriteAllText(MarkerPath(item), "");
                return true;
            });
        }

        /// <returns>false se o jogo não estava na fila.</returns>
        public bool Remove(string key)
        {
            return Locked(() =>
            {
                var items = Read();
                var item = items.FirstOrDefault(i => SameKey(i.Key, key));
                if (item == null)
                    return false;

                items.Remove(item);
                Write(items);

                // Dois sistemas podem ter um arquivo de mesmo nome; o tema só vê o nome.
                if (!items.Any(i => string.Equals(i.LocalName, item.LocalName, StringComparison.OrdinalIgnoreCase)))
                    DeleteQuietly(MarkerPath(item));

                return true;
            });
        }

        /// <summary>
        /// Põe a fila em dia depois de uma interrupção: tira o que não está mais pendente (o jogo foi instalado
        /// por outro caminho) e apaga marcadores sem jogo na fila.
        /// </summary>
        public void Reconcile(Func<DownloadItem, bool> stillPending)
        {
            Locked(() =>
            {
                var items = Read();
                var kept = items.Where(stillPending).ToList();
                if (kept.Count != items.Count)
                    Write(kept);

                if (Directory.Exists(_markers))
                {
                    var names = new HashSet<string>(kept.Select(i => i.LocalName), StringComparer.OrdinalIgnoreCase);
                    foreach (var marker in Directory.GetFiles(_markers).Where(m => !names.Contains(Path.GetFileName(m))))
                        DeleteQuietly(marker);
                }

                foreach (var item in kept.Where(i => !File.Exists(MarkerPath(i))))
                {
                    Directory.CreateDirectory(_markers);
                    File.WriteAllText(MarkerPath(item), "");
                }

                return true;
            });
        }

        /// <summary>Quanto já foi baixado, para a tela de cancelar: fica no marcador do jogo.</summary>
        public void WriteProgress(DownloadItem item, long done, long total)
        {
            try
            {
                File.WriteAllText(MarkerPath(item), done.ToString(CultureInfo.InvariantCulture) + " " + total.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException) { } // Só informativo: alguém está lendo, ou o jogo acabou de sair da fila.
            catch (UnauthorizedAccessException) { }
        }

        public bool TryReadProgress(DownloadItem item, out long done, out long total)
        {
            done = total = 0;
            try
            {
                var parts = File.ReadAllText(MarkerPath(item)).Split(' ');
                return parts.Length == 2 &&
                       long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out done) &&
                       long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out total);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        string MarkerPath(DownloadItem item) => Path.Combine(_markers, item.LocalName);

        static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        List<DownloadItem> Read()
        {
            if (!File.Exists(_path))
                return new List<DownloadItem>();

            try
            {
                return XDocument.Load(_path).Root.Elements("game").Select(e => new DownloadItem
                {
                    System = (string)e.Attribute("system"),
                    CloudName = (string)e.Attribute("cloud"),
                    LocalName = (string)e.Attribute("local"),
                    Title = (string)e.Attribute("title"),
                    AddedUtc = DateTime.Parse((string)e.Attribute("added"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                }).ToList();
            }
            catch (Exception ex) when (ex is XmlException || ex is IOException || ex is FormatException || ex is ArgumentNullException)
            {
                // Uma fila ilegível só faz o usuário apertar BAIXAR de novo; não pode travar nada.
                Log.Warn("Fila de downloads ilegível, começando vazia: " + ex.Message);
                return new List<DownloadItem>();
            }
        }

        void Write(List<DownloadItem> items)
        {
            if (items.Count == 0)
            {
                DeleteQuietly(_path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".retroboat-part";
            new XDocument(new XElement("downloads", items.Select(i => new XElement("game",
                new XAttribute("system", i.System),
                new XAttribute("cloud", i.CloudName),
                new XAttribute("local", i.LocalName),
                new XAttribute("title", i.Title ?? ""),
                new XAttribute("added", i.AddedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)))))).Save(temp);

            if (File.Exists(_path))
                File.Delete(_path);

            File.Move(temp, _path);
        }

        static void DeleteQuietly(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

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
