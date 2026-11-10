using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Retroboat.Library;
using Retroboat.Saves;
using Retroboat.Settings;

namespace Retroboat.Config
{
    /// <summary>
    /// As coleções personalizadas do ES na nuvem (M4, RB-005), uma coleção por entrada: a lista que a pessoa monta num
    /// PC ("Jogos de corrida", "Para jogar com os amigos") aparece nos outros.
    ///
    /// O ES guarda cada coleção em .emulationstation\collections: custom-&lt;nome&gt;.cfg tem um jogo por linha, e as
    /// coleções com filtro (&lt;nome&gt;.xcc) guardam só as regras. As linhas do .cfg são caminhos deste PC (absolutos,
    /// "./" a partir da pasta do RetroBat ou "~/" a partir da pasta do ES, até com "..": o ES escreve
    /// "~/../../Jogos/ps2/Jogo.iso" para uma pasta de jogos no mesmo disco). Na nuvem, um jogo da pasta de jogos vira
    /// "jogos:ps2/Jogo.iso" e volta como o caminho da pasta de jogos de cada PC; um da pasta do RetroBat fica "./...".
    /// Um jogo de fora dessas pastas só vale neste PC: fica no arquivo daqui e não vai para a nuvem.
    ///
    /// A pasta inteira é um grupo: apagar uma coleção num PC apaga nos outros; a pasta que sumiu volta da nuvem.
    /// Só grava com o ES fechado (ele regrava a coleção ao mudar um jogo nela).
    /// </summary>
    public sealed class EsCollectionsConfig : IConfigSource
    {
        public const string FolderName = "collections";
        const string GamesPrefix = "jogos:";

        readonly RetroBatInstall _install;
        readonly string _romsPath;

        /// <param name="romsPath">Pasta de jogos deste PC. Nulo: a do retroboat.ini.</param>
        public EsCollectionsConfig(RetroBatInstall install, string romsPath = null)
        {
            _install = install;
            Root = Path.Combine(install.UserEmulationStationPath, FolderName);
            _romsPath = romsPath ?? ReadRomsPath(install);
        }

        public string Name => "colecoes";
        public string DisplayName => "coleções";
        public string Root { get; }

        /// <summary>Sem pasta de jogos escolhida, os jogos das coleções não teriam para onde apontar.</summary>
        public bool IsAvailable => Directory.Exists(_install.UserEmulationStationPath) && !string.IsNullOrEmpty(_romsPath);

        public ConfigSnapshot Read()
        {
            var snapshot = new ConfigSnapshot();
            if (!Directory.Exists(Root))
                return snapshot;

            snapshot.ExistingGroups.Add(FolderName);
            foreach (var file in Directory.GetFiles(Root).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(file);
                if (IsCustomList(id))
                    snapshot.Values[id] = string.Join("\n", Lines(File.ReadAllText(file)).Select(ToPortable).Where(l => l != null));
                else if (IsFiltered(id))
                {
                    var text = Normalize(File.ReadAllText(file));
                    if (ConfigValues.HasAbsolutePath(text))
                        snapshot.Held.Add(id);
                    else
                        snapshot.Values[id] = text;
                }
            }

            return snapshot;
        }

        public string GroupOf(string id) => FolderName;

        public bool IsPortable(string id, string value)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOfAny(new[] { '\\', '/' }) >= 0 || id.Contains(".."))
                return false;

            if (IsCustomList(id))
                return value == null || Lines(value).All(l => l.StartsWith(GamesPrefix, StringComparison.Ordinal) || l.StartsWith("./", StringComparison.Ordinal));

            return IsFiltered(id) && (value == null || !ConfigValues.HasAbsolutePath(value));
        }

        public DateTime ChangedUtc(string id)
        {
            var path = Path.Combine(Root, id);
            if (File.Exists(path))
                return File.GetLastWriteTimeUtc(path);

            // Coleção apagada: a pasta mudou quando o arquivo saiu.
            return Directory.Exists(Root) ? Directory.GetLastWriteTimeUtc(Root) : DateTime.MinValue;
        }

        public IEnumerable<string> FilesFor(IEnumerable<string> ids) => ids.Select(id => Path.Combine(Root, id));

        public void Write(IReadOnlyDictionary<string, string> changes)
        {
            foreach (var change in changes)
            {
                var path = Path.Combine(Root, change.Key);
                if (change.Value == null)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    continue;
                }

                IEnumerable<string> lines;
                if (IsCustomList(change.Key))
                {
                    // Os jogos que só valem neste PC continuam na coleção daqui.
                    var localOnly = File.Exists(path) ? Lines(File.ReadAllText(path)).Where(l => ToPortable(l) == null) : Enumerable.Empty<string>();
                    lines = Lines(change.Value).Select(FromPortable).Concat(localOnly);
                }
                else
                {
                    lines = change.Value.Split('\n');
                }

                // Como o ES grava (std::endl num arquivo de texto do Windows).
                var text = string.Concat(lines.Select(l => l + "\r\n"));
                SaveSync.WriteAtomically(path, new UTF8Encoding(false).GetBytes(text));
            }
        }

        /// <summary>Uma linha do .cfg como vai para a nuvem; nulo se o jogo só vale neste PC.</summary>
        string ToPortable(string line)
        {
            var full = Resolve(line);
            if (full == null)
                return null;

            var roms = Path.GetFullPath(_romsPath).TrimEnd('\\', '/') + "\\";
            if (full.StartsWith(roms, StringComparison.OrdinalIgnoreCase))
                return GamesPrefix + full.Substring(roms.Length).Replace('\\', '/');

            var root = Path.GetFullPath(_install.Root).TrimEnd('\\', '/') + "\\";
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return "./" + full.Substring(root.Length).Replace('\\', '/');

            return null;
        }

        /// <summary>Uma linha da nuvem no formato que o ES lê neste PC.</summary>
        string FromPortable(string line)
        {
            if (line.StartsWith(GamesPrefix, StringComparison.Ordinal))
                return _romsPath.TrimEnd('\\', '/').Replace('\\', '/') + "/" + line.Substring(GamesPrefix.Length);

            return line;
        }

        /// <summary>O caminho absoluto de uma linha, como o ES resolve (Utils::FileSystem::resolveRelativePath).</summary>
        string Resolve(string line)
        {
            try
            {
                if (line.StartsWith("./", StringComparison.Ordinal) || line.StartsWith(".\\", StringComparison.Ordinal))
                    return Path.GetFullPath(Path.Combine(_install.Root, line.Substring(2)));

                if (line.StartsWith("~/", StringComparison.Ordinal) || line.StartsWith("~\\", StringComparison.Ordinal))
                    return Path.GetFullPath(Path.Combine(_install.EmulationStationPath, line.Substring(2)));

                return Path.IsPathRooted(line) && line.Length > 2 && line[1] == ':' ? Path.GetFullPath(line) : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>As linhas de jogo, como o ES lê: sem as vazias e sem as que começam com "#" ou "0".</summary>
        static IEnumerable<string> Lines(string text) =>
            Normalize(text).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#' && l[0] != '0');

        static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');

        static bool IsCustomList(string id) =>
            id.StartsWith("custom-", StringComparison.OrdinalIgnoreCase) && id.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) && id.Length > "custom-.cfg".Length;

        static bool IsFiltered(string id) => id.EndsWith(".xcc", StringComparison.OrdinalIgnoreCase) && id.Length > ".xcc".Length;

        static string ReadRomsPath(RetroBatInstall install)
        {
            try
            {
                return RetroboatSettings.Load(RetroboatSettings.GetPath(install)).RomsPath;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
