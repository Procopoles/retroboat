using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Retroboat.Downloads;
using Retroboat.Logging;

namespace Retroboat.Launch
{
    /// <summary>
    /// O aviso de download concluído com um jogo aberto, no canto inferior direito da tela do jogo, como as notificações
    /// da Steam. Com o jogo na frente, a região do cabeçalho do ES não aparece; este cartão é a única forma de saber que o
    /// jogo ficou pronto. Janela em camadas sem foco, sem clique e sem barra de tarefas: o controle e o teclado continuam
    /// indo para o jogo. Vários jogos prontos de uma vez aparecem um depois do outro.
    ///
    /// Aparece por cima de jogos em janela e em tela cheia sem borda (o padrão do RetroArch e do PCSX2 no RetroBat). Um
    /// emulador em tela cheia exclusiva não deixa nenhuma janela por cima, nem as da Steam.
    /// </summary>
    sealed class GameToast : NativeWindow, IDisposable
    {
        const int FrameInterval = 15;
        const int RestInterval = 250;

        // Entre um aviso e o próximo, para não parecerem o mesmo cartão piscando.
        static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(300);

        readonly Timer _timer = new Timer { Interval = RestInterval };
        readonly object _lock = new object();
        readonly Queue<Notice> _pending = new Queue<Notice>();

        LayeredSurface _surface;
        GameToastPlace _place;
        float _scale;
        DateTime _start;
        DateTime _nextAllowed;
        bool _showing;

        public GameToast()
        {
            CreateHandle(new CreateParams
            {
                Caption = "Retroboat",
                Style = unchecked((int)WS_POPUP),
                ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
            });

            _timer.Tick += (s, e) => Tick();
            _timer.Start();
        }

        /// <summary>Nada na tela nem na fila: o serviço de downloads já pode fechar.</summary>
        public bool IsIdle
        {
            get { lock (_lock) return !_showing && _pending.Count == 0; }
        }

        /// <summary>Pode ser chamado de qualquer thread; o cartão aparece na thread da janela.</summary>
        /// <param name="cover">Arquivo da capa do jogo, ou nulo.</param>
        public void Add(string title, string cover)
        {
            lock (_lock)
                _pending.Enqueue(new Notice { Title = title, Cover = cover });
        }

        void Tick()
        {
            try
            {
                Step(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                // Um monitor que some ou uma capa ilegível não pode derrubar o download: o aviso é só deixado de lado.
                Log.Warn("Aviso de download concluído no jogo: " + ex.Message);
                Finish(DateTime.UtcNow);
            }
        }

        void Step(DateTime now)
        {
            if (!_showing)
            {
                if (now < _nextAllowed)
                    return;

                Notice next;
                lock (_lock)
                {
                    if (_pending.Count == 0)
                        return;

                    next = _pending.Dequeue();
                    _showing = true;
                }

                Begin(next, now);
                return;
            }

            var frame = GameToastLayout.At(now - _start, _scale);
            if (frame.Done)
            {
                Finish(now);
                return;
            }

            _surface.Move(Handle, _place.X, _place.Y + frame.OffsetY, frame.Opacity);
            KeepOnTop();
        }

        void Begin(Notice notice, DateTime now)
        {
            var game = GetForegroundWindow();
            var screen = game != IntPtr.Zero ? Screen.FromHandle(game) : Screen.PrimaryScreen;
            var area = CoversScreen(game, screen.Bounds) ? screen.Bounds : screen.WorkingArea;

            _scale = GameToastLayout.ScaleFor(screen.Bounds.Height);
            _place = GameToastLayout.Place(area.X, area.Y, area.Width, area.Height, _scale);

            var pixels = new int[_place.W * _place.H];
            using (var cover = LoadCover(notice.Cover))
            using (var face = new GameToastFace(_place.W, _place.H))
                face.Paint(notice.Title, cover, pixels);

            _surface?.Dispose();
            _surface = new LayeredSurface(_place.W, _place.H);

            var first = GameToastLayout.At(TimeSpan.Zero, _scale);
            _surface.Present(Handle, pixels, _place.X, _place.Y + first.OffsetY, first.Opacity);
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            KeepOnTop();

            _start = now;
            _timer.Interval = FrameInterval;
        }

        void Finish(DateTime now)
        {
            ShowWindow(Handle, SW_HIDE);
            _surface?.Dispose();
            _surface = null;
            _nextAllowed = now + Gap;
            _timer.Interval = RestInterval;

            lock (_lock)
                _showing = false;
        }

        /// <summary>Lê a capa para a memória: o arquivo não fica preso enquanto o ES ou a sincronização mexem nele.</summary>
        static Image LoadCover(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (var image = Image.FromStream(stream))
                    return new Bitmap(image);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OutOfMemoryException)
            {
                Log.Warn("Capa do aviso ilegível (" + path + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>Jogo em tela cheia: a barra de tarefas está escondida e o canto é o da tela inteira.</summary>
        static bool CoversScreen(IntPtr window, Rectangle bounds)
        {
            if (window == IntPtr.Zero || !GetWindowRect(window, out var rect))
                return false;

            return rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
        }

        /// <summary>Um emulador que se põe "sempre visível" ficaria por cima; o cartão volta para a frente a cada quadro.</summary>
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

        sealed class Notice
        {
            public string Title;
            public string Cover;
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

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RECT rect);
    }
}
