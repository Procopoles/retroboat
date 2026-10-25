using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Retroboat.Cloud;
using Retroboat.Es;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Settings;

namespace Retroboat.Launcher
{
    /// <summary>
    /// Retroboat.exe: substitui o atalho do retrobat.exe. Prepara a biblioteca (RB-003), sincroniza
    /// com a nuvem (BIOS) e abre o RetroBat de sempre; o resultado da sincronização aparece como
    /// notificação no ES. Se algo der errado na preparação, o RetroBat abre mesmo assim com a
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

            if (!PrepareLibrary(install, settings.RomsPath, CreateCommandWrapper(install, settings), showWarnings: firstRun || own.Setup))
                return 0;

            var sync = RunStartupSync(install, settings);

            if (own.NoLaunch)
            {
                if (sync != null && EsApi.IsRunning())
                    Log.Info(EsApi.Notify(string.Join("\n", sync.Messages)) ? "Notificação mostrada no ES." : "Falha ao notificar o ES.");

                return 0;
            }

            int exitCode = StartRetroBat(install, passThrough);
            if (exitCode == 0 && sync != null)
                NotifyWhenEsIsReady(sync);

            return exitCode;
        }

        /// <summary>
        /// Sistemas com jogos na nuvem abrem pelo retroboat-launch, que baixa o jogo antes do emulador.
        /// O caminho é relativo a %HOME% (a pasta do ES), para o RetroBat continuar portátil.
        /// </summary>
        static CommandWrapper CreateCommandWrapper(RetroBatInstall install, RetroboatSettings settings)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (!File.Exists(Path.Combine(exeDir, "retroboat-launch.exe")))
            {
                Log.Warn("retroboat-launch.exe não encontrado ao lado do Retroboat.exe: jogos da nuvem não serão baixados ao jogar.");
                return null;
            }

            string launcherPath;
            if (RetroBatInstall.SamePath(exeDir, install.RetroboatDataPath))
                launcherPath = @"%HOME%\..\retroboat\retroboat-launch.exe";
            else if (RetroBatInstall.SamePath(exeDir, install.Root))
                launcherPath = @"%HOME%\..\retroboat-launch.exe";
            else
                launcherPath = Path.Combine(exeDir, "retroboat-launch.exe");

            try
            {
                var cloud = CloudLocator.Locate(settings.CloudPath);
                if (cloud == null || !Directory.Exists(cloud.GamesPath))
                    return null;

                var systems = new CloudLibrary(cloud.GamesPath).GetGames().Select(g => g.System).Distinct().ToList();
                return systems.Count > 0 ? new CommandWrapper(launcherPath, systems) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Não foi possível listar a biblioteca na nuvem: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Sincroniza com a nuvem antes do ES ler os arquivos. A janela de progresso só aparece se demorar.
        /// Falha na sincronização nunca impede de jogar.
        /// </summary>
        static StartupSyncReport RunStartupSync(RetroBatInstall install, RetroboatSettings settings)
        {
            StartupSyncReport report = null;
            Exception error = null;

            var worker = new Thread(() =>
            {
                try { report = StartupSync.Run(install, settings.CloudPath, settings.RomsPath); }
                catch (Exception ex) { error = ex; }
            }) { IsBackground = true };

            worker.Start();
            if (!worker.Join(400))
            {
                using (var form = new SyncProgressForm())
                using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                {
                    timer.Tick += (s, e) => { if (!worker.IsAlive) form.Close(); };
                    timer.Start();
                    form.ShowDialog();
                }

                worker.Join();
            }

            if (error != null)
            {
                Log.Error("Falha na sincronização com a nuvem", error);
                return null;
            }

            foreach (var message in report.Messages)
            {
                if (message.StartsWith("Atenção", StringComparison.Ordinal) || message.StartsWith("Nuvem: Google Drive", StringComparison.Ordinal))
                    Log.Warn(message);
                else
                    Log.Info(message);
            }

            if (report.Bios != null)
            {
                foreach (var file in report.Bios.Organized) Log.Info("BIOS organizada no Drive: " + file);
                foreach (var file in report.Bios.Downloaded) Log.Info("BIOS baixada: " + file);
                foreach (var file in report.Bios.Uploaded) Log.Info("BIOS enviada: " + file);
                foreach (var problem in report.Bios.Errors) Log.Warn("BIOS: " + problem);
            }

            if (report.Library != null)
            {
                foreach (var game in report.Library.PlaceholdersCreated) Log.Info("Jogo da nuvem no ES (marcador): " + game);
                foreach (var game in report.Library.PlaceholdersRemoved) Log.Info("Saiu da nuvem, marcador removido: " + game);
            }

            return report;
        }

        /// <summary>
        /// Mostra o resultado da sincronização como notificação do ES, pela API local dele. O lançador fica
        /// em segundo plano, sem janela, até o ES subir (a intro do RetroBat leva alguns segundos).
        /// </summary>
        static void NotifyWhenEsIsReady(StartupSyncReport report)
        {
            if (!EsApi.WaitUntilRunning(TimeSpan.FromMinutes(3)))
            {
                Log.Warn("O ES não respondeu em 3 minutos; o resultado da sincronização ficou só no log.");
                return;
            }

            // Dá tempo do ES sair da tela de carregamento, senão a notificação passa despercebida.
            Thread.Sleep(5000);
            if (EsApi.Notify(string.Join("\n", report.Messages)))
                Log.Info("Notificação mostrada no ES.");
            else
                Log.Warn("Não foi possível mostrar a notificação no ES.");
        }

        /// <returns>false se o usuário desistiu de abrir.</returns>
        static bool PrepareLibrary(RetroBatInstall install, string romsPath, CommandWrapper wrapper, bool showWarnings)
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
                var result = new IsolatedLibrary(install).Apply(romsPath, wrapper: wrapper);
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
