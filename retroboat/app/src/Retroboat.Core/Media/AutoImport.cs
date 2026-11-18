using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Media.Background;
using Retroboat.Media.Sources;

namespace Retroboat.Media
{
    public sealed class AutoImportResult
    {
        /// <summary>Jogos que ganharam algum campo (texto ou mídia).</summary>
        public int GamesFilled { get; set; }

        public int FilesDownloaded { get => DownloadedCount; set => DownloadedCount = value; }

        /// <summary>Os downloads de um jogo correm em paralelo (<see cref="AutoImport"/>).</summary>
        internal int DownloadedCount;

        /// <summary>RB-041: mídias secundárias (vídeo, manual, imagens extras) que ficaram para o segundo plano.</summary>
        public int FilesQueued { get; set; }

        /// <summary>Jogos que nenhuma fonte conhece (nome fora do padrão No-Intro/Redump, jogo obscuro).</summary>
        public List<string> NotFound { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Mídias e informações automáticas dos jogos novos, como na Steam (RB-038): todo jogo da biblioteca, baixado ou não,
    /// ganha capa, telas, logo, vídeo, nome, descrição, gênero, data, estúdio, editora e jogadores pela busca PERSONALIZADO
    /// (<see cref="CustomMediaSearch"/>), com as prioridades que a pessoa escolheu no ES (<see cref="MediaProfile"/>). Sem
    /// ela pedir. Depois, o <see cref="CloudMediaSync"/> envia para o Drive, e os outros PCs nem precisam buscar.
    ///
    /// Cada jogo é buscado uma vez só, por qualquer PC, achado ou não (<see cref="MediaImportLog"/>): jogos que já tinham sido
    /// buscados (inclusive pela regra antiga, só libretro e Wikipédia) ficam como estão. Um jogo que a pessoa procurou no ES
    /// não é mais tocado. Mídia só entra onde não há arquivo; texto troca o que a fonte tiver. Se uma fonte não respondeu, o
    /// jogo é buscado de novo na próxima sincronização.
    ///
    /// RB-041: a pessoa só espera o essencial (<see cref="MediaPriority"/>): textos, capa, tela, logo e fan art, baixados em
    /// paralelo, um por vez de cada servidor. Vídeo, manual e imagens extras vão para a fila de segundo plano
    /// (<see cref="BackgroundMediaQueue"/>) e aparecem no ES quando chegarem.
    /// </summary>
    public sealed class AutoImport
    {
        /// <summary>Servidores diferentes ao mesmo tempo; cada um recebe um pedido por vez (o ScreenScraper limita).</summary>
        const int ParallelHosts = 4;

        readonly RetroBatInstall _install;
        readonly string _romsRoot;
        readonly CustomMediaSearch _search;
        readonly MediaImportLog _log;
        readonly IMediaHttp _http;
        readonly BackgroundMediaQueue _background;

        /// <param name="log">O que já foi buscado, de todos os PCs (<see cref="MediaImportLog.InCloud"/>). Sem ele, só o deste PC.</param>
        /// <param name="http">Quem baixa as imagens e os vídeos (os testes trocam).</param>
        /// <param name="background">Para onde vão as mídias secundárias; nulo = baixar tudo agora.</param>
        public AutoImport(RetroBatInstall install, string romsRoot, CustomMediaSearch search, MediaImportLog log = null, IMediaHttp http = null,
                          BackgroundMediaQueue background = null)
        {
            _install = install;
            _romsRoot = romsRoot;
            _search = search;
            _log = log ?? MediaImportLog.Local(install);
            _http = http ?? new MediaHttp();
            _background = background;
            _log.ImportLegacy(MediaImportLog.LegacyInfoFile(install));
        }

        /// <param name="progress">Jogo atual (1 em diante), total de jogos a buscar e o nome do jogo, para a barra do ES.</param>
        public AutoImportResult Apply(Action<int, int, string> progress = null)
        {
            var result = new AutoImportResult();
            var index = CloudIndex.Load(CloudLibrarySync.IndexPath(_install));
            var systemNames = GameMedia.SystemNamesByFolder(_install);

            var systems = new List<SystemWork>();
            foreach (var group in index.Entries.GroupBy(e => e.System, StringComparer.OrdinalIgnoreCase))
            {
                var systemName = systemNames.TryGetValue(group.Key, out var name) ? name : group.Key;
                var querySystem = _search.Sources.Any(s => s.Supports(systemName)) ? systemName : group.Key;
                if (!_search.Sources.Any(s => s.Supports(querySystem)))
                    continue;

                try
                {
                    var work = Plan(group.Key, systemName, querySystem, group.Select(e => e.LocalName).ToList());
                    if (work.Games.Count > 0)
                        systems.Add(work);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    result.Errors.Add(systemName + ": " + ex.Message);
                }
            }

            int total = systems.Sum(s => s.Games.Count), done = 0;
            foreach (var work in systems)
            {
                try
                {
                    _search.Prepare(work.QuerySystem, work.Games.Select(g => Path.GetFileNameWithoutExtension(g.File)).ToList(), result.Errors);
                    foreach (var game in work.Games)
                    {
                        progress?.Invoke(++done, total, Path.GetFileNameWithoutExtension(game.File));
                        Import(work, game, result);
                    }

                    if (work.Changed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(work.GamelistPath));
                        work.Gamelist.Save(work.GamelistPath);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Errors.Add(work.SystemName + ": " + ex.Message);
                }
            }

            _log.Save();
            return result;
        }

        sealed class GameWork
        {
            public string File;
            public XElement Element;
            public bool NeedsInfo;
            public bool NeedsMedia;
        }

        sealed class SystemWork
        {
            public string SystemName;
            public string QuerySystem;
            public string RomFolder;
            public string GamelistPath;
            public XDocument Gamelist;
            public bool Changed;
            public List<GameWork> Games = new List<GameWork>();
        }

        /// <summary>Os jogos do sistema que ainda não foram buscados, e o que falta de cada um.</summary>
        SystemWork Plan(string folder, string systemName, string querySystem, List<string> games)
        {
            var work = new SystemWork
            {
                SystemName = systemName,
                QuerySystem = querySystem,
                RomFolder = Path.Combine(_romsRoot, folder),
                GamelistPath = Path.Combine(_install.GamelistsPath, systemName, "gamelist.xml")
            };
            work.Gamelist = File.Exists(work.GamelistPath) ? XDocument.Load(work.GamelistPath) : new XDocument(new XElement("gameList"));

            foreach (var file in games)
            {
                var element = CloudLibrarySync.FindGame(work.Gamelist, file);
                if (GameMedia.WasSearchedByHand(element))
                    continue;

                var game = new GameWork
                {
                    File = file,
                    Element = element,
                    NeedsInfo = !_log.WasSearched(MediaImportKind.Info, systemName, file),
                    NeedsMedia = !_log.WasSearched(MediaImportKind.Images, systemName, file)
                };

                if (game.NeedsInfo || game.NeedsMedia)
                    work.Games.Add(game);
            }

            return work;
        }

        void Import(SystemWork work, GameWork game, AutoImportResult result)
        {
            var stem = Path.GetFileNameWithoutExtension(game.File);
            var fields = new List<string>();
            if (game.NeedsInfo)
                fields.AddRange(MediaFields.Texts.Select(f => f.Id));
            if (game.NeedsMedia)
                fields.AddRange(MediaFields.Medias.Where(f => !HasFile(game.Element, f.GamelistFields[0], work.RomFolder)).Select(f => f.Id));

            CustomMediaResult found = null;
            if (fields.Count > 0)
                found = _search.Find(new MediaQuery(work.QuerySystem, game.File), fields);

            bool filled = false;
            try
            {
                if (found != null && found.Found)
                {
                    if (game.Element == null)
                    {
                        game.Element = new XElement("game", new XElement("path", "./" + game.File), new XElement("name", stem));
                        work.Gamelist.Root.Add(game.Element);
                        work.Changed = true;
                    }

                    filled |= FillTexts(game.Element, found);
                    filled |= FillMedia(work, game, stem, found, result);
                }
            }
            catch (Exception ex) when (ex is System.Net.WebException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // Uma mídia que não baixou não impede os outros jogos; este é buscado de novo na próxima vez.
                result.Errors.Add(work.SystemName + "/" + game.File + ": " + ex.Message);
                work.Changed |= filled;
                return;
            }

            if (filled)
            {
                result.GamesFilled++;
                work.Changed = true;
            }

            foreach (var error in found?.Errors ?? new List<string>())
                result.Errors.Add(work.SystemName + "/" + game.File + ": " + error);

            // Sem internet ou com uma fonte fora do ar, tenta de novo na próxima sincronização.
            if (found != null && found.Errors.Count > 0)
                return;

            if (found != null && !found.Found)
                result.NotFound.Add(work.SystemName + "/" + game.File);

            if (game.NeedsInfo)
                _log.Record(MediaImportKind.Info, work.SystemName, game.File, found: found != null && found.Texts.Count > 0);
            if (game.NeedsMedia)
                _log.Record(MediaImportKind.Images, work.SystemName, game.File, found: found == null || found.Media.Count > 0);
        }

        /// <summary>Põe no jogo o que as fontes têm, campo a campo; o que nenhuma tem continua como estava. Retorna true se mudou.</summary>
        static bool FillTexts(XElement game, CustomMediaResult found)
        {
            bool filled = false;
            foreach (var field in MediaFields.Texts)
            {
                if (!found.Texts.TryGetValue(field.Id, out var value))
                    continue;

                if (field.Id == MediaFields.Description)
                {
                    var desc = (string)game.Element("desc");
                    if (GameDescription.WithoutStatus(desc).Trim() != value)
                    {
                        game.SetElementValue("desc", GameDescription.ReplaceBody(desc, value));
                        filled = true;
                    }

                    continue;
                }

                if ((string)game.Element(field.Id) != value)
                {
                    game.SetElementValue(field.Id, value);
                    filled = true;
                }
            }

            return filled;
        }

        /// <summary>
        /// Baixa as mídias essenciais que faltam para a pasta de mídia do sistema e aponta o gamelist para elas; as secundárias
        /// vão para a fila de segundo plano (RB-041).
        /// </summary>
        bool FillMedia(SystemWork work, GameWork game, string stem, CustomMediaResult found, AutoImportResult result)
        {
            var missing = MediaFields.Medias
                .Where(f => found.Media.ContainsKey(f.Id) && !HasFile(game.Element, f.GamelistFields[0], work.RomFolder))
                .ToList();
            var now = missing.Where(f => _background == null || MediaPriority.IsEssential(f.Id)).ToList();
            var later = missing.Except(now).ToList();

            // A capa que também serve de tela do jogo é o mesmo arquivo: um download, com o nome do primeiro campo.
            var files = new Dictionary<string, (MediaField Field, MediaUrl Media)>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in now.Where(f => !files.ContainsKey(found.Media[f.Id].Url)))
                files[found.Media[field.Id].Url] = (field, found.Media[field.Id]);

            var paths = DownloadAll(work.SystemName, stem, files.Values.ToList(), result, out var failure);

            bool filled = false;
            foreach (var field in now)
            {
                if (paths.TryGetValue(found.Media[field.Id].Url, out var path) && path != null)
                    filled |= Point(game.Element, field.GamelistFields, path, work.RomFolder);
            }

            var queued = new List<BackgroundMediaItem>();
            foreach (var field in later)
            {
                var media = found.Media[field.Id];
                var target = MediaFile(work.SystemName, stem, field, media);

                // Já no PC (uma importação interrompida): entra agora, sem fila.
                if (File.Exists(target) && new FileInfo(target).Length > 0)
                {
                    filled |= Point(game.Element, field.GamelistFields, target, work.RomFolder);
                    continue;
                }

                queued.Add(new BackgroundMediaItem
                {
                    System = work.SystemName,
                    RomFolder = work.RomFolder,
                    Game = game.File,
                    Fields = field.GamelistFields.ToList(),
                    Url = media.Url,
                    Target = target,
                    Replaces = (string)game.Element.Element(field.GamelistFields[0])
                });
            }

            if (queued.Count > 0)
                result.FilesQueued += _background.Add(queued);

            if (failure != null)
            {
                // O que chegou fica; o jogo é buscado de novo na próxima vez (Import).
                work.Changed |= filled;
                throw failure;
            }

            return filled;
        }

        bool Point(XElement game, IEnumerable<string> gamelistFields, string path, string romFolder)
        {
            bool filled = false;
            foreach (var gamelistField in gamelistFields)
            {
                if (HasFile(game, gamelistField, romFolder))
                    continue;

                game.SetElementValue(gamelistField, GameMedia.ToHomeRelative(path, _install));
                filled = true;
            }

            return filled;
        }

        /// <summary>
        /// Os arquivos de um jogo, em paralelo: um servidor por vez cada (o ScreenScraper limita os pedidos simultâneos), até
        /// <see cref="ParallelHosts"/> servidores ao mesmo tempo. Um arquivo que falhou não impede os outros.
        /// </summary>
        /// <returns>Endereço → arquivo no PC (nulo se a fonte não tinha mais o arquivo).</returns>
        Dictionary<string, string> DownloadAll(string systemName, string stem, List<(MediaField Field, MediaUrl Media)> files, AutoImportResult result, out Exception failure)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Exception first = null;
            var hosts = files.GroupBy(f => Uri.TryCreate(f.Media.Url, UriKind.Absolute, out var uri) ? uri.Host : f.Media.Url, StringComparer.OrdinalIgnoreCase).ToList();

            System.Threading.Tasks.Parallel.ForEach(hosts, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = ParallelHosts }, host =>
            {
                foreach (var (field, media) in host)
                {
                    string path = null;
                    try
                    {
                        path = Download(systemName, stem, field, media, result);
                    }
                    catch (Exception ex) when (ex is System.Net.WebException || ex is IOException || ex is UnauthorizedAccessException)
                    {
                        lock (paths)
                            first = first ?? ex;
                        continue;
                    }

                    lock (paths)
                        paths[media.Url] = path;
                }
            });

