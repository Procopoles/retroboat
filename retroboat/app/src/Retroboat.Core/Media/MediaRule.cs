using System;
using System.Collections.Generic;
using System.Linq;

namespace Retroboat.Media
{
    /// <summary>
    /// A regra de busca de mídia do Retroboat para um jogo, numa peça só: como achar o jogo nas imagens do libretro pelo
    /// nome No-Intro/Redump, qual imagem vai em qual campo do gamelist, e de qual fonte vem cada informação. A importação
    /// automática (<see cref="AutoMedia"/>, <see cref="AutoInfo"/>) e o PROCURAR MÍDIAS do ES (<see cref="MediaSearch"/>)
    /// usam esta mesma regra; mudar aqui muda nos dois.
    /// </summary>
    public static class MediaRule
    {
        /// <summary>O que cada tipo de imagem preenche no gamelist, e o sufixo do arquivo (como o scraper do ES faz).</summary>
        public static readonly IReadOnlyList<(ThumbnailKind Kind, string Suffix, string[] Fields)> ImageTargets = new[]
        {
            (ThumbnailKind.Boxart, "thumb", new[] { "thumbnail", "boxart" }),
            (ThumbnailKind.Snap, "image", new[] { "image" }),
            (ThumbnailKind.Title, "titleshot", new[] { "titleshot" })
        };

        /// <summary>Gênero, estúdio e nome: o Wikidata é mais específico; o libretro tem "Action" para quase tudo.</summary>
        static readonly GameInfoOrigin[] WikidataFirst = { GameInfoOrigin.Wikidata, GameInfoOrigin.Libretro };

        /// <summary>Editora, data e jogadores: o libretro é o da edição do arquivo; o Wikidata mistura regiões e relançamentos.</summary>
        static readonly GameInfoOrigin[] LibretroFirst = { GameInfoOrigin.Libretro, GameInfoOrigin.Wikidata };

        /// <summary>Resumo em português menor que isso é um esboço; a descrição do libretro, mesmo em inglês, diz mais.</summary>
        const int MinPortugueseLength = 120;

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

        /// <summary>
        /// Campo do gamelist → imagem (arquivo ou endereço), para as imagens que existem. Sem tela do jogo, a capa também
        /// serve de imagem na visão detalhada.
        /// </summary>
        public static IDictionary<string, T> ImageFields<T>(IReadOnlyDictionary<ThumbnailKind, T> images)
        {
            var fields = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var target in ImageTargets)
            {
                if (!images.TryGetValue(target.Kind, out var image))
                    continue;

                foreach (var field in target.Fields)
                    fields[field] = image;
            }

            if (!fields.ContainsKey("image") && images.TryGetValue(ThumbnailKind.Boxart, out var cover))
                fields["image"] = cover;

            return fields;
        }

        /// <summary>
        /// As informações do jogo a partir do que cada fonte achou, campo a campo, pela fonte que mais acertou nele. Fica
        /// nulo o campo que nenhuma fonte tem. A descrição é a da Wikipédia em português, a não ser que seja só uma frase;
        /// depois a do libretro, em inglês.
        /// </summary>
        public static GameInfo Merge(IReadOnlyList<GameInfo> infos)
        {
            var portuguese = infos.FirstOrDefault(i => i.DescriptionInPortuguese && (i.Description?.Length ?? 0) >= MinPortugueseLength);
            return new GameInfo
            {
                Title = Pick(infos, WikidataFirst, i => i.Title),
                Description = portuguese?.Description ?? Pick(infos, LibretroFirst, i => i.Description),
                DescriptionInPortuguese = portuguese != null,
                Genre = Pick(infos, WikidataFirst, i => i.Genre),
                Developer = Pick(infos, WikidataFirst, i => i.Developer),
                Publisher = Pick(infos, LibretroFirst, i => i.Publisher),
                ReleaseDate = Pick(infos, LibretroFirst, i => i.ReleaseDate),
                Players = Pick(infos, LibretroFirst, i => i.Players)
            };
        }

        static string Pick(IReadOnlyList<GameInfo> infos, GameInfoOrigin[] order, Func<GameInfo, string> field)
        {
            return order.SelectMany(o => infos.Where(i => i.Origin == o)).Select(field).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }
    }
}
