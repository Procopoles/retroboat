using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Retroboat.Library;
using Retroboat.Saves;

namespace Retroboat.Config
{
    /// <summary>Um emulador cujas configurações vão para a nuvem: com regra própria (PCSX2) ou pela regra genérica.</summary>
    public interface IEmulatorConfigSource : IConfigSource
    {
        /// <summary>Pasta do emulador em &lt;RetroBat&gt;\emulators.</summary>
        string Folder { get; }

        /// <summary>O emulador está aberto: os arquivos dele não podem ser trocados agora.</summary>
        bool IsRunning();
    }

    /// <summary>
    /// A regra genérica, que vale para qualquer emulador em &lt;RetroBat&gt;\emulators sem regra própria. O que é
    /// configuração, o que é do PC e o que nunca sai dele é decidido pelo próprio arquivo, sem lista de emuladores:
    /// <list type="bullet">
    /// <item>entram os arquivos de configuração (.ini, .cfg, .conf, .opt, .rmp, .json, .xml, .yml, .yaml, .toml) de até
    /// 512 KB, fora das pastas que nunca são configuração (BIOS, firmware, chaves, NAND, saves, cache, shaders, jogos
    /// instalados, recursos que vêm com o emulador);</item>
    /// <item>.ini, .cfg, .conf, .opt e .rmp vão chave a chave. Ficam no PC as chaves com caminho absoluto, de monitor,
    /// janela, placa de vídeo, dispositivo e driver, as de "último" e "recente", e as de login, senha e token; linhas
    /// que não são chave (códigos de cheat, [autoexec]) vão juntas, por seção;</item>
    /// <item>os outros formatos vão inteiros, só se não tiverem caminho absoluto nem login;</item>
    /// <item>uma chave de versão do formato (SettingsVersion, ConfigVersion, version) separa os PCs: as chaves do arquivo
    /// só vão e vêm entre PCs com a mesma versão, porque um emulador de outra versão pode zerar o arquivo, e o zerado
    /// não pode chegar aos outros.</item>
    /// </list>
    /// Exceções com armadilhas próprias ganham uma regra própria (EmulatorConfigRules), como o PCSX2.
    /// </summary>
    public static class AutoEmulatorRules
    {
        public const long MaxFileBytes = 512 * 1024;

        /// <summary>Mais que isso numa pasta de emulador não é configuração: a fonte para com um aviso, em vez de levar a pasta toda.</summary>
        public const int MaxFiles = 1000;

        /// <summary>Níveis de pasta abaixo da do emulador (Dolphin: User\Config; RetroArch: config\&lt;núcleo&gt;).</summary>
        public const int MaxDepth = 6;

        /// <summary>Chave reservada para as linhas de uma seção que não são chave = valor. Uma chave de verdade nunca começa com "[".</summary>
        public const string LinesKey = "[linhas]";

        static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ini", ".cfg", ".conf", ".opt", ".rmp", ".json", ".xml", ".yml", ".yaml", ".toml"
        };

