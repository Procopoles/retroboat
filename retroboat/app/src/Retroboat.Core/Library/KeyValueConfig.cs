using System;
using System.Collections.Generic;

namespace Retroboat.Library
{
    /// <summary>
    /// Edita uma chave num arquivo "chave=valor" (como o emulatorLauncher.cfg) sem mexer no resto.
    /// A linha que o Retroboat gerencia leva um comentário marcador logo acima; só uma chave com o
    /// marcador é removida quando o valor volta ao padrão, para nunca apagar algo que o usuário configurou.
    /// </summary>
    public static class KeyValueConfig
    {
        public static string SetManagedValue(string content, string key, string value, string marker)
        {
            content = content ?? "";
            string newline = content.Contains("\r\n") || content.Length == 0 ? "\r\n" : "\n";

            var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            int keyIndex = lines.FindIndex(line => IsKey(line, key));
            bool hasMarker = keyIndex > 0 && lines[keyIndex - 1].Trim() == marker;

            if (value == null)
            {
                if (keyIndex < 0 || !hasMarker)
                    return content;

                lines.RemoveAt(keyIndex);
                lines.RemoveAt(keyIndex - 1);
            }
            else
            {
                var line = key + "=" + value;
                if (keyIndex >= 0 && hasMarker && lines[keyIndex] == line)
                    return content;

                if (keyIndex >= 0)
                {
                    lines[keyIndex] = line;
                    if (!hasMarker)
                        lines.Insert(keyIndex, marker);
                }
                else
                {
                    lines.Add(marker);
                    lines.Add(line);
                }
            }

            return lines.Count == 0 ? "" : string.Join(newline, lines) + newline;
        }

        public static string GetValue(string content, string key)
        {
            foreach (var line in (content ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (IsKey(line, key))
                    return line.Substring(line.IndexOf('=') + 1).Trim();
            }

            return null;
        }

        static bool IsKey(string line, string key)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal))
                return false;

            int equals = trimmed.IndexOf('=');
            return equals > 0 && string.Equals(trimmed.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase);
        }
    }
}
