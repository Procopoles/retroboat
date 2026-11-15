using System;
using System.Collections.Generic;
using System.Linq;

namespace Retroboat.Media.Sources
{
    /// <summary>
    /// libretro no PERSONALIZADO (RB-038): capa, tela do jogo e tela de título dos thumbnails, e editora, data, jogadores
    /// e gênero da base de dados, pelo nome No-Intro/Redump do arquivo (<see cref="MediaRule"/>). Sem chave nem conta. As
    /// listas de nomes vêm uma vez por sistema e ficam em cache no PC.
    /// </summary>
    public sealed class LibretroMediaSource : IBatchMediaSource
    {
        readonly IThumbnailSource _thumbnails;
        readonly IGameInfoSource _metadata;
        readonly Dictionary<string, Dictionary<ThumbnailKind, ThumbnailIndex>> _indexes = new Dictionary<string, Dictionary<ThumbnailKind, ThumbnailIndex>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, GameInfo> _prepared = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _preparedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public LibretroMediaSource(IThumbnailSource thumbnails, IGameInfoSource metadata)
        {
            _thumbnails = thumbnails;
            _metadata = metadata;
        }

        public string Id => MediaSourceIds.Libretro;

        public bool Supports(string system) => system != null && LibretroThumbnails.Playlists.ContainsKey(system);

        public void Prepare(string system, IReadOnlyCollection<string> stems)
        {
            if (!Supports(system) || _metadata == null)
                return;

            var missing = stems.Where(s => !_preparedStems.Contains(Key(system, s))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count == 0)
                return;

            foreach (var found in _metadata.Find(system, missing))
                _prepared[Key(system, found.Key)] = found.Value;
            foreach (var stem in missing)
                _preparedStems.Add(Key(system, stem));
        }

        public MediaCandidate Find(MediaQuery query, IReadOnlyCollection<string> wanted)
        {
            if (!Supports(query.System))
                return null;

            var candidate = new MediaCandidate(Id);
            var playlist = LibretroThumbnails.Playlists[query.System];

            if (wanted.Any(w => MediaFields.Find(w)?.IsMedia == true))
            {
                if (!_indexes.TryGetValue(playlist, out var indexes))
                    _indexes[playlist] = indexes = MediaRule.Indexes(_thumbnails, playlist);

                var names = MediaRule.MatchThumbnails(query.SearchName, indexes);
                foreach (var target in MediaRule.ImageTargets)
                {
                    if (names.TryGetValue(target.Kind, out var name))
                        candidate.SetMedia(target.Fields[0], _thumbnails.UrlOf(playlist, target.Kind, name), ".png");
                }
            }

            if (_metadata != null && wanted.Any(w => MediaFields.Find(w)?.IsMedia == false))
            {
                var key = Key(query.System, query.SearchName);
                if (!_preparedStems.Contains(key))
                    Prepare(query.System, new[] { query.SearchName });

                if (_prepared.TryGetValue(key, out var info))
                    WikipediaMediaSource.CopyInfo(info, candidate);
            }

            return candidate.IsEmpty ? null : candidate;
        }

        static string Key(string system, string stem) => system + "/" + stem;
    }
}
