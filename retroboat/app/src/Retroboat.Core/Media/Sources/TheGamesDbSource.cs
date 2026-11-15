using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace Retroboat.Media.Sources
{
    /// <summary>
    /// TheGamesDB (RB-038), com a chave do credenciais.ini (RB-037). A chave tem limite por mês, então só consulta o que
    /// falta: a busca pelo nome já traz a capa; telas, logo e fan art são uma segunda consulta, feita só quando algum
    /// desses campos é esperado desta fonte. Textos em inglês.
    /// </summary>
    public sealed class TheGamesDbSource : IMediaSource
    {
        const string Api = "https://api.thegamesdb.net/v1/";

        /// <summary>Tipo de imagem do TheGamesDB para cada campo (fora a capa, que vem com a busca).</summary>
        static readonly Dictionary<string, string> ImageTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MediaFields.Screenshot] = "screenshot",
            [MediaFields.TitleScreen] = "titlescreen",
            [MediaFields.Logo] = "clearlogo",
            [MediaFields.FanArt] = "fanart"
        };

        readonly IMediaHttp _http;
        readonly string _key;
        Dictionary<string, string> _genres;

        /// <param name="key">GAMESDB_APIKEY; nulo = fonte indisponível.</param>
        public TheGamesDbSource(IMediaHttp http, string key)
        {
            _http = http;
            _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }

        public string Id => MediaSourceIds.TheGamesDb;

        public bool Supports(string system) => _key != null && MediaSystems.Ids(Id, system).Count > 0;

        public MediaCandidate Find(MediaQuery query, IReadOnlyCollection<string> wanted)
        {
            var platforms = string.Join(",", MediaSystems.Ids(Id, query.System));
            var root = Call("Games/ByGameName", "&name=" + Uri.EscapeDataString(GameInfoText.CleanTitle(query.SearchName)) +
                            "&fields=players,genres,overview&include=boxart&filter%5Bplatform%5D=" + Uri.EscapeDataString(platforms));

            var games = Items(Object(root, "data"), "games").ToList();
            var game = MediaNames.Best(games, query.SearchName, g => new[] { Text(g, "game_title") }.Where(n => n != null));
            if (game == null)
                return null;

            var id = Number(game, "id");
            var candidate = new MediaCandidate(Id);
            candidate.SetText(MediaFields.Name, Text(game, "game_title"));
            candidate.SetText(MediaFields.Description, Text(game, "overview"));
            candidate.SetText(MediaFields.ReleaseDate, MediaNames.Date(Text(game, "release_date")));
            candidate.SetText(MediaFields.Players, MediaNames.Players(Number(game, "players")));

            if (wanted.Contains(MediaFields.Genre, StringComparer.OrdinalIgnoreCase))
                candidate.SetText(MediaFields.Genre, GenreNames(game));

            var boxart = Object(Object(root, "include"), "boxart");
            var front = Items(Object(boxart, "data"), id).FirstOrDefault(i => Text(i, "side") == "front") ?? Items(Object(boxart, "data"), id).FirstOrDefault();
            if (front != null)
                candidate.SetMedia(MediaFields.Cover, BaseUrl(Object(boxart, "base_url")) + Text(front, "filename"));

            // A caixa traseira vem na mesma resposta da capa: não gasta consulta.
            var back = Items(Object(boxart, "data"), id).FirstOrDefault(i => Text(i, "side") == "back");
            if (back != null)
                candidate.SetMedia(MediaFields.BoxBack, BaseUrl(Object(boxart, "base_url")) + Text(back, "filename"));

            var images = ImageTypes.Where(t => wanted.Contains(t.Key, StringComparer.OrdinalIgnoreCase)).ToList();
            if (images.Count > 0 && id != null)
            {
                var data = Object(Call("Games/Images", "&games_id=" + Uri.EscapeDataString(id) + "&filter%5Btype%5D=" + string.Join(",", images.Select(i => i.Value))), "data");
                var all = Items(Object(data, "images"), id).ToList();
                foreach (var image in images)
                {
                    var found = all.FirstOrDefault(i => Text(i, "type") == image.Value);
                    if (found != null)
                        candidate.SetMedia(image.Key, BaseUrl(Object(data, "base_url")) + Text(found, "filename"));
                }
            }

            return candidate.IsEmpty ? null : candidate;
        }

        /// <summary>Gêneros pelo id: a lista vem uma vez por execução, e só quando um gênero desta fonte é esperado.</summary>
        string GenreNames(IDictionary<string, object> game)
        {
            if (!game.TryGetValue("genres", out var raw) || !(raw is IEnumerable list) || raw is string)
                return null;

            var ids = list.Cast<object>().Select(g => Convert.ToString(g, CultureInfo.InvariantCulture)).ToList();
            if (ids.Count == 0)
                return null;

            if (_genres == null)
            {
                _genres = new Dictionary<string, string>(StringComparer.Ordinal);
                var genres = Object(Object(Call("Genres", ""), "data"), "genres");
                if (genres != null)
                {
                    foreach (var genre in genres.Values.OfType<IDictionary<string, object>>())
                    {
                        var id = Number(genre, "id");
                        var name = Text(genre, "name");
                        if (id != null && name != null)
                            _genres[id] = name;
                    }
                }
            }

            var names = ids.Select(i => _genres.TryGetValue(i, out var n) ? GameInfoText.TranslateGenre(n) : null).Where(n => n != null).Take(2).ToList();
            return names.Count == 0 ? null : string.Join(" / ", names);
        }

        IDictionary<string, object> Call(string endpoint, string arguments)
        {
            var answer = _http.Get(Api + endpoint + "?apikey=" + Uri.EscapeDataString(_key) + arguments);
            if (answer.Status == 404)
                return null;

            // 403: chave recusada ou limite do mês ("allowance").
            if (!answer.Ok)
                throw new MediaSourceException("TheGamesDB: HTTP " + answer.Status);

            try
            {
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(answer.Body);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new MediaSourceException("TheGamesDB: resposta inválida");
            }
        }

        static string BaseUrl(IDictionary<string, object> baseUrl) => Text(baseUrl, "large") ?? Text(baseUrl, "original") ?? "";

        static IDictionary<string, object> Object(IDictionary<string, object> item, string key) =>
            item != null && item.TryGetValue(key, out var value) ? value as IDictionary<string, object> : null;

        static string Text(IDictionary<string, object> item, string key) =>
            item != null && item.TryGetValue(key, out var value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;

        static string Number(IDictionary<string, object> item, string key) =>
            item != null && item.TryGetValue(key, out var value) && value != null && !(value is IEnumerable) ? Convert.ToString(value, CultureInfo.InvariantCulture) :
            Text(item, key);

        static IEnumerable<IDictionary<string, object>> Items(IDictionary<string, object> item, string key) =>
            item != null && key != null && item.TryGetValue(key, out var value) && value is IEnumerable list && !(value is string)
                ? list.OfType<IDictionary<string, object>>()
                : Enumerable.Empty<IDictionary<string, object>>();
    }
}
