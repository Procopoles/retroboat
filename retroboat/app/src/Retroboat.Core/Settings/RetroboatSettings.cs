using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Retroboat.Library;

namespace Retroboat.Settings
{
    /// <summary>
    /// Configuração local deste PC, em &lt;RetroBat&gt;\retroboat\retroboat.ini. Não vai para a nuvem:
    /// a pasta de jogos e a letra do Drive mudam de um PC para outro.
    /// </summary>
    public sealed class RetroboatSettings
    {
        public const string FileName = "retroboat.ini";

        /// <summary>Pasta de jogos local. Vazio = ainda não escolhida (primeiro uso).</summary>
        public string RomsPath { get; set; }

        /// <summary>Pasta do Retroboat na nuvem. Vazio = detectar o Google Drive para computador.</summary>
        public string CloudPath { get; set; }

        /// <summary>Baixar também os vídeos dos jogos da nuvem (as imagens sempre vêm).</summary>
        public bool CloudVideos { get; set; }

        /// <summary>Baixar também manuais e revistas (PDF) dos jogos da nuvem.</summary>
        public bool CloudManuals { get; set; }

        /// <summary>Capas automáticas (thumbnails do libretro) para jogos sem imagem. Ligado por padrão.</summary>
        public bool AutoMedia { get; set; } = true;

        public static string GetPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, FileName);

        public static RetroboatSettings Load(string path)
        {
            var ini = ReadIni(path);
            return new RetroboatSettings
            {
                RomsPath = Get(ini, "Library", "RomsPath"),
                CloudPath = Get(ini, "Cloud", "Path"),
                CloudVideos = Get(ini, "Cloud", "Videos") == "1",
                CloudManuals = Get(ini, "Cloud", "Manuals") == "1",
                AutoMedia = Get(ini, "Media", "Automatic") != "0"
            };
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var text = new StringBuilder()
                .AppendLine("; Configuração do Retroboat neste PC (não é sincronizada).")
                .AppendLine()
                .AppendLine("[Library]")
                .AppendLine("; Pasta onde ficam os jogos. Os metadados ficam em emulationstation\\.emulationstation\\gamelists.")
                .AppendLine("RomsPath=" + (RomsPath ?? ""))
                .AppendLine()
                .AppendLine("[Cloud]")
                .AppendLine("; Pasta do Retroboat na nuvem. Vazio = detectar o Google Drive para computador.")
                .AppendLine("Path=" + (CloudPath ?? ""))
                .AppendLine("; Capas e descrições dos jogos da nuvem sempre vêm. 1 = baixar também vídeos / manuais.")
                .AppendLine("Videos=" + (CloudVideos ? "1" : "0"))
                .AppendLine("Manuals=" + (CloudManuals ? "1" : "0"))
                .AppendLine()
                .AppendLine("[Media]")
                .AppendLine("; 1 = capa, tela e título automáticos (libretro) para jogos sem imagem. 0 = desligado.")
                .AppendLine("Automatic=" + (AutoMedia ? "1" : "0"));

            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return sections;

            Dictionary<string, string> current = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    if (!sections.TryGetValue(name, out current))
                        sections[name] = current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    continue;
                }

                int equals = line.IndexOf('=');
                if (current != null && equals > 0)
                    current[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim().Trim('"');
            }

            return sections;
        }

        static string Get(Dictionary<string, Dictionary<string, string>> ini, string section, string key)
        {
            if (ini.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) && value.Length > 0)
                return value;

            return null;
        }
    }
}
