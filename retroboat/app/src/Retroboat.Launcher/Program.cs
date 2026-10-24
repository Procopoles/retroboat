using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Settings;

namespace Retroboat.Launcher
{
    /// <summary>
    /// Retroboat.exe: substitui o atalho do retrobat.exe. Prepara a biblioteca (RB-003) e abre o
    /// RetroBat de sempre. Se algo der errado na preparação, o RetroBat abre mesmo assim com a
    /// configuração anterior: o lançador nunca impede de jogar.
    ///
    /// Argumentos próprios: --retrobat &lt;pasta&gt;, --setup (escolher a pasta de jogos de novo) e
    /// --no-launch (só preparar). Os demais são repassados ao retrobat.exe.
    /// </summary>
    static class Program
    {
        const string Title = "Retroboat";

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var own = ParseOwnArgs(args, out var passThrough);

            var install = own.RetroBatPath != null
                ? new RetroBatInstall(own.RetroBatPath)
                : RetroBatInstall.FindFrom(AppDomain.CurrentDomain.BaseDirectory);

            if (install == null || !File.Exists(install.RetrobatExePath))
            {
                MessageBox.Show("Não achei o retrobat.exe. O Retroboat.exe precisa ficar na pasta do RetroBat (ou na subpasta \"retroboat\").",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            Log.Init(Path.Combine(install.RetroboatDataPath, "logs", "launcher.log"));
            Log.Info("Início: " + install.Root + (args.Length > 0 ? " | args: " + string.Join(" ", args) : ""));

            var settingsPath = RetroboatSettings.GetPath(install);
            var settings = RetroboatSettings.Load(settingsPath);

            bool firstRun = string.IsNullOrEmpty(settings.RomsPath);
            if (firstRun || own.Setup)
            {
                using (var form = new LibraryFolderForm(settings.RomsPath ?? install.DefaultRomsPath, install.DefaultRomsPath))
                {
                    if (form.ShowDialog() != DialogResult.OK)
                    {
                        Log.Info("Escolha da pasta de jogos cancelada; nada foi aberto.");
                        return 0;
                    }

                    settings.RomsPath = form.SelectedPath;
                    settings.Save(settingsPath);
                    Log.Info("Pasta de jogos escolhida: " + settings.RomsPath);
                }
            }

            if (!PrepareLibrary(install, settings.RomsPath, showWarnings: firstRun || own.Setup))
                return 0;

            if (own.NoLaunch)
                return 0;

            return StartRetroBat(install, passThrough);
        }

        /// <returns>false se o usuário desistiu de abrir.</returns>
        static bool PrepareLibrary(RetroBatInstall install, string romsPath, bool showWarnings)
        {
            if (!Directory.Exists(romsPath) && !Directory.Exists(Path.GetPathRoot(romsPath)))
            {
                Log.Warn("Pasta de jogos inacessível: " + romsPath);
                var answer = MessageBox.Show(
                    "A pasta de jogos " + romsPath + " não está acessível (disco desconectado?).\r\n\r\n" +
                    "Abrir o RetroBat mesmo assim? Os sistemas dessa pasta não vão aparecer.",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                    return false;
            }

            try
            {
                var result = new IsolatedLibrary(install).Apply(romsPath);
                Log.Info(string.Format("Biblioteca: {0} sistemas, overlay {1}, emulatorLauncher.cfg {2}, {3} pastas criadas, {4} gamelists criados, movidos: {5}",
                    result.SystemsMapped,
                    result.OverlayWritten ? "gravado" : result.OverlayRemoved ? "removido" : "sem mudança",
                    result.LauncherConfigUpdated ? "atualizado" : "sem mudança",
                    result.FoldersCreated,
                    result.GamelistsCreated,
                    result.GamelistsMoved.Count == 0 ? "nenhum" : string.Join(", ", result.GamelistsMoved)));

                foreach (var warning in result.Warnings)
                    Log.Warn(warning);

                if (showWarnings && result.Warnings.Count > 0)
                    MessageBox.Show(string.Join("\r\n\r\n", result.Warnings), Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao preparar a biblioteca", ex);
                MessageBox.Show("Não foi possível preparar a pasta de jogos:\r\n" + ex.Message + "\r\n\r\nO RetroBat vai abrir com a configuração anterior.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return true;
        }

        static int StartRetroBat(RetroBatInstall install, IList<string> args)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = install.RetrobatExePath,
                    Arguments = string.Join(" ", args.Select(Quote)),
                    WorkingDirectory = install.Root,
                    UseShellExecute = false
                });

                Log.Info("retrobat.exe iniciado.");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao abrir o retrobat.exe", ex);
                MessageBox.Show("Não foi possível abrir o RetroBat:\r\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return arg;

            // Barras invertidas antes de aspas (inclusive a de fechamento) precisam ser dobradas.
            var escaped = System.Text.RegularExpressions.Regex.Replace(arg, @"(\\*)""", m => m.Groups[1].Value + m.Groups[1].Value + "\\\"");
            escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"(\\+)$", m => m.Groups[1].Value + m.Groups[1].Value);
            return "\"" + escaped + "\"";
        }

        static OwnArgs ParseOwnArgs(string[] args, out List<string> passThrough)
        {
            var own = new OwnArgs();
            passThrough = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--setup": own.Setup = true; break;
                    case "--no-launch": own.NoLaunch = true; break;
                    case "--retrobat" when i + 1 < args.Length: own.RetroBatPath = args[++i]; break;
                    default: passThrough.Add(args[i]); break;
                }
            }

            return own;
        }

        sealed class OwnArgs
        {
            public bool Setup;
            public bool NoLaunch;
            public string RetroBatPath;
        }
    }
}
