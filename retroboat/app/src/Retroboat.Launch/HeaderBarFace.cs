using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Retroboat.Downloads;
using Retroboat.Text;

namespace Retroboat.Launch
{
    /// <summary>
    /// A face da região do cabeçalho que mostra o download: status, nome do jogo, barra e detalhes, nos tokens do
    /// design system (seção 4.6). Desenha em pixels ARGB pré-multiplicados, prontos para a janela em camadas.
    /// </summary>
    sealed class HeaderBarFace : IDisposable
    {
        static readonly Color Panel = Color.FromArgb(0x17, 0x1D, 0x25);
        static readonly Color Border = Color.FromArgb(0x3D, 0x44, 0x50);
        static readonly Color Track = Color.FromArgb(0x2D, 0x32, 0x3D);
        static readonly Color TitleText = Color.FromArgb(0xF1, 0xF2, 0xF3);
        static readonly Color BodyText = Color.FromArgb(0xDC, 0xDE, 0xDF);
        static readonly Color SecondaryText = Color.FromArgb(0x8B, 0x92, 0x9A);
        static readonly Color Accent = Color.FromArgb(0x1A, 0x9F, 0xFF);
        static readonly Color Ready = Color.FromArgb(0x59, 0xBF, 0x40);
        static readonly Color Danger = Color.FromArgb(0xE5, 0x48, 0x4D);
        static readonly Color Warning = Color.FromArgb(0xFF, 0xC8, 0x2C);

        readonly int _width;
        readonly int _height;
        readonly float _k;
        readonly Bitmap _bitmap;
        readonly Graphics _graphics;
        readonly Font _caption;
        readonly Font _title;
        readonly Font _percent;
        readonly Font _detail;
        readonly StringFormat _singleLine = new StringFormat(StringFormat.GenericTypographic)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
            LineAlignment = StringAlignment.Center
        };

        public HeaderBarFace(int width, int height)
        {
            _width = width;
            _height = height;
            _k = height / 76f; // A região tem 76 px numa tela de 1080 linhas (0,07); os tamanhos abaixo são para ela.

            _bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            _graphics = Graphics.FromImage(_bitmap);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            _caption = new Font("Segoe UI Semibold", 16f * _k, GraphicsUnit.Pixel);
            _title = new Font("Segoe UI", 20f * _k, GraphicsUnit.Pixel);
            _percent = new Font("Segoe UI Semibold", 19f * _k, GraphicsUnit.Pixel);
            _detail = new Font("Segoe UI", 16f * _k, GraphicsUnit.Pixel);
        }

        public static string CaptionOf(DownloadPhase phase)
        {
            switch (phase)
            {
                case DownloadPhase.Preparing: return "PREPARANDO";
                case DownloadPhase.Downloading: return "BAIXANDO";
                case DownloadPhase.WaitingForCloud: return "AGUARDANDO";
                case DownloadPhase.Finished: return "PRONTO";
                default: return "NÃO BAIXOU";
            }
        }

        static Color ColorOf(DownloadPhase phase)
        {
            switch (phase)
            {
                case DownloadPhase.WaitingForCloud: return Warning;
                case DownloadPhase.Finished: return Ready;
                case DownloadPhase.Failed: return Danger;
                default: return Accent;
            }
        }

        public static string DetailOf(DownloadStatus status)
        {
            switch (status.Phase)
            {
                case DownloadPhase.Preparing:
                    return "Lendo o jogo na nuvem";
                case DownloadPhase.WaitingForCloud:
                    return "Continua sozinho quando o Google Drive voltar";
                case DownloadPhase.Finished:
                    return "Pronto para jogar neste PC";
                case DownloadPhase.Failed:
                    return "Veja o motivo na notificação";
                default:
                    return Format.Bytes(status.BytesDone) + " de " + Format.Bytes(status.BytesTotal) +
                           (status.BytesPerSecond > 0 ? "  ·  " + Format.Speed(status.BytesPerSecond) : "") +
                           (status.Remaining.HasValue ? "  ·  restam " + Format.Duration(status.Remaining.Value) : "");
            }
        }

