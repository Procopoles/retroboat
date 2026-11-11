using System.Drawing;
using System.Windows.Forms;

namespace Retroboat.Launcher
{
    /// <summary>Janela pequena mostrada enquanto o Retroboat sincroniza com a nuvem antes de abrir o RetroBat.</summary>
    sealed class SyncProgressForm : Form
    {
        public SyncProgressForm()
        {
            Text = "Retroboat";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            // RB-029: no centro do monitor do ES (o escolhido no retrobat.ini), depois da escala por DPI.
            StartPosition = FormStartPosition.Manual;
            Load += (s, e) =>
            {
                var area = Retroboat.Launch.Theme.GameScreen().WorkingArea;
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            };
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(420, 96);
            TopMost = true;

            var label = new Label
            {
                Text = "Sincronizando com a nuvem (BIOS, biblioteca e capas)...",
                AutoSize = true,
                Location = new Point(16, 18)
            };

            var bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Location = new Point(16, 54),
                Size = new Size(388, 18)
            };

            Controls.Add(label);
            Controls.Add(bar);
        }
    }
}
