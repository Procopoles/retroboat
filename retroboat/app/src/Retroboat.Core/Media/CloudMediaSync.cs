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

        /// <summary>Buscar mídias e informações automáticas para os jogos novos (<see cref="AutoImport"/>).</summary>
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
    /// Na leitura, cada campo vale o da entrada mais recente que o tem.
    ///
    /// A nuvem é a fonte da verdade da mídia: o que uma fonte pública ou o PROCURAR MÍDIAS trouxe em qualquer PC vai para lá e
    /// chega aos outros. No PC, cada campo acompanha a nuvem, a não ser que tenha mudado neste PC desde a última sincronização
    /// (midia-sync): aí é a mudança deste PC que vai para a nuvem. Num arquivo de mídia com o mesmo nome dos dois lados, vale o
    /// mais novo, então um PC desatualizado não regrava a capa velha por cima da nova.
    /// </summary>
    public sealed class CloudMediaSync
    {
        /// <summary>Fonte do PROCURAR MÍDIAS e quando foi usada, como campo: "scrap:ScreenScraper" = "20261114T154500".</summary>
        const string ScrapPrefix = "scrap:";

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
            var lastSyncPath = LastSyncPath(_install, folder);
            var romFolder = Path.Combine(_romsRoot, folder);
            var mediaRoot = GameMedia.LocalMediaRoot(_install, systemName);

            var own = ReadEntries(ownPath);
            var ownBefore = Serialize(own).ToString();
            var lastSync = ReadEntries(lastSyncPath);
            var lastSyncBefore = Serialize(lastSync).ToString();
            var winners = ReadWinners(metadataDir);
            var synced = new Dictionary<string, MediaEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var game in games)
            {
                winners.TryGetValue(game.LocalName, out var winner);
                var element = CloudLibrarySync.FindGame(gamelist, game.LocalName);
                if (element == null)
                {
                    if (winner == null)
                        continue;

                    element = new XElement("game", new XElement("path", "./" + game.LocalName));
                    gamelist.Root.Add(element);
                }

                // O que este PC tinha na última sincronização. Antes desta versão não havia esse registro: o que ele enviou da
                // última vez serve (era o que ele tinha).
                if (!lastSync.TryGetValue(game.LocalName, out var baseline))
                    own.TryGetValue(game.LocalName, out baseline);

                // 1. Recebe o que mudou na nuvem e não mudou aqui: um PROCURAR MÍDIAS em outro PC chega a este.
                if (winner != null && Receive(element, winner, baseline, cloudSystem, mediaRoot, romFolder, result))
                    result.GamesFilled++;

                // 2. Envia o que este PC tem e a nuvem não: a mídia nova vai junto.
                var fields = Share(element, romFolder, cloudSystem, result);
                lastSync.TryGetValue(game.LocalName, out var previousSync);
                synced[game.LocalName] = previousSync != null && SameFields(previousSync.Fields, fields)
                    ? previousSync
                    : new MediaEntry { Key = game.LocalName, Updated = DateTime.UtcNow, Fields = fields };

                if (fields.Count == 0 || (winner != null && SameFields(winner.Fields, fields)))
                    continue; // A nuvem já tem isso (talvez veio dela mesma).

                own.TryGetValue(game.LocalName, out var previous);
                if (previous != null && SameFields(previous.Fields, fields))
                    continue; // Já enviado; a data continua a do envio, para não passar na frente do que veio depois.

                own[game.LocalName] = new MediaEntry { Key = game.LocalName, Updated = DateTime.UtcNow, Fields = fields };
                result.GamesShared++;
            }

            if (Serialize(own).ToString() != ownBefore)
            {
                Directory.CreateDirectory(metadataDir);
                Save(ownPath, own);
            }

            if (gamelist.ToString() != gamelistBefore)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gamelistPath));
                gamelist.Save(gamelistPath);
            }

            // Por último: se algo acima falhar, a próxima vez compara com o estado antigo e refaz.
            if (Serialize(synced).ToString() != lastSyncBefore)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lastSyncPath));
                Save(lastSyncPath, synced);
            }
        }

        /// <summary>
        /// O que este PC tinha de cada jogo de um sistema na última sincronização, no formato da nuvem:
        /// &lt;RetroBat&gt;\retroboat\midia-sync\&lt;pasta&gt;.xml. É o que separa "mudou na nuvem" de "mudou aqui".
        /// </summary>
        public static string LastSyncPath(RetroBatInstall install, string folder) =>
            Path.Combine(install.RetroboatDataPath, "midia-sync", SafeFileName(folder) + ".xml");

        /// <summary>
        /// Traz da nuvem cada campo que lá mudou e aqui não, desde a última sincronização. Um campo vazio aqui sempre vem. Sem
        /// a última sincronização (um PC que nunca sincronizou o jogo), só o que está vazio: nada que a pessoa ou o scraper
        /// deste PC escreveu é trocado. Retorna true se mudou algo.
        /// </summary>
        bool Receive(XElement game, MediaEntry winner, MediaEntry baseline, string cloudSystem, string mediaRoot, string romFolder, CloudMediaSyncResult result)
        {
            var local = LocalFields(game, romFolder, out var files);
            bool changed = false;
            foreach (var field in winner.Fields)
            {
                local.TryGetValue(field.Key, out var mine);
                if (mine == field.Value)
                {
                    // O mesmo arquivo dos dois lados: o PROCURAR MÍDIAS regrava com o mesmo nome, então o conteúdo mais novo vale.
                    if (GameMedia.MediaFields.TryGetValue(field.Key, out var kind) && _options.Allows(kind))
                        changed |= Download(Path.Combine(cloudSystem, field.Value.Replace('/', '\\')), files[field.Key], result);

                    continue;
                }

                bool unchangedHere = mine == null ||
                                     (baseline != null && baseline.Fields.TryGetValue(field.Key, out var before) && before == mine);
                if (unchangedHere)
                    changed |= Take(game, field.Key, field.Value, cloudSystem, mediaRoot, result);
            }

            return changed;
        }

        /// <summary>Põe no jogo o valor da nuvem; a mídia é baixada para a pasta de mídia do ES. Retorna true se mudou algo.</summary>
        bool Take(XElement game, string field, string value, string cloudSystem, string mediaRoot, CloudMediaSyncResult result)
        {
            if (field.StartsWith("@", StringComparison.Ordinal))
            {
                var attribute = field.Substring(1);
                if ((string)game.Attribute(attribute) == value)
                    return false;

                game.SetAttributeValue(attribute, value);
                return true;
            }

            if (field.StartsWith(ScrapPrefix, StringComparison.Ordinal))
                return SetScrap(game, field.Substring(ScrapPrefix.Length), value);

            if (GameMedia.MediaFields.TryGetValue(field, out var kind))
            {
                if (!_options.Allows(kind))
                    return false;

                var fileName = Path.GetFileName(value.Replace('/', '\\'));
                var target = Path.Combine(mediaRoot, GameMedia.LocalFolder(kind), fileName);
                Download(Path.Combine(cloudSystem, GameMedia.CloudFolder(kind), fileName), target, result);
                if (!File.Exists(target))
                    return false;

                var relative = GameMedia.ToHomeRelative(target, _install);
                if ((string)game.Element(field) == relative)
                    return true; // O arquivo foi trocado, o caminho é o mesmo.

                game.SetElementValue(field, relative);
                return true;
            }

            if (field == "desc")
            {
                var current = (string)game.Element("desc");
                var updated = GameDescription.ReplaceBody(current, value);
                if (current == updated)
                    return false;

                game.SetElementValue("desc", updated);
                return true;
            }

            if ((string)game.Element(field) == value)
                return false;

            game.SetElementValue(field, value);
            return true;
        }

        /// <summary>
        /// Baixa o arquivo da nuvem se este PC não tem, ou se o da nuvem é outro (outro tamanho) e mais novo. Retorna true se
        /// baixou.
        /// </summary>
        static bool Download(string source, string target, CloudMediaSyncResult result)
        {
            if (!File.Exists(source))
                return false;

            if (File.Exists(target) && !Newer(source, than: target))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target + ".retroboat-part", true);
            if (File.Exists(target))
                File.Delete(target);
            File.Move(target + ".retroboat-part", target);
            result.FilesDownloaded++;
            return true;
        }

        /// <summary>
        /// Os dois arquivos têm conteúdo diferente e <paramref name="file"/> é o mais novo. O tamanho decide se são o mesmo (ler
        /// o conteúdo baixaria cada imagem do Drive), a data decide qual vale.
        /// </summary>
        static bool Newer(string file, string than)
        {
            var a = new FileInfo(file);
            var b = new FileInfo(than);
            return a.Length != b.Length && a.LastWriteTimeUtc > b.LastWriteTimeUtc;
        }

        /// <summary>Campos do jogo neste PC, no formato da nuvem; a mídia que a nuvem não tem, ou tem mais velha, vai junto.</summary>
        Dictionary<string, string> Share(XElement game, string romFolder, string cloudSystem, CloudMediaSyncResult result)
        {
            var fields = LocalFields(game, romFolder, out var files);
            foreach (var file in files)
            {
                var cloudFile = Path.Combine(cloudSystem, fields[file.Key].Replace('/', '\\'));
                if (File.Exists(cloudFile) && !Newer(file.Value, than: cloudFile))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(cloudFile));
                File.Copy(file.Value, cloudFile, true);
                result.FilesUploaded++;
            }

            return fields;
        }

        /// <summary>
        /// Tudo o que descreve o jogo neste PC, no formato da nuvem: os textos (a descrição sem a linha de status), a mídia
        /// que existe no disco ("imagens/Jogo-thumb.png"; em <paramref name="files"/>, o arquivo), as fontes que a pessoa usou
        /// no PROCURAR MÍDIAS ("scrap:ScreenScraper") e o id da fonte. O nome do arquivo como nome do jogo não conta.
        /// </summary>
        Dictionary<string, string> LocalFields(XElement game, string romFolder, out Dictionary<string, string> files)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            files = new Dictionary<string, string>(StringComparer.Ordinal);
            var defaultName = Path.GetFileNameWithoutExtension(StripDotSlash((string)game.Element("path")) ?? "");

            foreach (var name in GameMedia.TextFields)
            {
                var value = (string)game.Element(name);
                if (name == "desc")
                    value = GameDescription.WithoutStatus(value);

                if (string.IsNullOrWhiteSpace(value) || (name == "name" && value == defaultName))
                    continue;

                fields[name] = value;
            }

            foreach (var element in game.Elements().Where(e => GameMedia.MediaFields.ContainsKey(e.Name.LocalName)))
            {
                string local;
                try
                {
                    local = GameMedia.Resolve(element.Value, romFolder, _install);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    continue; // Caminho inválido no gamelist: conta como sem mídia.
                }

                if (local == null || !File.Exists(local))
                    continue;

                var kind = GameMedia.MediaFields[element.Name.LocalName];
                fields[element.Name.LocalName] = GameMedia.CloudFolder(kind) + "/" + Path.GetFileName(local);
                files[element.Name.LocalName] = local;
            }

            foreach (var scrap in game.Elements("scrap"))
            {
                var scraper = (string)scrap.Attribute("name");
                var date = (string)scrap.Attribute("date");
                if (!string.IsNullOrEmpty(scraper) && !string.IsNullOrEmpty(date))
                    fields[ScrapPrefix + scraper] = date;
            }

            foreach (var attribute in new[] { "id", "source" })
            {
                var value = (string)game.Attribute(attribute);
                if (!string.IsNullOrEmpty(value) && fields.Count > 0)
                    fields["@" + attribute] = value;
            }

            return fields;
        }

        /// <summary>Uma fonte do PROCURAR MÍDIAS no formato do ES: &lt;scrap name="ScreenScraper" date="20261114T154500"/&gt;.</summary>
        static bool SetScrap(XElement game, string scraper, string date)
        {
            var element = game.Elements("scrap").FirstOrDefault(e => (string)e.Attribute("name") == scraper);
            if (element != null && (string)element.Attribute("date") == date)
                return false;

            if (element == null)
                game.Add(element = new XElement("scrap", new XAttribute("name", scraper)));

            element.SetAttributeValue("date", date);
            return true;
        }

        /// <summary>
        /// Tira da nuvem o que este PC enviou de downloads pela metade, que versões anteriores tratavam como jogos
        /// (<see cref="PartialDownloads"/>): as entradas deste PC nos metadados e a mídia delas que nenhuma outra entrada, de
        /// nenhum PC, usa. Só mexe no arquivo deste PC; cada PC limpa o seu ao abrir.
        /// </summary>
        public static PartialDownloadsCleanup RemovePartialDownloads(string cloudRetroboatPath, string pcName = null)
        {
            var result = new PartialDownloadsCleanup();
            var media = Path.Combine(cloudRetroboatPath, "midia");
            if (!Directory.Exists(media))
                return result;

            var own = SafeFileName(string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName) + ".xml";
            foreach (var cloudSystem in Directory.GetDirectories(media))
            {
                try
                {
                    RemovePartialDownloads(cloudSystem, own, result);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException || ex is FormatException)
                {
                    result.Errors.Add(Path.GetFileName(cloudSystem) + ": " + ex.Message);
                }
            }

            return result;
        }

        static void RemovePartialDownloads(string cloudSystem, string ownFileName, PartialDownloadsCleanup result)
        {
            var metadataDir = Path.Combine(cloudSystem, "metadados");
            var ownPath = Path.Combine(metadataDir, ownFileName);
            if (!File.Exists(ownPath))
                return;

            var own = ReadEntries(ownPath);
            var partial = own.Values.Where(e => PartialDownloads.IsPartial(e.Key)).ToList();
            if (partial.Count == 0)
                return;

            foreach (var entry in partial)
            {
                own.Remove(entry.Key);
                result.Games.Add(Path.GetFileName(cloudSystem) + "/" + entry.Key);
            }

            Save(ownPath, own);

            // A mídia que outra entrada ainda usa fica. Um arquivo de outro PC no meio do envio pelo Drive não dá para ler:
            // nada é apagado desta vez.
            var stillUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(metadataDir, "*.xml"))
            {
                try
                {
                    stillUsed.UnionWith(ReadEntries(file).Values.SelectMany(MediaValues));
                }
                catch (System.Xml.XmlException)
                {
                    return;
                }
            }

            var root = Path.GetFullPath(cloudSystem).TrimEnd('\\') + "\\";
            foreach (var value in partial.SelectMany(MediaValues).Distinct(StringComparer.OrdinalIgnoreCase).Where(v => !stillUsed.Contains(v)))
            {
                var file = Path.GetFullPath(Path.Combine(cloudSystem, value.Replace('/', '\\')));
                if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                {
                    File.Delete(file);
                    result.FilesDeleted++;
                }
            }
        }

        /// <summary>A mídia de uma entrada, como a nuvem guarda (ex.: "imagens/Jogo-thumb.png").</summary>
        static IEnumerable<string> MediaValues(MediaEntry entry) =>
            entry.Fields.Where(f => GameMedia.MediaFields.ContainsKey(f.Key) && !string.IsNullOrEmpty(f.Value)).Select(f => f.Value);

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
                    entry.Fields[FieldKey(field)] = field.Value;

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
                    e.Fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => FieldElement(f.Key, f.Value)))));
        }

        /// <summary>Um campo no XML da nuvem: os atributos do jogo e as fontes do PROCURAR MÍDIAS têm elemento próprio.</summary>
        static XElement FieldElement(string key, string value)
        {
            if (key.StartsWith("@", StringComparison.Ordinal))
                return new XElement("attribute", new XAttribute("name", key.Substring(1)), value);

            if (key.StartsWith(ScrapPrefix, StringComparison.Ordinal))
                return new XElement("scrap", new XAttribute("name", key.Substring(ScrapPrefix.Length)), value);

            return new XElement(key, value);
        }

        static string FieldKey(XElement field)
        {
            switch (field.Name.LocalName)
            {
                case "attribute": return "@" + (string)field.Attribute("name");
                case "scrap": return ScrapPrefix + (string)field.Attribute("name");
                default: return field.Name.LocalName;
            }
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
