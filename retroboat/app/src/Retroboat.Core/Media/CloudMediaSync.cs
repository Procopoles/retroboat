using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Media.Background;

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

        /// <summary>RB-041: mídias secundárias da nuvem (vídeo, manual, imagens extras) que ficaram para o segundo plano.</summary>
        public int FilesQueued { get; set; }
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
    ///
    /// RB-041: num PC sem nenhuma mídia (a primeira abertura), a espera é só pelo essencial (<see cref="MediaPriority"/>), com
    /// várias cópias do Drive ao mesmo tempo; vídeo, manual e imagens extras vão para a fila de segundo plano.
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
        readonly BackgroundMediaQueue _background;

        /// <summary>Cópias do Drive ao mesmo tempo. Cada arquivo do Drive para computador demora pela ida e volta, não pelo tamanho.</summary>
        const int ParallelCopies = 6;

        /// <param name="background">Para onde vão as mídias secundárias (RB-041); nulo = copiar tudo agora.</param>
        public CloudMediaSync(RetroBatInstall install, string romsRoot, string cloudRetroboatPath, string pcName = null, CloudMediaOptions options = null,
                              BackgroundMediaQueue background = null)
        {
            _background = background;
            _install = install;
            _romsRoot = romsRoot;
            _cloudRoot = cloudRetroboatPath;
            _pcName = string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName;
            _options = options ?? new CloudMediaOptions();
        }

        /// <param name="progress">Arquivos essenciais copiados da nuvem e o total, para a barra da atualização (RB-041).</param>
        public CloudMediaSyncResult Apply(Action<int, int> progress = null)
        {
            var result = new CloudMediaSyncResult();
            var index = CloudIndex.Load(CloudLibrarySync.IndexPath(_install));
            var systemNames = GameMedia.SystemNamesByFolder(_install);

            // 1. Cada sistema recebe da nuvem os textos e diz quais arquivos faltam.
            var systems = new List<SystemSync>();
            foreach (var group in index.Entries.GroupBy(e => e.System, StringComparer.OrdinalIgnoreCase))
            {
                var systemName = systemNames.TryGetValue(group.Key, out var name) ? name : group.Key;
                try
                {
                    systems.Add(Begin(group.Key, systemName, group.ToList()));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    result.Errors.Add(systemName + ": " + ex.Message);
                }
            }

            // 2. Os arquivos essenciais de todos os sistemas, vários ao mesmo tempo: o Drive demora por arquivo, não por byte.
            CopyAll(systems.SelectMany(s => s.Copies).ToList(), result, progress);

            // 3. O gamelist aponta para o que chegou, e o que este PC tem vai para a nuvem.
            foreach (var system in systems)
            {
                try
                {
                    Finish(system, result);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    result.Errors.Add(system.SystemName + ": " + ex.Message);
                }
            }

            return result;
        }

        /// <summary>Uma cópia da nuvem para este PC, feita junto com as outras (<see cref="CopyAll"/>).</summary>
        sealed class PendingCopy
        {
            public string Source;
            public string Target;
            public XElement Game;
            public string GameKey;

            /// <summary>Campo do gamelist que passa a apontar para o arquivo; nulo quando ele já aponta (só o conteúdo muda).</summary>
            public string Field;

            public bool Copied;
        }

        sealed class SyncedGame
        {
            public CloudIndexEntry Entry;
            public XElement Element;
            public MediaEntry Winner;
        }

        /// <summary>Um sistema no meio da sincronização: o gamelist e os registros da nuvem abertos, e o que falta copiar.</summary>
        sealed class SystemSync
        {
            public string SystemName;
            public string GamelistPath;
            public XDocument Gamelist;
            public string GamelistBefore;
            public string CloudSystem;
            public string MetadataDir;
            public string OwnPath;
            public string LastSyncPath;
            public string RomFolder;
            public string MediaRoot;
            public Dictionary<string, MediaEntry> Own;
            public string OwnBefore;
            public Dictionary<string, MediaEntry> LastSync;
            public string LastSyncBefore;
            public readonly List<SyncedGame> Games = new List<SyncedGame>();
            public readonly List<PendingCopy> Copies = new List<PendingCopy>();
            public readonly List<BackgroundMediaItem> Later = new List<BackgroundMediaItem>();
            public readonly HashSet<string> Filled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        SystemSync Begin(string folder, string systemName, List<CloudIndexEntry> games)
        {
            var sys = new SystemSync
            {
                SystemName = systemName,
                GamelistPath = Path.Combine(_install.GamelistsPath, systemName, "gamelist.xml"),
                CloudSystem = Path.Combine(_cloudRoot, "midia", folder),
                LastSyncPath = LastSyncPath(_install, folder),
                RomFolder = Path.Combine(_romsRoot, folder),
                MediaRoot = GameMedia.LocalMediaRoot(_install, systemName)
            };

            sys.Gamelist = File.Exists(sys.GamelistPath) ? XDocument.Load(sys.GamelistPath) : new XDocument(new XElement("gameList"));
            sys.GamelistBefore = sys.Gamelist.ToString();
            sys.MetadataDir = Path.Combine(sys.CloudSystem, "metadados");
            sys.OwnPath = Path.Combine(sys.MetadataDir, SafeFileName(_pcName) + ".xml");
            sys.Own = ReadEntries(sys.OwnPath);
            sys.OwnBefore = Serialize(sys.Own).ToString();
            sys.LastSync = ReadEntries(sys.LastSyncPath);
            sys.LastSyncBefore = Serialize(sys.LastSync).ToString();
            var winners = ReadWinners(sys.MetadataDir);

            foreach (var game in games)
            {
                winners.TryGetValue(game.LocalName, out var winner);
                var element = CloudLibrarySync.FindGame(sys.Gamelist, game.LocalName);
                if (element == null)
                {
                    if (winner == null)
                        continue;

                    element = new XElement("game", new XElement("path", "./" + game.LocalName));
                    sys.Gamelist.Root.Add(element);
                }

                sys.Games.Add(new SyncedGame { Entry = game, Element = element, Winner = winner });

                // O que este PC tinha na última sincronização. Antes desta versão não havia esse registro: o que ele enviou da
                // última vez serve (era o que ele tinha).
                if (!sys.LastSync.TryGetValue(game.LocalName, out var baseline))
                    sys.Own.TryGetValue(game.LocalName, out baseline);

                // Recebe o que mudou na nuvem e não mudou aqui: um PROCURAR MÍDIAS em outro PC chega a este.
                if (winner != null && Receive(sys, game.LocalName, element, winner, baseline))
                    sys.Filled.Add(game.LocalName);
            }

            return sys;
        }

        void Finish(SystemSync sys, CloudMediaSyncResult result)
        {
            foreach (var copy in sys.Copies.Where(c => c.Copied))
            {
                if (copy.Field != null)
                    SetMediaField(copy.Game, copy.Field, copy.Target);
                sys.Filled.Add(copy.GameKey);
            }

            result.GamesFilled += sys.Filled.Count;
            if (sys.Later.Count > 0)
                result.FilesQueued += _background.Add(sys.Later);

            var synced = new Dictionary<string, MediaEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var game in sys.Games)
            {
                // Envia o que este PC tem e a nuvem não: a mídia nova vai junto.
                var fields = Share(game.Element, sys.RomFolder, sys.CloudSystem, result);
                sys.LastSync.TryGetValue(game.Entry.LocalName, out var previousSync);
                synced[game.Entry.LocalName] = previousSync != null && SameFields(previousSync.Fields, fields)
                    ? previousSync
                    : new MediaEntry { Key = game.Entry.LocalName, Updated = DateTime.UtcNow, Fields = fields };

                if (fields.Count == 0 || (game.Winner != null && SameFields(game.Winner.Fields, fields)))
                    continue; // A nuvem já tem isso (talvez veio dela mesma).

                sys.Own.TryGetValue(game.Entry.LocalName, out var previous);
                if (previous != null && SameFields(previous.Fields, fields))
                    continue; // Já enviado; a data continua a do envio, para não passar na frente do que veio depois.

                sys.Own[game.Entry.LocalName] = new MediaEntry { Key = game.Entry.LocalName, Updated = DateTime.UtcNow, Fields = fields };
                result.GamesShared++;
            }

            if (Serialize(sys.Own).ToString() != sys.OwnBefore)
            {
                Directory.CreateDirectory(sys.MetadataDir);
                Save(sys.OwnPath, sys.Own);
            }

            if (sys.Gamelist.ToString() != sys.GamelistBefore)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sys.GamelistPath));
                sys.Gamelist.Save(sys.GamelistPath);
            }

            // Por último: se algo acima falhar, a próxima vez compara com o estado antigo e refaz.
            if (Serialize(synced).ToString() != sys.LastSyncBefore)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sys.LastSyncPath));
                Save(sys.LastSyncPath, synced);
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
        /// deste PC escreveu é trocado. Os textos entram agora; os arquivos, depois da cópia (<see cref="CopyAll"/>) ou em
        /// segundo plano. Retorna true se mudou algo agora.
        /// </summary>
        bool Receive(SystemSync sys, string gameKey, XElement game, MediaEntry winner, MediaEntry baseline)
        {
            var local = LocalFields(game, sys.RomFolder, out var files);
            bool changed = false;
            foreach (var field in winner.Fields)
            {
                local.TryGetValue(field.Key, out var mine);
                if (mine == field.Value)
                {
                    // O mesmo arquivo dos dois lados: o PROCURAR MÍDIAS regrava com o mesmo nome, então o conteúdo mais novo vale.
                    if (GameMedia.MediaFields.TryGetValue(field.Key, out var kind) && _options.Allows(kind))
                        changed |= Fetch(sys, gameKey, game, field.Key, Path.Combine(sys.CloudSystem, field.Value.Replace('/', '\\')), files[field.Key], replaces: null);

                    continue;
                }

                bool unchangedHere = mine == null ||
                                     (baseline != null && baseline.Fields.TryGetValue(field.Key, out var before) && before == mine);
                if (unchangedHere)
                    changed |= Take(sys, gameKey, game, field.Key, field.Value);
            }

            return changed;
        }

        /// <summary>Põe no jogo o valor da nuvem; a mídia vem para a pasta de mídia do ES. Retorna true se mudou algo agora.</summary>
        bool Take(SystemSync sys, string gameKey, XElement game, string field, string value)
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
                var target = Path.Combine(sys.MediaRoot, GameMedia.LocalFolder(kind), fileName);
                return Fetch(sys, gameKey, game, field, Path.Combine(sys.CloudSystem, GameMedia.CloudFolder(kind), fileName), target, replaces: (string)game.Element(field) ?? "");
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
        /// Um arquivo da nuvem para este PC, se ele não tem ou se o da nuvem é outro (outro tamanho) e mais novo. Já em dia, o
        /// campo aponta para ele agora. O essencial entra na cópia em paralelo (<see cref="CopyAll"/>); o secundário (vídeo,
        /// manual, imagens extras), na fila de segundo plano (RB-041).
        /// </summary>
        /// <param name="replaces">O valor do campo agora, que o arquivo substitui; nulo quando o campo já aponta para ele.</param>
        /// <returns>true se o jogo mudou agora (o campo passou a apontar para um arquivo que já estava aqui).</returns>
        bool Fetch(SystemSync sys, string gameKey, XElement game, string field, string source, string target, string replaces)
        {
            if (!File.Exists(source))
                return false;

            if (File.Exists(target) && !Newer(source, than: target))
            {
                if (replaces == null)
                    return false;

                SetMediaField(game, field, target);
                return true;
            }

            if (_background != null && MediaPriority.IsSecondary(field))
            {
                sys.Later.Add(new BackgroundMediaItem
                {
                    System = sys.SystemName,
                    RomFolder = sys.RomFolder,
                    Game = gameKey,
                    Fields = new[] { field },
                    CloudFile = source,
                    Target = target,
                    Replaces = replaces
                });
                return false;
            }

            sys.Copies.Add(new PendingCopy { Source = source, Target = target, Game = game, GameKey = gameKey, Field = replaces == null ? null : field });
            return false;
        }

        void SetMediaField(XElement game, string field, string target)
        {
            var relative = GameMedia.ToHomeRelative(target, _install);
            if ((string)game.Element(field) != relative)
                game.SetElementValue(field, relative);
        }

        /// <summary>
        /// Até <see cref="ParallelCopies"/> cópias do Drive ao mesmo tempo: num PC novo, as capas de centenas de jogos chegam
        /// em segundos, não em minutos. Uma que falhou fica para a próxima sincronização; as outras seguem.
        /// </summary>
        static void CopyAll(List<PendingCopy> copies, CloudMediaSyncResult result, Action<int, int> progress)
        {
            if (copies.Count == 0)
                return;

            int done = 0, copied = 0;
            var errors = new List<string>();
            var report = new object();

            // Dois campos podem querer o mesmo arquivo (a capa que é também a caixa): uma cópia por arquivo, as duas apontam.
            var byTarget = copies.GroupBy(c => c.Target, StringComparer.OrdinalIgnoreCase).ToList();
            progress?.Invoke(0, byTarget.Count);
            System.Threading.Tasks.Parallel.ForEach(byTarget, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = ParallelCopies }, group =>
            {
                var copy = group.First();
                try
                {
                    if (!File.Exists(copy.Target) || Newer(copy.Source, than: copy.Target))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(copy.Target));
                        File.Copy(copy.Source, copy.Target + ".retroboat-part", true);
                        if (File.Exists(copy.Target))
                            File.Delete(copy.Target);
                        File.Move(copy.Target + ".retroboat-part", copy.Target);
                        System.Threading.Interlocked.Increment(ref copied);
                    }

                    foreach (var same in group)
                        same.Copied = true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    lock (errors)
                        errors.Add(Path.GetFileName(copy.Target) + ": " + ex.Message);
                }

                int now = System.Threading.Interlocked.Increment(ref done);
                lock (report)
                    progress?.Invoke(now, byTarget.Count);
            });

            result.FilesDownloaded += copied;
            result.Errors.AddRange(errors);
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
