using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Retroboat.Cloud
{
    public sealed class CloudGame
    {
        public string System { get; set; }

        /// <summary>Nome do arquivo (ou da pasta, para jogos que são pastas, como PS3).</summary>
        public string Name { get; set; }

        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }

        /// <summary>Identificador estável dentro da biblioteca: "sistema/nome".</summary>
        public string Id => System + "/" + Name;
    }

    /// <summary>
    /// Biblioteca de jogos na nuvem: &lt;pasta&gt;\&lt;sistema&gt;\&lt;jogo&gt;, com os mesmos nomes de
    /// sistema do RetroBat. Só lê metadados; nenhum jogo é baixado para listar.
    /// </summary>
    public sealed class CloudLibrary
    {
        static readonly HashSet<string> IgnoredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gamelist.xml", "_info.txt", "desktop.ini", "thumbs.db"
        };

        // Pastas que o scraper do ES cria dentro das pastas de jogos.
        static readonly HashSet<string> IgnoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "images", "videos", "manuals", "media", "medias", "downloaded_images", "downloaded_videos"
        };

        public CloudLibrary(string gamesPath)
        {
            GamesPath = gamesPath;
        }

        public string GamesPath { get; }

        public IReadOnlyList<string> GetSystems()
        {
            if (!Directory.Exists(GamesPath))
                return new List<string>();

            return new DirectoryInfo(GamesPath).EnumerateDirectories()
                .Where(IsVisible)
                .Select(d => d.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<CloudGame> GetGames(string system = null)
        {
            var systems = system == null ? GetSystems() : new[] { system };
            var games = new List<CloudGame>();

            foreach (var name in systems)
            {
                var folder = new DirectoryInfo(Path.Combine(GamesPath, name));
                if (!folder.Exists)
                    continue;

                foreach (var entry in folder.EnumerateFileSystemInfos().Where(IsVisible))
                {
                    if (entry is DirectoryInfo dir)
                    {
                        if (IgnoredFolders.Contains(dir.Name))
                            continue;

                        games.Add(new CloudGame
                        {
                            System = name,
                            Name = dir.Name,
                            FullPath = dir.FullName,
                            IsDirectory = true,
                            Size = dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length),
                            LastWriteTimeUtc = dir.LastWriteTimeUtc
                        });
                    }
                    else if (entry is FileInfo file && !IgnoredFiles.Contains(file.Name) && !file.Name.EndsWith(GameInstaller.PartSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        games.Add(new CloudGame
                        {
                            System = name,
                            Name = file.Name,
                            FullPath = file.FullName,
                            Size = file.Length,
                            LastWriteTimeUtc = file.LastWriteTimeUtc
                        });
                    }
                }
            }

            return games.OrderBy(g => g.System, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Procura pelo id "sistema/nome" (sem diferenciar maiúsculas).</summary>
        public CloudGame Find(string id)
        {
            var parts = (id ?? "").Replace('\\', '/').Split(new[] { '/' }, 2);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                return null;

            return GetGames(parts[0]).FirstOrDefault(g => string.Equals(g.Name, parts[1], StringComparison.OrdinalIgnoreCase));
        }

        static bool IsVisible(FileSystemInfo info)
        {
            return (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !info.Name.StartsWith(".", StringComparison.Ordinal);
        }
    }
}
