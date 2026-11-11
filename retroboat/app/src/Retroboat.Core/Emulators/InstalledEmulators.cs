using System;
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
    /// <summary>Um pacote que o Retroboat instalou (ou adotou, se já estava aqui).</summary>
    public sealed class InstalledRecord
    {
        public string Key { get; set; }

        /// <summary>A versão que o repositório anunciava ao instalar; vazia para cores (o repositório não dá versão).</summary>
        public string Version { get; set; }

        public long Length { get; set; }

        /// <summary>Data do pacote no servidor (Last-Modified): é o que diz se um core mudou.</summary>
        public DateTime? ServerModifiedUtc { get; set; }

        public DateTime InstalledUtc { get; set; }

        /// <summary>Já estava no PC quando o Retroboat passou a cuidar dos emuladores (baixado pelo RetroBat).</summary>
        public bool Adopted { get; set; }
    }

    /// <summary>
    /// Registro do que o Retroboat instalou, em &lt;RetroBat&gt;\retroboat\estado\emuladores-instalados.xml. Serve para saber se
    /// o repositório tem algo mais novo: a versão anunciada (emuladores) ou a data do pacote (cores).
    /// </summary>
    public sealed class InstalledEmulators
    {
        public const string FileName = "emuladores-instalados.xml";

        readonly string _path;

        public InstalledEmulators(RetroBatInstall install)
        {
            _path = Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, FileName);
        }

        public InstalledRecord Find(EmulatorPackage package)
        {
            var element = Load().Root.Elements("package").FirstOrDefault(e => (string)e.Attribute("key") == package.Key);
            if (element == null)
                return null;

            return new InstalledRecord
            {
                Key = package.Key,
                Version = (string)element.Attribute("version") ?? "",
                Length = long.TryParse((string)element.Attribute("length"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ? length : 0,
                ServerModifiedUtc = RepositoryCache.ParseDate((string)element.Attribute("modified")),
                InstalledUtc = RepositoryCache.ParseDate((string)element.Attribute("installed")) ?? DateTime.MinValue,
                Adopted = (string)element.Attribute("adopted") == "1"
            };
        }

        public void Save(InstalledRecord record)
        {
            Locked(() =>
            {
                var doc = Load();
                doc.Root.Elements("package").Where(e => (string)e.Attribute("key") == record.Key).Remove();
                doc.Root.Add(new XElement("package",
                    new XAttribute("key", record.Key),
                    new XAttribute("version", record.Version ?? ""),
                    new XAttribute("length", record.Length.ToString(CultureInfo.InvariantCulture)),
                    record.ServerModifiedUtc.HasValue ? new XAttribute("modified", RetroBatRepository.Iso(record.ServerModifiedUtc.Value)) : null,
                    new XAttribute("installed", RetroBatRepository.Iso(record.InstalledUtc == default ? DateTime.UtcNow : record.InstalledUtc)),
                    record.Adopted ? new XAttribute("adopted", "1") : null));

                var ordered = doc.Root.Elements("package").OrderBy(e => (string)e.Attribute("key"), StringComparer.Ordinal).ToList();
                doc.Root.RemoveNodes();
                doc.Root.Add(new XComment(" Pacotes de emulador que o Retroboat instalou (RB-033). "), ordered);

                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var temp = _path + ".retroboat-part";
                doc.Save(temp);
                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(temp, _path);
            });
        }

        XDocument Load()
        {
            try
            {
                if (File.Exists(_path))
                    return XDocument.Load(_path);
            }
            catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // Sem o registro, o pior caso é atualizar um emulador uma vez a mais.
            }

            return new XDocument(new XElement("installed"));
        }

        static void Locked(Action action)
        {
            using (var mutex = new Mutex(false, @"Local\RetroboatInstalledEmulators"))
            {
                try { mutex.WaitOne(); }
                catch (AbandonedMutexException) { }

                try { action(); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
