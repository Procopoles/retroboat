using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Retroboat.Accounts;
using Retroboat.Downloads;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Launch
{
    /// <summary>
    /// A região de ícones do cabeçalho do ES, que gira e mostra o download. O ES não tem como receber dados ao
    /// vivo no tema, então isto é uma janela nossa por cima do cabeçalho: sem foco, sem clique e sem barra de
    /// tarefas, para o controle e o teclado continuarem indo para o ES. Com o RB-010 (ES nativo), isto vira um
    /// componente de verdade do ES, e a janela sai.
    ///
    /// A face de ícones é uma foto do próprio cabeçalho, tirada na hora de girar, e assim serve para qualquer
    /// tema. Fora da rotação, com os ícones à mostra, a janela fica escondida: o que aparece é o ES de verdade.
    /// Só aparece com o ES em primeiro plano; com um jogo aberto ou outro programa na frente, ela some.
    /// </summary>
    sealed class HeaderBar : NativeWindow, IDownloadDisplay
    {
        const int FlipMilliseconds = 450;
        const int LeaveCaptureMilliseconds = 60;
        const int RestInterval = 125;
        const int FlipInterval = 15;
        const int RetryInterval = 2000;
        const int PulseMilliseconds = 1800;

        static readonly TimeSpan ShowBarFor = TimeSpan.FromSeconds(10);
        static readonly TimeSpan ShowIconsFor = TimeSpan.FromSeconds(6);
        static readonly TimeSpan ShowNoticeFor = TimeSpan.FromSeconds(6);

        // A rotação começa um instante depois de o ES voltar para a frente: ele redesenha a tela ao voltar.
        static readonly TimeSpan AfterEsReturns = TimeSpan.FromMilliseconds(1200);

        // Fundo atrás do cubo enquanto gira (neutral-2).
        const int Backdrop = unchecked((int)0xFF0E141B);

        enum State { Hidden, FlippingIn, Showing, LeavingCapture, FlippingOut }

        readonly string _settingsPath;
        readonly Timer _timer = new Timer { Interval = RestInterval };
        readonly object _lock = new object();
        readonly Queue<DownloadStatus> _notices = new Queue<DownloadStatus>();
        readonly DateTime _started = DateTime.UtcNow;

        DownloadStatus _live;
        DownloadStatus _notice;
        DownloadStatus _noticeOnScreen;
        State _state = State.Hidden;
        DateTime _flipStart;
        DateTime _showUntil;
        DateTime _nextShow;
        DateTime _esSettled;

        HeaderRegion _region;
        HeaderBarFace _face;
        LayeredSurface _surface;
        int[] _front;
        int[] _back;
        int[] _frame;
        int[] _flipFrom;
        int[] _flipTo;

        IntPtr _es;
        int _esProcess;
        DateTime _nextEsSearch;
        bool _esWasInFront;

        public HeaderBar(RetroBatInstall install)
        {
            _settingsPath = Path.Combine(install.UserEmulationStationPath, "es_settings.cfg");

            CreateHandle(new CreateParams
            {
                Caption = "Retroboat",
                Style = unchecked((int)WS_POPUP),
                ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
            });

            _timer.Tick += (s, e) => Tick();
            _timer.Start();
        }

        /// <summary>Não há nada para mostrar nem por vir: a janela já pode fechar.</summary>
        public bool IsIdle
        {
            get
            {
                lock (_lock)
                    return _state == State.Hidden && _live == null && _notice == null && _notices.Count == 0;
            }
        }

        /// <summary>O ES está ocupado (jogo, scraper): o aviso dele fica no mesmo canto, então a região espera.</summary>
        public bool EsBusy { get; set; }

        /// <summary>Chamado por uma notificação mostrada ao terminar a face de um jogo que falhou.</summary>
        public event Action<DownloadStatus> NoticeShown;

        public void SetLive(DownloadStatus status)
        {
            lock (_lock)
                _live = status;
        }

        public void AddNotice(DownloadStatus status)
        {
            lock (_lock)
                _notices.Enqueue(status);
        }

        void Tick()
        {
            try
            {
                Step();
            }
            catch (Exception ex)
            {
                // Uma tela que some (monitor desligado, resolução mudando) não pode derrubar o download. O
                // download segue; a região tenta de novo com calma.
                Log.Warn("Região do cabeçalho: " + ex.Message);
                HideNow();
                _nextShow = DateTime.UtcNow + ShowIconsFor;
                _timer.Interval = RetryInterval;
            }
        }

        void Step()
        {
            var now = DateTime.UtcNow;
            bool esInFront = FindEs(now) && IsForeground();

            if (!esInFront)
            {
                _esWasInFront = false;
                HideNow();
                return;
            }

            if (!_esWasInFront)
            {
                _esWasInFront = true;
                _nextShow = _esSettled = now + AfterEsReturns;
            }

            switch (_state)
            {
                case State.Hidden:
                    // Um jogo que acabou de terminar não espera o tempo dos ícones.
                    bool due = now >= _nextShow || (now >= _esSettled && HasNoticeWaiting);
                    if (due && !EsBusy && Current() != null)
                        BeginFlipIn(now);
                    else
                        _timer.Interval = RestInterval;
                    break;

                case State.FlippingIn:
                case State.FlippingOut:
                    RenderFlip(now);
                    break;

                case State.Showing:
                    RenderShowing(now);
                    break;

                case State.LeavingCapture:
                    // O Windows aplica a exclusão na composição seguinte da tela.
                    if ((now - _flipStart).TotalMilliseconds >= LeaveCaptureMilliseconds)
                    {
                        CaptureHeader(_front);
                        SetWindowDisplayAffinity(Handle, WDA_NONE);
                        StartFlipOut(now);
                    }
                    break;
            }
        }

        /// <summary>O aviso de um jogo que terminou tem a vez; se não houver, vale o download em andamento.</summary>
        DownloadStatus Current()
        {
            lock (_lock)
            {
                if (_notice == null && _notices.Count > 0)
                    _notice = _notices.Dequeue();

                return _notice ?? _live;
            }
        }

        void BeginFlipIn(DateTime now)
        {
            PrepareSurface();

            CaptureHeader(_front);
            _face.Paint(Current(), Pulse(now), _back);

            _flipFrom = _front;
            _flipTo = _back;
            _flipStart = now;
            _state = State.FlippingIn;
            _timer.Interval = FlipInterval;

            Present(_flipFrom);
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            KeepOnTop();
        }

        /// <summary>
        /// A volta precisa de uma foto nova do cabeçalho (o relógio mudou, o usuário navegou), mas a captura enxerga
        /// a barra. Por um instante, a janela sai das capturas de tela; quem olha a tela não percebe nada. Num
        /// Windows sem esse recurso, vale a foto da ida.
        /// </summary>
        void BeginFlipOut(DateTime now)
        {
            _timer.Interval = FlipInterval;
            if (SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE))
            {
                _state = State.LeavingCapture;
                _flipStart = now;
            }
            else
            {
                StartFlipOut(now);
            }
        }

        void StartFlipOut(DateTime now)
        {
            _flipFrom = _back;
            _flipTo = _front;
            _flipStart = now;
            _state = State.FlippingOut;
        }

        void RenderFlip(DateTime now)
        {
            double t = (now - _flipStart).TotalMilliseconds / FlipMilliseconds;
            HeaderFlip.Render(_flipFrom, _flipTo, _region.W, _region.H, HeaderFlip.Ease(t), _frame, Backdrop);
            Present(_frame);

            if (t < 1)
                return;

            _timer.Interval = RestInterval;
            if (_state == State.FlippingIn)
            {
                _state = State.Showing;
                _noticeOnScreen = HasNotice ? Current() : null;
                _showUntil = now + (HasNotice ? ShowNoticeFor : ShowBarFor);
                KeepOnTop();
            }
            else
            {
                _state = State.Hidden;
                ShowWindow(Handle, SW_HIDE);
                _nextShow = now + ShowIconsFor;
                FinishNotice();
            }
        }

        bool HasNotice
        {
            get { lock (_lock) return _notice != null; }
        }

        bool HasNoticeWaiting
        {
            get { lock (_lock) return _notice != null || _notices.Count > 0; }
        }

        void RenderShowing(DateTime now)
        {
            var current = Current();
            if (current != null)
            {
                // O jogo terminou com a barra na tela: a face troca na hora e fica o tempo do aviso.
                if (HasNotice && !ReferenceEquals(current, _noticeOnScreen))
                {
                    _noticeOnScreen = current;
                    _showUntil = now + ShowNoticeFor;
                }

                _face.Paint(current, Pulse(now), _back);
                Present(_back);
            }

            KeepOnTop();

            // Não há mais nada (cancelado), ou o ES ficou ocupado: volta aos ícones sem esperar. O aviso de um
            // jogo que terminou fica o tempo dele.
            bool busy = EsBusy && !HasNotice;
            if (current == null || busy || now >= _showUntil)
                BeginFlipOut(now);
        }

        /// <summary>A face do jogo que terminou saiu da tela: vem o próximo aviso, se houver, ou o download seguinte.</summary>
        void FinishNotice()
        {
            DownloadStatus finished;
            lock (_lock)
            {
                finished = _notice;
                _notice = null;
            }

            _noticeOnScreen = null;
            if (finished != null)
                NoticeShown?.Invoke(finished);
        }

        double Pulse(DateTime now)
        {
            double phase = (now - _started).TotalMilliseconds / PulseMilliseconds;
            return 0.5 + 0.5 * Math.Sin(phase * 2 * Math.PI);
        }

        void HideNow()
        {
            if (_state != State.Hidden)
            {
                ShowWindow(Handle, SW_HIDE);
                _state = State.Hidden;
            }

            _timer.Interval = RestInterval;
        }

        /// <summary>Calcula a região na tela do ES e refaz as superfícies se o tamanho mudou.</summary>
        void PrepareSurface()
        {
            GetClientRect(_es, out var client);
            var origin = new POINT();
            ClientToScreen(_es, ref origin);

            var region = HeaderRegion.For(ReadTheme(), client.Right - client.Left, client.Bottom - client.Top);
            region.X += origin.X;
            region.Y += origin.Y;

            if (_surface == null || region.W != _region.W || region.H != _region.H)
            {
                _surface?.Dispose();
                _face?.Dispose();

                _surface = new LayeredSurface(region.W, region.H);
                _face = new HeaderBarFace(region.W, region.H);
                _front = new int[region.W * region.H];
                _back = new int[region.W * region.H];
                _frame = new int[region.W * region.H];
            }

            _region = region;
        }

        string ReadTheme()
        {
            try
            {
                return EsSettings.Load(_settingsPath).Get("ThemeSet");
            }
            catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException || ex is UnauthorizedAccessException)
            {
                return null; // O ES está gravando o arquivo: o canto superior direito serve.
            }
        }

        void CaptureHeader(int[] pixels)
        {
            using (var bitmap = new Bitmap(_region.W, _region.H, PixelFormat.Format32bppPArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(_region.X, _region.Y, 0, 0, new Size(_region.W, _region.H), CopyPixelOperation.SourceCopy);

                var data = bitmap.LockBits(new Rectangle(0, 0, _region.W, _region.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
                finally { bitmap.UnlockBits(data); }
            }

            for (int i = 0; i < pixels.Length; i++)
                pixels[i] |= unchecked((int)0xFF000000);
        }

        void Present(int[] pixels)
        {
            _surface.Present(Handle, pixels, _region.X, _region.Y);
        }

        void KeepOnTop()
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        bool FindEs(DateTime now)
        {
            if (_es != IntPtr.Zero && IsWindow(_es))
                return true;

            _es = IntPtr.Zero;
            if (now < _nextEsSearch)
                return false;

            _nextEsSearch = now + TimeSpan.FromSeconds(1);
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("emulationstation"))
            {
                using (process)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        _es = process.MainWindowHandle;
                        _esProcess = process.Id;
                        return true;
                    }
                }
            }

            return false;
        }

        bool IsForeground()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out int process);
            return process == _esProcess && !IsIconic(_es);
        }

        public void Dispose()
        {
            _timer.Dispose();
            _surface?.Dispose();
            _face?.Dispose();
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
        const uint WDA_NONE = 0x00;
        const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref POINT point);
    }
}
