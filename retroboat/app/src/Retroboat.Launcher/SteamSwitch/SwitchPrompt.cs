using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Retroboat.Downloads;
using Retroboat.Launch;

namespace Retroboat.Launcher.SteamSwitch
{
    /// <summary>
    /// O aviso da troca, no canto inferior direito da tela da frente, como as notificações da Steam (e o aviso de
    /// download concluído): janela em camadas sem foco, sem clique e sem barra de tarefas. Aparece por cima do ES e do
    /// Big Picture (os dois em tela cheia sem borda) e não tira o foco deles. Sobe e some com o mesmo movimento do
    /// <see cref="GameToastLayout"/>.
    /// </summary>
    sealed class SwitchPrompt : NativeWindow, IDisposable
    {
        const int FrameInterval = 15;

        readonly Timer _timer = new Timer { Interval = FrameInterval };
        LayeredSurface _surface;
        Rectangle _place;
        int _rise;
        DateTime _start;
        DateTime? _hideAt;
        DateTime? _leaving;

        public SwitchPrompt()
        {
            CreateHandle(new CreateParams
            {
                Caption = "Retroboat",
                Style = unchecked((int)WS_POPUP),
                ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
            });

            _timer.Tick += (s, e) => Animate(DateTime.UtcNow);
        }

        /// <param name="hideAfter">Some sozinho depois disso; nulo, fica até <see cref="Hide"/>.</param>
        public void Show(SwitchPromptText text, TimeSpan? hideAfter = null)
        {
            var now = DateTime.UtcNow;
            var front = GetForegroundWindow();
            var screen = front != IntPtr.Zero ? Screen.FromHandle(front) : Screen.PrimaryScreen;

            float scale = GameToastLayout.ScaleFor(screen.Bounds.Height);
            int margin = (int)Math.Round(GameToastLayout.Margin * scale);
            int width = Math.Min((int)Math.Round(SwitchPromptFace.Width * scale), Math.Max(1, screen.Bounds.Width - 2 * margin));
            int height = Math.Min((int)Math.Round(SwitchPromptFace.Height * scale), Math.Max(1, screen.Bounds.Height - 2 * margin));
            _place = new Rectangle(screen.Bounds.Right - margin - width, screen.Bounds.Bottom - margin - height, width, height);
            _rise = (int)Math.Round(GameToastLayout.Rise * scale);

            var pixels = new int[width * height];
            using (var face = new SwitchPromptFace(width, height))
                face.Paint(text, pixels);

            bool visible = _surface != null && _leaving == null;
            _surface?.Dispose();
            _surface = new LayeredSurface(width, height);

            // Já na tela (a pergunta virou "Abrindo a Steam"): troca o conteúdo sem subir de novo.
            _start = visible ? now - GameToastLayout.Enter : now;
            _leaving = null;
            _hideAt = hideAfter.HasValue ? now + hideAfter.Value : (DateTime?)null;

            var frame = Frame(now);
            _surface.Present(Handle, pixels, _place.X, _place.Y + frame.Item1, frame.Item2);
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            KeepOnTop();
            _timer.Start();
        }

        public void Hide()
        {
            if (_surface != null && _leaving == null)
                _leaving = DateTime.UtcNow;
        }

        void Animate(DateTime now)
        {
            if (_surface == null)
            {
                _timer.Stop();
                return;
            }

            if (_leaving == null && _hideAt.HasValue && now >= _hideAt.Value)
                _leaving = now;

            if (_leaving.HasValue && now - _leaving.Value >= GameToastLayout.Leave)
            {
                ShowWindow(Handle, SW_HIDE);
                _surface.Dispose();
                _surface = null;
                _leaving = null;
                _timer.Stop();
                return;
            }

            var frame = Frame(now);
            _surface.Move(Handle, _place.X, _place.Y + frame.Item1, frame.Item2);
            KeepOnTop();
        }

        /// <summary>Quanto abaixo do lugar final e quão opaco.</summary>
        Tuple<int, byte> Frame(DateTime now)
        {
            if (_leaving.HasValue)
            {
                double p = GameToastLayout.EaseOut((now - _leaving.Value).TotalMilliseconds / GameToastLayout.Leave.TotalMilliseconds);
                return Tuple.Create((int)Math.Round(_rise * p), (byte)Math.Round(255 * (1 - p)));
            }

            double q = GameToastLayout.EaseOut((now - _start).TotalMilliseconds / GameToastLayout.Enter.TotalMilliseconds);
            return Tuple.Create((int)Math.Round(_rise * (1 - q)), (byte)Math.Round(255 * q));
        }

        /// <summary>O Big Picture e a Game Bar também ficam "sempre visíveis": o aviso volta para cima a cada quadro.</summary>
        void KeepOnTop()
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        public void Dispose()
        {
            _timer.Dispose();
            _surface?.Dispose();
            DestroyHandle();
        }

        const uint WS_POPUP = 0x80000000;
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_TOPMOST = 0x00000008;
        const int SW_HIDE = 0;
        const int SW_SHOWNOACTIVATE = 4;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_NOOWNERZORDER = 0x0200;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    }
}
