using System;

namespace Retroboat.Downloads
{
    /// <summary>
    /// Onde fica a região de ícones que gira, em frações da tela do ES (como as posições dos temas): cobre os
    /// ícones globais do cabeçalho de cada tema. Um tema desconhecido usa o canto superior direito, onde quase
    /// todos põem esses ícones.
    /// </summary>
    public struct HeaderRegion
    {
        // 76 px numa tela de 1080 linhas: 3 linhas de texto, legíveis de longe.
        const double Height = 0.07;

        public int X;
        public int Y;
        public int W;
        public int H;

        public static HeaderRegion For(string theme, int screenWidth, int screenHeight)
        {
            double aspect = (double)screenWidth / Math.Max(1, screenHeight);
            double right = 0.985;
            double baseWidth = 0.30;
            double centerY = 0.04;

            if (string.Equals(theme, "es-theme-PlayStation-X", StringComparison.OrdinalIgnoreCase))
            {
                // O cabeçalho deste tema fica mais baixo; nas telas mais quadradas, os ícones sobem de linha. A
                // região começa logo depois do logo do tema, para não cortar o nome dele ao meio.
                centerY = aspect < 1.55 ? 0.036 : 0.066;
                baseWidth = 0.33;
            }

            // A largura vale para 16:9; em telas mais quadradas, cresce para o texto continuar cabendo.
            double width = Math.Min(0.62, baseWidth * (16.0 / 9.0) / aspect);

            int w = (int)Math.Round(width * screenWidth);
            int h = (int)Math.Round(Height * screenHeight);
            return new HeaderRegion
            {
                X = (int)Math.Round(right * screenWidth) - w,
                Y = Math.Max(0, (int)Math.Round(centerY * screenHeight - h / 2.0)),
                W = w,
                H = h
            };
        }
    }
}
