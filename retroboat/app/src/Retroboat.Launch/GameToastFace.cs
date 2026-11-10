using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Retroboat.Downloads;

namespace Retroboat.Launch
{
    /// <summary>
    /// O cartão do aviso de download concluído durante o jogo, como as notificações da Steam: a capa do jogo à
    /// esquerda e, ao lado, a legenda em verde, o nome e a linha de apoio. Tokens do design system (seção 3); desenha em
    /// pixels ARGB pré-multiplicados, prontos para a janela em camadas.
    /// </summary>
    sealed class GameToastFace : IDisposable
    {
        public const string Caption = "DOWNLOAD CONCLUÍDO";
        public const string Detail = "Pronto para jogar neste PC";

        static readonly Color Panel = Color.FromArgb(0x17, 0x1D, 0x25);
        static readonly Color Border = Color.FromArgb(0x3D, 0x44, 0x50);
        static readonly Color CoverBackground = Color.FromArgb(0x2D, 0x32, 0x3D);
        static readonly Color TitleText = Color.FromArgb(0xF1, 0xF2, 0xF3);
        static readonly Color SecondaryText = Color.FromArgb(0x8B, 0x92, 0x9A);
        static readonly Color Ready = Color.FromArgb(0x59, 0xBF, 0x40);

        readonly int _width;
        readonly int _height;
        readonly float _k;
        readonly Bitmap _bitmap;
        readonly Graphics _graphics;
        readonly Font _caption;
        readonly Font _title;
        readonly Font _detail;
        readonly StringFormat _singleLine = new StringFormat(StringFormat.GenericTypographic)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            LineAlignment = StringAlignment.Center
        };

        public GameToastFace(int width, int height)
        {
            _width = width;
            _height = height;
            _k = height / (float)GameToastLayout.Height; // Os tamanhos abaixo são os de uma tela de 1080 linhas.

            _bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            _graphics = Graphics.FromImage(_bitmap);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            _graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            _caption = new Font("Segoe UI Semibold", 15f * _k, GraphicsUnit.Pixel);
            _title = new Font("Segoe UI Semibold", 22f * _k, GraphicsUnit.Pixel);
            _detail = new Font("Segoe UI", 16f * _k, GraphicsUnit.Pixel);
        }

        /// <param name="cover">A capa do jogo; nula, um quadro com o check no lugar.</param>
        public void Paint(string title, Image cover, int[] pixels)
        {
            var g = _graphics;
            g.Clear(Color.Transparent);

            var outer = new RectangleF(0.5f, 0.5f, _width - 1, _height - 1);
            using (var path = Rounded(outer, 4 * _k))
            using (var fill = new SolidBrush(Panel))
            using (var line = new Pen(Border, Math.Max(1f, _k)))
            {
                g.FillPath(fill, path);
                g.DrawPath(line, path);
            }

            float padding = 12 * _k;
            float coverHeight = _height - 2 * padding;
            float coverWidth = coverHeight;
            if (cover != null && cover.Height > 0)
                coverWidth = Math.Max(56 * _k, Math.Min(120 * _k, coverHeight * cover.Width / cover.Height));

            var coverBox = new RectangleF(padding, padding, coverWidth, coverHeight);
            if (cover != null)
                DrawCover(cover, coverBox);
            else
                DrawCheckTile(coverBox);

            float left = coverBox.Right + 16 * _k;
            float width = _width - 16 * _k - left;
            if (width > 0)
            {
                DrawText(Caption, _caption, Ready, left, 14 * _k, width, 22 * _k);
                DrawText(title ?? "", _title, TitleText, left, 37 * _k, width, 30 * _k);
                DrawText(Detail, _detail, SecondaryText, left, 68 * _k, width, 22 * _k);
            }

            var data = _bitmap.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, _width * _height); }
            finally { _bitmap.UnlockBits(data); }
        }

        /// <summary>Preenche o quadro sem distorcer: corta o que sobrar da imagem, pelo centro.</summary>
        void DrawCover(Image cover, RectangleF box)
        {
            float boxRatio = box.Width / box.Height;
            float imageRatio = cover.Width / (float)cover.Height;
            var source = imageRatio > boxRatio
                ? new RectangleF((cover.Width - cover.Height * boxRatio) / 2, 0, cover.Height * boxRatio, cover.Height)
                : new RectangleF(0, (cover.Height - cover.Width / boxRatio) / 2, cover.Width, cover.Width / boxRatio);

            using (var path = Rounded(box, 2 * _k))
            {
                var clip = _graphics.Clip;
                _graphics.SetClip(path);
                _graphics.DrawImage(cover, box, source, GraphicsUnit.Pixel);
                _graphics.Clip = clip;
            }
        }

        void DrawCheckTile(RectangleF box)
        {
            using (var path = Rounded(box, 2 * _k))
            using (var fill = new SolidBrush(CoverBackground))
                _graphics.FillPath(fill, path);

            float s = box.Height * 0.42f;
            float x = box.X + (box.Width - s) / 2;
            float y = box.Y + (box.Height - s) / 2;
            using (var pen = new Pen(Ready, Math.Max(2f, 3.2f * _k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                _graphics.DrawLines(pen, new[]
                {
                    new PointF(x + s * 0.10f, y + s * 0.52f),
                    new PointF(x + s * 0.38f, y + s * 0.80f),
                    new PointF(x + s * 0.92f, y + s * 0.22f)
                });
            }
        }

        void DrawText(string text, Font font, Color color, float x, float y, float width, float height)
        {
            using (var brush = new SolidBrush(color))
                _graphics.DrawString(text, font, brush, new RectangleF(x, y, width, height), _singleLine);
        }

        static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            _graphics.Dispose();
            _bitmap.Dispose();
            _caption.Dispose();
            _title.Dispose();
            _detail.Dispose();
            _singleLine.Dispose();
        }
    }
}
