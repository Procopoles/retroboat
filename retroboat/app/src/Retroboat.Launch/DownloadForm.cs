using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Retroboat.Cloud;
using Retroboat.Text;
using Retroboat.Transfers;

namespace Retroboat.Launch
{
    /// <summary>
    /// Tela cheia mostrada enquanto um jogo da nuvem é baixado antes de abrir. Feita para ser usada com
    /// controle: não precisa de mouse, e B (ou Esc) cancela e volta ao EmulationStation.
    /// </summary>
    sealed class DownloadForm : Form, IProgress<TransferProgress>
    {
        readonly OnDemandInstaller _installer;
        readonly CloudIndexEntry _entry;
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        readonly System.Windows.Forms.Timer _gamepadTimer = new System.Windows.Forms.Timer { Interval = 100 };
        readonly Label _title;
        readonly Label _game;
        readonly Label _status;
        readonly Label _hint;
        readonly Panel _barTrack;
        readonly Panel _barFill;
        bool _finished;

        public DownloadForm(OnDemandInstaller installer, CloudIndexEntry entry)
        {
            _installer = installer;
            _entry = entry;

            Theme.ApplyFullScreen(this);

            _title = Theme.MakeLabel("Baixando da nuvem", 16f, Theme.Muted);
            _game = Theme.MakeLabel(Path.GetFileNameWithoutExtension(entry.LocalName), 30f, Theme.Foreground, FontStyle.Bold);
            _status = Theme.MakeLabel("Preparando...", 16f, Theme.Foreground);
            _hint = Theme.MakeLabel("B no controle ou Esc: cancelar e voltar", 13f, Theme.Muted);
            _barTrack = new Panel { BackColor = Theme.Surface, Height = 14 };
            _barFill = new Panel { BackColor = Theme.Accent, Height = 14, Width = 0 };
            _barTrack.Controls.Add(_barFill);
            Controls.AddRange(new Control[] { _title, _game, _barTrack, _status, _hint });

            Resize += (s, e) => LayoutControls();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back) Cancel(); };
            _gamepadTimer.Tick += (s, e) => { if (GamepadButtons.IsPressed(GamepadButtons.B)) Cancel(); };
        }

        public OnDemandResult Result { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutControls();
            Activate();
            _gamepadTimer.Start();

            var worker = new Thread(() =>
            {
                OnDemandResult result;
                try { result = _installer.Install(_entry, this, _cancel.Token); }
                catch (Exception ex) { result = new OnDemandResult(OnDemandOutcome.Failed, "Erro inesperado: " + ex.Message); }

                SafeInvoke(() => Finish(result));
            }) { IsBackground = true };

            worker.Start();
        }

        public void Report(TransferProgress p)
        {
            if (IsDisposed || _finished)
                return;

            SafeInvoke(() =>
            {
                if (_finished)
                    return;

                double fraction = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : 0;
                _barFill.Width = (int)(_barTrack.Width * Math.Min(1, fraction));
                _status.Text = Format.Percent(p.BytesDone, p.BytesTotal) + "   ·   " + Format.Bytes(p.BytesDone) + " de " + Format.Bytes(p.BytesTotal) +
                               (p.BytesPerSecond > 0 ? "   ·   " + Format.Speed(p.BytesPerSecond) : "") +
                               (p.Remaining.HasValue ? "   ·   restam " + Format.Duration(p.Remaining.Value) : "");
            });
        }

        void SafeInvoke(Action action)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException) { } // Janela já fechada.
        }

        void Finish(OnDemandResult result)
        {
            _finished = true;
            Result = result;
            _gamepadTimer.Stop();

            if (result.Outcome == OnDemandOutcome.Installed || result.Outcome == OnDemandOutcome.Cancelled)
            {
                Close();
                return;
            }

            // Erro: mostra o motivo até o usuário apertar um botão (ou por alguns segundos).
            _title.Text = result.Outcome == OnDemandOutcome.Corrupt ? "Jogo corrompido na nuvem" : "Não foi possível baixar";
            _title.ForeColor = Theme.Danger;
            _barTrack.Visible = false;
            _status.Text = result.Message;
            _hint.Text = "Qualquer botão ou tecla para voltar";
            LayoutControls();

            var closeTimer = new System.Windows.Forms.Timer { Interval = 100 };
            int ticks = 0;
            KeyDown += (s, e) => Close();
            closeTimer.Tick += (s, e) =>
            {
                if (++ticks > 100 || (ticks > 10 && GamepadButtons.IsPressed(0xFFFF)))
                {
                    closeTimer.Stop();
                    Close();
                }
            };
            closeTimer.Start();
        }

        void Cancel()
        {
            if (_finished || _cancel.IsCancellationRequested)
                return;

            _status.Text = "Cancelando...";
            _cancel.Cancel();
        }

        void LayoutControls()
        {
            int width = Math.Min(ClientSize.Width - 160, 1100);
            int left = (ClientSize.Width - width) / 2;
            int top = ClientSize.Height / 2 - 120;

            foreach (var label in new[] { _title, _game })
                label.MaximumSize = new Size(width, 0);

            _status.MaximumSize = new Size(width, 0);
            _hint.MaximumSize = new Size(width, 0);

            _title.Location = new Point(left, top);
            _game.Location = new Point(left, _title.Bottom + 8);
            _barTrack.Bounds = new Rectangle(left, _game.Bottom + 28, width, 14);
            _status.Location = new Point(left, (_barTrack.Visible ? _barTrack.Bottom : _game.Bottom) + 18);
            _hint.Location = new Point(left, _status.Bottom + 40);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gamepadTimer.Dispose();
                _cancel.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
