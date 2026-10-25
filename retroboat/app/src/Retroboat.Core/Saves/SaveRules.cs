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

        public bool AppliesTo(string system, string emulator)
        {
            return string.Equals(System, system, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(emulator) || Emulators.Contains(emulator));
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
            Validate = Ps2MemoryCard.Validate
        };

        public static readonly IReadOnlyList<SaveRule> All = new[] { Pcsx2MemoryCards };

        public static IReadOnlyList<SaveRule> For(string system, string emulator) => All.Where(r => r.AppliesTo(system, emulator)).ToList();

        /// <summary>Sistemas que têm saves sincronizados: o comando deles passa pelo retroboat-launch.</summary>
        public static IEnumerable<string> Systems => All.Select(r => r.System).Distinct(StringComparer.OrdinalIgnoreCase);

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
