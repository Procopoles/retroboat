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

namespace Retroboat.Emulators
{
    /// <summary>
    /// O que o repositório respondeu, guardado em &lt;RetroBat&gt;\retroboat\estado\emuladores-servidor.xml por algumas horas:
    /// abrir um jogo não espera a internet toda vez. Sem conexão, vale a última resposta, mesmo velha. Não lança exceção: um
    /// problema fica em <see cref="Problem"/> (e <see cref="FormatChanged"/>, para a verificação de compatibilidade).
    /// </summary>
    public sealed class RepositoryCache
    {
        public const string FileName = "emuladores-servidor.xml";

        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

        /// <summary>Sem conexão, as próximas consultas esperam isto antes de tentar de novo: abrir um jogo não espera o timeout toda vez.</summary>
        public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(10);

        const int TimeoutMs = 4000;

        static readonly object Sync = new object();

        readonly string _path;
        readonly Func<DateTime> _now;

        public RepositoryCache(RetroBatInstall install, IPackageSource source, Func<DateTime> now = null)
        {
            _path = Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, FileName);
            Source = source;
            _now = now ?? (() => DateTime.UtcNow);
        }

        public IPackageSource Source { get; }

        /// <summary>Por que a última consulta falhou (em português), ou nulo.</summary>
        public string Problem { get; private set; }

        /// <summary>O servidor respondeu num formato que o Retroboat não conhece.</summary>
        public bool FormatChanged { get; private set; }

        /// <summary>Nome do pacote → versão. Nulo se nunca deu para ler.</summary>
        public IReadOnlyDictionary<string, string> Versions()
        {
            var doc = Load();
            var cached = doc.Root.Element("versions");
            if (cached != null && IsFresh(cached))
                return ReadVersions(cached);

            if (RecentlyOffline(doc))
                return cached != null ? ReadVersions(cached) : null;

            try
            {
                var versions = Source.ReadVersions(TimeoutMs);
                Save(d =>
                {
                    d.Root.Element("offline")?.Remove();
                    d.Root.Element("versions")?.Remove();
                    d.Root.Add(new XElement("versions", new XAttribute("fetched", RetroBatRepository.Iso(_now())),
                        versions.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(v => new XElement("v", new XAttribute("name", v.Key), new XAttribute("version", v.Value)))));
                });
                return versions;
            }
            catch (RepositoryException ex)
            {
                Failed(ex);
                return cached != null ? ReadVersions(cached) : null;
            }
        }

        /// <summary>Tamanho e data do pacote no servidor. Nulo se nunca deu para consultar.</summary>
        public RemoteFile Probe(EmulatorPackage package)
        {
            var doc = Load();
            var cached = doc.Root.Elements("file").FirstOrDefault(f => (string)f.Attribute("key") == package.Key);
            if (cached != null && IsFresh(cached))
                return ReadFile(cached);

            if (RecentlyOffline(doc))
                return cached != null ? ReadFile(cached) : null;

            try
            {
                var remote = Source.Probe(package, TimeoutMs);
                Save(d =>
                {
                    d.Root.Element("offline")?.Remove();
                    d.Root.Elements("file").Where(f => (string)f.Attribute("key") == package.Key).Remove();
                    d.Root.Add(new XElement("file",
                        new XAttribute("key", package.Key),
                        new XAttribute("fetched", RetroBatRepository.Iso(_now())),
                        new XAttribute("exists", remote.Exists ? "1" : "0"),
                        new XAttribute("length", remote.Length.ToString(CultureInfo.InvariantCulture)),
                        remote.ModifiedUtc.HasValue ? new XAttribute("modified", RetroBatRepository.Iso(remote.ModifiedUtc.Value)) : null));
                });
                return remote;
            }
            catch (RepositoryException ex)
            {
                Failed(ex);
                return cached != null ? ReadFile(cached) : null;
            }
        }

        /// <summary>"Tentar de novo": a próxima consulta vai ao servidor mesmo logo depois de uma falha.</summary>
        public void RetryNow() => Save(d => d.Root.Element("offline")?.Remove());

        /// <summary>Uma consulta falhou há pouco por falta de conexão: não tenta de novo ainda (o motivo vira o Problem).</summary>
        bool RecentlyOffline(XDocument doc)
        {
            var offline = doc.Root.Element("offline");
            var since = ParseDate((string)offline?.Attribute("since"));
            if (!since.HasValue || _now() - since.Value >= RetryAfterFailure || since.Value > _now())
                return false;

            Problem = (string)offline.Attribute("problem");
            return true;
        }

        void Failed(RepositoryException ex)
        {
            Problem = ex.Message;
            FormatChanged |= ex.FormatChanged;
            if (!ex.FormatChanged)
            {
                Save(d =>
                {
                    d.Root.Element("offline")?.Remove();
                    d.Root.Add(new XElement("offline", new XAttribute("since", RetroBatRepository.Iso(_now())), new XAttribute("problem", ex.Message)));
                });
            }
        }

        /// <summary>Depois de instalar: a próxima consulta vai ao servidor de novo.</summary>
        public void Forget(EmulatorPackage package)
        {
            Save(d => d.Root.Elements("file").Where(f => (string)f.Attribute("key") == package.Key).Remove());
        }

        bool IsFresh(XElement element)
        {
            var fetched = ParseDate((string)element.Attribute("fetched"));
            return fetched.HasValue && _now() - fetched.Value < MaxAge && fetched.Value <= _now();
        }

        static IReadOnlyDictionary<string, string> ReadVersions(XElement element) =>
            element.Elements("v").Where(v => v.Attribute("name") != null && v.Attribute("version") != null)
                .GroupBy(v => (string)v.Attribute("name"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (string)g.First().Attribute("version"), StringComparer.OrdinalIgnoreCase);

        static RemoteFile ReadFile(XElement element) => new RemoteFile
        {
            Exists = (string)element.Attribute("exists") == "1",
            Length = long.TryParse((string)element.Attribute("length"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ? length : 0,
            ModifiedUtc = ParseDate((string)element.Attribute("modified"))
        };

        internal static DateTime? ParseDate(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value) ? value : (DateTime?)null;

        XDocument Load()
        {
            lock (Sync)
            {
                try
                {
                    if (File.Exists(_path))
                        return XDocument.Load(_path);
                }
                catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Só um cache: ilegível, consulta de novo.
                }

                return new XDocument(new XElement("repository"));
            }
        }

        void Save(Action<XDocument> change)
        {
            lock (Sync)
            {
                using (var mutex = new Mutex(false, @"Local\RetroboatEmulatorServerCache"))
                {
                    try { mutex.WaitOne(); }
                    catch (AbandonedMutexException) { }

                    try
                    {
                        var doc = Load();
                        change(doc);
                        Directory.CreateDirectory(Path.GetDirectoryName(_path));
                        var temp = _path + ".retroboat-part";
                        doc.Save(temp);
                        if (File.Exists(_path))
                            File.Delete(_path);
                        File.Move(temp, _path);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // Só um cache: fica para a próxima consulta.
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }
    }
}
