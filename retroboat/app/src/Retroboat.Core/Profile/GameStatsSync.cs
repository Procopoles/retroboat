using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Config;
using Retroboat.Library;

namespace Retroboat.Profile
{
    public sealed class GameStatsSyncResult
    {
        /// <summary>Jogos ("ps2/Jogo.iso") com partidas, tempo jogado ou favorito deste PC que foram para a nuvem.</summary>
        public List<string> Sent { get; } = new List<string>();

        /// <summary>Jogos que mudaram no gamelist deste PC com o que veio de outros PCs.</summary>
        public List<string> Received { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Favoritos e tempo jogado na nuvem (M4, RB-005), como na Steam: o jogo mostra as partidas e as horas de todos os
    /// PCs, e o favorito marcado num PC aparece nos outros.
    ///
    /// O ES guarda tudo no gamelist (favorite, playcount, gametime em segundos, lastplayed). Não dá para ficar com o
    /// gamelist mais novo, porque dois PCs jogam ao mesmo tempo: cada campo tem a sua mescla.
    /// <list type="bullet">
    /// <item>partidas e tempo jogado: somados. Cada instalação envia só o que foi jogado nela;</item>
    /// <item>última partida: a mais recente;</item>
    /// <item>favorito: vale a mudança mais nova, para desmarcar num PC desmarcar em todos.</item>
    /// </list>
    ///
    /// Na nuvem: perfil\jogos\&lt;PC&gt;-&lt;id&gt;.xml, um por instalação do RetroBat, com o que foi jogado nela. Cada
    /// instalação grava só o seu, então o Drive nunca cria cópias de conflito. Uma pasta copiada para outro PC, ou um
    /// RetroBat num pen drive levado para outro PC, ganha um arquivo novo: o anterior continua contando, e nada conta
    /// duas vezes.
    ///
    /// Neste PC: retroboat\perfil-sync\jogos.xml guarda o que o gamelist tinha na última sincronização, para separar o
    /// que foi jogado aqui do que veio da nuvem, e uma cópia do arquivo desta instalação na nuvem.
    ///
    /// Só roda com o ES fechado: ele guarda as partidas na memória (e em .emulationstation\recovery) e regrava o
    /// gamelist ao sair. Um jogo que ficou na pasta recovery (o ES foi fechado à força) vale por ela, como no ES.
    /// </summary>
    public sealed class GameStatsSync
    {
        public const string CloudFolderName = @"perfil\jogos";
        public const string StateFileName = @"perfil-sync\jogos.xml";

        static readonly Regex EsDate = new Regex(@"^\d{8}T\d{6}$", RegexOptions.CultureInvariant);

        readonly RetroBatInstall _install;
        readonly string _romsPath;
        readonly string _statePath;

        /// <param name="romsPath">A pasta de jogos deste PC: um jogo com partidas na nuvem e sem linha no gamelist ganha a
        /// linha se o arquivo estiver nela. Nulo: só os jogos que já estão nos gamelists.</param>
        public GameStatsSync(RetroBatInstall install, string cloudRetroboatPath, string romsPath = null, string pcName = null)
        {
            _install = install;
            _romsPath = romsPath;
            PcName = string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName;
            CloudFolder = Path.Combine(cloudRetroboatPath, CloudFolderName);
            _statePath = Path.Combine(install.RetroboatDataPath, StateFileName);
        }

        public string PcName { get; }
        public string CloudFolder { get; }

        public GameStatsSyncResult Sync()
        {
            var result = new GameStatsSyncResult();
            var now = DateTime.UtcNow;

            // 1. A instalação: um arquivo novo na nuvem quando a pasta mudou de PC ou de lugar.
            var state = LoadState(_statePath);
            if (state == null || !string.Equals(state.Pc, PcName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(state.Root)
                || !RetroBatInstall.SamePath(state.Root, _install.Root))
            {
                state = new SyncState
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                    Pc = PcName,
                    Root = _install.Root,
                    // O que o gamelist já tinha continua sendo o ponto de partida: nada conta duas vezes.
                    Seen = state?.Seen ?? new Dictionary<string, Played>(StringComparer.OrdinalIgnoreCase)
                };
            }

            var ownPath = Path.Combine(CloudFolder, SafeFileName(PcName + "-" + state.Id) + ".xml");
            var own = state.Own;
            var others = ReadOthers(ownPath);
            var systems = LoadGamelists(result);

            // 2. O que foi jogado aqui desde a última sincronização vai para o arquivo desta instalação.
            foreach (var system in systems)
            {
                foreach (var game in system.Games())
                {
                    var key = system.Name + "/" + game.Key;
                    var local = game.Value.Played;
                    var before = state.Seen.TryGetValue(key, out var seen) ? seen : new Played();
                    if (!own.TryGetValue(key, out var mine))
                        mine = new Contribution { Key = key };

                    bool changed = false;
                    if (local.PlayCount > before.PlayCount)
                    {
                        mine.PlayCount += local.PlayCount - before.PlayCount;
                        changed = true;
                    }

                    if (local.GameTime > before.GameTime)
                    {
                        mine.GameTime += local.GameTime - before.GameTime;
                        changed = true;
                    }

                    if (local.LastPlayed != null && local.LastPlayed != before.LastPlayed && Later(local.LastPlayed, mine.LastPlayed))
                    {
                        mine.LastPlayed = local.LastPlayed;
                        changed = true;
                    }

                    if (local.Favorite != before.Favorite)
                    {
                        mine.Favorite = local.Favorite;
                        mine.FavoriteUpdatedUtc = game.Value.ChangedUtc > now ? now : game.Value.ChangedUtc;
                        changed = true;
                    }

                    if (changed)
                    {
                        own[key] = mine;
                        result.Sent.Add(key);
                    }
                }
            }

            // 3. O total de cada jogo, de todas as instalações.
            var totals = Merge(own.Values.Concat(others));

            // 4. O total vai para o gamelist (e para a recovery do ES, que vale por cima dele).
            var newSeen = new Dictionary<string, Played>(state.Seen, StringComparer.OrdinalIgnoreCase);
            foreach (var system in systems)
            {
                foreach (var game in system.Games())
                {
                    var key = system.Name + "/" + game.Key;
                    var local = game.Value.Played;
                    var target = totals.TryGetValue(key, out var total) ? total.For(local) : local;
                    if (!target.SameAs(local))
                    {
                        foreach (var node in game.Value.Nodes)
                            Apply(node, target);

                        result.Received.Add(key);
                    }

                    newSeen[key] = target;
                }
            }

            foreach (var added in AddMissingGames(systems, totals, SystemFolders(result)))
            {
                newSeen[added.Key] = added.Value;
                result.Received.Add(added.Key);
            }

            // 5. Primeiro o estado, depois os gamelists e por último a nuvem. Interrompido em qualquer ponto, a próxima
            // sincronização não conta nada duas vezes: o estado já tem a partida, e o gamelist ainda sem o total é
            // menor que o visto, o que não conta como partida nova.
            state.Seen = newSeen;
            SaveState(_statePath, state);

            bool failed = false;
            foreach (var system in systems.Where(s => s.Changed))
            {
                try
                {
                    system.Save();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Errors.Add(system.Name + ": " + ex.Message);
                    foreach (var game in system.Games())
                        state.Seen[system.Name + "/" + game.Key] = game.Value.Played;
                    failed = true;
                }
            }

            if (failed)
                SaveState(_statePath, state); // O gamelist que não foi gravado continua com o valor de antes.

            var content = Serialize(own.Values, PcName).ToString();
            string published = null;
            try { published = File.Exists(ownPath) ? XDocument.Load(ownPath).Root.ToString() : null; }
            catch (Exception ex) when (ex is XmlException || ex is IOException) { }

            // Instalação que ainda não jogou nada: nenhum arquivo vazio na nuvem.
            if (published != content && (published != null || own.Count > 0))
            {
                try
                {
                    ConfigSync.WriteXml(ownPath, new XDocument(Serialize(own.Values, PcName)));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // O estado tem a cópia: vai na próxima sincronização.
                    result.Errors.Add("nuvem: " + ex.Message);
                }
            }

            return result;
        }

        /// <summary>Partidas e tempo somados, a última partida mais recente e o favorito da mudança mais nova.</summary>
        static Dictionary<string, Total> Merge(IEnumerable<Contribution> contributions)
        {
            var totals = new Dictionary<string, Total>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in contributions)
            {
                if (!totals.TryGetValue(item.Key, out var total))
                    totals[item.Key] = total = new Total();

                total.PlayCount += item.PlayCount;
                total.GameTime += item.GameTime;
                if (Later(item.LastPlayed, total.LastPlayed))
                    total.LastPlayed = item.LastPlayed;

                if (item.Favorite.HasValue && (total.FavoriteUpdatedUtc == null || item.FavoriteUpdatedUtc > total.FavoriteUpdatedUtc
                    || (item.FavoriteUpdatedUtc == total.FavoriteUpdatedUtc && string.CompareOrdinal(item.Pc, total.FavoritePc) > 0)))
                {
                    total.Favorite = item.Favorite.Value;
                    total.FavoriteUpdatedUtc = item.FavoriteUpdatedUtc;
                    total.FavoritePc = item.Pc;
                }
            }

            return totals;
        }

        /// <summary>
        /// Jogo com partidas ou favorito na nuvem, com o arquivo neste PC (instalado ou marcador da nuvem), mas sem linha no
        /// gamelist: a linha é criada, com o caminho como o ES grava.
        /// </summary>
        IEnumerable<KeyValuePair<string, Played>> AddMissingGames(List<SystemGamelist> systems, Dictionary<string, Total> totals, Dictionary<string, string> folders)
        {
            if (string.IsNullOrEmpty(_romsPath) || folders.Count == 0)
                yield break;

            var present = new HashSet<string>(systems.SelectMany(s => s.Games().Select(g => s.Name + "/" + g.Key)), StringComparer.OrdinalIgnoreCase);
            foreach (var pair in totals.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                int slash = pair.Key.IndexOf('/');
                if (slash <= 0 || present.Contains(pair.Key))
                    continue;

                var systemName = pair.Key.Substring(0, slash);
                var relative = pair.Key.Substring(slash + 1);
                if (!folders.TryGetValue(systemName, out var folder) || relative.Split('/').Any(p => p == ".." || p.Length == 0))
                    continue;

                if (!File.Exists(Path.Combine(_romsPath, folder, relative.Replace('/', '\\'))))
                    continue;

                var target = pair.Value.For(new Played());
                if (target.SameAs(new Played()))
                    continue;

                var gamelist = systems.FirstOrDefault(s => string.Equals(s.Name, systemName, StringComparison.OrdinalIgnoreCase));
                if (gamelist == null)
                {
                    gamelist = SystemGamelist.Create(systemName, Path.Combine(_install.GamelistsPath, systemName, "gamelist.xml"));
                    systems.Add(gamelist);
                }

                var node = new XElement("game", new XElement("path", "./" + relative));
                gamelist.Document.Root.Add(node);
                Apply(node, target);
                gamelist.MarkChanged();
                yield return new KeyValuePair<string, Played>(pair.Key, target);
            }
        }

        /// <summary>A pasta de cada sistema na pasta de jogos (o "gw" fica em "gameandwatch"). Vazio se não der para ler.</summary>
        Dictionary<string, string> SystemFolders(GameStatsSyncResult result)
        {
            var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(_romsPath) || _install.SystemsConfigPath == null)
                return folders;

            try
            {
                foreach (var system in IsolatedLibrary.ReadSystems(_install.SystemsConfigPath).Where(s => !folders.ContainsKey(s.Name)))
                    folders[system.Name] = system.Folder;
            }
            catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Errors.Add("es_systems.cfg: " + ex.Message);
            }

