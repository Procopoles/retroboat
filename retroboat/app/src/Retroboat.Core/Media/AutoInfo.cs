using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;

namespace Retroboat.Media
{
    public sealed class AutoInfoResult
    {
        public int GamesFilled { get; set; }

        /// <summary>Jogos que nenhuma fonte conhece (nome fora do padrão No-Intro/Redump, jogo obscuro).</summary>
        public List<string> NotFound { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Informações automáticas, como na loja da Steam: todo jogo da biblioteca, baixado ou não, ganha nome,
    /// descrição, gênero, data, desenvolvedora, editora e jogadores, sem conta e sem precisar do arquivo do jogo.
    /// O scraper do ES vem por último: numa comparação de 50 jogos de PS2 ele errou gênero e inverteu
    /// desenvolvedora e editora em vários (Shadow of the Colossus como "Briga de rua"). Cada campo tem a sua
    /// ordem, pela fonte que mais acertou nele; o que nenhuma fonte tem continua como estava. Depois, o
    /// <see cref="CloudMediaSync"/> envia para o Drive, e os outros PCs nem precisam buscar.
    /// </summary>
    public sealed class AutoInfo
    {
        /// <summary>Gênero, estúdio e nome: o Wikidata é mais específico; o libretro tem "Action" para quase tudo.</summary>
        static readonly GameInfoOrigin[] WikidataFirst = { GameInfoOrigin.Wikidata, GameInfoOrigin.Libretro };

        /// <summary>Editora, data e jogadores: o libretro é o da edição do arquivo; o Wikidata mistura regiões e relançamentos.</summary>
        static readonly GameInfoOrigin[] LibretroFirst = { GameInfoOrigin.Libretro, GameInfoOrigin.Wikidata };

        /// <summary>Um jogo consultado, achado ou não, só volta às fontes depois disso (elas mudam pouco).</summary>
        static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(30);

        /// <summary>Resumo em português menor que isso é um esboço; a descrição do libretro, mesmo em inglês, diz mais.</summary>
        const int MinPortugueseLength = 120;

        readonly RetroBatInstall _install;
        readonly IReadOnlyList<IGameInfoSource> _sources;
        readonly string _checkedFile;

        /// <param name="sources">Em ordem de preferência para cada campo (a descrição em português vem antes de qualquer outra).</param>
        public AutoInfo(RetroBatInstall install, IReadOnlyList<IGameInfoSource> sources)
        {
            _install = install;
            _sources = sources;
            _checkedFile = Path.Combine(install.RetroboatDataPath, "cache", "informacoes", "consultados.txt");
        }

        public static IReadOnlyList<IGameInfoSource> DefaultSources(RetroBatInstall install) => new IGameInfoSource[]
        {
            new LibretroMetadata(Path.Combine(install.RetroboatDataPath, "cache", "libretro")),
            new WikidataInfo()
        };

        public AutoInfoResult Apply()
        {
            var result = new AutoInfoResult();
            var index = CloudIndex.Load(CloudLibrarySync.IndexPath(_install));
            var systemNames = GameMedia.SystemNamesByFolder(_install);
            var checkedAt = LoadChecked();
            bool checkedChanged = false;

            foreach (var group in index.Entries.GroupBy(e => e.System, StringComparer.OrdinalIgnoreCase))
            {
                var systemName = systemNames.TryGetValue(group.Key, out var name) ? name : group.Key;
                try
                {
                    checkedChanged |= FillSystem(systemName, group.Select(e => e.LocalName).ToList(), checkedAt, result);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
                {
                    result.Errors.Add(systemName + ": " + ex.Message);
                }
            }

            if (checkedChanged)
                SaveChecked(checkedAt);

            return result;
        }

        /// <returns>true se marcou jogos como consultados.</returns>
        bool FillSystem(string systemName, List<string> games, Dictionary<string, DateTime> checkedAt, AutoInfoResult result)
        {
            var gamelistPath = Path.Combine(_install.GamelistsPath, systemName, "gamelist.xml");
            var gamelist = File.Exists(gamelistPath) ? XDocument.Load(gamelistPath) : new XDocument(new XElement("gameList"));

            var pending = games
                .Select(file => new { File = file, Stem = Path.GetFileNameWithoutExtension(file), Element = CloudLibrarySync.FindGame(gamelist, file) })
                .Where(g => !(checkedAt.TryGetValue(Key(systemName, g.File), out var when) && DateTime.UtcNow - when < RecheckAfter))
                .ToList();
            if (pending.Count == 0)
                return false;

            var stems = pending.Select(g => g.Stem).ToList();
            var answers = new List<IDictionary<string, GameInfo>>();
            bool allAnswered = true;
            foreach (var source in _sources)
            {
                // Uma fonte fora do ar não impede a outra.
                try
                {
                    answers.Add(source.Find(systemName, stems));
                }
                catch (Exception ex) when (ex is WebException || ex is IOException || ex is InvalidOperationException || ex is ArgumentException)
                {
                    result.Errors.Add(systemName + ": " + ex.Message);
                    allAnswered = false;
                }
            }

            bool changed = false;
            foreach (var game in pending)
            {
                var infos = answers.Select(a => a.TryGetValue(game.Stem, out var info) ? info : null).Where(i => i != null).ToList();
                if (infos.Count == 0)
                {
                    if (allAnswered)
                        result.NotFound.Add(systemName + "/" + game.File);
                }
                else if (Fill(gamelist, game.Element, game.File, game.Stem, infos))
                {
                    result.GamesFilled++;
                    changed = true;
                }

                // Sem internet ou com uma fonte fora do ar, tenta de novo na próxima abertura.
                if (allAnswered)
                    checkedAt[Key(systemName, game.File)] = DateTime.UtcNow;
            }

            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gamelistPath));
                gamelist.Save(gamelistPath);
            }

            return allAnswered;
        }

