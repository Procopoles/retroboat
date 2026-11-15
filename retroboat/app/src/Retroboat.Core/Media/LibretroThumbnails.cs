using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Retroboat.Library;

namespace Retroboat.Media
{
    /// <summary>Tipos de imagem do libretro e o campo do gamelist que cada um preenche.</summary>
    public enum ThumbnailKind
    {
        /// <summary>Capa da caixa: campos thumbnail (grade) e boxart.</summary>
        Boxart,

        /// <summary>Tela do jogo: campo image (visão detalhada).</summary>
        Snap,

        /// <summary>Tela de título: campo titleshot.</summary>
        Title
    }

    /// <summary>De onde vêm as imagens automáticas. Separado para os testes não dependerem da internet.</summary>
    public interface IThumbnailSource
    {
        /// <summary>Nomes (sem ".png") das imagens de um sistema. Lança WebException/IOException sem conexão.</summary>
        IReadOnlyCollection<string> List(string playlist, ThumbnailKind kind);

        /// <summary>Conteúdo da imagem, ou null se ela não existir.</summary>
        byte[] Download(string playlist, ThumbnailKind kind, string name);

        /// <summary>Endereço da imagem, para quem baixa por conta própria (o PROCURAR MÍDIAS do ES, RB-036).</summary>
        string UrlOf(string playlist, ThumbnailKind kind, string name);
    }

    /// <summary>
    /// Thumbnails do libretro (thumbnails.libretro.com): capas, telas e títulos de quase todos os jogos
    /// catalogados pelo No-Intro e pelo Redump, sem conta nem chave de API. A lista de nomes de cada sistema
    /// fica em cache no PC por uma semana, então o lançador só vai à internet quando falta imagem.
    /// </summary>
    public sealed class LibretroThumbnails : IThumbnailSource
    {
        public const string BaseUrl = "https://thumbnails.libretro.com/";
        static readonly TimeSpan ListingLifetime = TimeSpan.FromDays(7);

        readonly string _cacheDir;

        /// <param name="cacheDir">Pasta para as listas de nomes (ex.: &lt;RetroBat&gt;\retroboat\cache\libretro).</param>
        public LibretroThumbnails(string cacheDir)
        {
            _cacheDir = cacheDir;
        }

        /// <summary>Cache das listas e da base do libretro neste PC: &lt;RetroBat&gt;\retroboat\cache\libretro.</summary>
        public static string CacheDir(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, "cache", "libretro");

        public static string Folder(ThumbnailKind kind) => kind == ThumbnailKind.Boxart ? "Named_Boxarts" : kind == ThumbnailKind.Snap ? "Named_Snaps" : "Named_Titles";

        /// <summary>Sistema do ES -> pasta do sistema no libretro (thumbnails e base de dados).</summary>
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

        public string UrlOf(string playlist, ThumbnailKind kind, string name) => Url(playlist, kind, name);

        public IReadOnlyCollection<string> List(string playlist, ThumbnailKind kind)
        {
            var cacheFile = Path.Combine(_cacheDir, Safe(playlist), Folder(kind) + ".txt");
            if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < ListingLifetime)
                return File.ReadAllLines(cacheFile, Encoding.UTF8);

            try
            {
                var html = Encoding.UTF8.GetString(Get(Url(playlist, kind, null)));
                var names = ParseListing(html);
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                File.WriteAllLines(cacheFile, names, new UTF8Encoding(false));
                return names;
            }
            catch (WebException) when (File.Exists(cacheFile))
            {
                return File.ReadAllLines(cacheFile, Encoding.UTF8); // Sem conexão: a lista antiga ainda serve.
            }
        }

        public byte[] Download(string playlist, ThumbnailKind kind, string name)
        {
            try
            {
                return Get(Url(playlist, kind, name));
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        /// <summary>Nomes das imagens numa página de listagem do servidor (links "Nome%20(USA).png").</summary>
        public static List<string> ParseListing(string html)
        {
            return Regex.Matches(html, "href=\"([^\"?/][^\"]*)\\.png\"", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(m => Uri.UnescapeDataString(m.Groups[1].Value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        static string Url(string playlist, ThumbnailKind kind, string name)
        {
            var url = BaseUrl + Uri.EscapeDataString(playlist) + "/" + Folder(kind) + "/";
            return name == null ? url : url + Uri.EscapeDataString(name) + ".png";
        }

        static byte[] Get(string url) => WebFetch.Get(url);

        internal static string Safe(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name;
        }
    }
}
