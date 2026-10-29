using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Library;

namespace Retroboat.Media
{
    public enum MediaKind
    {
        Image,
        Video,
        Manual
    }

    /// <summary>Campos de mídia e de metadados do gamelist do ES, e onde a mídia de cada sistema fica no PC.</summary>
    public static class GameMedia
    {
        /// <summary>Campos do gamelist que apontam para arquivos de mídia.</summary>
        public static readonly IReadOnlyDictionary<string, MediaKind> MediaFields = new Dictionary<string, MediaKind>
        {
            ["image"] = MediaKind.Image,
            ["thumbnail"] = MediaKind.Image,
            ["marquee"] = MediaKind.Image,
            ["fanart"] = MediaKind.Image,
            ["titleshot"] = MediaKind.Image,
            ["map"] = MediaKind.Image,
            ["bezel"] = MediaKind.Image,
            ["cartridge"] = MediaKind.Image,
            ["boxart"] = MediaKind.Image,
            ["boxback"] = MediaKind.Image,
            ["wheel"] = MediaKind.Image,
            ["mix"] = MediaKind.Image,
            ["video"] = MediaKind.Video,
            ["manual"] = MediaKind.Manual,
            ["magazine"] = MediaKind.Manual
        };

        /// <summary>
        /// Metadados que descrevem o jogo e valem para qualquer PC. Ficam de fora os dados de uso (partidas,
        /// tempo jogado, favorito, oculto), que são do perfil de cada pessoa (M4).
        /// </summary>
        public static readonly IReadOnlyList<string> TextFields = new[]
        {
            "name", "desc", "rating", "releasedate", "developer", "publisher", "genre", "family",
            "players", "lang", "region", "arcadesystemname", "crc32", "md5"
        };

        public static string LocalFolder(MediaKind kind) => kind == MediaKind.Video ? "videos" : kind == MediaKind.Manual ? "manuals" : "images";

        public static string CloudFolder(MediaKind kind) => kind == MediaKind.Video ? "videos" : kind == MediaKind.Manual ? "manuais" : "imagens";

        /// <summary>Mídia do sistema neste PC: &lt;ES&gt;\.emulationstation\media\&lt;sistema&gt;, fora da pasta de jogos.</summary>
        public static string LocalMediaRoot(RetroBatInstall install, string systemName) => Path.Combine(install.UserEmulationStationPath, "media", systemName);

        /// <summary>
        /// Caminho absoluto de um valor do gamelist. "./" é relativo à pasta do sistema; "~/" é relativo à
        /// pasta do ES (o RetroBat abre o ES com --home apontando para ela).
        /// </summary>
        public static string Resolve(string value, string systemFolder, RetroBatInstall install)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var text = value.Trim();
            if (text.StartsWith("./", StringComparison.Ordinal) || text.StartsWith(".\\", StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(systemFolder, text.Substring(2)));

            if (text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("~\\", StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(install.EmulationStationPath, text.Substring(2)));

            return Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(systemFolder, text));
        }

        /// <summary>Valor para o gamelist relativo à pasta do ES ("~/..."), para o RetroBat continuar portátil.</summary>
        public static string ToHomeRelative(string fullPath, RetroBatInstall install)
        {
            var home = install.EmulationStationPath.TrimEnd('\\', '/') + "\\";
            if (!fullPath.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                return fullPath;

            return "~/" + fullPath.Substring(home.Length).Replace('\\', '/');
        }

        /// <summary>Nome do sistema no ES para cada pasta de jogos (ex.: "gameandwatch" → "gw").</summary>
        public static Dictionary<string, string> SystemNamesByFolder(RetroBatInstall install)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var config = install.SystemsConfigPath;
            if (config == null)
                return map;

            foreach (var system in IsolatedLibrary.ReadSystems(config).Where(s => !map.ContainsKey(s.Folder)))
                map[system.Folder] = system.Name;

            return map;
        }
    }
}
