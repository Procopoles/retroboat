using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Retroboat.Library;
using Retroboat.Saves;

namespace Retroboat.Config
{
    /// <summary>Um .ini do emulador sincronizado chave a chave.</summary>
    public sealed class IniConfigFile
    {
        public IniConfigFile(string path, Func<string, string, bool> isPcOnly)
        {
            Path = path;
            IsPcOnly = isPcOnly;
        }

        /// <summary>Relativo à pasta do emulador, com barras normais ("inis/PCSX2.ini").</summary>
        public string Path { get; }

        /// <summary>(seção, chave) que nunca sai deste PC. Caminhos absolutos ficam no PC sem precisar estar aqui.</summary>
        public Func<string, string, bool> IsPcOnly { get; }

        /// <summary>
        /// Chave com a versão do formato do arquivo (no PCSX2, [UI] SettingsVersion). As chaves só vão e vêm entre PCs
        /// com a mesma versão: um emulador de outra versão zera o próprio arquivo, e o zerado não pode chegar aos outros.
        /// Nulo: arquivo sem versão.
        /// </summary>
        public (string Section, string Key)? VersionKey { get; set; }

        /// <summary>A versão quando o arquivo ainda não existe; é gravada junto ao criar o arquivo.</summary>
        public string DefaultVersion { get; set; }
    }

    /// <summary>Uma pasta do emulador em que cada arquivo vai inteiro (ex.: as configurações por jogo).</summary>
    public sealed class ConfigFolder
    {
        public ConfigFolder(string path, string pattern)
        {
            Path = path;
            Pattern = pattern;
        }

        /// <summary>Relativo à pasta do emulador, com barras normais ("gamesettings").</summary>
        public string Path { get; }

        /// <summary>Arquivos sincronizados, só os da própria pasta.</summary>
        public string Pattern { get; }
    }

    /// <summary>
    /// As configurações que um emulador grava nos próprios arquivos: o que a pessoa muda no menu do emulador e o que
    /// ele guarda por jogo. O emulatorLauncher reescreve parte disso a cada jogo a partir das opções do ES (que viajam
    /// pelo es_settings.cfg); o resto só existe nesses arquivos.
    /// </summary>
    public sealed class EmulatorConfigRule
    {
        public EmulatorConfigRule(string emulator, string displayName, string system, string folder, string[] executables)
        {
            Emulator = emulator;
            DisplayName = displayName;
            System = system;
            Folder = folder;
            Executables = executables;
        }

        /// <summary>O -emulator que o ES passa, e o nome da pasta na nuvem (retroboat\configuracoes\pcsx2).</summary>
        public string Emulator { get; }

        public string DisplayName { get; }

        /// <summary>Sistema do RetroBat (o -system que o ES passa).</summary>
        public string System { get; }

        /// <summary>Pasta do emulador, relativa a &lt;RetroBat&gt;\emulators.</summary>
        public string Folder { get; }

        /// <summary>Um destes precisa existir para o emulador contar como instalado; também é o processo que indica "aberto".</summary>
        public string[] Executables { get; }

        public List<IniConfigFile> IniFiles { get; } = new List<IniConfigFile>();
        public List<ConfigFolder> Folders { get; } = new List<ConfigFolder>();

