using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Retroboat.Library;

namespace Retroboat.Media
{
    public sealed class MediaSearchResult
    {
        /// <summary>As informações do jogo pela regra do Retroboat, ou nulo se nenhuma fonte conhece o jogo.</summary>
        public GameInfo Info { get; set; }

        /// <summary>Endereço de cada imagem que o libretro tem do jogo.</summary>
        public Dictionary<ThumbnailKind, string> Images { get; } = new Dictionary<ThumbnailKind, string>();

        /// <summary>Fontes que não responderam (sem internet, fora do ar). As outras continuam valendo.</summary>
        public List<string> Errors { get; } = new List<string>();

        public bool Found => Info != null || Images.Count > 0;
    }

    /// <summary>
    /// PROCURAR MÍDIAS de um jogo no ES (RB-036), com a regra da importação automática (<see cref="MediaRule"/>): as
    /// mesmas fontes, o mesmo jeito de achar o jogo pelo nome e a mesma escolha de cada campo. Não grava nada: o ES mostra
    /// o resultado, baixa as imagens e grava no gamelist, como faz com os scrapers dele.
    /// </summary>
    public sealed class MediaSearch
    {
        readonly IThumbnailSource _thumbnails;
        readonly IReadOnlyList<IGameInfoSource> _sources;

        public MediaSearch(IThumbnailSource thumbnails, IReadOnlyList<IGameInfoSource> sources)
        {
            _thumbnails = thumbnails;
            _sources = sources;
        }

        /// <summary>Os sistemas do ES que alguma fonte conhece: o ES só oferece a busca do Retroboat neles.</summary>
        public static IReadOnlyList<string> Systems =>
            AutoMedia.Playlists.Keys.Concat(WikidataInfo.Platforms.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();

        /// <summary>As fontes da importação, com o cache deste PC.</summary>
        public static MediaSearch ForInstall(RetroBatInstall install) =>
            new MediaSearch(new LibretroThumbnails(LibretroThumbnails.CacheDir(install)), AutoInfo.DefaultSources(install));

        /// <param name="system">O sistema do ES (ex.: "ps2").</param>
        /// <param name="name">O nome do arquivo sem extensão, ou o nome do jogo que a pessoa digitou.</param>
        public MediaSearchResult Find(string system, string name)
        {
            var result = new MediaSearchResult();
            if (string.IsNullOrWhiteSpace(system) || string.IsNullOrWhiteSpace(name))
                return result;

            name = name.Trim();
            if (AutoMedia.Playlists.TryGetValue(system, out var playlist))
            {
                // Uma fonte fora do ar não impede a outra, como na importação.
                try
                {
                    foreach (var image in MediaRule.MatchThumbnails(name, MediaRule.Indexes(_thumbnails, playlist)))
                        result.Images[image.Key] = _thumbnails.UrlOf(playlist, image.Key, image.Value);
                }
                catch (Exception ex) when (ex is WebException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Errors.Add("imagens do libretro: " + ex.Message);
                }
            }

            var infos = new List<GameInfo>();
            foreach (var source in _sources)
            {
                try
                {
                    if (source.Find(system, new[] { name }).TryGetValue(name, out var info))
                        infos.Add(info);
                }
                catch (Exception ex) when (ex is WebException || ex is IOException || ex is InvalidOperationException || ex is ArgumentException)
                {
                    result.Errors.Add(ex.Message);
                }
            }

            if (infos.Count > 0)
                result.Info = MediaRule.Merge(infos);

            return result;
        }
    }
}
