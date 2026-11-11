using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Accounts;
using Retroboat.Cloud;
using Retroboat.Launch;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Media;

namespace Retroboat.Emulators
{
    /// <summary>Um emulador como o ES o abre: -emulator e -core (vazio quando o emulador não tem cores).</summary>
    public sealed class EmulatorChoice
    {
        public EmulatorChoice(string emulator, string core)
        {
            Emulator = emulator ?? "";
            Core = core ?? "";
        }

        public string Emulator { get; }
        public string Core { get; }

        public override string ToString() => Core.Length == 0 ? Emulator : Emulator + "/" + Core;

        /// <summary>O que o ES passou ao abrir o jogo (-emulator e -core). Nulo sem -emulator.</summary>
        public static EmulatorChoice FromArguments(string[] args)
        {
            var emulator = CommandLine.GetOption(args, "-emulator");
            return string.IsNullOrWhiteSpace(emulator) ? null : new EmulatorChoice(emulator.Trim(), CommandLine.GetOption(args, "-core")?.Trim());
        }

        /// <summary>
        /// O emulador que o ES vai usar para este jogo, antes de ele ser aberto (download junto com o jogo). Mesma ordem do ES
        /// (FileData::getEmulator/getCore): a escolha do jogo no gamelist, a do sistema no es_settings.cfg, o padrão do sistema
        /// (do overlay do Retroboat ou do es_systems.cfg). Nulo se o sistema não é achado.
        /// </summary>
        public static EmulatorChoice ForGame(RetroBatInstall install, string systemFolder, string romPath)
        {
            try
            {
                var system = FindSystem(install, systemFolder);
                if (system == null)
                    return null;

                var emulators = Definitions(OverlayEmulators(install, system.Name) ?? system.Emulators);
                ReadGameChoice(install, system.Name, romPath, out var gameEmulator, out var gameCore);

                var settingsPath = Path.Combine(install.UserEmulationStationPath, "es_settings.cfg");
                var settings = File.Exists(settingsPath) ? EsSettings.Load(settingsPath) : null;
                return Resolve(emulators, gameEmulator, gameCore, settings?.Get(system.Name + ".emulator"), settings?.Get(system.Name + ".core"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
            {
                Log.Warn("Emulador do jogo não identificado (" + systemFolder + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>A regra do ES, sem arquivos: o que é escolhido e não existe na lista do sistema é ignorado.</summary>
        internal static EmulatorChoice Resolve(IReadOnlyList<EmulatorDefinition> emulators, string gameEmulator, string gameCore, string systemEmulator, string systemCore)
        {
            if (emulators.Count == 0)
                return null;

            EmulatorDefinition Find(string name) => string.IsNullOrWhiteSpace(name) || name == "auto"
                ? null : emulators.FirstOrDefault(e => string.Equals(e.Name, name.Trim(), StringComparison.Ordinal));

            string ValidCore(EmulatorDefinition emulator, string core) => string.IsNullOrWhiteSpace(core) || core == "auto"
                ? null : emulator.Cores.FirstOrDefault(c => string.Equals(c.Name, core.Trim(), StringComparison.Ordinal))?.Name;

            var standard = emulators.FirstOrDefault(e => e.Cores.Any(c => c.IsDefault)) ?? emulators[0];
            var forGame = Find(gameEmulator);
            var forSystem = Find(systemEmulator);
            var emulator = forGame ?? forSystem ?? standard;

            // O core do sistema só vale para o emulador do sistema: um jogo com emulador próprio usa o core dele ou o padrão.
            var core = ValidCore(emulator, gameCore) ?? (forGame == null ? ValidCore(emulator, systemCore) : null);
            return new EmulatorChoice(emulator.Name, core ?? emulator.DefaultCore);
        }

        /// <summary>A lista &lt;emulators&gt; de um sistema, como o ES a lê.</summary>
        internal static IReadOnlyList<EmulatorDefinition> Definitions(XElement emulators)
        {
            if (emulators == null)
                return new EmulatorDefinition[0];

            return emulators.Elements("emulator").Select(e => new EmulatorDefinition(
                ((string)e.Attribute("name") ?? "").Trim(),
                e.Descendants("core").Select(c => new CoreDefinition(c.Value.Trim(), (string)c.Attribute("default") == "true")).Where(c => c.Name.Length > 0).ToList()))
                .Where(e => e.Name.Length > 0).ToList();
        }

        static SystemFolder FindSystem(RetroBatInstall install, string systemFolder)
        {
            var config = install.SystemsConfigPath;
            if (config == null)
                return null;

            var systems = IsolatedLibrary.FirstPerName(IsolatedLibrary.ReadSystems(config));
            return systems.FirstOrDefault(s => string.Equals(s.Folder, systemFolder, StringComparison.OrdinalIgnoreCase))
                ?? systems.FirstOrDefault(s => string.Equals(s.Name, systemFolder, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>O ES troca a lista inteira pela do overlay do Retroboat (RB-022), quando ele tem uma.</summary>
        static XElement OverlayEmulators(RetroBatInstall install, string systemName)
        {
            var overlay = Path.Combine(install.UserEmulationStationPath, IsolatedLibrary.OverlayFileName);
            if (!File.Exists(overlay))
                return null;

            return XDocument.Load(overlay).Root?.Elements("system")
                .FirstOrDefault(s => string.Equals(((string)s.Element("name"))?.Trim(), systemName, StringComparison.Ordinal))
                ?.Element("emulators");
        }

        static void ReadGameChoice(RetroBatInstall install, string systemName, string romPath, out string emulator, out string core)
        {
            emulator = core = null;
            if (string.IsNullOrEmpty(romPath))
                return;

            var gamelist = Path.Combine(install.GamelistsPath, systemName, "gamelist.xml");
            if (!File.Exists(gamelist))
                return;

            var game = CloudLibrarySync.FindGame(XDocument.Load(gamelist), Path.GetFileName(romPath));
            emulator = (string)game?.Element("emulator");
            core = (string)game?.Element("core");
        }
    }

    internal sealed class EmulatorDefinition
    {
        public EmulatorDefinition(string name, IReadOnlyList<CoreDefinition> cores)
        {
            Name = name;
            Cores = cores;
        }

        public string Name { get; }
        public IReadOnlyList<CoreDefinition> Cores { get; }

        /// <summary>O core marcado default="true", senão o primeiro (SystemData::getDefaultCore).</summary>
        public string DefaultCore => (Cores.FirstOrDefault(c => c.IsDefault) ?? Cores.FirstOrDefault())?.Name ?? "";
    }

    internal sealed class CoreDefinition
    {
        public CoreDefinition(string name, bool isDefault)
        {
            Name = name;
            IsDefault = isDefault;
        }

        public string Name { get; }
        public bool IsDefault { get; }
    }
}