        static readonly HashSet<string> IniExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ini", ".cfg", ".conf", ".opt", ".rmp"
        };

        /// <summary>Pastas que nunca são configuração, em qualquer nível.</summary>
        static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // BIOS, firmware, chaves e o sistema do console: nunca saem do PC.
            "bios", "firmware", "keys", "sys", "system", "nand", "sdcard", "sdmc", "bis", "dev_flash", "wii",
            // Saves e memory cards: têm o próprio sync (M3).
            "saves", "savedata", "savestates", "statesaves", "states", "sstates", "memcards", "gc",
            // Jogos, updates e DLC instalados.
            "games", "game", "roms", "content", "dlc", "mlc01", "hdd0", "dev_hdd0", "load",
            // Caches, logs, capturas.
            "cache", "shadercache", "shaders", "logs", "log", "dump", "dumps", "screenshots", "snaps", "covers",
            "thumbnails", "textures", "temp", "tmp", "crashdumps", "downloads", "backup", "backups",
            // O que vem com o emulador e é igual em todo PC.
            "assets", "autoconfig", "overlays", "cheats", "database", "info", "filters", "cores", "resources",
            "resourcepacks", "translations", "lang", "languages", "locale", "locales", "i18n", "themes", "styles",
            "docs", "doc", "licenses", "platforms", "imageformats", "iconengines", "plugins", "playlists", "hash",
            "artwork", "ctrlr"
        };

        static readonly Regex PcKey = new Regex(
            @"monitor|window|geometry|adapter|device|gpu|driver|^last|recent|history|mru|hostname|ip_?address|mac_?address",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex PcSection = new Regex(
            @"^(folders|paths|directories|gamelist|gamelisttableview|autoupdater|updater|recentfiles|recent|mru)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex Secret = new Regex(
            @"password|passwd|token|secret|api_?key|username|user_name|login|cookie",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex VersionKey = new Regex(
            @"^(settings|config|configuration|ini)?_?version$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Nomes conhecidos, só para as mensagens. Os outros aparecem com o nome da pasta.</summary>
        static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "retroarch", "RetroArch" }, { "dolphin-emu", "Dolphin" }, { "dolphin-triforce", "Dolphin Triforce" },
            { "duckstation", "DuckStation" }, { "flycast", "Flycast" }, { "redream", "Redream" }, { "demul", "Demul" },
            { "ppsspp", "PPSSPP" }, { "pcsx2-16", "PCSX2 1.6" }
        };

        public static string DisplayName(string folder) => DisplayNames.TryGetValue(folder, out var name) ? name : folder;

        public static bool IsConfigFile(string name) => Extensions.Contains(Path.GetExtension(name));

        public static bool IsIniFile(string relative) => IniExtensions.Contains(Path.GetExtension(relative));

        public static bool IsSkippedFolder(string name) => SkippedFolders.Contains(name);

        /// <summary>Seção ou chave que é deste PC (hardware, janelas, caminhos, histórico) ou de uma conta.</summary>
        public static bool IsPcOnly(string section, string key) =>
            PcSection.IsMatch(section) || (key != LinesKey && (PcKey.IsMatch(key) || Secret.IsMatch(key)));

        public static bool HasSecret(string text) => text != null && Secret.IsMatch(text);

        public static bool IsVersionKey(string key) => VersionKey.IsMatch(key);

        /// <summary>
        /// Caminho de arquivo relativo à pasta do emulador, com barras normais, que pode ir e vir: dentro da pasta (nada
        /// de "..\" ou caminho absoluto vindo da nuvem), de configuração, fora das pastas que nunca são configuração.
        /// "@" e "|" separam as partes da entrada.
        /// </summary>
        public static bool IsValidRelative(string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.IndexOfAny(new[] { '\\', '@', '|', ':', '\r', '\n' }) >= 0 || relative.StartsWith("/", StringComparison.Ordinal))
                return false;

            var parts = relative.Split('/');
            if (parts.Length - 1 > MaxDepth)
                return false;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length == 0 || part == "." || part == ".." || part.Trim() != part || part.EndsWith(".", StringComparison.Ordinal)
                    || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return false;

                if (i < parts.Length - 1 && IsSkippedFolder(part))
                    return false;
            }

            return IsConfigFile(parts[parts.Length - 1]);
        }
    }

    /// <summary>
    /// Os arquivos de configuração de um emulador pela regra genérica (AutoEmulatorRules). Entradas:
    /// <list type="bullet">
    /// <item>"ini|retroarch.cfg|&lt;seção&gt;|video_shader_enable": os valores da chave (um por linha, se ela se
    /// repete). Com versão do formato: "ini|settings.ini@3|Main|...";</item>
    /// <item>"ini|User/GameSettings/GALE01.ini|OnFrame|[linhas]": as linhas da seção que não são chave;</item>
    /// <item>"file|config.json": o arquivo inteiro, em Base64.</item>
    /// </list>
    /// Cada arquivo da raiz do emulador é um grupo; nas subpastas, o grupo é a pasta. Assim, apagar uma configuração por
    /// jogo apaga nos outros PCs, e uma pasta ou arquivo principal que sumiu (emulador reinstalado) volta da nuvem.
    /// </summary>
    public sealed class AutoEmulatorConfigSource : IEmulatorConfigSource
    {
        const string IniPrefix = "ini|";
        const string FilePrefix = "file|";

        readonly Dictionary<string, string> _versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<string> _executables;

        public AutoEmulatorConfigSource(RetroBatInstall install, string folder)
        {
            Folder = folder;
            Root = Path.Combine(install.Root, "emulators", folder);
        }

        public string Folder { get; }
        public string Name => Folder.ToLowerInvariant();
        public string DisplayName => AutoEmulatorRules.DisplayName(Folder);
        public string Root { get; }

        /// <summary>
        /// Com o programa do emulador na pasta. Antes de o RetroBat baixá-lo, a pasta só tem os modelos de configuração
        /// do RetroBat, e nada vai nem vem: o emulador recém-baixado recebe tudo da nuvem depois do primeiro jogo.
        /// </summary>
        public bool IsAvailable => Executables().Count > 0;

        public bool IsRunning()
        {
            foreach (var name in Executables().Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var process in processes)
                    process.Dispose();

                if (processes.Length > 0)
                    return true;
            }

            return false;
        }

        /// <summary>Os .exe da pasta do emulador e das subpastas diretas.</summary>
        List<string> Executables()
        {
            if (_executables != null)
                return _executables;

            var found = new List<string>();
            if (Directory.Exists(Root))
            {
                found.AddRange(Directory.GetFiles(Root, "*.exe"));
                foreach (var dir in Directory.GetDirectories(Root))
                {
                    if (!AutoEmulatorRules.IsSkippedFolder(Path.GetFileName(dir)))
                        found.AddRange(Directory.GetFiles(dir, "*.exe"));
                }
            }

            _executables = found.Where(f => string.Equals(Path.GetExtension(f), ".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            return _executables;
        }

        public ConfigSnapshot Read()
        {
            var snapshot = new ConfigSnapshot();
            _versions.Clear();
            var files = new List<string>();
            Walk(Root, "", 0, files, snapshot.ExistingGroups);
            if (files.Count > AutoEmulatorRules.MaxFiles)
                throw new InvalidOperationException("a pasta " + Root + " tem " + files.Count + " arquivos de configuração (mais de " + AutoEmulatorRules.MaxFiles + "); nada foi sincronizado");

            foreach (var relative in files)
            {
                var path = Local(relative);
                if (AutoEmulatorRules.IsIniFile(relative))
                    ReadIni(relative, path, snapshot);
                else
                    ReadWhole(relative, path, snapshot);
            }

            return snapshot;
        }

        /// <summary>Arquivos de configuração e pastas (grupos) que existem, sem entrar nas pastas que nunca são configuração.</summary>
        void Walk(string dir, string relative, int depth, List<string> files, HashSet<string> groups)
        {
            if (depth > 0)
                groups.Add(DirGroup(relative));

            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(file);
                var fileRelative = relative.Length == 0 ? name : relative + "/" + name;
                if (!AutoEmulatorRules.IsValidRelative(fileRelative))
                    continue;

                var info = new FileInfo(file);
                if (info.Length > AutoEmulatorRules.MaxFileBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                files.Add(fileRelative);
                if (depth == 0)
                    groups.Add(FileGroup(fileRelative));
            }

            if (depth >= AutoEmulatorRules.MaxDepth)
                return;

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (AutoEmulatorRules.IsSkippedFolder(name) || (new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                Walk(sub, relative.Length == 0 ? name : relative + "/" + name, depth + 1, files, groups);
            }
        }

        void ReadIni(string relative, string path, ConfigSnapshot snapshot)
        {
            var document = IniDocument.Load(path);
            var values = document.Values();
            var version = VersionIn(values);
            if (version != null && !IsValidVersion(version))
                return; // Versão com caracteres que não cabem na entrada: o arquivo fica no PC.

            _versions[relative] = version;
            var file = version == null ? relative : relative + "@" + version;
            foreach (var pair in values)
            {
                var (section, key) = pair.Key;
                if (!IsValidSection(section) || !IsValidKey(key) || AutoEmulatorRules.IsVersionKey(key) || AutoEmulatorRules.IsPcOnly(section, key))
                    continue;

                Add(snapshot, IniPrefix + file + "|" + section + "|" + key, string.Join("\n", pair.Value));
            }

            foreach (var pair in document.RawLines())
            {
                if (IsValidSection(pair.Key) && !AutoEmulatorRules.IsPcOnly(pair.Key, AutoEmulatorRules.LinesKey))
                    Add(snapshot, IniPrefix + file + "|" + pair.Key + "|" + AutoEmulatorRules.LinesKey, string.Join("\n", pair.Value));
            }
        }

        static void Add(ConfigSnapshot snapshot, string id, string value)
        {
            if (ConfigValues.HasAbsolutePath(value) || (id.EndsWith("|" + AutoEmulatorRules.LinesKey, StringComparison.Ordinal) && AutoEmulatorRules.HasSecret(value)))
                snapshot.Held.Add(id);
            else
                snapshot.Values[id] = value;
        }

        void ReadWhole(string relative, string path, ConfigSnapshot snapshot)
        {
            var bytes = File.ReadAllBytes(path);
            var id = FilePrefix + relative;
            if (IsPortableContent(bytes))
                snapshot.Values[id] = Convert.ToBase64String(bytes);
            else
                snapshot.Held.Add(id);
        }

        /// <summary>Um arquivo inteiro só vai sem caminho absoluto e sem login: nele não dá para separar o que é do PC.</summary>
        static bool IsPortableContent(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            return !AutoEmulatorRules.HasSecret(text) && !AbsolutePathAnywhere.IsMatch(text);
        }

        /// <summary>
        /// "C:\", "C:/" ou "\\servidor" em qualquer lugar do texto, também escapados como em JSON e TOML ("C:\\", "\\\\servidor").
        /// A letra do disco não pode vir depois de outra letra: "http://" não é caminho.
        /// </summary>
        static readonly Regex AbsolutePathAnywhere = new Regex(
            @"(?<![A-Za-z0-9])[A-Za-z]:[\\/]|(?:^|[""'=\s>])(?:\\\\){1,2}[A-Za-z0-9]",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);

        public string GroupOf(string id)
        {
            if (TryParseIni(id, out var relative, out _, out _, out _))
                return GroupOfFile(relative);

            if (TryParseFile(id, out relative))
                return GroupOfFile(relative);

            return "";
        }

        /// <summary>Na raiz do emulador, o próprio arquivo (retroarch.cfg); numa subpasta, a pasta (config/Flycast).</summary>
        static string GroupOfFile(string relative)
        {
            int slash = relative.LastIndexOf('/');
            return slash < 0 ? FileGroup(relative) : DirGroup(relative.Substring(0, slash));
        }

        static string FileGroup(string relative) => relative;

        static string DirGroup(string relative) => relative + "/";

        public bool IsPortable(string id, string value)
        {
            if (TryParseIni(id, out var relative, out var version, out var section, out var key))
            {
                // Arquivo que não existe neste PC só é criado sem versão: com versão, espera o emulador criá-lo.
                return version == LocalVersion(relative) && !AutoEmulatorRules.IsVersionKey(key) && !AutoEmulatorRules.IsPcOnly(section, key)
                    && !ConfigValues.HasAbsolutePath(value) && (value == null || value.IndexOf('\r') < 0)
                    && (key != AutoEmulatorRules.LinesKey || !AutoEmulatorRules.HasSecret(value));
            }

            if (TryParseFile(id, out _))
            {
                if (value == null)
                    return true;

                try
                {
                    return IsPortableContent(Convert.FromBase64String(value));
                }
                catch (FormatException)
                {
                    return false;
                }
            }

            return false;
        }

        public DateTime ChangedUtc(string id)
        {
            var path = FileOf(id);
            return path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        public IEnumerable<string> FilesFor(IEnumerable<string> ids) =>
            ids.Select(FileOf).Where(p => p != null && File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase);

        public void Write(IReadOnlyDictionary<string, string> changes)
        {
            var ini = new Dictionary<string, List<(string Section, string Key, string Value)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
            {
                if (TryParseIni(change.Key, out var relative, out _, out var section, out var key))
                {
                    if (!ini.TryGetValue(relative, out var list))
                        ini[relative] = list = new List<(string, string, string)>();
                    list.Add((section, key, change.Value));
                }
                else if (TryParseFile(change.Key, out relative))
                {
                    var path = Local(relative);
                    if (change.Value != null)
                        SaveSync.WriteAtomically(path, Convert.FromBase64String(change.Value));
                    else if (File.Exists(path))
                        File.Delete(path);
                }
            }

            foreach (var file in ini)
            {
                var path = Local(file.Key);
                var document = IniDocument.Load(path);
                foreach (var (section, key, value) in file.Value)
                {
                    var lines = value?.Split('\n');
                    if (key == AutoEmulatorRules.LinesKey)
                        document.SetLines(section, lines);
                    else
                        document.Set(section, key, lines);
                }

                // Apagado no outro PC (todas as chaves removidas): apaga aqui também, em vez de deixar um arquivo vazio.
                if (document.Values().Count == 0 && document.RawLines().Count == 0 && file.Value.All(c => c.Value == null))
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                else
                {
                    SaveSync.WriteAtomically(path, document.ToBytes());
                }
            }

            _versions.Clear();
        }

        string FileOf(string id)
        {
            if (TryParseIni(id, out var relative, out _, out _, out _) || TryParseFile(id, out relative))
                return Local(relative);

            return null;
        }

        string Local(string relative) => Path.Combine(Root, relative.Replace('/', '\\'));

        /// <summary>A versão do formato no .ini deste PC; nula se ele não tem versão ou ainda não existe.</summary>
        string LocalVersion(string relative)
        {
            if (_versions.TryGetValue(relative, out var version))
                return version;

            var path = Local(relative);
            version = File.Exists(path) ? VersionIn(IniDocument.Load(path).Values()) : null;
            _versions[relative] = version;
            return version;
        }

        static string VersionIn(List<KeyValuePair<(string Section, string Key), List<string>>> values)
        {
            foreach (var pair in values)
            {
                if (AutoEmulatorRules.IsVersionKey(pair.Key.Key) && pair.Value.Count > 0 && pair.Value[0].Length > 0)
                    return pair.Value[0];
            }

            return null;
        }

        static bool IsValidVersion(string version) => version.Length <= 64 && version.IndexOfAny(new[] { '|', '@', '\n', '\r' }) < 0;

        // Uma seção ou chave com quebra de linha, "|", "=" ou "]" viraria outra linha no .ini ou outra entrada.
        static bool IsValidSection(string section) => section.IndexOfAny(new[] { '\r', '\n', ']', '|' }) < 0;

        static bool IsValidKey(string key) =>
            key.Length > 0 && key.IndexOfAny(new[] { '\r', '\n', '=', '[', ';', '#' }) < 0 && key.Trim() == key;

        bool TryParseIni(string id, out string relative, out string version, out string section, out string key)
        {
            relative = version = section = key = null;
            if (!id.StartsWith(IniPrefix, StringComparison.Ordinal))
                return false;

            var parts = id.Substring(IniPrefix.Length).Split(new[] { '|' }, 3);
            if (parts.Length != 3)
                return false;

            var file = parts[0];
            int at = file.IndexOf('@');
            relative = at < 0 ? file : file.Substring(0, at);
            version = at < 0 ? null : file.Substring(at + 1);
            section = parts[1];
            key = parts[2];

            return AutoEmulatorRules.IsValidRelative(relative) && AutoEmulatorRules.IsIniFile(relative)
                && (version == null || (version.Length > 0 && IsValidVersion(version)))
                && IsValidSection(section) && (key == AutoEmulatorRules.LinesKey || IsValidKey(key));
        }

        static bool TryParseFile(string id, out string relative)
        {
            relative = null;
            if (!id.StartsWith(FilePrefix, StringComparison.Ordinal))
                return false;

            relative = id.Substring(FilePrefix.Length);
            return AutoEmulatorRules.IsValidRelative(relative) && !AutoEmulatorRules.IsIniFile(relative);
        }
    }
}
