using System;
using System.Collections.Generic;
using System.Linq;

namespace Retroboat.Integrations
{
    public enum IntegrationKind
    {
        /// <summary>Usa uma chave de desenvolvedor que vem no build do ES; o Retroboat troca sozinho quando ela para.</summary>
        BuiltInKey,

        /// <summary>Fonte pública, sem chave nem conta.</summary>
        Public,

        /// <summary>Usa a conta da própria pessoa, que ela informa no ES (o Retroboat não tem como trocar).</summary>
        Account
    }

    /// <summary>Um serviço de fora que o Retroboat ou o nosso ES usa, como aparece na tela INTEGRAÇÕES do menu NUVEM.</summary>
    public sealed class Integration
    {
        public Integration(string id, string name, string purpose, IntegrationKind kind, params string[] credentials)
        {
            Id = id;
            Name = name;
            Purpose = purpose;
            Kind = kind;
            Credentials = credentials;
        }

        /// <summary>Identificador estável (arquivo de estado, linha de comando).</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>Para que serve, numa frase curta (a descrição da linha no ES).</summary>
        public string Purpose { get; }

        public IntegrationKind Kind { get; }

        /// <summary>
        /// As credenciais, com os nomes do build do ES (CMakeLists do fork). A primeira é a que a verificação testa e que a busca
        /// automática troca; as outras acompanham (o nome do software do ScreenScraper).
        /// </summary>
        public IReadOnlyList<string> Credentials { get; }
    }

    /// <summary>
    /// As integrações do Retroboat (RB-037). As chaves de desenvolvedor são as do RetroBat, reaproveitadas porque o Retroboat
    /// é um mod pessoal e privado; quando uma para de funcionar, a busca automática acha a nova no repositório do RetroBat
    /// (<see cref="CredentialRefresh"/>).
    /// </summary>
    public static class IntegrationCatalog
    {
        public const string ScreenScraperLogin = "SCREENSCRAPER_DEV_LOGIN";
        public const string ScreenScraperSoftName = "SCREENSCRAPER_SOFTNAME";
        public const string GamesDbKey = "GAMESDB_APIKEY";
        public const string CheevosLogin = "CHEEVOS_DEV_LOGIN";
        public const string HfsLogin = "HFS_DEV_LOGIN";

        public static readonly IReadOnlyList<Integration> All = new[]
        {
            new Integration("screenscraper", "ScreenScraper", "Mídias e informações no PROCURAR MÍDIAS do ES.", IntegrationKind.BuiltInKey, ScreenScraperLogin, ScreenScraperSoftName),
            new Integration("thegamesdb", "TheGamesDB", "Mídias e informações no PROCURAR MÍDIAS do ES.", IntegrationKind.BuiltInKey, GamesDbKey),
            new Integration("hfsdb", "HfsDB", "Mídias e informações no PROCURAR MÍDIAS do ES.", IntegrationKind.BuiltInKey, HfsLogin),
            new Integration("retroachievements", "RetroAchievements", "Conquistas dos jogos no ES.", IntegrationKind.BuiltInKey, CheevosLogin),
            new Integration("libretro", "libretro", "Capas e informações automáticas.", IntegrationKind.Public),
            new Integration("wikidata", "Wikidata e Wikipédia", "Descrições em português, gênero e estúdio.", IntegrationKind.Public),
            new Integration("arcadedb", "ArcadeDB", "Mídias de jogos de arcade no PROCURAR MÍDIAS do ES.", IntegrationKind.Public),
            new Integration("igdb", "IGDB", "PROCURAR MÍDIAS do ES com a sua conta Twitch.", IntegrationKind.Account)
        };

        /// <summary>Todas as credenciais de desenvolvedor, na ordem do arquivo.</summary>
        public static IEnumerable<string> AllCredentials => All.SelectMany(i => i.Credentials).Distinct(StringComparer.Ordinal);

        public static Integration Find(string id) => All.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
