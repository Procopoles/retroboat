using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Retroboat.Media.Sources
{
    /// <summary>
    /// ScreenScraper (RB-038), com a chave de desenvolvedor do credenciais.ini (RB-037) e, se houver, a conta da pessoa no
    /// ES. Acha o jogo pelo nome do arquivo (jeuInfos, o mesmo que o scraper do ES faz) e, sem resultado ou com um nome
    /// digitado, pela busca de texto (jeuRecherche). Textos em português quando existem; imagens da região do Brasil, do
    /// mundo, dos EUA, da Europa e do Japão, nessa ordem.
    /// </summary>
    public sealed class ScreenScraperSource : IMediaSource
    {
        const string Api = "https://api.screenscraper.fr/api2/";

        /// <summary>Ordem das regiões para nome, data e mídia, depois das regiões do próprio arquivo.</summary>
        static readonly string[] Regions = { "br", "wor", "us", "ss", "eu", "jp", "cus", "" };

        /// <summary>Tipos de mídia do ScreenScraper para cada campo, do melhor para o substituto.</summary>
        static readonly Dictionary<string, string[]> MediaTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [MediaFields.Cover] = new[] { "box-2D" },
            [MediaFields.BoxBack] = new[] { "box-2D-back" },
            [MediaFields.Screenshot] = new[] { "ss" },
            [MediaFields.TitleScreen] = new[] { "sstitle" },
            [MediaFields.Logo] = new[] { "wheel-hd", "wheel", "wheel-steel", "wheel-carbon", "screenmarqueesmall", "screenmarquee" },
            [MediaFields.FanArt] = new[] { "fanart" },
            [MediaFields.Cartridge] = new[] { "support-2D" },
            [MediaFields.Video] = new[] { "video-normalized", "video" },
            [MediaFields.Manual] = new[] { "manuel" },
            [MediaFields.Map] = new[] { "maps" },
            [MediaFields.Bezel] = new[] { "bezel-16-9" }
        };

        /// <summary>
        /// Imagens que já vêm no tamanho da tela (como o scraper do ES pede). Fan art, mapa e moldura vêm inteiros: são fundo de
        /// tela ou ocupam a tela toda; vídeo e manual não são imagem.
        /// </summary>
        static readonly HashSet<string> Resized = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MediaFields.Cover, MediaFields.BoxBack, MediaFields.Screenshot, MediaFields.TitleScreen, MediaFields.Logo, MediaFields.Cartridge
        };

        readonly IMediaHttp _http;
        readonly string _login;
        readonly string _softName;
        readonly string _user;
        readonly string _password;

        /// <param name="login">"devid=...&amp;devpassword=..." (SCREENSCRAPER_DEV_LOGIN); nulo = fonte indisponível.</param>
        /// <param name="user">A conta da pessoa no ES (ScreenScraperUser), opcional: dá mais consultas por dia.</param>
        public ScreenScraperSource(IMediaHttp http, string login, string softName, string user = null, string password = null)
        {
            _http = http;
            _login = string.IsNullOrWhiteSpace(login) ? null : login.Trim();
            _softName = string.IsNullOrWhiteSpace(softName) ? "Retroboat" : softName.Trim();
            _user = string.IsNullOrWhiteSpace(user) ? null : user;
            _password = password;
        }

        public string Id => MediaSourceIds.ScreenScraper;

        public bool Supports(string system) => _login != null && MediaSystems.Ids(Id, system).Count > 0;

        public MediaCandidate Find(MediaQuery query, IReadOnlyCollection<string> wanted)
        {
            var system = MediaSystems.Ids(Id, query.System).FirstOrDefault();
            if (system == null)
                return null;

            XElement game = null;
            if (query.TypedName == null && !string.IsNullOrWhiteSpace(query.FileName))
                game = Games(Call("jeuInfos.php", "&romtype=rom&romnom=" + Uri.EscapeDataString(query.FileName) + "&systemeid=" + system)).FirstOrDefault();

            if (game == null)
            {
                var name = GameInfoText.CleanTitle(query.SearchName);
                var found = Games(Call("jeuRecherche.php", "&recherche=" + Uri.EscapeDataString(name) + "&systemeid=" + system));
                game = MediaNames.Best(found, query.SearchName, g => g.Element("noms")?.Elements("nom").Select(n => n.Value) ?? new string[0]);
            }

            return game == null ? null : ToCandidate(game, MediaNames.FileRegions(query.FileName));
        }

        /// <summary>A resposta da API, ou nulo se o jogo não existe lá.</summary>
        XDocument Call(string endpoint, string arguments)
        {
            var url = Api + endpoint + "?" + _login + "&softname=" + Uri.EscapeDataString(_softName) + "&output=xml" + arguments;
            if (_user != null && !string.IsNullOrEmpty(_password))
                url += "&ssid=" + Uri.EscapeDataString(_user) + "&sspassword=" + Uri.EscapeDataString(_password);

            var answer = _http.Get(url);

            // 404: o jogo não está lá. 401/423: API fechada (servidor cheio); 429: consultas demais ao mesmo tempo;
            // 430/431: cota do dia. 403: a chave foi recusada (a tela INTEGRAÇÕES troca sozinha).
            if (answer.Status == 404)
                return null;
            if (!answer.Ok)
                throw new MediaSourceException("ScreenScraper: HTTP " + answer.Status + " " + Short(answer.Body));

            try
            {
                return XDocument.Parse(answer.Body);
            }
            catch (XmlException)
            {
                // Texto em vez de XML: "Erreur : Rom/Iso/Dossier non trouvée !" é jogo não achado; o resto é erro da API.
                if (answer.Body.IndexOf("non trouv", StringComparison.OrdinalIgnoreCase) >= 0)
                    return null;

                throw new MediaSourceException("ScreenScraper: " + Short(answer.Body));
            }
        }

        static IEnumerable<XElement> Games(XDocument document)
        {
            var data = document?.Root;
            if (data == null)
                return new XElement[0];

            return data.Element("jeux")?.Elements("jeu") ?? data.Elements("jeu");
        }

        /// <param name="fileRegions">As regiões do arquivo ("us" para "(USA)"): a capa e o nome da edição dele vêm antes.</param>
        internal static MediaCandidate ToCandidate(XElement game, IReadOnlyList<string> fileRegions = null)
        {
            var candidate = new MediaCandidate(MediaSourceIds.ScreenScraper);
            var regions = (fileRegions ?? new string[0]).Concat(Regions).Distinct().ToList();
            XElement ByRegion(IEnumerable<XElement> items) => ScreenScraperSource.ByRegion(items, regions);

            candidate.SetText(MediaFields.Name, ByRegion(game.Element("noms")?.Elements("nom") ?? new XElement[0])?.Value);

            var synopsis = game.Element("synopsis")?.Elements("synopsis").ToList() ?? new List<XElement>();
            var portuguese = synopsis.FirstOrDefault(s => (string)s.Attribute("langue") == "pt" && s.Value.Trim().Length > 0);
            var description = portuguese ?? synopsis.FirstOrDefault(s => (string)s.Attribute("langue") == "en" && s.Value.Trim().Length > 0);
            candidate.SetText(MediaFields.Description, description?.Value);
            candidate.DescriptionInPortuguese = portuguese != null;

            candidate.SetText(MediaFields.Genre, Genre(game.Element("genres")));
            candidate.SetText(MediaFields.Family, Family(game.Element("familles")));
            candidate.SetText(MediaFields.Rating, Rating(game.Element("note")?.Value));
            candidate.SetText(MediaFields.ReleaseDate, MediaNames.Date(ByRegion(game.Element("dates")?.Elements("date") ?? new XElement[0])?.Value));
            candidate.SetText(MediaFields.Developer, Clean(game.Element("developpeur")?.Value));
            candidate.SetText(MediaFields.Publisher, Clean(game.Element("editeur")?.Value));
            candidate.SetText(MediaFields.Players, MediaNames.Players(game.Element("joueurs")?.Value));

            var medias = game.Element("medias")?.Elements("media").ToList() ?? new List<XElement>();
            foreach (var field in MediaTypes)
            {
                var media = field.Value.Select(type => ByRegion(medias.Where(m => (string)m.Attribute("type") == type))).FirstOrDefault(m => m != null);
                if (media == null)
                    continue;

                var url = media.Value.Trim().Replace(" ", "%20").Replace("#screenscraperserveur#", "https://www.screenscraper.fr/");
                if (Resized.Contains(field.Key))
                    url += "&maxwidth=1000&maxheight=1000"; // como o ES faz: imagem já no tamanho da tela, sem baixar o original
                candidate.SetMedia(field.Key, url, (string)media.Attribute("format"));
            }

            return candidate.IsEmpty ? null : candidate;
        }

        static XElement ByRegion(IEnumerable<XElement> items, IReadOnlyList<string> regions)
        {
            var list = items.Where(i => i.Value.Trim().Length > 0).ToList();
            foreach (var region in regions)
            {
                var item = list.FirstOrDefault(i => ((string)i.Attribute("region") ?? "") == region);
                if (item != null)
                    return item;
            }

            return list.FirstOrDefault();
        }

        /// <summary>
        /// Gênero principal com o subgênero, como o scraper do ES monta ("Ação / Plataforma"), em português se houver. O
        /// subgênero do ScreenScraper já vem com o pai ("Action / Platform").
        /// </summary>
        static string Genre(XElement genres)
        {
            if (genres == null)
                return null;

            foreach (var language in new[] { "pt", "en" })
            {
                var inLanguage = genres.Elements("genre").Where(g => (string)g.Attribute("langue") == language).ToList();
                var main = inLanguage.FirstOrDefault(g => (string)g.Attribute("principale") == "1")?.Value.Trim();
                var sub = inLanguage.FirstOrDefault(g => (string)g.Attribute("principale") == "0")?.Value.Trim();
                if (string.IsNullOrEmpty(main) && string.IsNullOrEmpty(sub))
                    continue;

                if (!string.IsNullOrEmpty(main) && main.Contains("/"))
                    main = main.Substring(0, main.IndexOf('/')).Trim();

                if (string.IsNullOrEmpty(main) || (sub != null && sub.Contains("/")))
                    return string.IsNullOrEmpty(sub) ? main : sub;

                return string.IsNullOrEmpty(sub) ? main : main + " / " + sub;
            }

            return null;
        }

        /// <summary>A série principal ("Super Mario"), em português se houver.</summary>
        static string Family(XElement families)
        {
            var all = families?.Elements("famille").Where(f => f.Value.Trim().Length > 0).ToList() ?? new List<XElement>();
            var main = all.Where(f => (string)f.Attribute("principale") == "1").ToList();
            var pick = main.FirstOrDefault(f => (string)f.Attribute("langue") == "pt") ?? main.FirstOrDefault(f => (string)f.Attribute("langue") == "en") ??
                       main.FirstOrDefault() ?? all.FirstOrDefault();
            return pick?.Value.Trim();
        }

        /// <summary>A nota do ScreenScraper (0 a 20) no formato do gamelist (0 a 1, como o scraper do ES grava).</summary>
        internal static string Rating(string note)
        {
            if (!double.TryParse((note ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
                return null;

            return Math.Min(1.0, value / 20.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        static string Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Replace("&nbsp;", " ").Trim();

        static string Short(string body)
        {
            var text = (body ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length > 120 ? text.Substring(0, 120) + "..." : text;
        }
    }
}
