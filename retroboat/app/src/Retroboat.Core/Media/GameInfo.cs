using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Retroboat.Media
{
    /// <summary>
    /// O que a página de um jogo mostra além das imagens, como a loja da Steam: descrição, gênero, data,
    /// desenvolvedora, editora e jogadores. Já no formato dos campos do gamelist do ES.
    /// </summary>
    public sealed class GameInfo
    {
        /// <summary>Nome do jogo sem as etiquetas de região e idioma do arquivo.</summary>
        public string Title { get; set; }

        public string Description { get; set; }

        public bool DescriptionInPortuguese { get; set; }

        public string Genre { get; set; }

        /// <summary>Gênero específico e revisado (Wikidata), que vale mais que a categoria larga do libretro.</summary>
        public bool GenreIsDetailed { get; set; }

        /// <summary>Data no formato do gamelist (yyyyMMddT000000).</summary>
        public string ReleaseDate { get; set; }

        public string Developer { get; set; }

        public string Publisher { get; set; }

        /// <summary>Jogadores no formato do gamelist ("1", "1-4").</summary>
        public string Players { get; set; }
    }

    /// <summary>De onde vêm as informações automáticas. Separado para os testes não dependerem da internet.</summary>
    public interface IGameInfoSource
    {
        /// <summary>
        /// Informações dos jogos de um sistema do ES (ex.: "ps2"), pelo nome do arquivo sem extensão. Os jogos que a
        /// fonte não conhece ficam de fora. Lança WebException/IOException sem conexão.
        /// </summary>
        IDictionary<string, GameInfo> Find(string system, IReadOnlyCollection<string> stems);
    }

    /// <summary>Conversões de nome, data, gênero e jogadores para o formato do gamelist.</summary>
    public static class GameInfoText
    {
        /// <summary>
        /// "Warriors, The (USA) (En,Fr)" -> "The Warriors"; "Grand Theft Auto - San Andreas (USA)" ->
        /// "Grand Theft Auto: San Andreas" (o Redump troca ":" por " - " porque o Windows não aceita ":").
        /// </summary>
        public static string CleanTitle(string name)
        {
            return ThumbnailIndex.MoveArticle(ThumbnailIndex.Title(name)).Replace(" - ", ": ");
        }

        /// <summary>Títulos para procurar o jogo pelo nome, do mais provável ao menos provável.</summary>
        public static IReadOnlyList<string> TitleCandidates(string stem)
        {
            var title = ThumbnailIndex.MoveArticle(ThumbnailIndex.Title(stem));
            return new[] { title.Replace(" - ", ": "), title, title.Replace(" - ", " ") }
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string ReleaseDate(string year, string month = null, string day = null)
        {
            if (!int.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) || y < 1950 || y > 2100)
                return null;

            int.TryParse(month, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m);
            int.TryParse(day, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d);
            return string.Format(CultureInfo.InvariantCulture, "{0:0000}{1:00}{2:00}T000000", y, m >= 1 && m <= 12 ? m : 1, d >= 1 && d <= 31 ? d : 1);
        }

        /// <summary>Máximo de jogadores ("4") no formato do ES ("1-4").</summary>
        public static string Players(string maxUsers)
        {
            if (!int.TryParse(maxUsers, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) || max < 1)
                return null;

            return max == 1 ? "1" : "1-" + max.ToString(CultureInfo.InvariantCulture);
        }

        static readonly Dictionary<string, string> Genres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Action"] = "Ação",
            ["Adventure"] = "Aventura",
            ["Racing"] = "Corrida",
            ["Role-playing (RPG)"] = "RPG",
            ["Role-Playing"] = "RPG",
            ["Simulation"] = "Simulação",
            ["Sports"] = "Esporte",
            ["Fighting"] = "Luta",
            ["Shooter"] = "Tiro",
            ["Strategy"] = "Estratégia",
            ["Gambling"] = "Cassino",
            ["Music"] = "Música",
            ["Puzzle"] = "Quebra-cabeça",
            ["Board"] = "Tabuleiro",
            ["Quiz"] = "Perguntas",
            ["Dancing"] = "Dança",
            ["Sing"] = "Karaokê",
            ["Rhythm"] = "Ritmo",
            ["Wrestling"] = "Luta livre",
            ["Boxing"] = "Boxe",
            ["Tennis"] = "Tênis",
            ["Basketball"] = "Basquete",
            ["Football"] = "Futebol",
            ["Soccer"] = "Futebol",
            ["Mini Games"] = "Minigames",
            ["Card"] = "Cartas",
            ["Gun"] = "Tiro com pistola",
            ["Flight Simulator"] = "Simulador de voo",
            ["Educational"] = "Educativo",
            ["Hunting and Fishing"] = "Caça e pesca",
            ["Interactive Movie"] = "Filme interativo",
            ["Platform"] = "Plataforma",
            ["Beat'em Up"] = "Beat 'em up",
            ["Data / Pictures"] = "Dados e imagens"
        };

        /// <summary>Gênero do libretro ("Board / Mini Games") em português ("Tabuleiro / Minigames").</summary>
        public static string TranslateGenre(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
                return null;

            if (Genres.TryGetValue(genre.Trim(), out var whole))
                return whole;

            return string.Join(" / ", genre.Split('/').Select(p => p.Trim()).Where(p => p.Length > 0)
                .Select(p => Genres.TryGetValue(p, out var pt) ? pt : p));
        }

        /// <summary>Até dois gêneros do Wikidata, como as tags da Steam: "Ação-aventura / Tiro em terceira pessoa".</summary>
        public static string WikidataGenres(IEnumerable<string> labels)
        {
            var genres = labels.Select(WikidataGenre).Where(g => g != null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.Ordinal).Take(2).ToList();
            return genres.Count == 0 ? null : string.Join(" / ", genres);
        }

        /// <summary>Gênero do Wikidata ("jogo eletrônico de ação-aventura") como no gamelist ("Ação-aventura").</summary>
        public static string WikidataGenre(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return null;

            var text = Regex.Replace(label.Trim(), @"^(jogo eletrônico|jogo de computador|jogo)( de| do)? ", "", RegexOptions.IgnoreCase);
            return text.Length == 0 ? null : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }

    /// <summary>GET com o nome do Retroboat no User-Agent, como as fontes públicas pedem.</summary>
    static class WebFetch
    {
        public const string UserAgent = "Retroboat/0.1 (https://github.com/Procopoles/retroboat)";

        public static byte[] Get(string url, string accept = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (accept != null)
                request.Accept = accept;

            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        /// <summary>POST de formulário (a consulta SPARQL do Wikidata pode passar do limite de uma URL).</summary>
        public static byte[] PostForm(string url, IDictionary<string, string> form, string accept)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var body = System.Text.Encoding.UTF8.GetBytes(string.Join("&", form.Select(f => f.Key + "=" + Uri.EscapeDataString(f.Value))));
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.UserAgent = UserAgent;
            request.Accept = accept;
            request.ContentType = "application/x-www-form-urlencoded";
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var stream = request.GetRequestStream())
                stream.Write(body, 0, body.Length);

            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        public static bool IsNotFound(WebException ex) => (ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound;
    }
}
