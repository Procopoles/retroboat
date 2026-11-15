using System;
using System.Collections.Generic;
using System.Linq;

namespace Retroboat.Media.Sources
{
    /// <summary>
    /// Wikidata e Wikipédia no PERSONALIZADO (RB-038): nome oficial, descrição em português, gênero, estúdio, editora e
    /// data, sem chave nem conta (<see cref="WikidataInfo"/>). Os jogos de um sistema vão numa consulta só.
    /// </summary>
    public sealed class WikipediaMediaSource : IBatchMediaSource
    {
        readonly IGameInfoSource _info;
        readonly Dictionary<string, GameInfo> _prepared = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _preparedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public WikipediaMediaSource(IGameInfoSource info)
        {
            _info = info;
        }

        public string Id => MediaSourceIds.Wikipedia;

        public bool Supports(string system) => system != null && WikidataInfo.Platforms.ContainsKey(system);

        public void Prepare(string system, IReadOnlyCollection<string> stems)
        {
            if (!Supports(system))
                return;

            var missing = stems.Where(s => !_preparedStems.Contains(Key(system, s))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count == 0)
                return;

            foreach (var found in _info.Find(system, missing))
                _prepared[Key(system, found.Key)] = found.Value;
            foreach (var stem in missing)
                _preparedStems.Add(Key(system, stem));
        }

        public MediaCandidate Find(MediaQuery query, IReadOnlyCollection<string> wanted)
        {
            if (!Supports(query.System))
                return null;

            var key = Key(query.System, query.SearchName);
            if (!_preparedStems.Contains(key))
                Prepare(query.System, new[] { query.SearchName });

            if (!_prepared.TryGetValue(key, out var info))
                return null;

            var candidate = new MediaCandidate(Id);
            CopyInfo(info, candidate);
            return candidate.IsEmpty ? null : candidate;
        }

        /// <summary>As informações de uma fonte da importação antiga (libretro, Wikidata) nos campos do PERSONALIZADO.</summary>
        internal static void CopyInfo(GameInfo info, MediaCandidate candidate)
        {
            candidate.SetText(MediaFields.Name, info.Title);
            candidate.SetText(MediaFields.Description, info.Description);
            candidate.DescriptionInPortuguese = info.DescriptionInPortuguese && !string.IsNullOrWhiteSpace(info.Description);
            candidate.SetText(MediaFields.Genre, info.Genre);
            candidate.SetText(MediaFields.Developer, info.Developer);
            candidate.SetText(MediaFields.Publisher, info.Publisher);
            candidate.SetText(MediaFields.ReleaseDate, info.ReleaseDate);
            candidate.SetText(MediaFields.Players, info.Players);
        }

        static string Key(string system, string stem) => system + "/" + stem;
    }
}
