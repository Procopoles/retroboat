using System;
using System.Collections.Generic;
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

        /// <summary>O monitor do ES gravado no retrobat.ini; 0 (o principal do Windows) sem arquivo ou sem a linha.</summary>
        public static int GetMonitorIndex(string path)
        {
            if (!File.Exists(path))
                return 0;

            bool inSection = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inSection = string.Equals(line.Substring(1, line.Length - 2).Trim(), Section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                var match = inSection ? MonitorLine.Match(raw) : Match.Empty;
                if (match.Success)
                    return int.TryParse(match.Groups[2].Value.Trim('"'), out var index) && index > 0 && index <= MaxMonitorIndex ? index : 0;
            }

            return 0;
        }

        /// <summary>
        /// Onde está o monitor <paramref name="monitorIndex"/> do ES na lista de monitores do Windows (os nomes \\.\DISPLAYn,
        /// na ordem de Screen.AllScreens). O SDL do ES põe o principal primeiro e os outros na ordem dos adaptadores.
        /// </summary>
        /// <returns>O índice em <paramref name="deviceNames"/>; -1 se esse monitor não está ligado.</returns>
        public static int ScreenFor(int monitorIndex, IReadOnlyList<string> deviceNames, int primary)
        {
            var order = Enumerable.Range(0, deviceNames.Count).Where(i => i != primary)
                .OrderBy(i => DisplayNumber(deviceNames[i])).ThenBy(i => i).ToList();
            if (primary >= 0 && primary < deviceNames.Count)
                order.Insert(0, primary);

            return monitorIndex >= 0 && monitorIndex < order.Count ? order[monitorIndex] : -1;
        }

        static int DisplayNumber(string deviceName)
        {
            var digits = Regex.Match(deviceName ?? "", @"(\d+)\s*$");
            return digits.Success && int.TryParse(digits.Groups[1].Value, out var number) ? number : int.MaxValue;
        }

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
