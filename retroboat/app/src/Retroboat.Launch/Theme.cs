using System;
using System.Drawing;
using System.Runtime.InteropServices;
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

        static readonly TimeSpan ReclaimEvery = TimeSpan.FromMilliseconds(500);
        static DateTime _holdFocusUntil;
        static DateTime _lastReclaim;

        /// <summary>
        /// Por um tempo, as telas cheias pegam o foco de volta. O Google Drive puxa o foco ao abrir, e aí o
        /// teclado deixaria de chegar nelas (o controle continua, porque é lido direto do XInput) e o emulador
        /// abriria sem foco.
        /// </summary>
        public static void HoldFocus(TimeSpan duration) => _holdFocusUntil = DateTime.UtcNow + duration;

        /// <summary>Chamar no timer da tela. Fora do tempo de <see cref="HoldFocus"/>, não faz nada.</summary>
        public static void ReclaimFocus(Form form)
        {
            var now = DateTime.UtcNow;
            if (now >= _holdFocusUntil || now - _lastReclaim < ReclaimEvery || Form.ActiveForm == form)
                return;

            _lastReclaim = now;

            // O Windows nega SetForegroundWindow a quem não está em primeiro plano (o Activate sozinho não
            // resolve); ligado à fila de entrada de quem está, o pedido passa.
            uint current = GetCurrentThreadId();
            uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            bool attached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
            try
            {
                BringWindowToTop(form.Handle);
                SetForegroundWindow(form.Handle);
                form.Activate();
            }
            finally
            {
                if (attached)
                    AttachThreadInput(current, foreground, false);
            }
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

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
