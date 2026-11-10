using System;

namespace Retroboat.Downloads
{
    /// <summary>Onde o cartão do aviso fica na tela, em pixels.</summary>
    public struct GameToastPlace
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }

    /// <summary>Um quadro do aviso: quanto ele está abaixo do lugar final e quão opaco.</summary>
    public struct GameToastFrame
    {
        public int OffsetY;
        public byte Opacity;
        public bool Done;
    }

    /// <summary>
    /// O aviso de download concluído durante o jogo, como as notificações da Steam: um cartão no canto inferior
    /// direito, que sobe da borda, fica alguns segundos e desce. Tamanhos para uma tela de 1080 linhas, na escala da
    /// tela do jogo; movimento de 200 ms em ease-out, sem quique (design system, seção 3.6).
    /// </summary>
    public static class GameToastLayout
    {
        public const int Width = 420;
        public const int Height = 104;
        public const int Margin = 32;

        /// <summary>Quanto o cartão sobe ao entrar (e desce ao sair).</summary>
        public const int Rise = 24;

        public static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan Hold = TimeSpan.FromSeconds(6);
        public static readonly TimeSpan Leave = TimeSpan.FromMilliseconds(200);

        public static TimeSpan Total => Enter + Hold + Leave;

        /// <summary>Escala da tela: 1 numa tela de 1080 linhas.</summary>
        public static float ScaleFor(int screenHeight) => Math.Max(0.5f, screenHeight / 1080f);

        /// <summary>
        /// O lugar final do cartão. <paramref name="areaX"/>..<paramref name="areaHeight"/> é a tela inteira com o jogo
        /// em tela cheia, ou a área sem a barra de tarefas com o jogo em janela.
        /// </summary>
        public static GameToastPlace Place(int areaX, int areaY, int areaWidth, int areaHeight, float scale)
        {
            int margin = (int)Math.Round(Margin * scale);
            int width = Math.Min((int)Math.Round(Width * scale), Math.Max(1, areaWidth - 2 * margin));
            int height = Math.Min((int)Math.Round(Height * scale), Math.Max(1, areaHeight - 2 * margin));

            return new GameToastPlace
            {
                X = areaX + areaWidth - margin - width,
                Y = areaY + areaHeight - margin - height,
                W = width,
                H = height
            };
        }

        public static GameToastFrame At(TimeSpan elapsed, float scale)
        {
            int rise = (int)Math.Round(Rise * scale);
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            if (elapsed < Enter)
            {
                double p = EaseOut(elapsed.TotalMilliseconds / Enter.TotalMilliseconds);
                return new GameToastFrame { OffsetY = (int)Math.Round(rise * (1 - p)), Opacity = (byte)Math.Round(255 * p) };
            }

            if (elapsed < Enter + Hold)
                return new GameToastFrame { OffsetY = 0, Opacity = 255 };

            var leaving = elapsed - Enter - Hold;
            if (leaving < Leave)
            {
                double p = EaseOut(leaving.TotalMilliseconds / Leave.TotalMilliseconds);
                return new GameToastFrame { OffsetY = (int)Math.Round(rise * p), Opacity = (byte)Math.Round(255 * (1 - p)) };
            }

            return new GameToastFrame { OffsetY = rise, Opacity = 0, Done = true };
        }

        /// <summary>Ease-out cúbico: rápido no começo, pousa devagar.</summary>
        public static double EaseOut(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return 1 - Math.Pow(1 - t, 3);
        }
    }
}