        public bool AppliesTo(string system, string emulator)
        {
            return string.Equals(System, system, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(emulator) || string.Equals(Emulator, emulator, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class EmulatorConfigRules
    {
        /// <summary>Seções do PCSX2.ini que são deste PC.</summary>
        static readonly HashSet<string> Pcsx2PcSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Folders",            // pastas do RetroBat neste PC (o emulatorLauncher regrava a cada jogo)
            "GameList",           // pastas de jogos deste PC
            "GameListTableView",  // colunas da lista de jogos da janela
            "AutoUpdater",        // o RetroBat cuida das versões dos emuladores
            "MemoryCards",        // o emulatorLauncher escolhe os cartões de cada slot; os cartões têm o próprio sync
            "Filenames",          // a BIOS que o emulatorLauncher escolheu entre as deste PC
            "DEV9/Eth",           // placa de rede deste PC
        };

        /// <summary>Chaves do PCSX2.ini que são deste PC: janelas, monitor, placa de vídeo, dispositivo de áudio, login.</summary>
        static readonly HashSet<string> Pcsx2PcKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "UI/SetupWizardIncomplete", "UI/MainWindowGeometry", "UI/MainWindowState", "UI/DisplayWindowGeometry",
            "EmuCore/GS/Adapter", "EmuCore/GS/FullscreenMode",
            "SPU2/Output/DeviceName", "SPU2/Output/DriverName",
            "Achievements/Username", "Achievements/Token", "Achievements/LoginTimestamp"
        };

        /// <summary>
        /// PCSX2 (Qt, o padrão do RetroBat para PS2), em &lt;RetroBat&gt;\emulators\pcsx2:
        /// <list type="bullet">
        /// <item>inis\PCSX2.ini, chave a chave, menos o que é deste PC, só entre PCs com o mesmo [UI] SettingsVersion. O
        /// PCSX2 zera o arquivo (e pergunta) se essa chave falta ou é de outra versão, e o emulatorLauncher não a grava;
        /// por isso ela vai junto quando o Retroboat cria o arquivo;</item>
        /// <item>gamesettings\*.ini, as configurações por jogo (por serial e CRC do disco), inteiros;</item>
        /// <item>inputprofiles\*.ini, os perfis de controle criados no PCSX2, inteiros.</item>
        /// </list>
        /// O PCSX2 1.6 (pcsx2-16) e o núcleo libretro gravam em outro lugar e ainda não entram.
        /// </summary>
        public static readonly EmulatorConfigRule Pcsx2 = CreatePcsx2();

        public static readonly IReadOnlyList<EmulatorConfigRule> All = new[] { Pcsx2 };

        public static IReadOnlyList<EmulatorConfigRule> For(string system, string emulator) => All.Where(r => r.AppliesTo(system, emulator)).ToList();

        /// <summary>Sistemas com configurações de emulador na nuvem: o comando deles passa pelo retroboat-launch.</summary>
        public static IEnumerable<string> Systems => All.Select(r => r.System).Distinct(StringComparer.OrdinalIgnoreCase);

        static EmulatorConfigRule CreatePcsx2()
        {
            var rule = new EmulatorConfigRule("pcsx2", "PCSX2", "ps2", "pcsx2", new[] { "pcsx2-qt.exe", "pcsx2-qtx64.exe", "pcsx2-qtx64-avx2.exe" });
            rule.IniFiles.Add(new IniConfigFile("inis/PCSX2.ini",
                (section, key) => Pcsx2PcSections.Contains(section) || Pcsx2PcKeys.Contains(section + "/" + key))
            {
                // VMManager::Internal::CheckSettingsVersion, SETTINGS_VERSION = 1.
                VersionKey = ("UI", "SettingsVersion"),
                DefaultVersion = "1"
            });
            rule.Folders.Add(new ConfigFolder("gamesettings", "*.ini"));
            rule.Folders.Add(new ConfigFolder("inputprofiles", "*.ini"));
            return rule;
        }
    }

    /// <summary>
    /// Os arquivos de configuração de um emulador como fonte do ConfigSync. Entradas:
    /// <list type="bullet">
    /// <item>"ini|inis/PCSX2.ini@1|EmuCore/GS|upscale_multiplier": os valores da chave (um por linha, se ela se repete),
    /// com a versão do formato do arquivo depois do @;</item>
    /// <item>"file|gamesettings/SLUS-20312_8B52C23E.ini": o arquivo inteiro, em Base64.</item>
    /// </list>
    /// Só grava com o emulador fechado (antes e depois do jogo): ele regrava os .ini ao sair.
    /// </summary>
    public sealed class EmulatorConfigSource : IConfigSource
    {
        const string IniPrefix = "ini|";
        const string FilePrefix = "file|";

        readonly EmulatorConfigRule _rule;

        public EmulatorConfigSource(RetroBatInstall install, EmulatorConfigRule rule)
        {
            _rule = rule;
            Root = Path.Combine(install.Root, "emulators", rule.Folder);
        }

        public EmulatorConfigRule Rule => _rule;
        public string Name => _rule.Emulator;
        public string DisplayName => _rule.DisplayName;
        public string Root { get; }

        /// <summary>Instalado neste PC. Sem o emulador (ainda não baixado pelo RetroBat, pasta sumiu), nada vai nem vem.</summary>
        public bool IsAvailable => _rule.Executables.Any(exe => File.Exists(Path.Combine(Root, exe)));

        /// <summary>O emulador está aberto: os arquivos dele não podem ser trocados agora.</summary>
        public bool IsRunning()
        {
            foreach (var exe in _rule.Executables)
            {
                var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
                foreach (var process in processes)
                    process.Dispose();

                if (processes.Length > 0)
                    return true;
            }

            return false;
        }

        public ConfigSnapshot Read()
        {
            var snapshot = new ConfigSnapshot();
            foreach (var ini in _rule.IniFiles)
            {
                var path = Local(ini.Path);
                _versions.Remove(ini);
                if (!File.Exists(path))
                    continue; // Sem o arquivo, vale a versão padrão (LocalVersion).

                var values = IniDocument.Load(path).Values();
                var version = ini.VersionKey == null ? null : VersionIn(ini, values) ?? ini.DefaultVersion;
                _versions[ini] = version;
                var group = IniGroup(ini, version);
                snapshot.ExistingGroups.Add(group);
                foreach (var pair in values)
                {
                    if (ini.IsPcOnly(pair.Key.Section, pair.Key.Key) || IsVersionKey(ini, pair.Key.Section, pair.Key.Key))
                        continue;

                    var id = IniPrefix + group + "|" + pair.Key.Section + "|" + pair.Key.Key;
                    var value = string.Join("\n", pair.Value);
                    if (ConfigValues.HasAbsolutePath(value))
                        snapshot.Held.Add(id);
                    else
                        snapshot.Values[id] = value;
                }
            }

            foreach (var folder in _rule.Folders)
            {
                var path = Local(folder.Path);
                if (!Directory.Exists(path))
                    continue;

                snapshot.ExistingGroups.Add(folder.Path);
                // GetFiles("*.ini") também acha "x.ini~": o padrão é conferido de novo.
                foreach (var file in Directory.GetFiles(path, folder.Pattern).Where(f => MatchesPattern(Path.GetFileName(f), folder.Pattern)))
                    snapshot.Values[FilePrefix + folder.Path + "/" + Path.GetFileName(file)] = Convert.ToBase64String(File.ReadAllBytes(file));
            }

            return snapshot;
        }

        public string GroupOf(string id)
        {
            if (TryParseIni(id, out var ini, out var version, out _, out _))
                return IniGroup(ini, version);

            if (TryParseFile(id, out var folder, out _))
                return folder.Path;

            return "";
        }

        public bool IsPortable(string id, string value)
        {
            if (TryParseIni(id, out var ini, out var version, out var section, out var key))
                return version == LocalVersion(ini) && !IsVersionKey(ini, section, key) && !ini.IsPcOnly(section, key)
                    && !ConfigValues.HasAbsolutePath(value) && (value == null || value.IndexOf('\r') < 0);

            if (TryParseFile(id, out _, out _))
                return value == null || IsBase64(value);

            return false;
        }

        public DateTime ChangedUtc(string id)
        {
            var path = FileOf(id);
            return path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        public IEnumerable<string> FilesFor(IEnumerable<string> ids) => ids.Select(FileOf).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase);

        public void Write(IReadOnlyDictionary<string, string> changes)
        {
            foreach (var ini in _rule.IniFiles)
            {
                var ownChanges = changes.Where(c => TryParseIni(c.Key, out var target, out _, out _, out _) && target == ini).ToList();
                if (ownChanges.Count == 0)
                    continue;

                var path = Local(ini.Path);
                var document = IniDocument.Load(path);
                string version = null;
                foreach (var change in ownChanges)
                {
                    TryParseIni(change.Key, out _, out version, out var section, out var key);
                    document.Set(section, key, change.Value?.Split('\n'));
                }

                // Arquivo criado agora (ou sem versão): sem ela, o emulador zeraria tudo na primeira abertura.
                if (ini.VersionKey is (string, string) versionKey && VersionIn(ini, document.Values()) == null && version != null)
                    document.Set(versionKey.Section, versionKey.Key, new[] { version });

                SaveSync.WriteAtomically(path, document.ToBytes());
                _versions.Remove(ini);
            }

            foreach (var change in changes)
            {
                if (!TryParseFile(change.Key, out var folder, out var name))
                    continue;

                var path = Path.Combine(Local(folder.Path), name);
                if (change.Value != null)
                    SaveSync.WriteAtomically(path, Convert.FromBase64String(change.Value));
                else if (File.Exists(path))
                    File.Delete(path);
            }
        }

        string FileOf(string id)
        {
            if (TryParseIni(id, out var ini, out _, out _, out _))
                return Local(ini.Path);

            if (TryParseFile(id, out var folder, out var name))
                return Path.Combine(Local(folder.Path), name);

            return null;
        }

        string Local(string relative) => Path.Combine(Root, relative.Replace('/', '\\'));

        /// <summary>O grupo de um .ini leva a versão do formato: "inis/PCSX2.ini@1".</summary>
        static string IniGroup(IniConfigFile ini, string version) => ini.VersionKey == null ? ini.Path : ini.Path + "@" + version;

        static bool IsVersionKey(IniConfigFile ini, string section, string key) =>
            ini.VersionKey is (string, string) version && version.Section == section && version.Key == key;

        /// <summary>A versão escrita no arquivo, ou nulo se ele não tem.</summary>
        static string VersionIn(IniConfigFile ini, List<KeyValuePair<(string Section, string Key), List<string>>> values)
        {
            if (!(ini.VersionKey is (string, string) version))
                return null;

            var found = values.FirstOrDefault(v => v.Key.Section == version.Section && v.Key.Key == version.Key).Value;
            return found != null && found.Count > 0 && found[0].Length > 0 ? found[0] : null;
        }

        /// <summary>A versão do .ini deste PC: a do arquivo, ou a padrão se ele ainda não existe ou não diz.</summary>
        string LocalVersion(IniConfigFile ini)
        {
            if (ini.VersionKey == null)
                return null;

            if (!_versions.TryGetValue(ini, out var version))
            {
                var path = Local(ini.Path);
                version = (File.Exists(path) ? VersionIn(ini, IniDocument.Load(path).Values()) : null) ?? ini.DefaultVersion;
                _versions[ini] = version;
            }

            return version;
        }

        readonly Dictionary<IniConfigFile, string> _versions = new Dictionary<IniConfigFile, string>();

        bool TryParseIni(string id, out IniConfigFile ini, out string version, out string section, out string key)
        {
            ini = null;
            version = section = key = null;
            if (!id.StartsWith(IniPrefix, StringComparison.Ordinal))
                return false;

            var parts = id.Substring(IniPrefix.Length).Split(new[] { '|' }, 3);
            if (parts.Length != 3)
                return false;

            var file = parts[0];
            ini = _rule.IniFiles.FirstOrDefault(f => string.Equals(f.Path, file, StringComparison.OrdinalIgnoreCase)
                                                  || file.StartsWith(f.Path + "@", StringComparison.OrdinalIgnoreCase));
            if (ini == null || (ini.VersionKey == null) != (file.Length == ini.Path.Length))
                return false;

            version = ini.VersionKey == null ? null : file.Substring(ini.Path.Length + 1);
            section = parts[1];
            key = parts[2];

            // Uma seção ou chave com quebra de linha, "=" ou "]" viraria outra linha no .ini.
            return ini != null && key.Length > 0 && section.IndexOfAny(new[] { '\r', '\n', ']' }) < 0
                && key.IndexOfAny(new[] { '\r', '\n', '=', '[', ';', '#' }) < 0 && key.Trim() == key;
        }

        bool TryParseFile(string id, out ConfigFolder folder, out string name)
        {
            folder = null;
            name = null;
            if (!id.StartsWith(FilePrefix, StringComparison.Ordinal))
                return false;

            var relative = id.Substring(FilePrefix.Length);
            int slash = relative.LastIndexOf('/');
            if (slash <= 0)
                return false;

            var folderPath = relative.Substring(0, slash);
            name = relative.Substring(slash + 1);
            folder = _rule.Folders.FirstOrDefault(f => string.Equals(f.Path, folderPath, StringComparison.OrdinalIgnoreCase));

            // Só um nome de arquivo simples, do padrão da pasta: nada de "..\" vindo da nuvem.
            return folder != null && name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name != "." && name != ".."
                && MatchesPattern(name, folder.Pattern);
        }

        static bool MatchesPattern(string name, string pattern)
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        static bool IsBase64(string value)
        {
            try
            {
                Convert.FromBase64String(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
