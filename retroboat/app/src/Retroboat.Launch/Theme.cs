using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Retroboat.Library;
using Retroboat.Settings;

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
            ForceForeground(form.Handle);
            form.Activate();
        }

        /// <summary>ChoiceForm: uma tela do Retroboat apareceu, e o foco saiu do ES.</summary>
        public static void ScreenShown() => _screensShown = true;

        static bool _screensShown;

        /// <summary>
        /// RB-029: o monitor das telas do Retroboat. Com o ES aberto, o dele; antes, o do retrobat.ini (onde o RetroBat vai
        /// abrir o ES); senão, o principal do Windows. Sem isto, o Windows abre a tela no principal e tira o foco do ES.
        /// </summary>
        public static Screen GameScreen()
        {
            var es = EsWindow();
            if (es != IntPtr.Zero)
                return Screen.FromHandle(es);

            try
            {
                var install = RetroBatInstall.FindFrom(AppDomain.CurrentDomain.BaseDirectory);
                if (install != null)
                {
                    var screens = Screen.AllScreens;
                    int index = RetroBatIni.ScreenFor(RetroBatIni.GetMonitorIndex(RetroBatIni.PathFor(install)),
                        screens.Select(s => s.DeviceName).ToList(), Array.FindIndex(screens, s => s.Primary));
                    if (index >= 0)
                        return screens[index];
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return Screen.PrimaryScreen;
        }

        /// <summary>
        /// RB-029: ao sair do retroboat-launch, se uma tela dele apareceu por cima do ES, o foco volta para o ES. Sem isto, o
        /// Windows dá o foco a outra janela (às vezes em outro monitor) e é preciso clicar no ES. Só no fim: antes de um jogo,
        /// quem pega o foco é o emulador.
        /// </summary>
        public static void ReturnFocusToEs()
        {
            if (!_screensShown)
                return;

            var es = EsWindow();
            if (es != IntPtr.Zero && GetForegroundWindow() != es)
                ForceForeground(es);
        }

        /// <summary>A janela do ES, se ele está aberto e visível.</summary>
        static IntPtr EsWindow()
        {
            foreach (var process in Process.GetProcessesByName("emulationstation"))
            {
                using (process)
                {
                    var window = process.MainWindowHandle;
                    if (window != IntPtr.Zero && IsWindowVisible(window))
                        return window;
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// O Windows nega SetForegroundWindow a quem não está em primeiro plano (o Activate sozinho não resolve); ligado à
        /// fila de entrada de quem está, o pedido passa.
        /// </summary>
        static void ForceForeground(IntPtr window)
        {
            uint current = GetCurrentThreadId();
            uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            bool attached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
            try
            {
                BringWindowToTop(window);
                SetForegroundWindow(window);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(current, foreground, false);
            }
        }

        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
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

        /// <summary>Tela cheia, sem borda, por cima do ES, no monitor dele.</summary>
        public static void ApplyFullScreen(Form form)
        {
            form.Text = "Retroboat";
            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = GameScreen().WorkingArea.Location; // maximiza no monitor onde a janela está
            form.WindowState = FormWindowState.Maximized;
            form.BackColor = Background;
            form.ForeColor = Foreground;
            form.TopMost = true;
            form.KeyPreview = true;
            form.ShowInTaskbar = true;
        }
    }
}
