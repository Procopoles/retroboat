using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;

namespace Retroboat.Media
{
    public sealed class AutoMediaResult
    {
        public int GamesFilled { get; set; }
        public int FilesDownloaded { get; set; }

        /// <summary>Jogos sem imagem que a fonte não conhece (nome fora do padrão No-Intro/Redump).</summary>
        public List<string> NotFound { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Capas automáticas, como no Steam: todo jogo da biblioteca que não tem imagem ganha capa, tela do jogo
    /// e tela de título dos thumbnails do libretro, sem conta e sem o usuário pedir. Só preenche o que está
    /// vazio; o que veio do scraper do ES ou de outro PC pela nuvem continua valendo. Depois, o
    /// <see cref="CloudMediaSync"/> envia as imagens para o Drive, e os outros PCs nem precisam buscar.
    /// </summary>
    public sealed class AutoMedia
    {
        /// <summary>Sistema do ES -> pasta do sistema no libretro.</summary>
        public static readonly IReadOnlyDictionary<string, string> Playlists = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ps2"] = "Sony - PlayStation 2",
            ["psx"] = "Sony - PlayStation",
            ["psp"] = "Sony - PlayStation Portable",
            ["ps3"] = "Sony - PlayStation 3",
            ["snes"] = "Nintendo - Super Nintendo Entertainment System",
            ["nes"] = "Nintendo - Nintendo Entertainment System",
            ["n64"] = "Nintendo - Nintendo 64",
            ["gb"] = "Nintendo - Game Boy",
            ["gbc"] = "Nintendo - Game Boy Color",
            ["gba"] = "Nintendo - Game Boy Advance",
            ["nds"] = "Nintendo - Nintendo DS",
            ["3ds"] = "Nintendo - Nintendo 3DS",
            ["gamecube"] = "Nintendo - GameCube",
            ["wii"] = "Nintendo - Wii",
            ["virtualboy"] = "Nintendo - Virtual Boy",
            ["megadrive"] = "Sega - Mega Drive - Genesis",
            ["genesis"] = "Sega - Mega Drive - Genesis",
            ["mastersystem"] = "Sega - Master System - Mark III",
            ["gamegear"] = "Sega - Game Gear",
            ["saturn"] = "Sega - Saturn",
            ["dreamcast"] = "Sega - Dreamcast",
            ["segacd"] = "Sega - Mega-CD - Sega CD",
            ["sega32x"] = "Sega - 32X",
            ["pcengine"] = "NEC - PC Engine - TurboGrafx 16",
            ["atari2600"] = "Atari - 2600",
            ["xbox"] = "Microsoft - Xbox",
            ["ngpc"] = "SNK - Neo Geo Pocket Color"
        };

        /// <summary>O que cada tipo de imagem preenche no gamelist, e o sufixo do arquivo (como o scraper do ES faz).</summary>
        static readonly (ThumbnailKind Kind, string Suffix, string[] Fields)[] Targets =
        {
            (ThumbnailKind.Boxart, "thumb", new[] { "thumbnail", "boxart" }),
            (ThumbnailKind.Snap, "image", new[] { "image" }),
            (ThumbnailKind.Title, "titleshot", new[] { "titleshot" })
        };

        readonly RetroBatInstall _install;
        readonly string _romsRoot;
        readonly IThumbnailSource _source;

        public AutoMedia(RetroBatInstall install, string romsRoot, IThumbnailSource source)
        {
            _install = install;
            _romsRoot = romsRoot;
            _source = source;
        }

        public AutoMediaResult Apply()
        {
            var result = new AutoMediaResult();
            var index = CloudIndex.Load(CloudLibrarySync.IndexPath(_install));
            var systemNames = GameMedia.SystemNamesByFolder(_install);

            foreach (var group in index.Entries.GroupBy(e => e.System, StringComparer.OrdinalIgnoreCase))
            {
                var systemName = systemNames.TryGetValue(group.Key, out var name) ? name : group.Key;
                if (!Playlists.TryGetValue(systemName, out var playlist) && !Playlists.TryGetValue(group.Key, out playlist))
                    continue;

                try
                {
                    FillSystem(group.Key, systemName, playlist, group.Select(e => e.LocalName).ToList(), result);
                }
                catch (Exception ex) when (ex is WebException || ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    result.Errors.Add(systemName + ": " + ex.Message);
                }
            }

            return result;
        }

        void FillSystem(string folder, string systemName, string playlist, List<string> games, AutoMediaResult result)
        {
            var gamelistPath = Path.Combine(_install.GamelistsPath, systemName, "gamelist.xml");
            var gamelist = File.Exists(gamelistPath) ? XDocument.Load(gamelistPath) : new XDocument(new XElement("gameList"));
            var romFolder = Path.Combine(_romsRoot, folder);
            var imagesDir = Path.Combine(GameMedia.LocalMediaRoot(_install, systemName), GameMedia.LocalFolder(MediaKind.Image));

            // Só jogos sem nenhuma imagem: quem já tem mídia do scraper do ES não ganha imagens misturadas.
            // E só vai à fonte (e baixa as listas de nomes) se houver algum.
            var missing = games
                .Select(game => new { Name = game, Element = CloudLibrarySync.FindGame(gamelist, game) })
                .Where(g => !HasFile(g.Element, "thumbnail", romFolder) && !HasFile(g.Element, "image", romFolder))
                .ToList();
            if (missing.Count == 0)
                return;

            // As listas de nomes vêm antes: sem conexão, o erro aparece uma vez por sistema, não por jogo.
            var indexes = Targets.ToDictionary(t => t.Kind, t => new ThumbnailIndex(_source.List(playlist, t.Kind)));

            bool changed = false;
            foreach (var game in missing)
            {
                // Um jogo com problema (imagem que não baixa, caminho longo demais) não impede os outros.
                try
                {
                    if (FillGame(game.Name, game.Element, gamelist, playlist, indexes, romFolder, imagesDir, systemName, result))
                    {
                        result.GamesFilled++;
                        changed = true;
                    }
                }
                catch (Exception ex) when (ex is WebException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Errors.Add(systemName + "/" + game.Name + ": " + ex.Message);
                }
            }

            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gamelistPath));
                gamelist.Save(gamelistPath);
            }
        }

        /// <summary>Busca e grava as imagens de um jogo. Retorna true se preencheu algum campo.</summary>
        bool FillGame(string gameName, XElement element, XDocument gamelist, string playlist, Dictionary<ThumbnailKind, ThumbnailIndex> indexes,
                      string romFolder, string imagesDir, string systemName, AutoMediaResult result)
        {
            var stem = Path.GetFileNameWithoutExtension(gameName);
            var match = indexes[ThumbnailKind.Boxart].Find(stem) ?? indexes[ThumbnailKind.Snap].Find(stem);
            if (match == null)
            {
                result.NotFound.Add(systemName + "/" + gameName);
                return false;
            }

            if (element == null)
            {
                element = new XElement("game", new XElement("path", "./" + gameName), new XElement("name", stem));
                gamelist.Root.Add(element);
            }

            bool filled = false;
            foreach (var target in Targets)
            {
                if (HasFile(element, target.Fields[0], romFolder))
                    continue;

                // O mesmo jogo pode ter nome um pouco diferente em cada tipo de imagem.
                var name = indexes[target.Kind].Find(match) ?? indexes[target.Kind].Find(stem);
                if (name == null)
                    continue;

                var file = Path.Combine(imagesDir, stem + "-" + target.Suffix + ".png");
                if (!File.Exists(file))
                {
                    var data = _source.Download(playlist, target.Kind, name);
                    if (data == null || data.Length == 0)
                        continue;

                    Directory.CreateDirectory(imagesDir);
                    File.WriteAllBytes(file + ".retroboat-part", data);
                    if (File.Exists(file))
                        File.Delete(file);
                    File.Move(file + ".retroboat-part", file);
                    result.FilesDownloaded++;
                }

                foreach (var field in target.Fields.Where(f => !HasFile(element, f, romFolder)))
                    element.SetElementValue(field, GameMedia.ToHomeRelative(file, _install));

                filled = true;
            }

            // Sem tela do jogo, a capa também serve de imagem na visão detalhada.
            if (!HasFile(element, "image", romFolder) && HasFile(element, "thumbnail", romFolder))
            {
                element.SetElementValue("image", (string)element.Element("thumbnail"));
                filled = true;
            }

            return filled;
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
                return false; // Caminho inválido ou longo demais no gamelist: conta como sem imagem.
            }
        }
    }
}
