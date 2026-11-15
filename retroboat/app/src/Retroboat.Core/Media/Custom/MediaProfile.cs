using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Accounts;
using Retroboat.Library;

namespace Retroboat.Media
{
    /// <summary>As fontes do PERSONALIZADO, com o nome que aparece no ES.</summary>
    public static class MediaSourceIds
    {
        public const string ScreenScraper = "screenscraper";
        public const string HfsDb = "hfsdb";
        public const string TheGamesDb = "thegamesdb";
        public const string Libretro = "libretro";
        public const string Wikipedia = "wikipedia";

        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ScreenScraper] = "SCREENSCRAPER",
            [HfsDb] = "HFSDB",
            [TheGamesDb] = "THEGAMESDB",
            [Libretro] = "LIBRETRO",
            [Wikipedia] = "WIKIPÉDIA"
        };

        /// <summary>Fontes que podem ter a descrição em português (as outras só têm em inglês).</summary>
        public static readonly IReadOnlyCollection<string> Portuguese = new[] { ScreenScraper, Wikipedia, HfsDb };

        public static string Label(string id) => id != null && Labels.TryGetValue(id, out var label) ? label : (id ?? "").ToUpperInvariant();
    }

    /// <summary>
    /// O PERSONALIZADO (RB-038): de qual fonte vem cada campo, em ordem de prioridade. A primeira fonte que tem o campo
    /// vale; sem ela, a seguinte. Vale para o PROCURAR MÍDIAS do ES e para a importação automática dos jogos novos
    /// (<see cref="AutoImport"/>).
    ///
    /// O padrão (<see cref="Preset"/>) é a melhor fonte de cada campo; a pessoa troca a primeira de cada um no ES (tela
    /// PRIORIDADE DAS FONTES), e o resto da ordem segue o padrão. Fica no es_settings.cfg, chave
    /// <see cref="EsSetting"/>, que viaja com as opções do ES entre os PCs (RB-005): "thumbnail=hfsdb;video=none". Só os
    /// campos mudados ficam lá; os outros seguem o padrão, inclusive quando ele melhorar.
    /// </summary>
    public sealed class MediaProfile
    {
        public const string EsSetting = "RetroboatMediaProfile";

        /// <summary>No perfil, "não buscar este campo".</summary>
        public const string Off = "none";

        /// <summary>
        /// A melhor ordem de cada campo, e também a lista das fontes que têm o campo. Gênero e estúdio: o Wikidata acertou
        /// mais numa comparação de 50 jogos de PS2 (o ScreenScraper errou gênero e trocou estúdio e editora em vários);
        /// editora, data e jogadores: o libretro é o da edição do arquivo. Capa e vídeo: o HfsDB tem as mais limpas.
        /// Descrição: a primeira em português que tiver mais que uma frase vale (<see cref="CustomMediaSearch"/>).
        /// Avaliação, série e mapa: só o ScreenScraper tem.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Preset = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [MediaFields.Cover] = new[] { MediaSourceIds.HfsDb, MediaSourceIds.ScreenScraper, MediaSourceIds.Libretro, MediaSourceIds.TheGamesDb },
            [MediaFields.BoxBack] = new[] { MediaSourceIds.HfsDb, MediaSourceIds.ScreenScraper, MediaSourceIds.TheGamesDb },
            [MediaFields.Screenshot] = new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.Libretro, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb },
            [MediaFields.TitleScreen] = new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.Libretro, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb },
            [MediaFields.Logo] = new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb },
            [MediaFields.FanArt] = new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.TheGamesDb, MediaSourceIds.HfsDb },
            [MediaFields.Cartridge] = new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.HfsDb },
            [MediaFields.Video] = new[] { MediaSourceIds.HfsDb, MediaSourceIds.ScreenScraper },
            [MediaFields.Manual] = new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.HfsDb },
            [MediaFields.Map] = new[] { MediaSourceIds.ScreenScraper },
            [MediaFields.Bezel] = new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.HfsDb },
            [MediaFields.Name] = new[] { MediaSourceIds.Wikipedia, MediaSourceIds.ScreenScraper, MediaSourceIds.Libretro, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb },
            [MediaFields.Description] = new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.Wikipedia, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb, MediaSourceIds.Libretro },
            [MediaFields.Rating] = new[] { MediaSourceIds.ScreenScraper },
            [MediaFields.Genre] = new[] { MediaSourceIds.Wikipedia, MediaSourceIds.ScreenScraper, MediaSourceIds.Libretro, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb },
            [MediaFields.Family] = new[] { MediaSourceIds.ScreenScraper },
            [MediaFields.Developer] = new[] { MediaSourceIds.Wikipedia, MediaSourceIds.ScreenScraper, MediaSourceIds.Libretro, MediaSourceIds.HfsDb },
            [MediaFields.Publisher] = new[] { MediaSourceIds.Libretro, MediaSourceIds.ScreenScraper, MediaSourceIds.Wikipedia, MediaSourceIds.HfsDb },
            [MediaFields.ReleaseDate] = new[] { MediaSourceIds.Libretro, MediaSourceIds.ScreenScraper, MediaSourceIds.Wikipedia, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb },
            [MediaFields.Players] = new[] { MediaSourceIds.Libretro, MediaSourceIds.ScreenScraper, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb }
        };

        /// <summary>
        /// Campos que o padrão não busca (NÃO BUSCAR), mas que a pessoa pode ligar: a moldura do jogo troca a moldura do
        /// emulador dentro do jogo, o que o RetroBat não faz sem a pessoa pedir.
        /// </summary>
        public static readonly IReadOnlyCollection<string> OffByDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MediaFields.Bezel };

        readonly Dictionary<string, IReadOnlyList<string>> _orders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        MediaProfile()
        {
            foreach (var field in MediaFields.All)
                _orders[field.Id] = OffByDefault.Contains(field.Id) ? new string[0] : Preset[field.Id];
        }

        /// <summary>A primeira fonte do campo no padrão, ou <see cref="Off"/> num campo que o padrão não busca.</summary>
        public static string DefaultFirst(string field) => OffByDefault.Contains(field) ? Off : Preset[field][0];

        public static MediaProfile Default => new MediaProfile();

        /// <summary>
        /// "thumbnail=hfsdb;desc=screenscraper,wikipedia;video=none". Cada campo: a(s) primeira(s) fonte(s), e o resto na
        /// ordem do padrão. Campo, fonte ou formato desconhecidos ficam como no padrão.
        /// </summary>
        public static MediaProfile Parse(string text)
        {
            var profile = new MediaProfile();
            foreach (var part in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = part.IndexOf('=');
                if (equals <= 0)
                    continue;

                var field = MediaFields.Find(part.Substring(0, equals).Trim());
                if (field == null)
                    continue;

                var value = part.Substring(equals + 1).Trim();
                profile.Set(field.Id, value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray());
            }

            return profile;
        }

        /// <summary>O perfil do es_settings.cfg deste PC; o padrão sem ele.</summary>
        public static MediaProfile ForInstall(RetroBatInstall install)
        {
            var path = Path.Combine(install.UserEmulationStationPath, "es_settings.cfg");
            try
            {
                return File.Exists(path) ? Parse(EsSettings.Load(path).Get(EsSetting)) : Default;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                return Default;
            }
        }

        /// <summary>A ordem das fontes do campo; vazia se o campo não é buscado.</summary>
        public IReadOnlyList<string> Order(string field) => _orders.TryGetValue(field, out var order) ? order : new string[0];

        public bool IsOff(string field) => Order(field).Count == 0;

        /// <summary>A primeira fonte do campo, ou <see cref="Off"/>.</summary>
        public string First(string field) => IsOff(field) ? Off : Order(field)[0];

        /// <summary>Põe estas fontes na frente (as que têm o campo); as outras seguem na ordem do padrão. "none" desliga o campo.</summary>
        public void Set(string field, params string[] first)
        {
            var known = MediaFields.Find(field);
            if (known == null)
                throw new ArgumentException("Campo desconhecido: " + field, nameof(field));

            bool offByDefault = OffByDefault.Contains(known.Id);
            if (first.Length == 1 && string.Equals(first[0], Off, StringComparison.OrdinalIgnoreCase))
            {
                _orders[known.Id] = new string[0];
                if (offByDefault)
                    _changed.Remove(known.Id);
                else
                    _changed[known.Id] = Off;
                return;
            }

            var preset = Preset[known.Id];
            var chosen = first.Where(s => preset.Contains(s, StringComparer.OrdinalIgnoreCase))
                              .Select(s => preset.First(p => string.Equals(p, s, StringComparison.OrdinalIgnoreCase)))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .ToList();
            if (chosen.Count == 0)
                return;

            _orders[known.Id] = chosen.Concat(preset.Where(p => !chosen.Contains(p, StringComparer.OrdinalIgnoreCase))).ToList();
            if (!offByDefault && _orders[known.Id].SequenceEqual(preset, StringComparer.OrdinalIgnoreCase))
                _changed.Remove(known.Id);
            else
                _changed[known.Id] = string.Join(",", chosen);
        }

        /// <summary>Todas as fontes que algum campo usa.</summary>
        public IReadOnlyCollection<string> Sources => _orders.Values.SelectMany(o => o).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>O valor da chave do es_settings.cfg: só os campos mudados, na ordem da tela do ES.</summary>
        public override string ToString() =>
            string.Join(";", MediaFields.All.Where(f => _changed.ContainsKey(f.Id)).Select(f => f.Id + "=" + _changed[f.Id]));
    }
}