        /// <param name="pulse">De 0 a 1: a respiração do ícone enquanto baixa.</param>
        public void Paint(DownloadStatus status, double pulse, int[] pixels)
        {
            var g = _graphics;
            g.Clear(Color.Transparent);

            var color = ColorOf(status.Phase);
            var outer = new RectangleF(0.5f, 0.5f, _width - 1, _height - 1);
            using (var path = Rounded(outer, 4 * _k))
            using (var fill = new SolidBrush(Panel))
            using (var line = new Pen(Border, Math.Max(1f, _k)))
            {
                g.FillPath(fill, path);
                g.DrawPath(line, path);
            }

            float padding = 16 * _k;
            float iconSize = 30 * _k;
            DrawIcon(status.Phase, new RectangleF(padding, (_height - iconSize) / 2, iconSize, iconSize), color, pulse);

            float left = padding + iconSize + 14 * _k;
            float right = _width - padding;

            // Linha 1: status, nome do jogo e porcentagem.
            float rowTop = 6 * _k;
            float rowHeight = 28 * _k;
            var caption = CaptionOf(status.Phase);
            float captionWidth = g.MeasureString(caption, _caption, 1000, _singleLine).Width;
            DrawText(caption, _caption, color, left, rowTop, captionWidth + 2, rowHeight, StringAlignment.Near);

            float textRight = right;
            if (status.Phase == DownloadPhase.Downloading)
            {
                var percent = ((int)Math.Floor(status.Fraction * 100)).ToString(Format.Culture) + "%";
                float percentWidth = g.MeasureString(percent, _percent, 200, _singleLine).Width + 2;
                DrawText(percent, _percent, TitleText, right - percentWidth, rowTop, percentWidth, rowHeight, StringAlignment.Far);
                textRight -= percentWidth + 10 * _k;
            }

            float titleLeft = left + captionWidth + 10 * _k;
            if (textRight > titleLeft)
                DrawText(status.Item?.Title ?? "", _title, BodyText, titleLeft, rowTop, textRight - titleLeft, rowHeight, StringAlignment.Near);

            // Linha 2: a barra.
            float barTop = 37 * _k;
            float barHeight = Math.Max(2, 6 * _k);
            var track = new RectangleF(left, barTop, right - left, barHeight);
            using (var path = Rounded(track, 2 * _k))
            using (var fill = new SolidBrush(Track))
                g.FillPath(fill, path);

            double fraction = status.Phase == DownloadPhase.Finished ? 1 : status.Fraction;
            if (fraction > 0)
            {
                var filled = new RectangleF(left, barTop, Math.Max(barHeight, (float)(track.Width * fraction)), barHeight);
                using (var path = Rounded(filled, 2 * _k))
                using (var fill = new SolidBrush(color))
                    g.FillPath(fill, path);
            }

            // Linha 3: detalhes e fila.
            float detailTop = 46 * _k;
            float detailHeight = 24 * _k;
            float detailRight = right;
            if (status.Waiting > 0)
            {
                var waiting = status.Waiting == 1 ? "mais 1 na fila" : "mais " + status.Waiting.ToString(Format.Culture) + " na fila";
                float waitingWidth = g.MeasureString(waiting, _detail, 400, _singleLine).Width + 2;
                DrawText(waiting, _detail, SecondaryText, right - waitingWidth, detailTop, waitingWidth, detailHeight, StringAlignment.Far);
                detailRight -= waitingWidth + 10 * _k;
            }

            if (detailRight > left)
                DrawText(DetailOf(status), _detail, SecondaryText, left, detailTop, detailRight - left, detailHeight, StringAlignment.Near);

            var data = _bitmap.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, _width * _height); }
            finally { _bitmap.UnlockBits(data); }
        }

        void DrawText(string text, Font font, Color color, float x, float y, float width, float height, StringAlignment alignment)
        {
            _singleLine.Alignment = alignment;
            using (var brush = new SolidBrush(color))
                _graphics.DrawString(text, font, brush, new RectangleF(x, y, width, height), _singleLine);
        }

        void DrawIcon(DownloadPhase phase, RectangleF box, Color color, double pulse)
        {
            var g = _graphics;
            float cx = box.X + box.Width / 2;
            float s = box.Width;
            float stroke = Math.Max(2f, 2.6f * _k);

            bool active = phase == DownloadPhase.Downloading || phase == DownloadPhase.Preparing;
            int alpha = active ? 150 + (int)(105 * pulse) : 255;

            using (var pen = new Pen(Color.FromArgb(alpha, color), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (phase)
                {
                    case DownloadPhase.Finished:
                        g.DrawLines(pen, new[]
                        {
                            new PointF(box.X + s * 0.14f, box.Y + s * 0.52f),
                            new PointF(box.X + s * 0.40f, box.Y + s * 0.78f),
                            new PointF(box.X + s * 0.88f, box.Y + s * 0.24f)
                        });
                        break;

                    case DownloadPhase.Failed:
                    case DownloadPhase.WaitingForCloud:
                        g.DrawLine(pen, cx, box.Y + s * 0.14f, cx, box.Y + s * 0.60f);
                        g.DrawLine(pen, cx, box.Y + s * 0.84f, cx, box.Y + s * 0.85f);
                        break;

                    default:
                        // Seta para baixo sobre a linha da "bandeja": o mesmo desenho do botão BAIXAR.
                        g.DrawLine(pen, cx, box.Y + s * 0.10f, cx, box.Y + s * 0.62f);
                        g.DrawLines(pen, new[]
                        {
                            new PointF(cx - s * 0.26f, box.Y + s * 0.38f),
                            new PointF(cx, box.Y + s * 0.64f),
                            new PointF(cx + s * 0.26f, box.Y + s * 0.38f)
                        });
                        g.DrawLine(pen, box.X + s * 0.12f, box.Y + s * 0.88f, box.X + s * 0.88f, box.Y + s * 0.88f);
                        break;
                }
            }
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
            _percent.Dispose();
            _detail.Dispose();
            _singleLine.Dispose();
        }
    }
}
