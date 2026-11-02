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
    /// Informações automáticas, como na loja da Steam: todo jogo da biblioteca, baixado ou não, ganha descrição,
    /// gênero, data, desenvolvedora, editora, jogadores e o nome sem as etiquetas do arquivo, sem conta e sem
    /// precisar do arquivo do jogo. Só preenche o que está vazio; o que veio do scraper do ES, do usuário ou de
    /// outro PC pela nuvem continua valendo. A descrição em português (Wikipédia) vem antes da em inglês
    /// (libretro), a não ser que seja só uma frase. Depois, o <see cref="CloudMediaSync"/> envia para o Drive,
    /// e os outros PCs nem precisam buscar.
    /// </summary>
    public sealed class AutoInfo
    {
        /// <summary>Campos da página do jogo, além do nome e da descrição.</summary>
        static readonly string[] Fields = { "genre", "releasedate", "developer", "publisher", "players" };

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
                .Where(g => NeedsInfo(g.Element, g.Stem))
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

        static bool NeedsInfo(XElement game, string stem)
        {
            if (game == null)
                return true;

            var name = (string)game.Element("name");
            if (string.IsNullOrWhiteSpace(name) || name == stem)
                return true;

            if (GameDescription.WithoutStatus((string)game.Element("desc")).Trim().Length == 0)
                return true;

            return Fields.Any(f => string.IsNullOrWhiteSpace((string)game.Element(f)));
        }

        /// <summary>Preenche os campos vazios do jogo. Retorna true se mudou algo.</summary>
        static bool Fill(XDocument gamelist, XElement game, string file, string stem, List<GameInfo> infos)
        {
            if (game == null)
            {
                game = new XElement("game", new XElement("path", "./" + file), new XElement("name", stem));
                gamelist.Root.Add(game);
            }

            bool filled = false;

            // O nome só troca se ainda é o do arquivo: um nome do scraper ou do usuário fica.
            var name = (string)game.Element("name");
            var title = infos.Select(i => i.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if ((string.IsNullOrWhiteSpace(name) || name == stem) && title != null && title != name)
            {
                game.SetElementValue("name", title);
                filled = true;
            }

            var desc = (string)game.Element("desc");
            if (GameDescription.WithoutStatus(desc).Trim().Length == 0)
            {
                var text = infos.FirstOrDefault(i => i.DescriptionInPortuguese && (i.Description?.Length ?? 0) >= MinPortugueseLength)?.Description
                           ?? infos.Select(i => i.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
                if (text != null)
                {
                    game.SetElementValue("desc", GameDescription.ReplaceBody(desc, text));
                    filled = true;
                }
            }

            // Gênero: o específico do Wikidata antes da categoria larga do libretro (que tem erros, como Black = RPG).
            filled |= SetIfEmpty(game, "genre", infos.Where(i => i.GenreIsDetailed).Concat(infos.Where(i => !i.GenreIsDetailed)).Select(i => i.Genre));
            filled |= SetIfEmpty(game, "releasedate", infos.Select(i => i.ReleaseDate));
            filled |= SetIfEmpty(game, "developer", infos.Select(i => i.Developer));
            filled |= SetIfEmpty(game, "publisher", infos.Select(i => i.Publisher));
            filled |= SetIfEmpty(game, "players", infos.Select(i => i.Players));
            return filled;
        }

        static bool SetIfEmpty(XElement game, string field, IEnumerable<string> candidates)
        {
            if (!string.IsNullOrWhiteSpace((string)game.Element(field)))
                return false;

            var value = candidates.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (value == null)
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
