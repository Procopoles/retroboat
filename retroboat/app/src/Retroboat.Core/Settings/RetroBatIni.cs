using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Retroboat.Library;

namespace Retroboat.Settings
{
    /// <summary>
    /// RB-029: o retrobat.ini do RetroBat. O Retroboat só muda [EmulationStation] MonitorIndex, o monitor do ES: o
    /// retrobat.exe passa --monitor N ao ES quando N &gt; 0, e o ES passa -monitor N aos emuladores. 0 = o monitor principal
    /// do Windows. O resto do arquivo fica como estava: comentários, ordem, BOM e fim de linha.
    /// </summary>
    public static class RetroBatIni
    {
        public const string FileName = "retrobat.ini";
        public const string Section = "EmulationStation";
        public const string MonitorIndexKey = "MonitorIndex";

        /// <summary>O ES numera os monitores a partir de 0; nenhum PC tem mais que isto.</summary>
        public const int MaxMonitorIndex = 15;

        static readonly Regex MonitorLine = new Regex(@"^(\s*MonitorIndex\s*=\s*)(.*?)(\s*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string PathFor(RetroBatInstall install) => Path.Combine(install.Root, FileName);

        /// <summary>
        /// Grava o monitor do ES. Não cria o retrobat.ini: quem cria é o retrobat.exe, com todas as opções; um arquivo só com
        /// esta deixaria as outras no padrão dele, que pode não ser o do arquivo de fábrica.
        /// </summary>
        /// <returns>true se o arquivo mudou.</returns>
        public static bool SetMonitorIndex(string path, int index)
        {
            if (index < 0 || index > MaxMonitorIndex)
                throw new ArgumentOutOfRangeException(nameof(index), index, "O monitor vai de 0 a " + MaxMonitorIndex + ".");

            if (!File.Exists(path))
                throw new FileNotFoundException("retrobat.ini não encontrado: " + path, path);

            var bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            var newLine = text.Contains("\r\n") || !text.Contains("\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            var value = index.ToString();

            int header = -1, lastContent = -1;
            bool done = false;
            for (int i = 0; i < lines.Count && !done; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    if (header >= 0)
                        break; // fim da seção, sem a chave

                    if (string.Equals(trimmed.Substring(1, trimmed.Length - 2).Trim(), Section, StringComparison.OrdinalIgnoreCase))
                        header = lastContent = i;

                    continue;
                }

                if (header < 0)
                    continue;

                if (trimmed.Length > 0)
                    lastContent = i;

                var match = MonitorLine.Match(lines[i]);
                if (match.Success)
                {
                    lines[i] = match.Groups[1].Value + value + match.Groups[3].Value;
                    done = true;
                }
            }

            if (!done && header >= 0)
            {
                lines.Insert(lastContent + 1, MonitorIndexKey + "=" + value);
            }
            else if (!done)
            {
                // Antes do "" que sobra do último fim de linha.
                int end = lines.Count > 0 && lines[lines.Count - 1].Length == 0 ? lines.Count - 1 : lines.Count;
                if (end > 0 && lines[end - 1].Trim().Length > 0)
                    lines.Insert(end++, "");

                lines.Insert(end, "[" + Section + "]");
                lines.Insert(end + 1, MonitorIndexKey + "=" + value);
                if (end + 2 == lines.Count)
                    lines.Add("");
            }

            var body = Encoding.UTF8.GetBytes(string.Join(newLine, lines));
            var result = bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
            if (result.SequenceEqual(bytes))
                return false;

            File.WriteAllBytes(path, result);
            return true;
        }
    }
}
