using System.Collections.Generic;
using System.Linq;

namespace Retroboat.Media
{
    /// <summary>
    /// Como achar um jogo nas imagens do libretro pelo nome No-Intro/Redump, e qual imagem vai em qual campo do gamelist.
    /// É a parte libretro do PERSONALIZADO (<see cref="Sources.LibretroMediaSource"/>, RB-038); qual fonte vale em cada
    /// campo é do <see cref="MediaProfile"/>.
    /// </summary>
    public static class MediaRule
    {
        /// <summary>O campo do <see cref="MediaFields"/> que cada tipo de imagem do libretro preenche.</summary>
        public static readonly IReadOnlyList<(ThumbnailKind Kind, string[] Fields)> ImageTargets = new[]
        {
            (ThumbnailKind.Boxart, new[] { MediaFields.Cover }),
            (ThumbnailKind.Snap, new[] { MediaFields.Screenshot }),
            (ThumbnailKind.Title, new[] { MediaFields.TitleScreen })
        };

        /// <summary>As listas de nomes de imagem de um sistema do libretro, uma por tipo. Lança WebException/IOException sem conexão.</summary>
        public static Dictionary<ThumbnailKind, ThumbnailIndex> Indexes(IThumbnailSource source, string playlist)
        {
            return ImageTargets.ToDictionary(t => t.Kind, t => new ThumbnailIndex(source.List(playlist, t.Kind)));
        }

        /// <summary>
        /// O nome de cada imagem do jogo no libretro. O jogo é achado pela capa (ou, sem ela, pela tela do jogo); o mesmo
        /// jogo pode ter nome um pouco diferente em cada tipo de imagem. Vazio se o libretro não conhece o jogo.
        /// </summary>
        /// <param name="name">O nome do arquivo sem extensão, ou o nome do jogo que a pessoa digitou.</param>
        public static IDictionary<ThumbnailKind, string> MatchThumbnails(string name, IReadOnlyDictionary<ThumbnailKind, ThumbnailIndex> indexes)
        {
            var names = new Dictionary<ThumbnailKind, string>();
            var match = indexes[ThumbnailKind.Boxart].Find(name) ?? indexes[ThumbnailKind.Snap].Find(name);
            if (match == null)
                return names;

            foreach (var target in ImageTargets)
            {
                var found = indexes[target.Kind].Find(match) ?? indexes[target.Kind].Find(name);
                if (found != null)
                    names[target.Kind] = found;
            }

            return names;
        }
    }
}
