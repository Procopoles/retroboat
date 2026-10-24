using System;
using System.IO;

namespace Retroboat.Library
{
    /// <summary>Caminhos de uma instalação do RetroBat.</summary>
    public sealed class RetroBatInstall
    {
        public RetroBatInstall(string root)
        {
            Root = Path.GetFullPath(root).TrimEnd('\\', '/');
        }

        public string Root { get; }

        public string RetrobatExePath => Path.Combine(Root, "retrobat.exe");
        public string DefaultRomsPath => Path.Combine(Root, "roms");
        public string EmulationStationPath => Path.Combine(Root, "emulationstation");
        public string UserEmulationStationPath => Path.Combine(EmulationStationPath, ".emulationstation");
        public string GamelistsPath => Path.Combine(UserEmulationStationPath, "gamelists");
        public string EmulatorLauncherConfigPath => Path.Combine(EmulationStationPath, "emulatorLauncher.cfg");

        /// <summary>Dados do Retroboat nesta instalação (configuração local do PC, logs).</summary>
        public string RetroboatDataPath => Path.Combine(Root, "retroboat");

        /// <summary>
        /// O arquivo de sistemas que o ES usa como principal: es_systems_custom.cfg tem prioridade
        /// sobre es_systems.cfg (SystemData::getConfigPath).
        /// </summary>
        public string SystemsConfigPath
        {
            get
            {
                foreach (var candidate in new[]
                {
                    Path.Combine(UserEmulationStationPath, "es_systems_custom.cfg"),
                    Path.Combine(UserEmulationStationPath, "es_systems.cfg"),
                    Path.Combine(EmulationStationPath, "es_systems.cfg")
                })
                {
                    if (File.Exists(candidate))
                        return candidate;
                }

                return null;
            }
        }

        /// <summary>
        /// Acha a raiz do RetroBat a partir da pasta do executável: a própria pasta ou a pasta-mãe
        /// (quando o Retroboat fica em &lt;RetroBat&gt;\retroboat).
        /// </summary>
        public static RetroBatInstall FindFrom(string directory)
        {
            var dir = new DirectoryInfo(directory);
            for (int i = 0; i < 2 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "retrobat.exe")))
                    return new RetroBatInstall(dir.FullName);
            }

            return null;
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\', '/'),
                Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
