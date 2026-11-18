using System;
using System.Collections.Generic;

namespace Retroboat.Media.Background
{
    /// <summary>
    /// RB-041: o que a pessoa espera na atualização da biblioteca e o que chega depois, em segundo plano.
    ///
    /// Essencial é o que aparece na lista de jogos de qualquer tema: os textos (nome, descrição, gênero...), a capa
    /// (thumbnail e boxart), a tela do jogo, o logo e a fan art. O resto é secundário: o vídeo (5 a 50 MB cada), o manual
    /// em PDF e as imagens que só aparecem na página do jogo (caixa traseira, disco/cartucho, tela de título, mapa,
    /// moldura). Vídeo e manual são 90% do tempo de uma importação; com eles em segundo plano, a espera fica nos segundos.
    ///
    /// Os campos são os do gamelist do ES (<see cref="GameMedia.MediaFields"/>).
    /// </summary>
    public static class MediaPriority
    {
        static readonly HashSet<string> EssentialMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "thumbnail", "boxart", "image", "marquee", "fanart", "wheel", "mix"
        };

        /// <summary>Campos de texto e mídia que a pessoa espera; um campo que não é de mídia é sempre essencial.</summary>
        public static bool IsEssential(string gamelistField) =>
            !GameMedia.MediaFields.ContainsKey(gamelistField) || EssentialMedia.Contains(gamelistField);

        /// <summary>Mídia que vai para a fila de segundo plano (<see cref="BackgroundMediaQueue"/>).</summary>
        public static bool IsSecondary(string gamelistField) => !IsEssential(gamelistField);

        /// <summary>
        /// Ordem na fila de segundo plano: as imagens secundárias (pequenas) primeiro, depois os vídeos e por último os
        /// manuais, que quase ninguém abre. Assim o que é rápido aparece logo.
        /// </summary>
        public static int Order(string gamelistField)
        {
            if (!GameMedia.MediaFields.TryGetValue(gamelistField, out var kind))
                return 0;

            return kind == MediaKind.Image ? 0 : kind == MediaKind.Video ? 1 : 2;
        }
    }
}
