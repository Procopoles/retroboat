using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace Retroboat.Media
{
    /// <summary>
    /// Informações dos jogos no Wikidata e o resumo da página do jogo na Wikipédia em português (ou em inglês,
    /// se não houver), sem conta nem chave. Cobre o que a base do libretro não tem e dá a descrição em
    /// português. Para não trazer outro jogo de mesmo nome, só aceita item que seja jogo eletrônico e saiu
    /// para a plataforma do sistema.
    /// </summary>
    public sealed class WikidataInfo : IGameInfoSource
    {
        const string SparqlEndpoint = "https://query.wikidata.org/sparql";
        const string SinglePlayer = "http://www.wikidata.org/entity/Q208850";
        const int BatchSize = 20;

        /// <summary>Sistema do ES -> plataforma no Wikidata (propriedade P400 do jogo).</summary>
        public static readonly IReadOnlyDictionary<string, string> Platforms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ps2"] = "Q10680",
            ["psp"] = "Q170325",
            ["ps3"] = "Q10683",
            ["nes"] = "Q172742",
            ["snes"] = "Q183259",
            ["n64"] = "Q184839",
            ["gb"] = "Q186437",
            ["gbc"] = "Q203992",
            ["gba"] = "Q188642",
            ["nds"] = "Q170323",
            ["3ds"] = "Q203597",
            ["gamecube"] = "Q182172",
            ["wii"] = "Q8079",
            ["megadrive"] = "Q10676",
            ["genesis"] = "Q10676",
            ["mastersystem"] = "Q209868",
            ["gamegear"] = "Q751719",
            ["saturn"] = "Q200912",
            ["dreamcast"] = "Q184198",
            ["segacd"] = "Q1047516",
            ["sega32x"] = "Q1063978",
            ["pcengine"] = "Q1057377",
            ["atari2600"] = "Q206261",
            ["ngpc"] = "Q1977455"
        };

        public IDictionary<string, GameInfo> Find(string system, IReadOnlyCollection<string> stems)
        {
            var found = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
            if (!Platforms.TryGetValue(system, out var platform))
                return found;

            foreach (var batch in stems.Select((stem, i) => new { stem, i }).GroupBy(x => x.i / BatchSize, x => x.stem))
            {
                var candidates = batch.ToDictionary(s => s, s => GameInfoText.TitleCandidates(s), StringComparer.OrdinalIgnoreCase);
                var byTitle = Query(candidates.Values.SelectMany(t => t).Distinct(StringComparer.OrdinalIgnoreCase), platform);

                foreach (var stem in batch)
                {
                    var title = candidates[stem].FirstOrDefault(t => byTitle.ContainsKey(t));
                    if (title == null)
                        continue;

                    var item = byTitle[title];
                    var info = new GameInfo
                    {
                        Title = item.Label ?? title,
                        Genre = GameInfoText.WikidataGenres(item.Genres),
                        GenreIsDetailed = true,
                        ReleaseDate = item.Released != null && item.Released.Length >= 10
                            ? GameInfoText.ReleaseDate(item.Released.Substring(0, 4), item.Released.Substring(5, 2), item.Released.Substring(8, 2))
                            : null,
                        Developer = item.Developer,
                        Publisher = item.Publisher,
                        Players = item.Modes.Count == 1 && item.Modes[0] == SinglePlayer ? "1" : null
                    };

                    var pt = Summary(item.PtWiki);
                    if (pt != null)
                    {
                        info.Description = pt;
                        info.DescriptionInPortuguese = true;
                    }
                    else
                    {
                        info.Description = Summary(item.EnWiki);
                    }

                    found[stem] = info;
                }
            }

            return found;
        }

        internal sealed class Item
        {
            public string Label;
            public string Released;
            public string Developer;
            public string Publisher;
            public List<string> Genres = new List<string>();
            public List<string> Modes = new List<string>();
            public string PtWiki;
            public string EnWiki;
        }

        /// <summary>Jogos da plataforma com esses nomes (rótulo ou nome alternativo, em inglês ou português).</summary>
        Dictionary<string, Item> Query(IEnumerable<string> titles, string platform)
        {
            var json = Encoding.UTF8.GetString(WebFetch.PostForm(SparqlEndpoint,
                new Dictionary<string, string> { ["query"] = BuildQuery(titles, platform) }, "application/sparql-results+json"));
            return ParseResults(json);
        }

        internal static string BuildQuery(IEnumerable<string> titles, string platform)
        {
            // "mul" é o rótulo multilíngue que o Wikidata usa no lugar do inglês em muitos itens (ex.: Rockstar North).
            var values = string.Join(" ", titles.SelectMany(t => new[] { Literal(t) + "@en", Literal(t) + "@mul", Literal(t) + "@pt" }));
            return @"SELECT ?title ?item (SAMPLE(STR(?label)) AS ?name) (MIN(?date) AS ?released)
  (GROUP_CONCAT(DISTINCT STR(?devName); separator="", "") AS ?developer) (GROUP_CONCAT(DISTINCT STR(?pubName); separator="", "") AS ?publisher)
  (GROUP_CONCAT(DISTINCT STR(?genreName); separator=""|"") AS ?genres) (GROUP_CONCAT(DISTINCT STR(?mode); separator="" "") AS ?modes) (SAMPLE(?pt) AS ?ptwiki) (SAMPLE(?en) AS ?enwiki)
WHERE {
  VALUES ?title { " + values + @" }
  ?item rdfs:label|skos:altLabel ?title ; wdt:P400 wd:" + platform + @" ; wdt:P31/wdt:P279* wd:Q7889 .
  OPTIONAL { ?item rdfs:label ?label FILTER(LANG(?label) IN (""en"", ""mul"")) }
  OPTIONAL { ?item wdt:P577 ?date }
  OPTIONAL { ?item wdt:P178 ?dev . ?dev rdfs:label ?devName FILTER(LANG(?devName) IN (""en"", ""mul"")) }
  OPTIONAL { ?item wdt:P123 ?pub . ?pub rdfs:label ?pubName FILTER(LANG(?pubName) IN (""en"", ""mul"")) }
  OPTIONAL { ?item wdt:P136 ?g . ?g rdfs:label ?genreName FILTER(LANG(?genreName) = ""pt"") }
  OPTIONAL { ?item wdt:P404 ?mode }
  OPTIONAL { ?pt schema:about ?item ; schema:isPartOf <https://pt.wikipedia.org/> }
  OPTIONAL { ?en schema:about ?item ; schema:isPartOf <https://en.wikipedia.org/> }
}
GROUP BY ?title ?item";
        }

        static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>Um item por título; se vários jogos têm o mesmo nome na plataforma, fica o mais antigo.</summary>
        internal static Dictionary<string, Item> ParseResults(string json)
        {
            var byTitle = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            var bindings = (root["results"] as IDictionary<string, object>)?["bindings"] as IEnumerable ?? new object[0];

            foreach (IDictionary<string, object> row in bindings)
            {
                string Value(string name) => row.TryGetValue(name, out var cell) && cell is IDictionary<string, object> c && c["value"] is string s && s.Length > 0 ? s : null;

                var title = Value("title");
                if (title == null)
                    continue;

                var item = new Item
                {
                    Label = Value("name"),
                    Released = Value("released"),
                    Developer = Value("developer"),
                    Publisher = Value("publisher"),
                    Genres = (Value("genres") ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
                    Modes = (Value("modes") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
                    PtWiki = Value("ptwiki"),
                    EnWiki = Value("enwiki")
                };

                if (!byTitle.TryGetValue(title, out var current) || string.CompareOrdinal(item.Released ?? "9", current.Released ?? "9") < 0)
                    byTitle[title] = item;
            }

            return byTitle;
        }

        /// <summary>Resumo (primeiros parágrafos) de uma página da Wikipédia, ou null.</summary>
        static string Summary(string pageUrl)
        {
            if (pageUrl == null)
                return null;

            var marker = pageUrl.IndexOf("/wiki/", StringComparison.Ordinal);
            if (marker < 0)
                return null;

            var api = pageUrl.Substring(0, marker) + "/api/rest_v1/page/summary/" + pageUrl.Substring(marker + "/wiki/".Length);
            try
            {
                return ParseSummary(Encoding.UTF8.GetString(WebFetch.Get(api, "application/json")));
            }
            catch (WebException)
            {
                return null; // Uma página que não abre não impede as informações do resto.
            }
        }

        internal static string ParseSummary(string json)
        {
            var page = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (page.TryGetValue("type", out var type) && (type as string) == "disambiguation")
                return null;

            var extract = page.TryGetValue("extract", out var text) ? (text as string)?.Trim() : null;
            return string.IsNullOrEmpty(extract) ? null : extract;
        }
    }
}
