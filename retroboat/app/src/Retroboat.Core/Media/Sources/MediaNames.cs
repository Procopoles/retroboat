using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Retroboat.Media.Sources
{
    /// <summary>Comparar o nome do jogo com o que a fonte devolveu, quando a busca é por texto e volta mais de um.</summary>
    public static class MediaNames
    {
        /// <summary>"Warriors, The (USA)" e "The Warriors" dão a mesma chave: sem etiquetas, acentos, artigo e pontuação.</summary>
        public static string Key(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";

            var title = GameInfoText.CleanTitle(name);
            var decomposed = title.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }

            var words = builder.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w != "the").ToArray();
            return string.Join(" ", words);
        }

        /// <summary>O resultado com o mesmo nome; sem nenhum igual, o primeiro (a ordem da própria fonte).</summary>
        public static T Best<T>(IEnumerable<T> results, string searched, Func<T, IEnumerable<string>> names) where T : class
        {
            var list = results.Where(r => r != null).ToList();
            if (list.Count == 0)
                return null;

            var key = Key(searched);
            return list.FirstOrDefault(r => names(r).Any(n => Key(n) == key)) ?? list[0];
        }

        /// <summary>Etiqueta de região do No-Intro/Redump → código de região do ScreenScraper.</summary>
        static readonly Dictionary<string, string> RegionTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USA"] = "us", ["Europe"] = "eu", ["Japan"] = "jp", ["Brazil"] = "br", ["World"] = "wor", ["Australia"] = "au",
            ["France"] = "fr", ["Germany"] = "de", ["Spain"] = "sp", ["Italy"] = "it", ["Netherlands"] = "nl", ["Sweden"] = "se",
            ["UK"] = "uk", ["Korea"] = "kr", ["Asia"] = "asi", ["China"] = "cn", ["Canada"] = "ca", ["Portugal"] = "pt"
        };

        /// <summary>As regiões do arquivo, na ordem das etiquetas: "Jogo (USA, Europe)" → us, eu. Vazio sem etiqueta de região.</summary>
        public static IReadOnlyList<string> FileRegions(string fileName)
        {
            var regions = new List<string>();
            foreach (Match tag in Regex.Matches(fileName ?? "", @"\(([^)]*)\)"))
            {
                foreach (var part in tag.Groups[1].Value.Split(','))
                {
                    if (RegionTags.TryGetValue(part.Trim(), out var code) && !regions.Contains(code))
                        regions.Add(code);
                }
            }

            return regions;
        }

        /// <summary>"1994-06-23", "1994-06-23T00:00:00" ou "1994" no formato do gamelist.</summary>
        public static string Date(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = Regex.Match(text.Trim(), @"^(\d{4})(?:-(\d{1,2})(?:-(\d{1,2}))?)?");
            return match.Success ? GameInfoText.ReleaseDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value) : null;
        }

        /// <summary>"2", "1-4", "2 joueurs", "+ de 4 joueurs" no formato do gamelist ("1-2", "1-4").</summary>
        public static string Players(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var range = Regex.Match(text, @"(\d+)\s*-\s*(\d+)");
            if (range.Success)
                return range.Groups[1].Value == range.Groups[2].Value ? range.Groups[1].Value : range.Groups[1].Value + "-" + range.Groups[2].Value;

            var number = Regex.Match(text, @"\d+");
            return number.Success ? GameInfoText.Players(number.Value) : null;
        }
    }
}
