using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Retroboat.Media.Sources
{
    /// <summary>
    /// HfsDB (db.hfsplay.fr, RB-038), com a chave de desenvolvedor do credenciais.ini (RB-037). Capas, logos e vídeos
    /// limpos. A busca é por texto no sistema; entre os resultados, vale o de mesmo nome. Os textos vêm em inglês (a base
    /// é francesa).
    /// </summary>
    public sealed class HfsDbSource : IMediaSource
    {
        const string Api = "https://db.hfsplay.fr/api/v1/";
        static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

        /// <summary>Tipos de mídia do HfsDB para cada campo ("tipo/subtipo"), do melhor para o substituto (os do scraper do ES).</summary>
        static readonly Dictionary<string, string[]> MediaTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [MediaFields.Cover] = new[] { "cover2d/front", "cover2d" },
            [MediaFields.BoxBack] = new[] { "cover2d/back" },
            [MediaFields.Screenshot] = new[] { "screenshot/in game", "screenshot" },
            [MediaFields.TitleScreen] = new[] { "screenshot/title" },
            [MediaFields.Logo] = new[] { "logo", "wheel" },
            [MediaFields.FanArt] = new[] { "wallpaper", "artwork" },
            [MediaFields.Cartridge] = new[] { "hardware" }, // o disco ou o cartucho (hardwaretype cd, cartridge...)
            [MediaFields.Video] = new[] { "video" },
            [MediaFields.Manual] = new[] { "manual" },
            [MediaFields.Bezel] = new[] { "bezel/16/9 horizontal" }
        };

        readonly IMediaHttp _http;
        readonly string _login;
        string _token;
        DateTime _tokenTime;

        /// <param name="login">"usuário:senha" (HFS_DEV_LOGIN); nulo = fonte indisponível.</param>
        public HfsDbSource(IMediaHttp http, string login)
        {
            _http = http;
            _login = login != null && login.IndexOf(':') > 0 ? login.Trim() : null;
        }

        public string Id => MediaSourceIds.HfsDb;

        public bool Supports(string system) => _login != null && MediaSystems.Ids(Id, system).Count > 0;

        public MediaCandidate Find(MediaQuery query, IReadOnlyCollection<string> wanted)
        {
            var name = GameInfoText.CleanTitle(query.SearchName);
            var games = new List<IDictionary<string, object>>();
            foreach (var system in MediaSystems.Ids(Id, query.System))
                games.AddRange(Search(name, system));

            var game = MediaNames.Best(games, query.SearchName, g => new[] { Text(g, "name_en"), Text(g, "name_fr") }.Where(n => n != null));
            return game == null ? null : ToCandidate(game, MediaNames.FileRegions(query.FileName));
        }

        /// <summary>
        /// A ordem das regiões das mídias do HfsDB (US, PAL, JPN, WORLD): as do arquivo primeiro ("(USA)" → US), depois as
        /// do mundo inteiro e as outras.
        /// </summary>
        internal static IReadOnlyList<string> MediaRegions(IReadOnlyList<string> fileRegions)
        {
            string ToHfs(string code)
            {
                switch (code)
                {
                    case "us": case "ca": return "US";
                    case "jp": return "JPN";
                    case "wor": return "WORLD";
                    case "br": return "BR";
                    default: return "PAL";
                }
            }

            return (fileRegions ?? new string[0]).Select(ToHfs).Concat(new[] { "WORLD", "US", "PAL", "JPN", "" }).Distinct().ToList();
        }

        IEnumerable<IDictionary<string, object>> Search(string name, string system)
        {
            var url = Api + "games?search=" + Uri.EscapeDataString(name) + "&system=" + Uri.EscapeDataString(system) + "&limit=25";
            var answer = _http.Get(url, new Dictionary<string, string> { ["Authorization"] = "Token " + Token(false) });
            if (answer.Status == 401 || answer.Status == 403)
                answer = _http.Get(url, new Dictionary<string, string> { ["Authorization"] = "Token " + Token(true) });

            if (answer.Status == 404)
                return new IDictionary<string, object>[0];
            if (!answer.Ok)
                throw new MediaSourceException("HfsDB: HTTP " + answer.Status);

            var root = Parse(answer.Body);
            return (root?["results"] as IEnumerable)?.OfType<IDictionary<string, object>>().ToList() ?? new List<IDictionary<string, object>>();
        }

        /// <summary>O token da API, renovado a cada 30 minutos (como o scraper do ES) ou quando a API recusa.</summary>
        string Token(bool renew)
        {
            if (!renew && _token != null && DateTime.UtcNow - _tokenTime < TokenLifetime)
                return _token;

            var separator = _login.IndexOf(':');
            var user = _login.Substring(0, separator);
            var password = _login.Substring(separator + 1);
            var answer = _http.Post(Api + "auth/token", "&username=" + Uri.EscapeDataString(user) + "&password=" + Uri.EscapeDataString(password),
                new Dictionary<string, string> { ["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(_login)) });

            var token = answer.Ok ? Text(Parse(answer.Body), "token") : null;
            if (token == null)
                throw new MediaSourceException("HfsDB: a chave foi recusada (HTTP " + answer.Status + ")");

            _token = token;
            _tokenTime = DateTime.UtcNow;
            return token;
        }

        internal static MediaCandidate ToCandidate(IDictionary<string, object> game, IReadOnlyList<string> fileRegions = null)
        {
            var candidate = new MediaCandidate(MediaSourceIds.HfsDb);
            var regions = MediaRegions(fileRegions);
            candidate.SetText(MediaFields.Name, Text(game, "name_en") ?? Text(game, "name_fr"));
            candidate.SetText(MediaFields.Description, Text(game, "description_pt") ?? Text(game, "description_en"));
            candidate.DescriptionInPortuguese = Text(game, "description_pt") != null;

            foreach (var metadata in Items(game, "metadata"))
            {
                var value = Text(metadata, "value");
                switch (Text(metadata, "name"))
                {
                    case "genre": candidate.SetText(MediaFields.Genre, value); break;
                    case "players": candidate.SetText(MediaFields.Players, MediaNames.Players(value)); break;
                    case "developer": candidate.SetText(MediaFields.Developer, value); break;
                    case "editor":
                    case "manufacturer":
                        if (!candidate.Texts.ContainsKey(MediaFields.Publisher))
                            candidate.SetText(MediaFields.Publisher, value);
                        break;
                }
            }

            foreach (var release in new[] { "released_at_WORLD", "released_at_US", "released_at_PAL", "released_at_JPN" })
            {
                var date = MediaNames.Date(Text(game, release));
                if (date != null)
                {
                    candidate.SetText(MediaFields.ReleaseDate, date);
                    break;
                }
            }

            var medias = Items(game, "medias").ToList();
            foreach (var field in MediaTypes)
            {
                var file = field.Value.Select(tag => FindMedia(medias, tag, regions)).FirstOrDefault(f => f != null);
                if (file != null)
                    candidate.SetMedia(field.Key, file);
            }

            return candidate.IsEmpty ? null : candidate;
        }

        /// <summary>
        /// O arquivo do tipo, da região que vem antes; com subtipo ("cover2d/front"), só os que o têm nos metadados ou na
        /// descrição. Na mesma região, o primeiro da lista.
        /// </summary>
        static string FindMedia(List<IDictionary<string, object>> medias, string tag, IReadOnlyList<string> regions)
        {
            var slash = tag.IndexOf('/');
            var type = slash < 0 ? tag : tag.Substring(0, slash);
            var subtype = slash < 0 ? null : tag.Substring(slash + 1);

            var matches = medias.Where(media => Text(media, "file") != null && Text(media, "type") == type &&
                (subtype == null || Items(media, "metadata").Any(m => Text(m, "name") == type + "type" && Text(m, "value") == subtype) || Text(media, "description") == subtype));

            int Rank(IDictionary<string, object> media)
            {
                var index = regions.ToList().IndexOf((Text(media, "region") ?? "").ToUpperInvariant());
                return index < 0 ? regions.Count : index;
            }

            return matches.Select((media, order) => new { media, order }).OrderBy(m => Rank(m.media)).ThenBy(m => m.order).Select(m => Text(m.media, "file")).FirstOrDefault();
        }

        static IDictionary<string, object> Parse(string json)
        {
            try
            {
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        static string Text(IDictionary<string, object> item, string key) =>
            item != null && item.TryGetValue(key, out var value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;

        static IEnumerable<IDictionary<string, object>> Items(IDictionary<string, object> item, string key) =>
            item != null && item.TryGetValue(key, out var value) && value is IEnumerable list && !(value is string)
                ? list.OfType<IDictionary<string, object>>()
                : Enumerable.Empty<IDictionary<string, object>>();
    }
}