        /// <summary>Põe no jogo o que as fontes têm, campo a campo. Retorna true se mudou algo.</summary>
        static bool Fill(XDocument gamelist, XElement game, string file, string stem, List<GameInfo> infos)
        {
            if (game == null)
            {
                game = new XElement("game", new XElement("path", "./" + file), new XElement("name", stem));
                gamelist.Root.Add(game);
            }

            bool filled = false;

            filled |= Set(game, "name", Pick(infos, WikidataFirst, i => i.Title));

            // Descrição: a da Wikipédia em português, a não ser que seja só uma frase; depois a do libretro, em inglês.
            var desc = (string)game.Element("desc");
            var text = infos.FirstOrDefault(i => i.DescriptionInPortuguese && (i.Description?.Length ?? 0) >= MinPortugueseLength)?.Description
                       ?? Pick(infos, LibretroFirst, i => i.Description);
            if (text != null && GameDescription.WithoutStatus(desc).Trim() != text)
            {
                game.SetElementValue("desc", GameDescription.ReplaceBody(desc, text));
                filled = true;
            }

            filled |= Set(game, "genre", Pick(infos, WikidataFirst, i => i.Genre));
            filled |= Set(game, "developer", Pick(infos, WikidataFirst, i => i.Developer));
            filled |= Set(game, "publisher", Pick(infos, LibretroFirst, i => i.Publisher));
            filled |= Set(game, "releasedate", Pick(infos, LibretroFirst, i => i.ReleaseDate));
            filled |= Set(game, "players", Pick(infos, LibretroFirst, i => i.Players));
            return filled;
        }

        static string Pick(List<GameInfo> infos, GameInfoOrigin[] order, Func<GameInfo, string> field)
        {
            return order.SelectMany(o => infos.Where(i => i.Origin == o)).Select(field).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        /// <summary>Troca o valor se a fonte tem um; o que nenhuma fonte tem continua como estava (scraper ou usuário).</summary>
        static bool Set(XElement game, string field, string value)
        {
            if (value == null || (string)game.Element(field) == value)
                return false;

            game.SetElementValue(field, value);
            return true;
        }

        static string Key(string system, string file) => system + "/" + file;

        /// <summary>Quando cada jogo foi consultado ("ps2/Jogo (USA).iso", tab, data UTC).</summary>
        Dictionary<string, DateTime> LoadChecked()
        {
            var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(_checkedFile))
                return map;

            foreach (var line in File.ReadAllLines(_checkedFile, Encoding.UTF8))
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 && DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                    map[parts[0]] = when;
            }

            return map;
        }

        void SaveChecked(Dictionary<string, DateTime> checkedAt)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_checkedFile));
            File.WriteAllLines(_checkedFile,
                checkedAt.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Select(c => c.Key + "\t" + c.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                new UTF8Encoding(false));
        }
    }
}