            failure = first;
            return paths;
        }

        string MediaFile(string systemName, string stem, MediaField field, MediaUrl media)
        {
            var kind = KindOf(field);
            var extension = media.Extension ?? (kind == MediaKind.Video ? ".mp4" : kind == MediaKind.Manual ? ".pdf" : ".png");
            var folder = Path.Combine(GameMedia.LocalMediaRoot(_install, systemName), GameMedia.LocalFolder(kind));
            return Path.Combine(folder, stem + "-" + field.Suffix + extension);
        }

        static MediaKind KindOf(MediaField field) =>
            field.Kind == MediaFieldKind.Video ? MediaKind.Video : field.Kind == MediaFieldKind.Document ? MediaKind.Manual : MediaKind.Image;

        /// <returns>O arquivo no PC, ou nulo se a fonte não tinha mais o arquivo.</returns>
        string Download(string systemName, string stem, MediaField field, MediaUrl media, AutoImportResult result)
        {
            var file = MediaFile(systemName, stem, field, media);
            if (File.Exists(file) && new FileInfo(file).Length > 0)
                return file;

            var data = _http.Download(media.Url);
            if (data == null || data.Length == 0)
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file + ".retroboat-part", data);
            if (File.Exists(file))
                File.Delete(file);
            File.Move(file + ".retroboat-part", file);
            System.Threading.Interlocked.Increment(ref result.DownloadedCount);
            return file;
        }

        bool HasFile(XElement game, string field, string romFolder)
        {
            try
            {
                var path = GameMedia.Resolve((string)game?.Element(field), romFolder, _install);
                return path != null && File.Exists(path);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is NotSupportedException)
            {
                return false; // Caminho inválido ou longo demais no gamelist: conta como sem arquivo.
            }
        }
    }
}
