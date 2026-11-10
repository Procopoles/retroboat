using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Config;
using Retroboat.Library;
using Retroboat.Media;

namespace Retroboat.Cloud
{
    public sealed class PartialDownloadsCleanup
    {
        /// <summary>"sistema/nome" de cada download pela metade tirado da lista de jogos deste PC.</summary>
        public List<string> Games { get; } = new List<string>();

        public int FilesDeleted { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Arquivo que ainda está chegando na pasta de um console: um navegador ou programa de download salvando direto no Drive
    /// (o Opera grava "Jogo.zip.opdownload" e só no fim renomeia para "Jogo.zip"). Não é jogo: não ganha marcador, linha no
    /// gamelist, capa nem informações automáticas. Quando o download termina, o arquivo com o nome final entra como qualquer
    /// jogo novo.
    /// </summary>
    public static class PartialDownloads
    {
        /// <summary>
        /// O fim do nome enquanto o arquivo é baixado: Opera, Chrome e Edge (e os outros feitos sobre o Chromium), Firefox e
        /// JDownloader, Edge antigo, Safari, µTorrent, qBittorrent, BitComet, aria2, DownThemAll, o arquivo de troca do Chrome,
        /// temporários e o do próprio Retroboat. Nenhum jogo termina assim.
        /// </summary>
        static readonly string[] Suffixes =
        {
            ".opdownload", ".crdownload", ".part", ".partial", ".download", ".!ut", ".!qb", ".bc!", ".aria2", ".dtapart", ".crswap",
            ".tmp", GameInstaller.PartSuffix
        };

        /// <summary>O nome é de um arquivo pela metade (ex.: "Jogo.zip.opdownload").</summary>
        public static bool IsPartial(string name) =>
            !string.IsNullOrEmpty(name) && Suffixes.Any(s => name.Length > s.Length && name.EndsWith(s, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// O arquivo já tem o nome final, mas o download ainda não acabou: o Firefox cria "Jogo.zip" vazio ao lado de
        /// "Jogo.zip.part", e o aria2 grava direto em "Jogo.zip" ao lado de "Jogo.zip.aria2".
        /// </summary>
        /// <param name="namesInFolder">Os nomes da pasta do console, sem diferenciar maiúsculas.</param>
        public static bool HasPartialSibling(string name, ISet<string> namesInFolder) => Suffixes.Any(s => namesInFolder.Contains(name + s));

        /// <summary>
        /// Tira deste PC o que versões anteriores criaram para downloads pela metade, que eram tratados como jogos: a entrada
        /// no índice da nuvem, o marcador vazio, a linha no gamelist e a mídia dela que nenhum outro jogo usa. Não depende do
        /// Drive (com ele fechado, o gamelist seria refeito a partir do índice). Um arquivo com conteúdo na pasta de jogos
        /// nunca é apagado.
        /// </summary>
        /// <param name="romsPath">A pasta de jogos deste PC; vazia: só o gamelist e a mídia.</param>
        public static PartialDownloadsCleanup Clean(RetroBatInstall install, string romsPath)
        {
            var result = new PartialDownloadsCleanup();

            try
            {
                CleanIndex(install, romsPath, result);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException || ex is FormatException)
            {
                result.Errors.Add("índice da nuvem: " + ex.Message);
            }

            if (!Directory.Exists(install.GamelistsPath))
                return result;

            foreach (var gamelist in Directory.GetDirectories(install.GamelistsPath).Select(d => Path.Combine(d, "gamelist.xml")).Where(File.Exists))
            {
                try
                {
                    CleanGamelist(install, gamelist, result);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
                {
                    result.Errors.Add(gamelist + ": " + ex.Message);
                }
            }

            return result;
        }

        static void CleanIndex(RetroBatInstall install, string romsPath, PartialDownloadsCleanup result)
        {
            var path = CloudLibrarySync.IndexPath(install);
            if (!File.Exists(path))
                return;

            var index = CloudIndex.Load(path);
            var partial = index.Entries.Where(e => IsPartial(e.CloudName) || IsPartial(e.LocalName)).ToList();
            if (partial.Count == 0)
                return;

            foreach (var entry in partial)
            {
                if (!string.IsNullOrEmpty(romsPath))
                {
                    var marker = new FileInfo(Path.Combine(romsPath, entry.System, entry.LocalName));
                    if (marker.Exists && marker.Length == 0)
                    {
                        marker.Delete();
                        result.FilesDeleted++;
                    }
                }

                index.Remove(entry);
            }

            index.Save();
        }

        static void CleanGamelist(RetroBatInstall install, string path, PartialDownloadsCleanup result)
        {
            var doc = XDocument.Load(path);
            var partial = doc.Root.Elements("game").Where(g => IsPartial(FileName((string)g.Element("path")))).ToList();
            if (partial.Count == 0)
                return;

            var system = Path.GetFileName(Path.GetDirectoryName(path));
            var media = partial.SelectMany(g => MediaFiles(install, g)).ToList();
            foreach (var game in partial)
            {
                result.Games.Add(system + "/" + FileName((string)game.Element("path")));
                game.Remove();
            }

            var stillUsed = new HashSet<string>(doc.Root.Elements("game").SelectMany(g => MediaFiles(install, g)), StringComparer.OrdinalIgnoreCase);
            ConfigSync.WriteXml(path, doc);

            // Só a mídia que fica na pasta do ES (.emulationstation\media), nunca algo da pasta de jogos.
            var mediaRoot = Path.GetFullPath(Path.Combine(install.UserEmulationStationPath, "media")).TrimEnd('\\') + "\\";
            foreach (var file in media.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (stillUsed.Contains(file) || !file.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                    continue;

                File.Delete(file);
                result.FilesDeleted++;
            }
        }

        /// <summary>Os arquivos de mídia de uma linha do gamelist que ficam na pasta do ES ("~/...").</summary>
        static IEnumerable<string> MediaFiles(RetroBatInstall install, XElement game)
        {
            foreach (var element in game.Elements().Where(e => GameMedia.MediaFields.ContainsKey(e.Name.LocalName)))
            {
                var value = element.Value.Trim();
                if (!value.StartsWith("~/", StringComparison.Ordinal) && !value.StartsWith("~\\", StringComparison.Ordinal))
                    continue;

                string full;
                try
                {
                    full = GameMedia.Resolve(value, install.EmulationStationPath, install);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    continue;
                }

                yield return full;
            }
        }

        static string FileName(string gamelistPath)
        {
            if (string.IsNullOrEmpty(gamelistPath))
                return null;

            var normalized = gamelistPath.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        }
    }
}
