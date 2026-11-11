using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Retroboat.Text;
using Retroboat.Transfers;

namespace Retroboat.Launch
{
    /// <summary>
    /// Tela cheia com andamento, no estilo das outras do Retroboat (Theme): título, o que está acontecendo, uma barra, quanto
    /// já foi, a velocidade e o tempo restante. B (ou Esc) cancela. O trabalho roda fora da tela e a fecha ao terminar.
    /// </summary>
    sealed class ProgressForm : Form
    {
        readonly Label _title;
        readonly Label _heading;
        readonly Label _status;
        readonly Label _hint;
        readonly Panel _track;
        readonly Panel _fill;
        readonly System.Windows.Forms.Timer _gamepadTimer = new System.Windows.Forms.Timer { Interval = 50 };
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        ushort _previousButtons;
        double _fraction;

        ProgressForm(string title, string heading, bool canCancel)
        {
            Theme.ApplyFullScreen(this);

            _title = Theme.MakeLabel(title, 16f, Theme.Muted);
            _heading = Theme.MakeLabel(heading, 28f, Theme.Foreground, FontStyle.Bold);
            _status = Theme.MakeLabel("Preparando...", 15f, Theme.Foreground);
            _hint = Theme.MakeLabel(canCancel ? "B ou Esc: cancelar" : "", 13f, Theme.Muted);
            _track = new Panel { BackColor = Theme.Surface, Height = 10 };
            _fill = new Panel { BackColor = Theme.Accent, Height = 10, Width = 0 };
            _track.Controls.Add(_fill);
            Controls.AddRange(new Control[] { _title, _heading, _status, _track, _hint });

            Resize += (s, e) => LayoutControls();
            KeyDown += (s, e) =>
            {
                if (canCancel && (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back))
                    Cancel();
            };

            _gamepadTimer.Tick += (s, e) =>
            {
                Theme.ReclaimFocus(this);
                var buttons = GamepadButtons.Current();
                var pressed = (ushort)(buttons & ~_previousButtons);
                _previousButtons = buttons;
                if (canCancel && (pressed & GamepadButtons.B) != 0)
                    Cancel();
            };
        }

        /// <summary>
        /// Mostra a tela enquanto <paramref name="work"/> roda. <paramref name="cancelled"/> diz se o usuário apertou B (o
        /// trabalho recebe o token e decide como parar). Exceções do trabalho chegam a quem chamou.
        /// </summary>
        public static T Run<T>(string title, string heading, Func<IProgress<TransferProgress>, CancellationToken, T> work, out bool cancelled, bool canCancel = true)
        {
            using (var form = new ProgressForm(title, heading, canCancel))
            {
                T result = default;
                Exception error = null;
                var progress = new Progress(form);
                var worker = new Thread(() =>
                {
                    try { result = work(progress, form._cancel.Token); }
                    catch (Exception ex) { error = ex; }
                    finally
                    {
                        try { form.BeginInvoke(new Action(form.Close)); }
                        catch (InvalidOperationException) { } // A tela ainda não abriu ou já fechou.
                    }
                }) { IsBackground = true, Name = "Andamento" };

                form.Shown += (s, e) => worker.Start();
                Application.Run(form);
                worker.Join();

                cancelled = form._cancel.IsCancellationRequested;
                if (error != null)
                    throw new InvalidOperationException(error.Message, error);

                return result;
            }
        }

        void Cancel()
        {
            if (_cancel.IsCancellationRequested)
                return;

            _cancel.Cancel();
            _status.Text = "Cancelando...";
            _hint.Text = "";
            LayoutControls();
        }

        void ShowProgress(TransferProgress value)
        {
            if (_cancel.IsCancellationRequested)
                return;

            if (value.BytesTotal > 0)
            {
                _fraction = Math.Min(1, (double)value.BytesDone / value.BytesTotal);
                var text = Format.Percent(value.BytesDone, value.BytesTotal) + "   ·   " + Format.Bytes(value.BytesDone) + " de " + Format.Bytes(value.BytesTotal);
                if (value.BytesPerSecond > 0 && value.BytesDone < value.BytesTotal)
                    text += "   ·   " + Format.Speed(value.BytesPerSecond);
                if (value.Remaining.HasValue && value.BytesDone < value.BytesTotal)
                    text += "   ·   " + Format.Duration(value.Remaining.Value);
                if (value.BytesDone >= value.BytesTotal)
                    text = "Conferindo e instalando...";

                _status.Text = text;
            }
            else
            {
                _status.Text = Format.Bytes(value.BytesDone);
            }

            LayoutControls();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Theme.ScreenShown();
            LayoutControls();
            Activate();
            _previousButtons = GamepadButtons.Current();
            _gamepadTimer.Start();
        }

        void LayoutControls()
        {
            int width = Math.Min(ClientSize.Width - 160, 1100);
            int left = (ClientSize.Width - width) / 2;
            foreach (var label in new[] { _title, _heading, _status, _hint })
                label.MaximumSize = new Size(width, 0);

            int top = Math.Max(48, ClientSize.Height / 2 - 160);
            _title.Location = new Point(left, top);
            _heading.Location = new Point(left, _title.Bottom + 8);
            _track.Bounds = new Rectangle(left, _heading.Bottom + 40, width, 10);
            _fill.Bounds = new Rectangle(0, 0, (int)(width * _fraction), 10);
            _status.Location = new Point(left, _track.Bottom + 20);
            _hint.Location = new Point(left, _status.Bottom + 48);
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

        /// <summary>Leva o andamento da thread do trabalho para a tela, no máximo umas 8 vezes por segundo.</summary>
        sealed class Progress : IProgress<TransferProgress>
        {
            readonly ProgressForm _form;
            int _last = Environment.TickCount - 1000;

            public Progress(ProgressForm form) => _form = form;

            public void Report(TransferProgress value)
            {
                bool finished = value.BytesTotal > 0 && value.BytesDone >= value.BytesTotal;
                if (!finished && Environment.TickCount - _last < 120)
                    return;

                _last = Environment.TickCount;
                try { _form.BeginInvoke(new Action(() => _form.ShowProgress(value))); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