            return folders;
        }

        /// <summary>Grava os valores no formato do ES: o valor padrão (0, falso) não aparece no gamelist.</summary>
        static void Apply(XElement game, Played value)
        {
            SetOrRemove(game, "favorite", value.Favorite ? "true" : null);
            SetOrRemove(game, "playcount", value.PlayCount > 0 ? value.PlayCount.ToString(CultureInfo.InvariantCulture) : null);
            SetOrRemove(game, "lastplayed", value.LastPlayed);
            SetOrRemove(game, "gametime", value.GameTime > 0 ? value.GameTime.ToString(CultureInfo.InvariantCulture) : null);
        }

        static void SetOrRemove(XElement game, string name, string value)
        {
            var element = game.Element(name);
            if (value == null)
                element?.Remove();
            else if (element == null)
                game.Add(new XElement(name, value));
            else
                element.Value = value;
        }

        List<SystemGamelist> LoadGamelists(GameStatsSyncResult result)
        {
            var systems = new List<SystemGamelist>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(_install.GamelistsPath))
            {
                foreach (var dir in Directory.GetDirectories(_install.GamelistsPath))
                    names.Add(Path.GetFileName(dir));
            }

            var recoveryRoot = Path.Combine(_install.UserEmulationStationPath, "recovery");
            if (Directory.Exists(recoveryRoot))
            {
                foreach (var dir in Directory.GetDirectories(recoveryRoot))
                    names.Add(Path.GetFileName(dir));
            }

            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var system = SystemGamelist.Load(name, Path.Combine(_install.GamelistsPath, name, "gamelist.xml"), Path.Combine(recoveryRoot, name));
                    if (system != null)
                        systems.Add(system);
                }
                catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Um gamelist que não dá para ler agora fica de fora; o ES o lê do mesmo jeito.
                    result.Errors.Add(name + ": " + ex.Message);
                }
            }

            return systems;
        }

        IEnumerable<Contribution> ReadOthers(string ownPath)
        {
            var list = new List<Contribution>();
            if (!Directory.Exists(CloudFolder))
                return list;

            foreach (var file in Directory.GetFiles(CloudFolder, "*.xml"))
            {
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(ownPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    list.AddRange(ReadContributions(XDocument.Load(file).Root, Path.GetFileNameWithoutExtension(file)));
                }
                catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is IOException)
                {
                    // Arquivo de outro PC no meio do envio pelo Drive: fica para a próxima.
                }
            }

            return list;
        }

        static IEnumerable<Contribution> ReadContributions(XElement root, string fallbackPc)
        {
            var pc = (string)root.Attribute("pc") ?? fallbackPc;
            foreach (var game in root.Elements("game"))
            {
                var key = (string)game.Attribute("key");
                if (string.IsNullOrEmpty(key))
                    continue;

                var item = new Contribution
                {
                    Key = key,
                    Pc = pc,
                    PlayCount = Math.Max(0, ParseLong((string)game.Attribute("playcount"))),
                    GameTime = Math.Max(0, ParseLong((string)game.Attribute("gametime"))),
                    LastPlayed = ParseDate((string)game.Attribute("lastplayed"))
                };

                var favorite = (string)game.Attribute("favorite");
                var updated = (string)game.Attribute("favoriteUpdated");
                if (favorite != null && updated != null)
                {
                    item.Favorite = IsTrue(favorite);
                    item.FavoriteUpdatedUtc = DateTime.Parse(updated, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                }

                yield return item;
            }
        }

        static XElement Serialize(IEnumerable<Contribution> contributions, string pc)
        {
            return new XElement("retroboatGameStats", new XAttribute("pc", pc),
                new XComment(" Partidas, tempo jogado (segundos) e favoritos desta instalação do RetroBat. Cada instalação grava só o próprio arquivo; o total soma todos. "),
                contributions.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Select(c => c.ToXml()));
        }

        static SyncState LoadState(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                var root = XDocument.Load(path).Root;
                var state = new SyncState
                {
                    Id = (string)root.Attribute("id"),
                    Pc = (string)root.Attribute("pc"),
                    Root = (string)root.Attribute("root"),
                    Seen = new Dictionary<string, Played>(StringComparer.OrdinalIgnoreCase)
                };

                foreach (var game in root.Element("seen")?.Elements("game") ?? Enumerable.Empty<XElement>())
                {
                    var key = (string)game.Attribute("key");
                    if (!string.IsNullOrEmpty(key))
                        state.Seen[key] = Played.FromAttributes(game);
                }

                foreach (var item in ReadContributions(root.Element("own") ?? new XElement("own"), state.Pc))
                    state.Own[item.Key] = item;

                return string.IsNullOrEmpty(state.Id) ? null : state;
            }
            catch (Exception ex) when (ex is XmlException || ex is FormatException)
            {
                // Estado danificado: começa um arquivo novo na nuvem. O anterior continua contando.
                return null;
            }
        }

        static void SaveState(string path, SyncState state)
        {
            ConfigSync.WriteXml(path, new XDocument(new XElement("retroboatGameStatsSync",
                new XAttribute("id", state.Id), new XAttribute("pc", state.Pc), new XAttribute("root", state.Root),
                new XElement("seen", state.Seen.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Where(s => !s.Value.SameAs(new Played()))
                    .Select(s => s.Value.ToXml(s.Key))),
                new XElement("own", state.Own.Values.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Select(c => c.ToXml())))));
        }

        static bool Later(string date, string than) => date != null && (than == null || string.CompareOrdinal(date, than) > 0);

        static long ParseLong(string text) =>
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

        static string ParseDate(string text) => text != null && EsDate.IsMatch(text.Trim()) ? text.Trim() : null;

        static bool IsTrue(string text) => text != null && (text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || text.Trim() == "1");

        static string SafeFileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name;
        }

        /// <summary>O que o gamelist de um jogo mostra.</summary>
        sealed class Played
        {
            public long PlayCount;
            public long GameTime;
            public string LastPlayed;
            public bool Favorite;

            public bool SameAs(Played other) =>
                PlayCount == other.PlayCount && GameTime == other.GameTime && LastPlayed == other.LastPlayed && Favorite == other.Favorite;

            public static Played FromGame(XElement game) => new Played
            {
                PlayCount = Math.Max(0, ParseLong((string)game.Element("playcount"))),
                GameTime = Math.Max(0, ParseLong((string)game.Element("gametime"))),
                LastPlayed = ParseDate((string)game.Element("lastplayed")),
                Favorite = IsTrue((string)game.Element("favorite"))
            };

            public static Played FromAttributes(XElement game) => new Played
            {
                PlayCount = Math.Max(0, ParseLong((string)game.Attribute("playcount"))),
                GameTime = Math.Max(0, ParseLong((string)game.Attribute("gametime"))),
                LastPlayed = ParseDate((string)game.Attribute("lastplayed")),
                Favorite = IsTrue((string)game.Attribute("favorite"))
            };

            public XElement ToXml(string key)
            {
                var element = new XElement("game", new XAttribute("key", key));
                if (PlayCount > 0) element.SetAttributeValue("playcount", PlayCount.ToString(CultureInfo.InvariantCulture));
                if (GameTime > 0) element.SetAttributeValue("gametime", GameTime.ToString(CultureInfo.InvariantCulture));
                if (LastPlayed != null) element.SetAttributeValue("lastplayed", LastPlayed);
                if (Favorite) element.SetAttributeValue("favorite", "true");
                return element;
            }
        }

        /// <summary>O que uma instalação jogou de um jogo, e a última vez que marcou ou desmarcou o favorito nela.</summary>
        sealed class Contribution
        {
            public string Key;
            public string Pc;
            public long PlayCount;
            public long GameTime;
            public string LastPlayed;
            public bool? Favorite;
            public DateTime FavoriteUpdatedUtc;

            public XElement ToXml()
            {
                var element = new XElement("game", new XAttribute("key", Key));
                if (PlayCount > 0) element.SetAttributeValue("playcount", PlayCount.ToString(CultureInfo.InvariantCulture));
                if (GameTime > 0) element.SetAttributeValue("gametime", GameTime.ToString(CultureInfo.InvariantCulture));
                if (LastPlayed != null) element.SetAttributeValue("lastplayed", LastPlayed);
                if (Favorite.HasValue)
                {
                    element.SetAttributeValue("favorite", Favorite.Value ? "true" : "false");
                    element.SetAttributeValue("favoriteUpdated", FavoriteUpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                }

                return element;
            }
        }

        sealed class Total
        {
            public long PlayCount;
            public long GameTime;
            public string LastPlayed;
            public bool? Favorite;
            public DateTime? FavoriteUpdatedUtc;
            public string FavoritePc;

            /// <summary>O que o gamelist deve mostrar. Sem favorito na nuvem, fica o daqui.</summary>
            public Played For(Played local) => new Played
            {
                PlayCount = PlayCount,
                GameTime = GameTime,
                LastPlayed = LastPlayed ?? local.LastPlayed,
                Favorite = Favorite ?? local.Favorite
            };
        }

        sealed class SyncState
        {
            public string Id;
            public string Pc;
            public string Root;
            public Dictionary<string, Played> Seen;
            public Dictionary<string, Contribution> Own { get; } = new Dictionary<string, Contribution>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Um jogo no gamelist e, se o ES foi fechado à força, na recovery, que vale por cima.</summary>
        sealed class LocalGame
        {
            public Played Played;
            public DateTime ChangedUtc;
            public List<XElement> Nodes = new List<XElement>();
        }

        /// <summary>O gamelist de um sistema e os arquivos da recovery do ES dele.</summary>
        sealed class SystemGamelist
        {
            readonly string _path;
            readonly string _before;
            readonly List<(string Path, XDocument Document, string Before)> _recovery = new List<(string, XDocument, string)>();
            readonly Dictionary<string, LocalGame> _games = new Dictionary<string, LocalGame>(StringComparer.OrdinalIgnoreCase);
            bool _forced;

            SystemGamelist(string name, string path, XDocument document)
            {
                Name = name;
                _path = path;
                Document = document;
                _before = document.ToString();
            }

            public string Name { get; }
            public XDocument Document { get; }

            public bool Changed => _forced || Document.ToString() != _before || _recovery.Any(r => r.Document.ToString() != r.Before);

            public IEnumerable<KeyValuePair<string, LocalGame>> Games() => _games;

            public void MarkChanged() => _forced = true;

            public static SystemGamelist Create(string name, string path) => new SystemGamelist(name, path, new XDocument(new XElement("gameList")));

            public static SystemGamelist Load(string name, string path, string recoveryDir)
            {
                bool hasGamelist = File.Exists(path) && new FileInfo(path).Length > 0;
                var system = new SystemGamelist(name, path, hasGamelist ? XDocument.Load(path) : new XDocument(new XElement("gameList")));
                if (system.Document.Root == null || system.Document.Root.Name.LocalName != "gameList")
                    return null;

                var changed = hasGamelist ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                foreach (var game in system.Document.Root.Elements("game"))
                    system.Add(game, changed, overrides: false);

                if (Directory.Exists(recoveryDir))
                {
                    foreach (var file in Directory.GetFiles(recoveryDir, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        XDocument document;
                        try { document = XDocument.Load(file); }
                        catch (XmlException) { continue; } // O ES também pula uma recovery que não consegue ler.

                        if (document.Root == null)
                            continue;

                        system._recovery.Add((file, document, document.ToString()));
                        foreach (var game in document.Root.Elements("game"))
                            system.Add(game, File.GetLastWriteTimeUtc(file), overrides: true);
                    }
                }

                return system;
            }

            void Add(XElement game, DateTime changedUtc, bool overrides)
            {
                var key = KeyOf((string)game.Element("path"));
                if (key == null)
                    return;

                if (!_games.TryGetValue(key, out var local))
                {
                    _games[key] = new LocalGame { Played = Played.FromGame(game), ChangedUtc = changedUtc, Nodes = { game } };
                    return;
                }

                if (overrides)
                {
                    local.Played = Played.FromGame(game);
                    local.ChangedUtc = changedUtc;
                    local.Nodes.Add(game);
                }
            }

            /// <summary>"./Jogo (USA).iso" → "Jogo (USA).iso"; caminhos absolutos ou da pasta do ES ficam de fora.</summary>
            static string KeyOf(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                var text = path.Trim().Replace('\\', '/');
                if (text.StartsWith("./", StringComparison.Ordinal))
                    text = text.Substring(2);
                else if (text.StartsWith("~", StringComparison.Ordinal) || text.StartsWith("/", StringComparison.Ordinal) || text.Contains(":"))
                    return null;

                return text.Length == 0 || text.Split('/').Any(p => p == "..") ? null : text;
            }

            public void Save()
            {
                if (Document.ToString() != _before || _forced)
                    ConfigSync.WriteXml(_path, Document);

                foreach (var recovery in _recovery.Where(r => r.Document.ToString() != r.Before))
                    ConfigSync.WriteXml(recovery.Path, recovery.Document);
            }
        }
    }
}
