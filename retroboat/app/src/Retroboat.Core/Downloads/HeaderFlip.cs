using System;

namespace Retroboat.Downloads
{
    /// <summary>
    /// A rotação da região de ícones do cabeçalho: as duas faces são lados de um cubo que gira no eixo
    /// horizontal, dentro da própria região. A face que sai sobe, e a que entra vem de baixo. Tudo em pixels
    /// ARGB pré-multiplicados, por linha, sem depender de placa de vídeo.
    /// </summary>
    public static class HeaderFlip
    {
        // Distância da câmera, em alturas da região: perspectiva leve, sem distorcer o texto.
        const double CameraDistance = 5;

        // Quanto a face escurece ao virar de lado: é o que dá a sensação de volume.
        const double MaxShade = 0.5;

        /// <summary>
        /// Curva da rotação: acelera e pousa, sem quique, como um objeto que gira de verdade. Um ease-out, que é o
        /// padrão do design system, resolve quase todo o giro no primeiro décimo de segundo, e o giro não aparece.
        /// </summary>
        public static double Ease(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return (1 - Math.Cos(t * Math.PI)) / 2;
        }

        /// <param name="t">0 mostra só <paramref name="from"/>; 1 mostra só <paramref name="to"/>.</param>
        /// <param name="backdrop">Cor atrás do cubo, para o cabeçalho de verdade não aparecer nas laterais.</param>
        public static void Render(int[] from, int[] to, int width, int height, double t, int[] frame, int backdrop)
        {
            if (t <= 0)
            {
                Array.Copy(from, frame, frame.Length);
                return;
            }

            if (t >= 1)
            {
                Array.Copy(to, frame, frame.Length);
                return;
            }

            for (int i = 0; i < frame.Length; i++)
                frame[i] = backdrop;

            double angle = t * Math.PI / 2;
            DrawFace(from, width, height, angle, frame);
            DrawFace(to, width, height, angle - Math.PI / 2, frame);
        }

        static void DrawFace(int[] source, int width, int height, double angle, int[] frame)
        {
            double apothem = height / 2.0;
            double distance = CameraDistance * height;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            int shade = (int)((1 - MaxShade * Math.Abs(sin)) * 256);
            double centerX = width / 2.0;

            for (int row = 0; row < height; row++)
            {
                double top = Project(row - apothem, apothem, distance, cos, sin, out _);
                double bottom = Project(row + 1 - apothem, apothem, distance, cos, sin, out double scale);

                int first = Math.Max(0, (int)Math.Round(top + apothem));
                int last = Math.Min(height, (int)Math.Round(bottom + apothem));
                if (last <= first)
                    continue;

                int left = Math.Max(0, (int)Math.Ceiling(centerX - centerX * scale));
                int right = Math.Min(width, (int)Math.Floor(centerX + centerX * scale));
                int sourceRow = row * width;

                for (int y = first; y < last; y++)
                {
                    int target = y * width;
                    for (int x = left; x < right; x++)
                    {
                        int sourceX = (int)(centerX + (x + 0.5 - centerX) / scale);
                        if (sourceX >= 0 && sourceX < width)
                            frame[target + x] = Darken(source[sourceRow + sourceX], shade);
                    }
                }
            }
        }

        /// <returns>A posição na tela, em relação ao centro da região, de um ponto da face.</returns>
        static double Project(double v, double apothem, double distance, double cos, double sin, out double scale)
        {
            double y = v * cos - apothem * sin;
            double z = apothem * cos + v * sin;
            scale = distance / (distance + apothem - z);
            return y * scale;
        }

        static int Darken(int pixel, int shade)
        {
            uint p = (uint)pixel;
            uint redBlue = ((p & 0x00FF00FF) * (uint)shade >> 8) & 0x00FF00FF;
            uint green = ((p & 0x0000FF00) * (uint)shade >> 8) & 0x0000FF00;
            return (int)((p & 0xFF000000) | redBlue | green);
        }
    }
}
