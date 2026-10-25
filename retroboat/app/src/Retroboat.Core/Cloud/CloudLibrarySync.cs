using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Library;
using Retroboat.Text;

namespace Retroboat.Cloud
{
    public sealed class CloudLibrarySyncResult
    {
        public List<string> PlaceholdersCreated { get; } = new List<string>();
        public List<string> PlaceholdersRemoved { get; } = new List<string>();
        public int InCloud { get; set; }
        public int Installed { get; set; }
        public int Corrupt { get; set; }

        /// <summary>Pastas de sistema (ex.: "ps2") que têm jogos na nuvem: o comando delas passa pelo retroboat-launch.</summary>
        public HashSet<string> SystemFolders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Faz os jogos da nuvem aparecerem no ES antes de serem baixados. Para cada jogo que só está na
    /// nuvem, cria um marcador vazio com o nome que o jogo terá ao ser instalado, e coloca no gamelist
    /// local uma descrição dizendo que ele está na nuvem. Quando o jogo é instalado, a descrição sai.
    /// Roda antes do ES abrir, porque o ES regrava os gamelists ao fechar.
    /// </summary>
    public sealed class CloudLibrarySync
    {
        public const string CloudDescriptionPrefix = "Na nuvem ·";
        public const string CorruptDescriptionPrefix = "Corrompido na nuvem:";

        readonly RetroBatInstall _install;
        readonly string _romsRoot;
        readonly CloudLibrary _library;

        public CloudLibrarySync(RetroBatInstall install, string romsRoot, CloudLibrary library)
        {
            _install = install;
            _romsRoot = romsRoot;
            _library = library;
        }

        public static string IndexPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, CloudIndex.FileName);

        public CloudLibrarySyncResult Apply()
        {
            var result = new CloudLibrarySyncResult();
            var index = CloudIndex.Load(IndexPath(_install));
            var games = _library.GetGames();
            var seen = new HashSet<CloudIndexEntry>();

            foreach (var game in games)
            {
                var entry = index.Find(game.System, game.Name);
                if (entry == null || !entry.Matches(game))
                {
                    entry = new CloudIndexEntry
                    {
                        System = game.System,
                        CloudName = game.Name,
                        LocalName = GameInstaller.GetLocalName(game),
                        CloudSize = game.Size,
                        CloudModifiedUtc = game.LastWriteTimeUtc,
                        State = CloudGameState.Cloud
                    };
                }

                var local = new FileInfo(Path.Combine(_romsRoot, game.System, entry.LocalName));
                if (local.Exists && local.Length > 0)
                {
                    entry.State = CloudGameState.Installed;
                }
                else
                {
                    if (entry.State == CloudGameState.Installed)
                        entry.State = CloudGameState.Cloud; // A cópia local foi apagada: volta a ser só da nuvem.

                    if (!local.Exists)
                    {
                        Directory.CreateDirectory(local.DirectoryName);
                        File.WriteAllBytes(local.FullName, new byte[0]);
                        result.PlaceholdersCreated.Add(game.System + "/" + entry.LocalName);
                    }
                }

                index.Set(entry);
                seen.Add(entry);
                result.SystemFolders.Add(game.System);
            }

            foreach (var gone in index.Entries.Where(e => !seen.Contains(e)).ToList())
            {
                var local = new FileInfo(Path.Combine(_romsRoot, gone.System, gone.LocalName));
                if (gone.State != CloudGameState.Installed && local.Exists && local.Length == 0)
                {
                    local.Delete();
                    result.PlaceholdersRemoved.Add(gone.System + "/" + gone.LocalName);
                }

                index.Remove(gone);
            }

            UpdateGamelists(index);
            index.Save();

            result.InCloud = index.Entries.Count(e => e.State == CloudGameState.Cloud);
            result.Installed = index.Entries.Count(e => e.State == CloudGameState.Installed);
            result.Corrupt = index.Entries.Count(e => e.State == CloudGameState.Corrupt);
            return result;
        }

        void UpdateGamelists(CloudIndex index)
        {
            var systemNames = SystemNamesByFolder();
            foreach (var group in index.Entries.GroupBy(e => e.System, StringComparer.OrdinalIgnoreCase))
            {
                var systemName = systemNames.TryGetValue(group.Key, out var name) ? name : group.Key;
                var path = Path.Combine(_install.GamelistsPath, systemName, "gamelist.xml");
                UpdateGamelist(path, group);
            }
        }

        internal static void UpdateGamelist(string path, IEnumerable<CloudIndexEntry> entries)
        {
            var doc = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("gameList"));
            var before = doc.ToString();

            foreach (var entry in entries)
            {
                var game = doc.Root.Elements("game").FirstOrDefault(g => SamePath((string)g.Element("path"), entry.LocalName));
                var current = (string)game?.Element("desc");
                bool ours = string.IsNullOrEmpty(current) || current.StartsWith(CloudDescriptionPrefix, StringComparison.Ordinal) || current.StartsWith(CorruptDescriptionPrefix, StringComparison.Ordinal);

                string description = null;
                if (entry.State == CloudGameState.Cloud)
                    description = CloudDescriptionPrefix + " " + Format.Bytes(entry.CloudSize) + " · baixa na primeira vez que você jogar.";
                else if (entry.State == CloudGameState.Corrupt)
                    description = CorruptDescriptionPrefix + " " + (entry.Problem ?? "o arquivo no Drive está danificado") + ". Substitua o arquivo no Drive para poder jogar.";

                if (description == null)
                {
                    // Instalado: tira só a descrição que o Retroboat colocou.
                    if (game != null && ours && current != null)
                        game.Element("desc").Remove();

                    continue;
                }

                if (!ours)
                    continue; // O usuário (ou o scraper) escreveu uma descrição: não sobrescreve.

                if (game == null)
                {
                    game = new XElement("game",
                        new XElement("path", "./" + entry.LocalName),
                        new XElement("name", Path.GetFileNameWithoutExtension(entry.LocalName)));
                    doc.Root.Add(game);
                }

                game.SetElementValue("desc", description);
            }

            if (doc.ToString() == before)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            doc.Save(path);
        }

        Dictionary<string, string> SystemNamesByFolder()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var config = _install.SystemsConfigPath;
            if (config == null)
                return map;

            foreach (var system in IsolatedLibrary.ReadSystems(config))
            {
                if (!map.ContainsKey(system.Folder))
                    map[system.Folder] = system.Name;
            }

            return map;
        }

        static bool SamePath(string gamelistPath, string localName)
        {
            if (string.IsNullOrEmpty(gamelistPath))
                return false;

            var normalized = gamelistPath.Replace('\\', '/');
            if (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            return string.Equals(normalized, localName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
