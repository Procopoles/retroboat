using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;

namespace Retroboat.Media
{
    public sealed class CloudMediaOptions
    {
        /// <summary>Vídeos são grandes (5 a 20 MB cada): só baixa se pedido.</summary>
        public bool Videos { get; set; }

        /// <summary>Manuais e revistas em PDF também podem ser grandes.</summary>
        public bool Manuals { get; set; }

        /// <summary>Buscar capas (<see cref="AutoMedia"/>) e informações (<see cref="AutoInfo"/>) automáticas para jogos sem elas.</summary>
        public bool Automatic { get; set; }

        public bool Allows(MediaKind kind) => kind == MediaKind.Image || (kind == MediaKind.Video && Videos) || (kind == MediaKind.Manual && Manuals);
    }

    public sealed class CloudMediaSyncResult
    {
        public int GamesShared { get; set; }
        public int FilesUploaded { get; set; }
        public int GamesFilled { get; set; }
        public int FilesDownloaded { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// RB-015: metadados e mídia dos jogos compartilhados pela nuvem, para a capa e a descrição aparecerem
    /// em qualquer PC antes do jogo ser baixado (como no Steam).
    ///
    /// Na nuvem, por sistema: midia\&lt;pasta&gt;\metadados\&lt;PC&gt;.xml (o que cada PC sabe de cada jogo; cada PC
    /// só grava o seu) e midia\&lt;pasta&gt;\imagens|videos|manuais (arquivos com o nome que o scraper do ES dá).
    /// Na leitura, cada campo vale o da entrada mais recente que o tem. No PC, a nuvem só preenche o que está vazio:
    /// nada que o usuário ou o scraper local escreveu é trocado.
    /// </summary>
    public sealed class CloudMediaSync
    {
        readonly RetroBatInstall _install;
        readonly string _romsRoot;
        readonly string _cloudRoot;
        readonly string _pcName;
        readonly CloudMediaOptions _options;

        public CloudMediaSync(RetroBatInstall install, string romsRoot, string cloudRetroboatPath, string pcName = null, CloudMediaOptions options = null)
        {
            _install = install;
            _romsRoot = romsRoot;
            _cloudRoot = cloudRetroboatPath;
            _pcName = string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName;
            _options = options ?? new CloudMediaOptions();
        }

        public CloudMediaSyncResult Apply()
        {
            var result = new CloudMediaSyncResult();
            var index = CloudIndex.Load(CloudLibrarySync.IndexPath(_install));
            var systemNames = GameMedia.SystemNamesByFolder(_install);

            foreach (var group in index.Entries.GroupBy(e => e.System, StringComparer.OrdinalIgnoreCase))
            {
                var systemName = systemNames.TryGetValue(group.Key, out var name) ? name : group.Key;
                try
                {
                    SyncSystem(group.Key, systemName, group.ToList(), result);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    result.Errors.Add(systemName + ": " + ex.Message);
                }
            }

            return result;
        }

        void SyncSystem(string folder, string systemName, List<CloudIndexEntry> games, CloudMediaSyncResult result)
        {
            var gamelistPath = Path.Combine(_install.GamelistsPath, systemName, "gamelist.xml");
            var gamelist = File.Exists(gamelistPath) ? XDocument.Load(gamelistPath) : new XDocument(new XElement("gameList"));
            var gamelistBefore = gamelist.ToString();

            var cloudSystem = Path.Combine(_cloudRoot, "midia", folder);
            var metadataDir = Path.Combine(cloudSystem, "metadados");
            var ownPath = Path.Combine(metadataDir, SafeFileName(_pcName) + ".xml");
            var romFolder = Path.Combine(_romsRoot, folder);

            var own = ReadEntries(ownPath);
            var ownBefore = Serialize(own).ToString();
            var winners = ReadWinners(metadataDir);

            // 1. Envia o que este PC sabe e a nuvem ainda não tem.
            foreach (var game in games)
            {
                var element = CloudLibrarySync.FindGame(gamelist, game.LocalName);
                if (element == null)
                    continue;

                var shared = Share(element, romFolder, cloudSystem, result);
                if (shared.Count == 0)
                    continue;

                winners.TryGetValue(game.LocalName, out var winner);
                if (winner != null && SameFields(winner.Fields, shared))
                    continue; // A nuvem já tem isso (talvez veio dela mesma).

                own.TryGetValue(game.LocalName, out var previous);
                var updated = previous != null && SameFields(previous.Fields, shared) ? previous.Updated : DateTime.UtcNow;
                own[game.LocalName] = new MediaEntry { Key = game.LocalName, Updated = updated, Fields = shared };
                result.GamesShared++;
            }

            if (Serialize(own).ToString() != ownBefore)
            {
                Directory.CreateDirectory(metadataDir);
                Save(ownPath, own);
                winners = ReadWinners(metadataDir);
            }

            // 2. Preenche o gamelist local com o que veio de outros PCs.
            var mediaRoot = GameMedia.LocalMediaRoot(_install, systemName);
            foreach (var game in games)
            {
                if (!winners.TryGetValue(game.LocalName, out var winner))
                    continue;

                var element = CloudLibrarySync.FindGame(gamelist, game.LocalName);
                if (element == null)
                {
                    element = new XElement("game", new XElement("path", "./" + game.LocalName));
                    gamelist.Root.Add(element);
                }

                if (Fill(element, winner, cloudSystem, mediaRoot, romFolder, result))
                    result.GamesFilled++;
            }

            if (gamelist.ToString() != gamelistBefore)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gamelistPath));
                gamelist.Save(gamelistPath);
            }
        }

