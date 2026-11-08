using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Retroboat.Config
{
    /// <summary>
    /// Um .ini de emulador, alterado chave a chave sem mexer no resto: ordem, comentários, linhas em branco, BOM e
    /// fim de linha ficam como estavam. Uma chave pode se repetir na seção (ex.: RecursivePaths no PCSX2.ini); os
    /// valores dela andam juntos.
    /// </summary>
    sealed class IniDocument
    {
        readonly List<string> _lines;
        readonly string _newLine;
        readonly bool _bom;

        IniDocument(List<string> lines, string newLine, bool bom)
        {
            _lines = lines;
            _newLine = newLine;
            _bom = bom;
        }

        public static IniDocument Load(string path)
        {
            if (!File.Exists(path))
                return new IniDocument(new List<string>(), "\r\n", false);

            var bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return Parse(Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
        }

        public static IniDocument Parse(string text, bool bom = false)
        {
            var newLine = text.Contains("\r\n") || !text.Contains("\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            return new IniDocument(lines, newLine, bom);
        }

        /// <summary>Seção e chave de cada valor, na ordem do arquivo.</summary>
        public List<KeyValuePair<(string Section, string Key), List<string>>> Values()
        {
            var values = new List<KeyValuePair<(string, string), List<string>>>();
            var index = new Dictionary<(string, string), List<string>>();
            string section = "";
            foreach (var line in _lines)
            {
                if (TryParseSection(line, out var name))
                {
                    section = name;
                    continue;
                }

                if (!TryParseKey(line, out var key, out var value))
                    continue;

                if (!index.TryGetValue((section, key), out var list))
                {
                    list = new List<string>();
                    index[(section, key)] = list;
                    values.Add(new KeyValuePair<(string, string), List<string>>((section, key), list));
                }

                list.Add(value);
            }

            return values;
        }

        /// <summary>Troca todos os valores da chave; nulo remove a chave. A seção é criada no fim, se faltar.</summary>
        public void Set(string section, string key, IList<string> values)
        {
            bool remove = values == null || values.Count == 0;
            int bodyStart = 0;
            if (section.Length > 0)
            {
                int header = FindSection(section);
                if (header < 0)
                {
                    if (remove)
                        return;

                    if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0)
                        _lines.Add("");

                    _lines.Add("[" + section + "]");
                    foreach (var value in values)
                        _lines.Add(Line(key, value));
                    return;
                }

                bodyStart = header + 1;
            }

            int end = NextSection(bodyStart);
            int insertAt = -1;
            for (int i = end - 1; i >= bodyStart; i--)
            {
                if (TryParseKey(_lines[i], out var existing, out _) && existing == key)
                {
                    _lines.RemoveAt(i);
                    end--;
                    insertAt = i;
                }
            }

            if (remove)
                return;

            if (insertAt < 0)
            {
                // No fim da seção, antes das linhas em branco que a separam da próxima.
                insertAt = end;
                while (insertAt > bodyStart && _lines[insertAt - 1].Trim().Length == 0)
                    insertAt--;
            }

            _lines.InsertRange(insertAt, values.Select(v => Line(key, v)));
        }

        public byte[] ToBytes()
        {
            var text = string.Join(_newLine, _lines) + (_lines.Count > 0 ? _newLine : "");
            var body = Encoding.UTF8.GetBytes(text);
            return _bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
        }

        static string Line(string key, string value) => key + " = " + value;

        /// <summary>Linha do cabeçalho da seção, ou -1 se ela não existe.</summary>
        int FindSection(string section)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                if (TryParseSection(_lines[i], out var name) && name == section)
                    return i;
            }

            return -1;
        }

        /// <summary>Primeira linha de cabeçalho a partir de <paramref name="from"/>: onde a seção atual termina.</summary>
        int NextSection(int from)
        {
            for (int i = from; i < _lines.Count; i++)
            {
                if (TryParseSection(_lines[i], out _))
                    return i;
            }

            return _lines.Count;
        }

        static bool TryParseSection(string line, out string name)
        {
            var trimmed = line.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                name = trimmed.Substring(1, trimmed.Length - 2).Trim();
                return true;
            }

            name = null;
            return false;
        }

        static bool TryParseKey(string line, out string key, out string value)
        {
            key = value = null;
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#' || trimmed[0] == '[')
                return false;

            int eq = trimmed.IndexOf('=');
            if (eq <= 0)
                return false;

            key = trimmed.Substring(0, eq).Trim();
            value = trimmed.Substring(eq + 1).Trim();
            return key.Length > 0;
        }
    }
}
