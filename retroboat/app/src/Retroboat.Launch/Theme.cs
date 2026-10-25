using System.Drawing;
using System.Windows.Forms;

namespace Retroboat.Launch
{
    /// <summary>Cores e textos das telas cheias do retroboat-launch (download, memory cards).</summary>
    static class Theme
    {
        public static readonly Color Background = Color.FromArgb(16, 20, 26);
        public static readonly Color Surface = Color.FromArgb(40, 46, 56);
        public static readonly Color Foreground = Color.FromArgb(235, 238, 242);
        public static readonly Color Muted = Color.FromArgb(150, 160, 172);
        public static readonly Color Accent = Color.FromArgb(76, 160, 255);
        public static readonly Color Danger = Color.FromArgb(255, 110, 100);

        public static Label MakeLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
                BackColor = Background,
                Font = new Font("Segoe UI", size, style)
            };
        }

        /// <summary>Tela cheia, sem borda, por cima do ES.</summary>
        public static void ApplyFullScreen(Form form)
        {
            form.Text = "Retroboat";
            form.FormBorderStyle = FormBorderStyle.None;
            form.WindowState = FormWindowState.Maximized;
            form.BackColor = Background;
            form.ForeColor = Foreground;
            form.TopMost = true;
            form.KeyPreview = true;
            form.ShowInTaskbar = true;
        }
    }
}
