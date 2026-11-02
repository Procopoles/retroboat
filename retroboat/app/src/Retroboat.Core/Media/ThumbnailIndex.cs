using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Retroboat.Media
{
    /// <summary>
    /// Acha, entre os nomes de imagem de um sistema no libretro (padrão No-Intro/Redump, como
    /// "Clock Tower 3 (Europe) (En,Fr,De,Es,It)"), o que corresponde a um arquivo de jogo. Aceita as
    /// variações que aparecem nas coleções: "_" no lugar de "," e de "&amp;", "The" no começo ou no fim,
    /// nome sem região, região no formato GoodTools ("(U)") e título sem o subtítulo.
    /// </summary>
    public sealed class ThumbnailIndex
    {
        static readonly Dictionary<string, string> GoodToolsRegions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["U"] = "USA", ["E"] = "Europe", ["J"] = "Japan", ["UE"] = "USA, Europe", ["JU"] = "Japan, USA", ["W"] = "World"
        };

        static readonly string[] Regions = { "USA", "Europe", "Japan", "World", "Korea", "Asia", "Brazil", "Australia", "France", "Germany", "Spain", "Italy" };

        /// <summary>Ordem de preferência quando o arquivo não diz a região.</summary>
        static readonly string[] PreferredRegions = { "usa", "world", "europe", "japan" };

        readonly Dictionary<string, string> _byFullName = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> _byTitle = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> _byMainTitle = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public ThumbnailIndex(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                var full = Key(name);
                if (!_byFullName.ContainsKey(full))
                    _byFullName[full] = name;

                Add(_byTitle, Key(MoveArticle(Title(name))), name);
                Add(_byMainTitle, Key(MoveArticle(Title(name).Split(new[] { " - " }, StringSplitOptions.None)[0])), name);
            }
        }

        public int Count => _byFullName.Count;

        /// <summary>Nome da imagem para o arquivo (sem extensão), ou null.</summary>
        public string Find(string gameStem)
        {
            if (string.IsNullOrWhiteSpace(gameStem))
                return null;

            if (_byFullName.TryGetValue(Key(gameStem), out var exact))
                return exact;

            var title = Title(gameStem);
            var region = Region(gameStem);
            if (_byTitle.TryGetValue(Key(MoveArticle(title)), out var sameTitle))
                return Pick(sameTitle, region);

            // "Fatal Frame II (U)" -> "Fatal Frame II - Crimson Butterfly (USA)". Só quando o arquivo não tem
            // subtítulo e só existe um jogo com esse título, para "Need for Speed" não virar outro da série.
            if (!title.Contains(" - ") && _byMainTitle.TryGetValue(Key(MoveArticle(title)), out var sameMain)
                && sameMain.Select(n => Key(MoveArticle(Title(n)))).Distinct().Count() == 1)
                return Pick(sameMain, region);

            return null;
        }

        static string Pick(List<string> candidates, string region)
        {
            if (region.Length > 0)
            {
                var sameRegion = candidates.FirstOrDefault(c => Region(c) == region)
                                 ?? candidates.FirstOrDefault(c => Region(c).Length > 0 && (Region(c).Contains(region) || region.Contains(Region(c))));
                if (sameRegion != null)
                    return sameRegion;
            }

            foreach (var preferred in PreferredRegions)
            {
                var match = candidates.FirstOrDefault(c => Region(c).StartsWith(preferred, StringComparison.Ordinal) && !IsSpecial(c));
                if (match != null)
                    return match;
            }

            return candidates.FirstOrDefault(c => !IsSpecial(c)) ?? candidates[0];
        }

        static bool IsSpecial(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("(beta") || lower.Contains("(demo") || lower.Contains("(proto") || lower.Contains("(sample") || lower.Contains("(disc 2");
        }

        /// <summary>Parte do nome antes da primeira etiqueta entre parênteses ou colchetes.</summary>
        internal static string Title(string name)
        {
            int cut = name.IndexOfAny(new[] { '(', '[' });
            return (cut < 0 ? name : name.Substring(0, cut)).Trim();
        }

        /// <summary>"Punisher, The" -> "The Punisher".</summary>
        internal static string MoveArticle(string title)
        {
            var match = Regex.Match(title, @"^(.*), (The|A|An)$");
            return match.Success ? match.Groups[2].Value + " " + match.Groups[1].Value : title;
        }

        /// <summary>Etiqueta de região normalizada ("europeaustralia"), ou vazio.</summary>
        static string Region(string name)
        {
            foreach (Match tag in Regex.Matches(name, @"[\(\[]([^\)\]]*)[\)\]]"))
            {
                var value = tag.Groups[1].Value.Trim();
                if (GoodToolsRegions.TryGetValue(value, out var expanded))
                    value = expanded;

                if (Regions.Any(r => value.IndexOf(r, StringComparison.Ordinal) >= 0))
                    return Key(value);
            }

            return "";
        }

        /// <summary>Só letras e números, minúsculos: "Tony Hawk's" e "Tony Hawks" viram a mesma chave.</summary>
        static string Key(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    builder.Append(c);
            }

            return builder.ToString();
        }

        static void Add(Dictionary<string, List<string>> map, string key, string name)
        {
            if (key.Length == 0)
                return;

            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<string>();

            list.Add(name);
        }
    }
}
