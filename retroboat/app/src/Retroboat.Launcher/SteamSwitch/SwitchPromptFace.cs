using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Retroboat.Launcher.SteamSwitch
{
    /// <summary>O texto de um aviso da troca.</summary>
    sealed class SwitchPromptText
    {
        public string Caption;
        public string Title;
        public string Detail;
        /// <summary>Legenda em amarelo (outro programa usa o botão); senão, em azul.</summary>
        public bool Warning;

        public static SwitchPromptText ToSteam() => new SwitchPromptText
        {
            Caption = "IR PARA A STEAM",
            Title = "Aperte o botão Xbox de novo",
            Detail = "para abrir o Big Picture"
        };

        public static SwitchPromptText ToEs() => new SwitchPromptText
        {
            Caption = "VOLTAR PARA O RETROBOAT",
            Title = "Aperte o botão Xbox de novo",
            Detail = "para sair do Big Picture"
        };

        public static SwitchPromptText Opening() => new SwitchPromptText
        {
            Caption = "ABRINDO A STEAM",
            Title = "Só um instante",
            Detail = "O Big Picture abre em seguida"
        };

        public static SwitchPromptText Conflict(Retroboat.SteamSwitch.GuideButtonConflict conflict) => new SwitchPromptText
        {
            Caption = "BOTÃO XBOX EM USO",
            Title = conflict.Title,
            Detail = conflict.Fix,
            Warning = true
        };
    }

    /// <summary>
    /// O cartão dos avisos da troca, no mesmo desenho do aviso de download concluído (<c>GameToastFace</c>): painel
    /// escuro, um ícone à esquerda e três linhas. Tokens do design system (seção 3); pixels ARGB pré-multiplicados.
    /// </summary>
    sealed class SwitchPromptFace : IDisposable
    {
        /// <summary>Tamanhos numa tela de 1080 linhas.</summary>
        public const int Width = 520;
        public const int Height = 104;

        static readonly Color Panel = Color.FromArgb(0x17, 0x1D, 0x25);
        static readonly Color Border = Color.FromArgb(0x3D, 0x44, 0x50);
        static readonly Color IconBackground = Color.FromArgb(0x2D, 0x32, 0x3D);
        static readonly Color TitleText = Color.FromArgb(0xF1, 0xF2, 0xF3);
        static readonly Color SecondaryText = Color.FromArgb(0x8B, 0x92, 0x9A);
        static readonly Color Accent = Color.FromArgb(0x1A, 0x9F, 0xFF);
        static readonly Color Warning = Color.FromArgb(0xFF, 0xC8, 0x2C);

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

        public SwitchPromptFace(int width, int height)
        {
            _width = width;
            _height = height;
            _k = height / (float)Height;

            _bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            _graphics = Graphics.FromImage(_bitmap);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            _caption = new Font("Segoe UI Semibold", 15f * _k, GraphicsUnit.Pixel);
            _title = new Font("Segoe UI Semibold", 22f * _k, GraphicsUnit.Pixel);
            _detail = new Font("Segoe UI", 16f * _k, GraphicsUnit.Pixel);
        }

        public void Paint(SwitchPromptText text, int[] pixels)
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

            var color = text.Warning ? Warning : Accent;
            float padding = 12 * _k;
            float side = _height - 2 * padding;
            var icon = new RectangleF(padding, padding, side, side);
            DrawIcon(icon, color, text.Warning);

            float left = icon.Right + 16 * _k;
            float width = _width - 16 * _k - left;
            if (width > 0)
            {
                DrawText(text.Caption, _caption, color, left, 14 * _k, width, 22 * _k);
                DrawText(text.Title ?? "", _title, TitleText, left, 37 * _k, width, 30 * _k);
                DrawText(text.Detail ?? "", _detail, SecondaryText, left, 68 * _k, width, 22 * _k);
            }

            var data = _bitmap.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, _width * _height); }
            finally { _bitmap.UnlockBits(data); }
        }

        /// <summary>Um botão redondo, como o do controle: com duas setas (trocar) ou com um "!" (aviso).</summary>
        void DrawIcon(RectangleF box, Color color, bool warning)
        {
            using (var path = Rounded(box, 2 * _k))
            using (var fill = new SolidBrush(IconBackground))
                _graphics.FillPath(fill, path);

            float d = box.Height * 0.62f;
            var circle = new RectangleF(box.X + (box.Width - d) / 2, box.Y + (box.Height - d) / 2, d, d);
            float stroke = Math.Max(2f, 3f * _k);
            using (var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                _graphics.DrawEllipse(pen, circle);

                float cx = circle.X + d / 2, cy = circle.Y + d / 2, r = d * 0.24f;
                if (warning)
                {
                    _graphics.DrawLine(pen, cx, cy - r, cx, cy + r * 0.25f);
                    _graphics.DrawLine(pen, cx, cy + r * 0.85f, cx, cy + r * 0.9f);
                    return;
                }

                float a = r * 0.45f;
                _graphics.DrawLine(pen, cx - r, cy - a, cx + r, cy - a);
                _graphics.DrawLines(pen, new[] { new PointF(cx + r - a, cy - 2 * a), new PointF(cx + r, cy - a), new PointF(cx + r - a, cy) });
                _graphics.DrawLine(pen, cx + r, cy + a, cx - r, cy + a);
                _graphics.DrawLines(pen, new[] { new PointF(cx - r + a, cy), new PointF(cx - r, cy + a), new PointF(cx - r + a, cy + 2 * a) });
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
            _caption.Dispose();
            _title.Dispose();
            _detail.Dispose();
            _singleLine.Dispose();
            _graphics.Dispose();
            _bitmap.Dispose();
        }
    }
}
