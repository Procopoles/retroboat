using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace Retroboat.Es
{
    /// <summary>Um jogo como o ES o conhece: o id da API (md5 do caminho) e o caminho do arquivo.</summary>
    public sealed class EsGame
    {
        public string Id { get; set; }
        public string Path { get; set; }
    }

    /// <summary>Os jogos que o ES aberto tem na memória (RB-041). Separado para os testes não precisarem de um ES.</summary>
    public interface IEsGames
    {
        /// <summary>O ES está aberto e sem jogo rodando, scraper ou "Atualizar listas de jogos".</summary>
        bool IsIdle();

        /// <summary>Os jogos do sistema. Nulo sem ES; vazio se o ES não tem o sistema.</summary>
        IReadOnlyList<EsGame> Games(string system);

        /// <summary>Os campos do jogo na memória do ES, com as mídias como caminho no disco. Nulo sem ES; vazio sem o jogo.</summary>
        IReadOnlyDictionary<string, string> Game(string system, string id);

        /// <summary>POST /addgames: false se o ES não recebeu.</summary>
        bool AddGames(string system, string gamelistXml);
    }

    /// <summary>A API HTTP local do ES (<see cref="EsApi"/>).</summary>
    public sealed class EsApiGames : IEsGames
    {
        public bool IsIdle() => EsApi.IsIdle();

        public IReadOnlyList<EsGame> Games(string system)
        {
            var json = EsApi.Get("/systems/" + Uri.EscapeDataString(system) + "/games", out var notFound, 15000);
            if (json == null)
                return notFound ? new List<EsGame>() : null;

            var games = new List<EsGame>();
            if (Parse(json) is IEnumerable list && !(list is IDictionary))
            {
                foreach (var entry in list.OfType<Dictionary<string, object>>())
                {
                    if (entry.TryGetValue("id", out var id) && entry.TryGetValue("path", out var path))
                        games.Add(new EsGame { Id = id as string, Path = path as string });
                }
            }

            return games;
        }

        public IReadOnlyDictionary<string, string> Game(string system, string id)
        {
            var json = EsApi.Get("/systems/" + Uri.EscapeDataString(system) + "/games/" + Uri.EscapeDataString(id) + "?localpaths=true", out var notFound);
            if (json == null)
                return notFound ? new Dictionary<string, string>() : null;
            if (!(Parse(json) is Dictionary<string, object> game))
                return new Dictionary<string, string>();

            return game.Where(p => p.Value is string).ToDictionary(p => p.Key, p => (string)p.Value, StringComparer.OrdinalIgnoreCase);
        }

        public bool AddGames(string system, string gamelistXml) => EsApi.AddGames(system, gamelistXml);

        static object Parse(string json)
        {
            try
            {
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
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
    }
}
