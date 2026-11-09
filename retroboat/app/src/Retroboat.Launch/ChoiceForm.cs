using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Retroboat.Launch
{
    /// <summary>
    /// Pergunta em tela cheia, feita para controle: cima/baixo escolhe, A (ou Enter) confirma, B (ou Esc)
    /// volta. Sem opções, serve de aviso enquanto algo termina ("Sincronizando memory cards...").
    /// </summary>
    sealed class ChoiceForm : Form
    {
        public const int Back = -1;

        /// <summary>A condição de <see cref="AutoClose"/> ficou verdadeira antes de alguém escolher.</summary>
        public const int AutoClosed = -2;

        readonly Label _title;
        readonly Label _heading;
        readonly Label _text;
        readonly Label _hint;
        readonly List<Label> _options = new List<Label>();
        readonly System.Windows.Forms.Timer _gamepadTimer = new System.Windows.Forms.Timer { Interval = 50 };
        readonly System.Windows.Forms.Timer _autoCloseTimer = new System.Windows.Forms.Timer();
        int _selected;
        ushort _previousButtons;

        public ChoiceForm(string title, string heading, string text, IReadOnlyList<string> options, bool canGoBack = true, string hint = null)
        {
            Theme.ApplyFullScreen(this);
            Result = Back;

            _title = Theme.MakeLabel(title, 16f, Theme.Muted);
            _heading = Theme.MakeLabel(heading, 28f, Theme.Foreground, FontStyle.Bold);
            _text = Theme.MakeLabel(text ?? "", 15f, Theme.Foreground);
            _hint = Theme.MakeLabel(hint ?? (options.Count == 0 ? "" : "A ou Enter: escolher" + (canGoBack ? "   ·   B ou Esc: voltar" : "")), 13f, Theme.Muted);
            Controls.AddRange(new Control[] { _title, _heading, _text, _hint });

            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var option = Theme.MakeLabel(options[i], 17f, Theme.Foreground);
                option.AutoSize = false;
                option.AutoEllipsis = true;
                option.Padding = new Padding(20, 0, 20, 0);
                option.TextAlign = ContentAlignment.MiddleLeft;
                option.Cursor = Cursors.Hand;
                option.Click += (s, e) => Choose(index);
                option.MouseEnter += (s, e) => SelectOption(index);
                _options.Add(option);
                Controls.Add(option);
            }

            Resize += (s, e) => LayoutControls();
            KeyDown += (s, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.Up: SelectOption(_selected - 1); break;
                    case Keys.Down: SelectOption(_selected + 1); break;
                    case Keys.Enter:
                    case Keys.Space: Choose(_selected); break;
                    case Keys.Escape:
                    case Keys.Back: if (canGoBack) Close(); break;
                }
            };

            _gamepadTimer.Tick += (s, e) =>
            {
                Theme.ReclaimFocus(this);

                var buttons = GamepadButtons.Current();
                var pressed = (ushort)(buttons & ~_previousButtons);
                _previousButtons = buttons;

                if ((pressed & GamepadButtons.DpadUp) != 0) SelectOption(_selected - 1);
                if ((pressed & GamepadButtons.DpadDown) != 0) SelectOption(_selected + 1);
                if ((pressed & GamepadButtons.A) != 0) Choose(_selected);
                if ((pressed & GamepadButtons.B) != 0 && canGoBack) Close();
            };

            _autoCloseTimer.Tick += (s, e) =>
            {
                if (AutoClose != null && AutoClose())
                {
                    Result = AutoClosed;
                    Close();
                }
            };
        }

        /// <summary>Índice da opção escolhida, <see cref="Back"/> ou <see cref="AutoClosed"/>.</summary>
        public int Result { get; private set; }

        /// <summary>Conferida periodicamente; verdadeira fecha a tela (ex.: a trava do outro PC saiu).</summary>
        public Func<bool> AutoClose { get; set; }

        public int AutoCloseInterval { get; set; } = 2000;

        /// <summary>Opção com o foco ao abrir.</summary>
        public int InitialSelection { get; set; }

        /// <summary>Mostra e espera a resposta.</summary>
        public static int Ask(string title, string heading, string text, IReadOnlyList<string> options, Func<bool> autoClose = null, bool canGoBack = true,
                              string hint = null, int autoCloseInterval = 2000, int selected = 0)
        {
            using (var form = new ChoiceForm(title, heading, text, options, canGoBack, hint) { AutoClose = autoClose, AutoCloseInterval = autoCloseInterval, InitialSelection = selected })
            {
                Application.Run(form);
                return form.Result;
            }
        }

        /// <summary>
        /// Roda <paramref name="work"/> fora da tela; se passar de 0,4 s, mostra o aviso até terminar. Exceções
        /// de <paramref name="work"/> chegam a quem chamou.
        /// </summary>
        public static T Busy<T>(string title, string heading, Func<T> work)
        {
            T result = default;
            Exception error = null;
            var worker = new Thread(() =>
            {
                try { result = work(); }
                catch (Exception ex) { error = ex; }
            }) { IsBackground = true };

            worker.Start();
            if (!worker.Join(400))
            {
                using (var form = new ChoiceForm(title, heading, null, new string[0], canGoBack: false) { AutoClose = () => !worker.IsAlive, AutoCloseInterval = 100 })
                    Application.Run(form);

                worker.Join();
            }

            if (error != null)
                throw new InvalidOperationException(error.Message, error);

            return result;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutControls();
            SelectOption(InitialSelection);
            Activate();

            // Um botão que já estava apertado (o A que abriu o jogo no ES) não conta.
            _previousButtons = GamepadButtons.Current();
            _gamepadTimer.Start();
            _autoCloseTimer.Interval = AutoCloseInterval;
            if (AutoClose != null)
                _autoCloseTimer.Start();
        }

        void SelectOption(int index)
        {
            if (_options.Count == 0)
                return;

            _selected = Math.Max(0, Math.Min(_options.Count - 1, index));
            for (int i = 0; i < _options.Count; i++)
            {
                _options[i].BackColor = i == _selected ? Theme.Accent : Theme.Surface;
                _options[i].ForeColor = i == _selected ? Color.White : Theme.Foreground;
            }
        }

        void Choose(int index)
        {
            if (index < 0 || index >= _options.Count)
                return;

            Result = index;
            Close();
        }

        void LayoutControls()
        {
            int width = Math.Min(ClientSize.Width - 160, 1100);
            int left = (ClientSize.Width - width) / 2;

            foreach (var label in new[] { _title, _heading, _text, _hint })
                label.MaximumSize = new Size(width, 0);

            // Mais alto com muitas opções (o setup tem até 6), para tudo caber numa tela de 1080 linhas.
            int content = _title.Height + 8 + _heading.Height + 16 + (_text.Text.Length > 0 ? _text.Height : 0) + 32 + _options.Count * 68 + 28 + _hint.Height;
            int top = Math.Max(48, Math.Min(ClientSize.Height / 2 - 160, (ClientSize.Height - content) / 2));

            _title.Location = new Point(left, top);
            _heading.Location = new Point(left, _title.Bottom + 8);
            _text.Location = new Point(left, _heading.Bottom + 16);

            int y = (_text.Text.Length > 0 ? _text.Bottom : _heading.Bottom) + 32;
            foreach (var option in _options)
            {
                option.Bounds = new Rectangle(left, y, width, 56);
                y = option.Bottom + 12;
            }

            _hint.Location = new Point(left, y + 28);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gamepadTimer.Dispose();
                _autoCloseTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
