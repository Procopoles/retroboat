using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Retroboat.Accounts;
using Retroboat.Integrations;
using Retroboat.Library;
using Retroboat.Media.Sources;

namespace Retroboat.Media
{
    public sealed class CustomMediaResult
    {
        /// <summary>Campo de texto → valor, já no formato do gamelist.</summary>
        public Dictionary<string, string> Texts { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool DescriptionInPortuguese { get; set; }

        /// <summary>Campo de mídia → arquivo para baixar.</summary>
        public Dictionary<string, MediaUrl> Media { get; } = new Dictionary<string, MediaUrl>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Campo → a fonte de onde ele veio (para o log e para o ES).</summary>
        public Dictionary<string, string> Origins { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fontes que não responderam (sem internet, fora do ar, limite do dia). As outras continuam valendo.</summary>
        public List<string> Errors { get; } = new List<string>();

        public bool Found => Texts.Count > 0 || Media.Count > 0;
    }

    /// <summary>
    /// A busca PERSONALIZADO (RB-038): cada campo vem da primeira fonte da sua lista no <see cref="MediaProfile"/> que o
    /// tenha. As fontes são consultadas aos poucos, em paralelo, e só as que ainda podem decidir algum campo: com o padrão,
    /// HfsDB, ScreenScraper, libretro e Wikipédia na primeira rodada; o TheGamesDB (limite por mês) só se faltar algo.
    ///
    /// A descrição tem uma regra a mais: vale a primeira, na ordem, que esteja em português e tenha mais que uma frase; sem
    /// nenhuma assim, a primeira que existir. Sem tela do jogo, a capa também serve de imagem (como o libretro fazia).
    ///
    /// Usada pelo PROCURAR MÍDIAS do ES (<c>retroboat-cli media search</c>) e pela importação automática dos jogos novos
    /// (<see cref="AutoImport"/>). Não grava nada.
    /// </summary>
    public sealed class CustomMediaSearch
    {
        /// <summary>Resumo em português menor que isso é um esboço; uma descrição mais completa, mesmo em inglês, diz mais.</summary>
        public const int MinPortugueseLength = 120;

        readonly MediaProfile _profile;
        readonly IReadOnlyList<IMediaSource> _sources;

        public CustomMediaSearch(MediaProfile profile, IReadOnlyList<IMediaSource> sources)
        {
            _profile = profile;
            _sources = sources;
        }

        public MediaProfile Profile => _profile;

        public IReadOnlyList<IMediaSource> Sources => _sources;

        /// <summary>As fontes deste PC: chaves do credenciais.ini, conta do ScreenScraper do ES e o cache do libretro.</summary>
        public static CustomMediaSearch ForInstall(RetroBatInstall install, MediaProfile profile = null) =>
            new CustomMediaSearch(profile ?? MediaProfile.ForInstall(install), DefaultSources(install));

        public static IReadOnlyList<IMediaSource> DefaultSources(RetroBatInstall install, IMediaHttp http = null)
        {
            http = http ?? new MediaHttp();
            var keys = CredentialStore.Load(CredentialStore.GetPath(install));

            string user = null, password = null;
            var esSettings = Path.Combine(install.UserEmulationStationPath, "es_settings.cfg");
            try
            {
                if (File.Exists(esSettings))
                {
                    var settings = EsSettings.Load(esSettings);
                    user = settings.Get("ScreenScraperUser");
                    password = settings.Get("ScreenScraperPass");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                // Sem a conta, a chave de desenvolvedor sozinha também serve.
            }

            var cache = LibretroThumbnails.CacheDir(install);
            return new IMediaSource[]
            {
                new ScreenScraperSource(http, keys.Get(IntegrationCatalog.ScreenScraperLogin), keys.Get(IntegrationCatalog.ScreenScraperSoftName), user, password),
                new HfsDbSource(http, keys.Get(IntegrationCatalog.HfsLogin)),
                new TheGamesDbSource(http, keys.Get(IntegrationCatalog.GamesDbKey)),
                new LibretroMediaSource(new LibretroThumbnails(cache), new LibretroMetadata(cache)),
                new WikipediaMediaSource(new WikidataInfo())
            };
        }

        /// <summary>Os sistemas do ES que alguma fonte conhece: o ES só oferece o PERSONALIZADO neles.</summary>
        public IReadOnlyList<string> Systems =>
            LibretroThumbnails.Playlists.Keys
                .Concat(WikidataInfo.Platforms.Keys)
                .Concat(new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb }.SelectMany(MediaSystems.SystemsOf))
                .Where(s => _sources.Any(source => source.Supports(s)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

        /// <summary>Consulta de uma vez, nas fontes que aceitam, os jogos de um sistema que vão ser buscados em seguida.</summary>
        public void Prepare(string system, IReadOnlyCollection<string> stems, List<string> errors)
        {
            foreach (var source in _sources.OfType<IBatchMediaSource>().Where(s => s.Supports(system) && _profile.Sources.Contains(s.Id, StringComparer.OrdinalIgnoreCase)))
            {
                try
                {
                    source.Prepare(system, stems);
                }
                catch (Exception ex) when (IsSourceFailure(ex))
                {
                    errors?.Add(MediaSourceIds.Label(source.Id) + ": " + ex.Message);
                }
            }
        }

        /// <param name="fields">Os campos que se quer (ids de <see cref="MediaFields"/>); nulo = todos os do perfil.</param>
        public CustomMediaResult Find(MediaQuery query, IEnumerable<string> fields = null)
        {
            var result = new CustomMediaResult();
            var wanted = (fields ?? MediaFields.All.Select(f => f.Id))
                .Where(f => MediaFields.Find(f) != null && !_profile.IsOff(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (wanted.Count == 0 || string.IsNullOrWhiteSpace(query.System) || string.IsNullOrWhiteSpace(query.SearchName))
                return result;

            var sources = _sources.Where(s => s.Supports(query.System)).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
            var answers = new Dictionary<string, MediaCandidate>(StringComparer.OrdinalIgnoreCase);
            var pending = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);

            while (pending.Count > 0)
            {
                // Quem decide cada campo agora: a próxima fonte da lista que ainda não respondeu. Cada fonte responde uma
                // vez só, então leva todos os campos que ainda podem vir dela, não só os que ela decide agora.
                var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in pending.ToList())
                {
                    var source = NextSource(field, sources, answers, result);
                    if (source != null)
                        next.Add(source);
                    else if (TryResolve(field, sources, answers, result))
                        pending.Remove(field);
                }

                if (next.Count == 0)
                    break;

                var round = next.ToDictionary(s => s, s => pending.Where(f => _profile.Order(f).Contains(s, StringComparer.OrdinalIgnoreCase)).ToList(), StringComparer.OrdinalIgnoreCase);

                Ask(query, round, sources, answers, result);

                foreach (var field in pending.ToList())
                {
                    if (TryResolve(field, sources, answers, result))
                        pending.Remove(field);
                }
            }

            // Sem tela do jogo, a capa também serve de imagem na visão detalhada.
            if (wanted.Contains(MediaFields.Screenshot, StringComparer.OrdinalIgnoreCase) && !result.Media.ContainsKey(MediaFields.Screenshot) &&
                result.Media.TryGetValue(MediaFields.Cover, out var cover))
            {
                result.Media[MediaFields.Screenshot] = cover;
                result.Origins[MediaFields.Screenshot] = result.Origins[MediaFields.Cover];
            }

            return result;
        }

        /// <summary>A próxima fonte que precisa responder para o campo ser decidido; nulo se ele já pode ser decidido.</summary>
        string NextSource(string field, Dictionary<string, IMediaSource> sources, Dictionary<string, MediaCandidate> answers, CustomMediaResult result)
        {
            var order = _profile.Order(field).Where(sources.ContainsKey).ToList();

            if (string.Equals(field, MediaFields.Description, StringComparison.OrdinalIgnoreCase))
            {
                // Primeiro, todas as que podem ter em português; a primeira delas que tiver, vale.
                foreach (var source in order.Where(s => MediaSourceIds.Portuguese.Contains(s, StringComparer.OrdinalIgnoreCase)))
                {
                    if (!answers.ContainsKey(source))
                        return source;
                    if (IsGoodPortuguese(answers[source]))
                        return null;
                }
            }

            foreach (var source in order)
            {
                if (!answers.ContainsKey(source))
                    return source;
                if (answers[source]?.Has(field) == true)
                    return null;
            }

            return null;
        }

        bool TryResolve(string field, Dictionary<string, IMediaSource> sources, Dictionary<string, MediaCandidate> answers, CustomMediaResult result)
        {
            if (NextSource(field, sources, answers, result) != null)
                return false;

            var order = _profile.Order(field).Where(answers.ContainsKey).ToList();
            MediaCandidate chosen = null;
            if (string.Equals(field, MediaFields.Description, StringComparison.OrdinalIgnoreCase))
                chosen = order.Select(s => answers[s]).FirstOrDefault(IsGoodPortuguese);
            chosen = chosen ?? order.Select(s => answers[s]).FirstOrDefault(c => c?.Has(field) == true);

            if (chosen != null)
            {
                if (chosen.Texts.TryGetValue(field, out var text))
                    result.Texts[field] = text;
                else
                    result.Media[field] = chosen.Media[field];

                if (string.Equals(field, MediaFields.Description, StringComparison.OrdinalIgnoreCase))
                    result.DescriptionInPortuguese = chosen.DescriptionInPortuguese;

                result.Origins[field] = chosen.Source;
            }

            return true;
        }

        static bool IsGoodPortuguese(MediaCandidate candidate) =>
            candidate != null && candidate.DescriptionInPortuguese &&
            candidate.Texts.TryGetValue(MediaFields.Description, out var text) && text.Length >= MinPortugueseLength;

        /// <summary>Uma rodada: as fontes em paralelo, cada uma com os campos que se espera dela. Uma que falha conta como sem o jogo.</summary>
        static void Ask(MediaQuery query, Dictionary<string, List<string>> round, Dictionary<string, IMediaSource> sources,
                        Dictionary<string, MediaCandidate> answers, CustomMediaResult result)
        {
            var asked = round.Keys.ToList();
            var found = new MediaCandidate[asked.Count];
            var errors = new string[asked.Count];

            Parallel.For(0, asked.Count, i =>
            {
                var source = sources[asked[i]];
                try
                {
                    found[i] = source.Find(query, round[asked[i]]);
                }
                catch (Exception ex) when (IsSourceFailure(ex))
                {
                    errors[i] = MediaSourceIds.Label(source.Id) + ": " + ex.Message;
                }
            });

            for (int i = 0; i < asked.Count; i++)
            {
                answers[asked[i]] = found[i];
                if (errors[i] != null)
                    result.Errors.Add(errors[i]);
            }
        }

        internal static bool IsSourceFailure(Exception ex) =>
            ex is WebException || ex is IOException || ex is MediaSourceException || ex is UnauthorizedAccessException ||
            ex is InvalidOperationException || ex is ArgumentException || ex is System.Xml.XmlException;
    }
}
