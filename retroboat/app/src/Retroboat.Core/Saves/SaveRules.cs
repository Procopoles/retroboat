using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Library;

namespace Retroboat.Saves
{
    /// <summary>
    /// Uma pasta de saves que o Retroboat sincroniza: quem decide o que vai nela é o emulador, o Retroboat
    /// só leva cada arquivo para a nuvem e traz de volta.
    /// </summary>
    public sealed class SaveRule
    {
        public SaveRule(string system, string displayName, string itemName, string folder, string pattern, params string[] emulators)
        {
            System = system;
            DisplayName = displayName;
            ItemName = itemName;
            Folder = folder;
            Pattern = pattern;
            Emulators = new HashSet<string>(emulators, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Sistema do RetroBat (o -system que o ES passa), ex.: ps2.</summary>
        public string System { get; }

        public string DisplayName { get; }

        /// <summary>Como o usuário chama cada arquivo, ex.: "memory card".</summary>
        public string ItemName { get; }

        /// <summary>Pasta relativa a &lt;RetroBat&gt;\saves; a mesma estrutura vale na nuvem, em retroboat\saves.</summary>
        public string Folder { get; }

        /// <summary>Arquivos sincronizados (só os da própria pasta, sem subpastas).</summary>
        public string Pattern { get; }

        /// <summary>Emuladores que gravam nesta pasta (o -emulator que o ES passa).</summary>
        public ISet<string> Emulators { get; }

        /// <summary>
        /// Confere o conteúdo antes de enviar ou de colocar no lugar do arquivo local: nulo se está bom, senão o
        /// motivo. Sem validador, só arquivos vazios ou zerados são recusados.
        /// </summary>
        public Func<byte[], string> Validate { get; set; }

        /// <summary>
        /// Arquivo novo que o emulador criou em branco (ex.: memory card sem formatar): não tem save, então não
        /// vai para a nuvem, mas também não é danificado. Sem isso, um cartão em branco seria tratado como defeito.
        /// </summary>
        public Func<byte[], bool> IsBlankNew { get; set; }

        /// <summary>
        /// A pasta inteira, com as subpastas: o nome de cada arquivo é o caminho relativo a ela ("mednafen\Jogo.srm").
        /// Sem isso, só os arquivos da própria pasta.
        /// </summary>
        public bool Recursive { get; set; }

        /// <summary>Subpastas (relativas à pasta da regra) que outra regra sincroniza, ex.: pcsx2\memcards dentro de ps2.</summary>
        public IReadOnlyList<string> ExcludedFolders { get; set; } = new string[0];

        /// <summary>Nomes de pasta, em qualquer nível, que nunca são saves (Recursive).</summary>
        public ISet<string> SkippedFolderNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Arquivos maiores que isso ficam de fora (nulo: sem limite).</summary>
        public long? MaxFileBytes { get; set; }

        /// <summary>
        /// Só entra o que mudou neste PC depois que a regra começou a valer aqui (ou o que já está na nuvem). O que veio
        /// com o RetroBat (NVRAM, EEPROM e configurações de fábrica na pasta saves) nunca vai para a nuvem e, num PC
        /// novo, é trocado pela versão da nuvem sem perguntar.
        /// </summary>
        public bool OnlyChangedHere { get; set; }

        /// <summary>Cópias locais guardadas de cada arquivo antes de substituir.</summary>
        public int SnapshotsKept { get; set; } = 20;

        /// <summary>
        /// Versões de cada arquivo mantidas na nuvem; nulo: todas. Cada PC apaga só as próprias versões antigas, e a
        /// mais nova nunca sai.
        /// </summary>
        public int? CloudVersionsKept { get; set; }

        /// <summary>Pergunta se abre o Google Drive fechado antes do jogo. Nas regras genéricas, só quando o sistema já tem saves na nuvem.</summary>
        public bool AlwaysAskForCloud { get; set; }

        public bool AppliesTo(string system, string emulator)
        {
            return string.Equals(System, system, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(emulator) || Emulators.Count == 0 || Emulators.Contains(emulator));
        }
    }

    public static class SaveRules
    {
        /// <summary>
        /// Memory cards do PCSX2: o emulatorLauncher aponta o PCSX2 para saves\ps2\pcsx2\memcards e escolhe o
        /// arquivo de cada slot (Mcd001.ps2, Mcd002.ps2 ou &lt;jogo&gt;.ps2). Os cartões criados no próprio
        /// PCSX2 também ficam lá. Cartões em pasta (Mcdf01.ps2) ainda não são sincronizados.
        /// </summary>
        public static readonly SaveRule Pcsx2MemoryCards = new SaveRule("ps2", "PlayStation 2", "memory card", @"ps2\pcsx2\memcards", "*.ps2", "pcsx2")
        {
            Validate = Ps2MemoryCard.Validate,
            IsBlankNew = Ps2MemoryCard.IsUnformatted,
            AlwaysAskForCloud = true
        };

        /// <summary>Regras próprias (com conferência do conteúdo). O resto de cada sistema vai pela regra genérica.</summary>
        public static readonly IReadOnlyList<SaveRule> All = new[] { Pcsx2MemoryCards };

        /// <summary>
        /// Sistemas fora da regra genérica por enquanto: os emuladores deles guardam, junto com os saves, jogos
        /// instalados, updates, DLC, firmware ou as chaves do console (RPCS3, Ryujinx e derivados, Cemu, Xenia, xemu,
        /// Vita3K, shadPS4, Citra e derivados). Cada um precisa de uma regra própria que pegue só os saves.
        /// </summary>
        public static readonly ISet<string> SystemsNotYetSupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ps3", "switch", "wiiu", "xbox360", "xbox", "chihiro", "psvita", "ps4", "3ds", "n3ds"
        };

        /// <summary>Pastas que nunca são saves, em qualquer nível da pasta de um sistema.</summary>
        static readonly string[] SkippedFolderNames =
        {
            // Firmware, chaves e jogos instalados.
            "bios", "firmware", "keys", "registered", "game", "games", "dlc",
            // Capturas, caches e o que o emulador baixa ou gera de novo.
            "covers", "screenshots", "snaps", "cache", "shadercache", "shaders", "textures", "dump", "dumps", "load",
            "logs", "log", "temp", "tmp", "crashdumps",
            // Configurações de controle e arte do MAME, que vêm com o RetroBat.
            "ctrlr", "artwork",
            // As pastas que o próprio Retroboat cria na nuvem.
            SaveSync.PcsFolderName, SaveSync.ConflictsFolderName
        };

        static readonly Dictionary<string, string> SystemNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "psx", "PlayStation" }, { "ps2", "PlayStation 2" }, { "psp", "PSP" }, { "gamecube", "GameCube" }, { "wii", "Wii" },
            { "dreamcast", "Dreamcast" }, { "naomi", "Naomi" }, { "naomi2", "Naomi 2" }, { "atomiswave", "Atomiswave" },
            { "saturn", "Saturn" }, { "n64", "Nintendo 64" }, { "snes", "Super Nintendo" }, { "nes", "NES" }, { "gba", "Game Boy Advance" },
            { "gb", "Game Boy" }, { "gbc", "Game Boy Color" }, { "nds", "Nintendo DS" }, { "megadrive", "Mega Drive" }, { "mame", "MAME" }
        };