        /// <summary>Campos do jogo neste PC, no formato da nuvem; a mídia vai junto para a nuvem.</summary>
        Dictionary<string, string> Share(XElement game, string romFolder, string cloudSystem, CloudMediaSyncResult result)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in GameMedia.TextFields)
            {
                var value = (string)game.Element(name);
                if (name == "desc")
                    value = GameDescription.WithoutStatus(value);

                if (!string.IsNullOrWhiteSpace(value))
                    fields[name] = value;
            }

            // Sem nada além do nome do arquivo, não há o que compartilhar.
            if (fields.Count == 1 && fields.ContainsKey("name") && fields["name"] == Path.GetFileNameWithoutExtension(StripDotSlash((string)game.Element("path"))))
                fields.Clear();

            foreach (var element in game.Elements().Where(e => GameMedia.MediaFields.ContainsKey(e.Name.LocalName)))
            {
                var local = GameMedia.Resolve(element.Value, romFolder, _install);
                if (local == null || !File.Exists(local))
                    continue;

                var kind = GameMedia.MediaFields[element.Name.LocalName];
                var cloudRelative = GameMedia.CloudFolder(kind) + "/" + Path.GetFileName(local);
                var cloudFile = Path.Combine(cloudSystem, GameMedia.CloudFolder(kind), Path.GetFileName(local));
                if (!File.Exists(cloudFile) || new FileInfo(cloudFile).Length != new FileInfo(local).Length)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cloudFile));
                    File.Copy(local, cloudFile, true);
                    result.FilesUploaded++;
                }

                fields[element.Name.LocalName] = cloudRelative;
            }

            foreach (var attribute in new[] { "id", "source" })
            {
                var value = (string)game.Attribute(attribute);
                if (!string.IsNullOrEmpty(value) && fields.Count > 0)
                    fields["@" + attribute] = value;
            }

            return fields;
        }

        /// <summary>Preenche só o que está vazio no PC. Retorna true se mudou algo.</summary>
        bool Fill(XElement game, MediaEntry winner, string cloudSystem, string mediaRoot, string romFolder, CloudMediaSyncResult result)
        {
            bool changed = false;
            foreach (var field in winner.Fields)
            {
                if (field.Key.StartsWith("@", StringComparison.Ordinal))
                {
                    var attribute = field.Key.Substring(1);
                    if (string.IsNullOrEmpty((string)game.Attribute(attribute)))
                    {
                        game.SetAttributeValue(attribute, field.Value);
                        changed = true;
                    }

                    continue;
                }

                if (GameMedia.MediaFields.TryGetValue(field.Key, out var kind))
                {
                    var current = GameMedia.Resolve((string)game.Element(field.Key), romFolder, _install);
                    if (current != null && File.Exists(current))
                        continue;

                    if (!_options.Allows(kind))
                        continue;

                    var fileName = Path.GetFileName(field.Value.Replace('/', '\\'));
                    var source = Path.Combine(cloudSystem, GameMedia.CloudFolder(kind), fileName);
                    var target = Path.Combine(mediaRoot, GameMedia.LocalFolder(kind), fileName);
                    if (!File.Exists(target))
                    {
                        if (!File.Exists(source))
                            continue;

                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(source, target + ".retroboat-part", true);
                        File.Move(target + ".retroboat-part", target);
                        result.FilesDownloaded++;
                    }

                    game.SetElementValue(field.Key, GameMedia.ToHomeRelative(target, _install));
                    changed = true;
                    continue;
                }

                if (field.Key == "desc")
                {
                    var current = (string)game.Element("desc");
                    if (GameDescription.WithoutStatus(current).Length > 0)
                        continue;

                    game.SetElementValue("desc", GameDescription.ReplaceBody(current, field.Value));
                    changed = true;
                    continue;
                }

                var existing = (string)game.Element(field.Key);
                bool isDefaultName = field.Key == "name" && existing == Path.GetFileNameWithoutExtension(StripDotSlash((string)game.Element("path")));
                if (string.IsNullOrWhiteSpace(existing) || isDefaultName)
                {
                    if (existing != field.Value)
                    {
                        game.SetElementValue(field.Key, field.Value);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        sealed class MediaEntry
        {
            public string Key;
            public DateTime Updated;
            public Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// O que a nuvem sabe de cada jogo, juntando os arquivos de todos os PCs campo a campo: cada campo
        /// vale o da entrada mais recente que o tem. Assim, um PC que só renomeou o jogo não esconde a
        /// descrição e a capa que outro PC enviou.
        /// </summary>
        static Dictionary<string, MediaEntry> ReadWinners(string metadataDir)
        {
            var winners = new Dictionary<string, MediaEntry>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(metadataDir))
                return winners;

            var all = new List<MediaEntry>();
            foreach (var file in Directory.GetFiles(metadataDir, "*.xml"))
            {
                try { all.AddRange(ReadEntries(file).Values); }
                catch (System.Xml.XmlException) { } // Arquivo de outro PC no meio do envio pelo Drive.
            }

            foreach (var entry in all.OrderBy(e => e.Updated))
            {
                if (!winners.TryGetValue(entry.Key, out var merged))
                    winners[entry.Key] = merged = new MediaEntry { Key = entry.Key };

                merged.Updated = entry.Updated;
                foreach (var field in entry.Fields)
                    merged.Fields[field.Key] = field.Value;
            }

            return winners;
        }

        static Dictionary<string, MediaEntry> ReadEntries(string path)
        {
            var entries = new Dictionary<string, MediaEntry>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return entries;

            foreach (var game in XDocument.Load(path).Root.Elements("game"))
            {
                var entry = new MediaEntry
                {
                    Key = (string)game.Attribute("key"),
                    Updated = DateTime.Parse((string)game.Attribute("updated"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                };

                foreach (var field in game.Elements())
                    entry.Fields[field.Name.LocalName == "attribute" ? "@" + (string)field.Attribute("name") : field.Name.LocalName] = field.Value;

                if (!string.IsNullOrEmpty(entry.Key))
                    entries[entry.Key] = entry;
            }

            return entries;
        }

        static void Save(string path, Dictionary<string, MediaEntry> entries)
        {
            var temp = path + ".retroboat-part";
            new XDocument(Serialize(entries)).Save(temp);
            if (File.Exists(path))
                File.Delete(path);

            File.Move(temp, path);
        }

        static XElement Serialize(Dictionary<string, MediaEntry> entries)
        {
            return new XElement("retroboatMedia",
                entries.Values.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => new XElement("game",
                    new XAttribute("key", e.Key),
                    new XAttribute("updated", e.Updated.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                    e.Fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key.StartsWith("@", StringComparison.Ordinal)
                        ? new XElement("attribute", new XAttribute("name", f.Key.Substring(1)), f.Value)
                        : new XElement(f.Key, f.Value)))));
        }

        static bool SameFields(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
        }

        static string StripDotSlash(string path) => path != null && path.StartsWith("./", StringComparison.Ordinal) ? path.Substring(2) : path;

        static string SafeFileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name;
        }
    }
}
