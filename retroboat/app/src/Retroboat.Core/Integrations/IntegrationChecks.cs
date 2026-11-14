using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Retroboat.Integrations
{
    public enum IntegrationState
    {
        /// <summary>Ainda não verificada (sem internet na primeira vez, ou nunca rodou).</summary>
        Unknown,

        Ok,

        /// <summary>A API recusou a chave: ela parou de funcionar e precisa ser trocada.</summary>
        KeyRejected,

        /// <summary>A chave vale, mas o limite de uso dela acabou (por dia ou por mês).</summary>
        LimitReached,

        /// <summary>O serviço não respondeu: fora do ar ou sem internet. Não é a chave.</summary>
        Unreachable,

        /// <summary>A integração usa a conta da pessoa, e não há conta informada no ES.</summary>
        NoAccount
    }

    /// <summary>O resultado de uma consulta de verificação.</summary>
    public sealed class CheckResult
    {
        public CheckResult(IntegrationState state, string detail = null)
        {
            State = state;
            Detail = detail;
        }

        public IntegrationState State { get; }

        /// <summary>O que a API respondeu, para o log (ex.: "HTTP 403: Invalid API key was provided.").</summary>
        public string Detail { get; }
    }

    /// <summary>Faz uma requisição HTTP e devolve o código e o corpo, mesmo de erro. Separado para os testes não irem à internet.</summary>
    public interface IHttpProbe
    {
        /// <returns>Código HTTP e corpo; lança WebException só sem resposta (sem internet, DNS, tempo esgotado).</returns>
        (int Status, string Body) Send(string url, string postBody = null, string basicAuth = null);
    }

    public sealed class HttpProbe : IHttpProbe
    {
        public const string UserAgent = "Retroboat/0.1 (https://github.com/Procopoles/retroboat)";

        public (int Status, string Body) Send(string url, string postBody = null, string basicAuth = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (basicAuth != null)
                request.Headers[HttpRequestHeader.Authorization] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(basicAuth));

            if (postBody != null)
            {
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                var body = Encoding.UTF8.GetBytes(postBody);
                using (var stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                    return ((int)response.StatusCode, Read(response));
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse error)
            {
                using (error)
                    return ((int)error.StatusCode, Read(error));
            }
        }

        static string Read(WebResponse response)
        {
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var text = reader.ReadToEnd();
                return text.Length > 2000 ? text.Substring(0, 2000) : text;
            }
        }
    }

    /// <summary>
    /// Uma consulta leve por integração, que separa "a chave parou" (troca automática) de "o serviço não respondeu" (não é a
    /// chave: tenta de novo depois). As respostas de chave recusada foram vistas ao vivo em 14/11/2026: ScreenScraper 403
    /// "Erreur de login : Vérifier vos identifiants développeur !", TheGamesDB 403 "Invalid API key", RetroAchievements 401
    /// "Unauthenticated.", HfsDB 403 "Invalid username/password.".
    /// </summary>
    public sealed class IntegrationChecks
    {
        readonly IHttpProbe _http;

        public IntegrationChecks(IHttpProbe http = null)
        {
            _http = http ?? new HttpProbe();
        }

        /// <param name="credentials">As credenciais em uso (nome do build → valor). As que faltam contam como sem chave.</param>
        /// <param name="account">Para as integrações com conta da pessoa: id e segredo (IGDB); nulo sem conta.</param>
        public CheckResult Check(Integration integration, IReadOnlyDictionary<string, string> credentials, (string Id, string Secret)? account = null)
        {
            try
            {
                switch (integration.Id)
                {
                    case "screenscraper": return ScreenScraper(Value(credentials, IntegrationCatalog.ScreenScraperLogin), Value(credentials, IntegrationCatalog.ScreenScraperSoftName));
                    case "thegamesdb": return TheGamesDb(Value(credentials, IntegrationCatalog.GamesDbKey));
                    case "hfsdb": return HfsDb(Value(credentials, IntegrationCatalog.HfsLogin));
                    case "retroachievements": return RetroAchievements(Value(credentials, IntegrationCatalog.CheevosLogin));
                    case "libretro": return Worst(Reachable("https://thumbnails.libretro.com/"), Reachable("https://raw.githubusercontent.com/libretro/libretro-database/master/README.md"));
                    case "wikidata": return Worst(Reachable("https://query.wikidata.org/sparql?query=ASK%7B%7D&format=json"), Reachable("https://pt.wikipedia.org/w/api.php?action=query&format=json"));
                    case "arcadedb": return Reachable("https://adb.arcadeitalia.net/service_scraper.php?ajax=query_mame&game_name=sf2");
                    case "igdb": return Igdb(account);
                    default: return new CheckResult(IntegrationState.Unknown, "integração desconhecida");
                }
            }
            catch (Exception ex) when (ex is WebException || ex is IOException || ex is UriFormatException)
            {
                return new CheckResult(IntegrationState.Unreachable, ex.Message);
            }
        }

        /// <summary>Testa uma chave candidata (troca automática): só a resposta "funciona" vale.</summary>
        public bool Works(Integration integration, IReadOnlyDictionary<string, string> credentials) =>
            Check(integration, credentials).State == IntegrationState.Ok;

        CheckResult ScreenScraper(string login, string softName)
        {
            if (login == null)
                return new CheckResult(IntegrationState.KeyRejected, "sem chave");

            var (status, body) = _http.Send("https://api.screenscraper.fr/api2/ssinfraInfos.php?" + login +
                                            "&softname=" + Uri.EscapeDataString(softName ?? "Retroboat") + "&output=json");
            if (status == 200 && body.Contains("\"success\": \"true\""))
                return new CheckResult(IntegrationState.Ok);

            if (body.IndexOf("identifiants", StringComparison.OrdinalIgnoreCase) >= 0 || body.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0)
                return new CheckResult(IntegrationState.KeyRejected, Describe(status, body));

            // 423: API fechada; 429: muitos acessos ao mesmo tempo; 430/431: cota do dia.
            if (status == 429 || status == 430 || status == 431)
                return new CheckResult(IntegrationState.LimitReached, Describe(status, body));

            return new CheckResult(IntegrationState.Unreachable, Describe(status, body));
        }

        CheckResult TheGamesDb(string key)
        {
            if (key == null)
                return new CheckResult(IntegrationState.KeyRejected, "sem chave");

            var (status, body) = _http.Send("https://api.thegamesdb.net/v1/Genres?apikey=" + Uri.EscapeDataString(key));
            if (status == 200 && body.Contains("\"code\":200"))
                return new CheckResult(IntegrationState.Ok);

            if (body.IndexOf("allowance", StringComparison.OrdinalIgnoreCase) >= 0 && body.IndexOf("Invalid API key", StringComparison.OrdinalIgnoreCase) < 0)
                return new CheckResult(IntegrationState.LimitReached, Describe(status, body));

            if (status == 401 || status == 403 || body.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0)
                return new CheckResult(IntegrationState.KeyRejected, Describe(status, body));

            return new CheckResult(IntegrationState.Unreachable, Describe(status, body));
        }

        CheckResult HfsDb(string login)
        {
            var separator = login?.IndexOf(':') ?? -1;
            if (separator <= 0)
                return new CheckResult(IntegrationState.KeyRejected, "sem chave");

            var user = login.Substring(0, separator);
            var password = login.Substring(separator + 1);
            var (status, body) = _http.Send("https://db.hfsplay.fr/api/v1/auth/token",
                "&username=" + Uri.EscapeDataString(user) + "&password=" + Uri.EscapeDataString(password), login);
            if (status == 200 && body.Contains("\"token\""))
                return new CheckResult(IntegrationState.Ok);

            if (status == 400 || status == 401 || status == 403)
                return new CheckResult(IntegrationState.KeyRejected, Describe(status, body));

            if (status == 429)
                return new CheckResult(IntegrationState.LimitReached, Describe(status, body));

            return new CheckResult(IntegrationState.Unreachable, Describe(status, body));
        }

        CheckResult RetroAchievements(string login)
        {
            if (login == null)
                return new CheckResult(IntegrationState.KeyRejected, "sem chave");

            var (status, body) = _http.Send("https://retroachievements.org/API/API_GetConsoleIDs.php?" + login);
            if (status == 200 && body.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return new CheckResult(IntegrationState.Ok);

            if (status == 401 || status == 403 || body.IndexOf("Unauthenticated", StringComparison.OrdinalIgnoreCase) >= 0)
                return new CheckResult(IntegrationState.KeyRejected, Describe(status, body));

            if (status == 429)
                return new CheckResult(IntegrationState.LimitReached, Describe(status, body));

            return new CheckResult(IntegrationState.Unreachable, Describe(status, body));
        }

        CheckResult Igdb((string Id, string Secret)? account)
        {
            if (account == null || string.IsNullOrEmpty(account.Value.Id) || string.IsNullOrEmpty(account.Value.Secret))
                return new CheckResult(IntegrationState.NoAccount);

            var (status, body) = _http.Send("https://id.twitch.tv/oauth2/token",
                "client_id=" + Uri.EscapeDataString(account.Value.Id) + "&client_secret=" + Uri.EscapeDataString(account.Value.Secret) + "&grant_type=client_credentials");
            if (status == 200 && body.Contains("access_token"))
                return new CheckResult(IntegrationState.Ok);

            if (status == 400 || status == 401 || status == 403)
                return new CheckResult(IntegrationState.KeyRejected, Describe(status, body));

            return new CheckResult(IntegrationState.Unreachable, Describe(status, body));
        }

        CheckResult Reachable(string url)
        {
            var (status, body) = _http.Send(url);
            if (status >= 200 && status < 400)
                return new CheckResult(IntegrationState.Ok);

            return new CheckResult(status == 429 ? IntegrationState.LimitReached : IntegrationState.Unreachable, Describe(status, body));
        }

        static CheckResult Worst(params CheckResult[] results)
        {
            foreach (var result in results)
                if (result.State != IntegrationState.Ok)
                    return result;

            return results[0];
        }

        static string Value(IReadOnlyDictionary<string, string> credentials, string name) =>
            credentials != null && credentials.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

        static string Describe(int status, string body)
        {
            var text = (body ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 160)
                text = text.Substring(0, 160) + "...";

            return "HTTP " + status + (text.Length > 0 ? ": " + text : "");
        }
    }
}
