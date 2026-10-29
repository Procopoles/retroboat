using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Retroboat.Cloud
{
    public enum CloudGameState
    {
        /// <summary>Só na nuvem: na pasta de jogos há um marcador vazio com o nome do jogo.</summary>
        Cloud,
        Installed,
        /// <summary>O arquivo na nuvem está danificado (zerado ou CRC não confere).</summary>
        Corrupt
    }

    public sealed class CloudIndexEntry
    {
        public string System { get; set; }
        public string CloudName { get; set; }
        public string LocalName { get; set; }
        public long CloudSize { get; set; }
        public DateTime CloudModifiedUtc { get; set; }
        public CloudGameState State { get; set; }
        public string Problem { get; set; }

        /// <summary>A entrada ainda descreve o mesmo arquivo da nuvem (nada foi trocado no Drive).</summary>
        public bool Matches(CloudGame game) => CloudSize == game.Size && Math.Abs((CloudModifiedUtc - game.LastWriteTimeUtc).TotalSeconds) < 2;
    }

    /// <summary>
    /// Índice local da biblioteca na nuvem, em &lt;RetroBat&gt;\retroboat\biblioteca-nuvem.xml: o que é
    /// marcador, o que foi instalado e o que está danificado na nuvem. Fica no PC (não sincroniza).
    /// </summary>
    public sealed class CloudIndex
    {
        public const string FileName = "biblioteca-nuvem.xml";

        readonly Dictionary<string, CloudIndexEntry> _entries = new Dictionary<string, CloudIndexEntry>(StringComparer.OrdinalIgnoreCase);

        public string Path { get; private set; }

        public IEnumerable<CloudIndexEntry> Entries => _entries.Values;

        public static CloudIndex Load(string path)
        {
            var index = new CloudIndex { Path = path };
            if (!File.Exists(path))
                return index;

            foreach (var element in XDocument.Load(path).Root.Elements("game"))
            {
                var entry = new CloudIndexEntry
                {
                    System = (string)element.Attribute("system"),
                    CloudName = (string)element.Attribute("cloud"),
                    LocalName = (string)element.Attribute("local"),
                    CloudSize = (long?)element.Attribute("size") ?? 0,
                    CloudModifiedUtc = DateTime.Parse((string)element.Attribute("modified") ?? "2000-01-01T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                    State = (CloudGameState)Enum.Parse(typeof(CloudGameState), (string)element.Attribute("state") ?? "Cloud"),
                    Problem = (string)element.Attribute("problem")
                };

                index._entries[Key(entry.System, entry.CloudName)] = entry;
            }

            return index;
        }

        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var root = new XElement("cloudLibrary",
                _entries.Values.OrderBy(e => e.System).ThenBy(e => e.CloudName).Select(e => new XElement("game",
                    new XAttribute("system", e.System),
                    new XAttribute("cloud", e.CloudName),
                    new XAttribute("local", e.LocalName),
                    new XAttribute("size", e.CloudSize),
                    new XAttribute("modified", e.CloudModifiedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                    new XAttribute("state", e.State),
                    e.Problem != null ? new XAttribute("problem", e.Problem) : null)));

            new XDocument(root).Save(Path);
        }

        public CloudIndexEntry Find(string system, string cloudName) => _entries.TryGetValue(Key(system, cloudName), out var entry) ? entry : null;

        /// <summary>Procura pelo caminho local (ex.: o -rom que o ES passa ao abrir um jogo).</summary>
        public CloudIndexEntry FindByLocalPath(string romsRoot, string localPath)
        {
            var full = System.IO.Path.GetFullPath(localPath);
            return _entries.Values.FirstOrDefault(e => string.Equals(System.IO.Path.GetFullPath(System.IO.Path.Combine(romsRoot, e.System, e.LocalName)), full, StringComparison.OrdinalIgnoreCase));
        }

        public void Set(CloudIndexEntry entry) => _entries[Key(entry.System, entry.CloudName)] = entry;

        public void Remove(CloudIndexEntry entry) => _entries.Remove(Key(entry.System, entry.CloudName));

        static string Key(string system, string cloudName) => system + "/" + cloudName;
    }
}
