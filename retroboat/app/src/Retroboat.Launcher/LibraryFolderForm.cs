using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Retroboat.Launcher
{
    /// <summary>Primeiro uso (ou --setup): onde ficam os jogos deste PC.</summary>
    sealed class LibraryFolderForm : Form
    {
        readonly TextBox _path;
        readonly string _defaultPath;

        public LibraryFolderForm(string currentPath, string defaultPath)
        {
            _defaultPath = defaultPath;

            Text = "Retroboat: pasta de jogos";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(600, 210);

            var title = new Label
            {
                Text = "Onde ficam os seus jogos neste PC?",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 16)
            };

            var explanation = new Label
            {
                Text = "Os jogos ficam separados do RetroBat: favoritos, metadados e saves não são gravados\r\n" +
                       "nesta pasta. Dá para mudar depois abrindo o Retroboat com --setup.",
                AutoSize = true,
                Location = new Point(16, 50)
            };

            _path = new TextBox
            {
                Text = currentPath,
                Location = new Point(16, 102),
                Width = 452
            };

            var browse = new Button { Text = "Procurar...", Location = new Point(476, 100), Width = 108 };
            browse.Click += (s, e) => Browse();

            var useDefault = new Button { Text = "Usar a pasta padrão", Location = new Point(16, 156), Width = 180 };
            useDefault.Click += (s, e) => _path.Text = _defaultPath;

            var ok = new Button { Text = "Continuar", Location = new Point(376, 156), Width = 100, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Location = new Point(484, 156), Width = 100, DialogResult = DialogResult.Cancel };

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { title, explanation, _path, browse, useDefault, ok, cancel });

            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK && !IsValid(SelectedPath))
                {
                    MessageBox.Show(this, "Escolha uma pasta num disco que exista neste PC.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            };
        }

        public string SelectedPath => (_path.Text ?? "").Trim().Trim('"');

        void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Pasta dos jogos", ShowNewFolderButton = true })
            {
                if (Directory.Exists(SelectedPath))
                    dialog.SelectedPath = SelectedPath;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _path.Text = dialog.SelectedPath;
            }
        }

        static bool IsValid(string path)
        {
            try
            {
                return path.Length > 0 && Path.IsPathRooted(path) && Directory.Exists(Path.GetPathRoot(Path.GetFullPath(path)));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
