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
    /// <see cref="CloudMediaSync"/> envia para o Drive, e os outros PCs nem precisam buscar. A escolha de cada campo é a do
    /// <see cref="MediaRule"/>, a mesma do PROCURAR MÍDIAS do ES.
    ///
    /// Cada jogo é consultado uma vez só, por qualquer PC, achado ou não (<see cref="MediaImportLog"/>); um jogo que a pessoa
    /// procurou no ES não é mais tocado. Consultar de novo é pelo PROCURAR MÍDIAS do ES.
    /// </summary>
    public sealed class AutoInfo
    {
        readonly RetroBatInstall _install;
        readonly IReadOnlyList<IGameInfoSource> _sources;
        readonly MediaImportLog _log;

        /// <param name="sources">Em ordem de preferência para cada campo (a descrição em português vem antes de qualquer outra).</param>
        /// <param name="log">O que já foi consultado, de todos os PCs (<see cref="MediaImportLog.InCloud"/>). Sem ele, só o deste PC.</param>
        public AutoInfo(RetroBatInstall install, IReadOnlyList<IGameInfoSource> sources, MediaImportLog log = null)
        {
            _install = install;
            _sources = sources;
            _log = log ?? MediaImportLog.Local(install);
            _log.ImportLegacy(MediaImportLog.LegacyInfoFile(install));
        }

        public static IReadOnlyList<IGameInfoSource> DefaultSources(RetroBatInstall install) => new IGameInfoSource[]
        {
            new LibretroMetadata(LibretroThumbnails.CacheDir(install)),
            new WikidataInfo()
        };

        public AutoInfoResult Apply()
        {
            var result = new AutoInfoResult();
            var index = CloudIndex.Load(CloudLibrarySync.IndexPath(_install));
            var systemNames = GameMedia.SystemNamesByFolder(_install);

            foreach (var group in index.Entries.GroupBy(e => e.System, StringComparer.OrdinalIgnoreCase))
            {
                var systemName = systemNames.TryGetValue(group.Key, out var name) ? name : group.Key;
                try
                {
                    FillSystem(systemName, group.Select(e => e.LocalName).ToList(), result);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
                {
                    result.Errors.Add(systemName + ": " + ex.Message);
                }
            }

            _log.Save();
            return result;
        }

        void FillSystem(string systemName, List<string> games, AutoInfoResult result)
        {
            var gamelistPath = Path.Combine(_install.GamelistsPath, systemName, "gamelist.xml");
            var gamelist = File.Exists(gamelistPath) ? XDocument.Load(gamelistPath) : new XDocument(new XElement("gameList"));

            var pending = games
                .Select(file => new { File = file, Stem = Path.GetFileNameWithoutExtension(file), Element = CloudLibrarySync.FindGame(gamelist, file) })
                .Where(g => !GameMedia.WasSearchedByHand(g.Element) && !_log.WasSearched(MediaImportKind.Info, systemName, g.File))
                .ToList();
            if (pending.Count == 0)
                return;

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
                    _log.Record(MediaImportKind.Info, systemName, game.File, found: infos.Count > 0);
            }

            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gamelistPath));
                gamelist.Save(gamelistPath);
            }
        }

        /// <summary>Põe no jogo o que as fontes têm, campo a campo. Retorna true se mudou algo.</summary>
        static bool Fill(XDocument gamelist, XElement game, string file, string stem, List<GameInfo> infos)
        {
            if (game == null)
            {
                game = new XElement("game", new XElement("path", "./" + file), new XElement("name", stem));
                gamelist.Root.Add(game);
            }

            var info = MediaRule.Merge(infos);
            bool filled = Set(game, "name", info.Title);

            var desc = (string)game.Element("desc");
            if (info.Description != null && GameDescription.WithoutStatus(desc).Trim() != info.Description)
            {
                game.SetElementValue("desc", GameDescription.ReplaceBody(desc, info.Description));
                filled = true;
            }

            filled |= Set(game, "genre", info.Genre);
            filled |= Set(game, "developer", info.Developer);
            filled |= Set(game, "publisher", info.Publisher);
            filled |= Set(game, "releasedate", info.ReleaseDate);
            filled |= Set(game, "players", info.Players);
            return filled;
        }

        /// <summary>Troca o valor se a fonte tem um; o que nenhuma fonte tem continua como estava (scraper ou usuário).</summary>
        static bool Set(XElement game, string field, string value)
        {
            if (value == null || (string)game.Element(field) == value)
                return false;

            game.SetElementValue(field, value);
            return true;
        }
    }
}