        public const long GenericMaxFileBytes = 256L * 1024 * 1024;

        /// <summary>
        /// Todos os saves de um sistema (saves\&lt;sistema&gt;, com as subpastas de cada emulador: saves do jogo, memory
        /// cards, VMUs, save states), menos as pastas de uma regra própria. Vale para qualquer sistema e emulador; o
        /// conteúdo não é conferido, só arquivos vazios ou zerados são recusados.
        /// </summary>
        public static SaveRule Generic(string system)
        {
            var displayName = SystemNames.TryGetValue(system, out var name) ? name : system;
            var excluded = All.Where(r => r.Folder.StartsWith(system + "\\", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Folder.Substring(system.Length + 1)).ToList();

            return new SaveRule(system.ToLowerInvariant(), displayName, "save", system.ToLowerInvariant(), "*")
            {
                Recursive = true,
                ExcludedFolders = excluded,
                SkippedFolderNames = new HashSet<string>(SkippedFolderNames, StringComparer.OrdinalIgnoreCase),
                MaxFileBytes = GenericMaxFileBytes,
                OnlyChangedHere = true,
                SnapshotsKept = 5,
                CloudVersionsKept = 10
            };
        }

        public static bool HasGenericRule(string system) =>
            !string.IsNullOrWhiteSpace(system) && !SystemsNotYetSupported.Contains(system) && system.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && system != "." && system != "..";

        /// <summary>As regras de um jogo: as próprias do emulador e a genérica do sistema.</summary>
        public static IReadOnlyList<SaveRule> For(string system, string emulator)
        {
            var rules = All.Where(r => r.AppliesTo(system, emulator)).ToList();
            if (HasGenericRule(system))
                rules.Add(Generic(system));

            return rules;
        }

        /// <summary>As regras próprias e a genérica de cada sistema com pasta em saves\ neste PC ou na nuvem.</summary>
        public static IReadOnlyList<SaveRule> AllFor(string savesPath, string cloudSavesPath)
        {
            var systems = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { savesPath, cloudSavesPath })
            {
                if (root != null && Directory.Exists(root))
                    systems.UnionWith(Directory.GetDirectories(root).Select(Path.GetFileName));
            }

            var rules = new List<SaveRule>(All);
            rules.AddRange(systems.Where(HasGenericRule).Select(Generic));
            return rules;
        }

        /// <summary>
        /// A pasta saves que o emulatorLauncher usa: a chave saves= do emulatorLauncher.cfg, relativa à pasta do
        /// ES, ou &lt;RetroBat&gt;\saves.
        /// </summary>
        public static string GetSavesPath(RetroBatInstall install)
        {
            var config = install.EmulatorLauncherConfigPath;
            var value = File.Exists(config) ? KeyValueConfig.GetValue(File.ReadAllText(config), "saves") : null;
            if (string.IsNullOrWhiteSpace(value))
                return Path.Combine(install.Root, "saves");

            return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(install.EmulationStationPath, value));
        }

        /// <summary>
        /// Cartões que o PCSX2 está usando nos slots, pelo PCSX2.ini que o emulatorLauncher acabou de gravar.
        /// Durante o jogo, esses são conferidos pelo conteúdo a cada passada (o Windows pode demorar a
        /// atualizar a data de um arquivo que o emulador mantém aberto).
        /// </summary>
        public static IReadOnlyList<string> Pcsx2ActiveCards(RetroBatInstall install)
        {
            var ini = Path.Combine(install.Root, "emulators", "pcsx2", "inis", "PCSX2.ini");
            var cards = new List<string>();
            if (!File.Exists(ini))
                return cards;

            string section = null;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(ini))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0 && string.Equals(section, "MemoryCards", StringComparison.OrdinalIgnoreCase))
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            foreach (var slot in new[] { "Slot1", "Slot2" })
            {
                if (values.TryGetValue(slot + "_Enable", out var enabled) && enabled.Equals("true", StringComparison.OrdinalIgnoreCase)
                    && values.TryGetValue(slot + "_Filename", out var file) && file.Length > 0)
                    cards.Add(file);
            }

            return cards;
        }
    }
}
