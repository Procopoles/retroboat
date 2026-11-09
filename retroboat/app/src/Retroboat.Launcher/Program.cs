using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Retroboat.Cloud;
using Retroboat.Es;
using Retroboat.Launch;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Saves;
using Retroboat.Settings;
using Retroboat.Themes;

namespace Retroboat.Launcher
{
    /// <summary>
    /// Retroboat.exe: substitui o atalho do retrobat.exe. Pergunta se abre o Google Drive se ele estiver
    /// fechado, prepara a biblioteca (RB-003), sincroniza com a nuvem (BIOS) e abre o RetroBat de sempre; o
    /// resultado da sincronização aparece como notificação no ES. Depois fica em segundo plano, sem janela,
    /// acompanhando o Drive enquanto o ES estiver aberto (CloudWatcher). Se algo der errado na preparação, o
    /// RetroBat abre mesmo assim com a configuração anterior: o lançador nunca impede de jogar.
    ///
    /// Argumentos próprios: --retrobat &lt;pasta&gt;, --setup (abrir o setup do RB-021: biblioteca, pasta de jogos e opções),
    /// --no-launch (só preparar) e --refresh (só sincronizar, sem janela: o "Atualizar listas de jogos" do ES). Os demais
    /// são repassados ao retrobat.exe.
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

            if (own.Refresh)
                return Refresh(install, settings);

            bool firstRun = string.IsNullOrEmpty(settings.RomsPath);

            // O Drive primeiro: o setup precisa ver as bibliotecas das contas.
            if (!own.NoLaunch)
                OfferToOpenCloudApp(install, settings);

            // RB-021: uma biblioteca só é adotada sem perguntar; duas, ou o Drive aberto sem nenhuma, abrem o setup.
            var choice = ChooseLibrary(install, settings, out var adopted);
            // Pedido do menu NUVEM que ficou para esta abertura (o RetroBat foi aberto sem o Retroboat.exe em segundo plano).
            bool requested = TakeSetupRequest(install);
            var scope = LibrarySetup.Decide(settings, own.Setup || requested, choice?.Outcome ?? LibraryChoiceOutcome.NoDrive);
            if (scope == SetupScope.LibraryOnly && own.NoLaunch)
                scope = SetupScope.None; // --no-launch não pergunta nada.

            if (scope != SetupScope.None)
            {
                if (!SetupFlow.Run(install, settings, settingsPath, scope))
                    return 0;

                adopted = null; // O setup acabou de mostrar a biblioteca; não precisa avisar de novo.
            }

            // Avisos técnicos (sistemas duplicados no es_systems.cfg etc.) numa caixa só no primeiro uso; depois, só no log.
            if (!PrepareLibrary(install, settings.RomsPath, CreateCommandWrapper(install, settings), showWarnings: firstRun))
                return 0;

            ApplyThemeOverlays(install);
            WriteUpdateGamelistsScript(install);
            TurnOffEsBiosCheck(install, settings, settingsPath);

            var sync = RunStartupSync(install, settings, adopted);

            if (own.NoLaunch)
            {
                if (sync != null && EsApi.IsRunning())
                    Log.Info(EsApi.Notify(string.Join("\n", sync.Messages)) ? "Notificação mostrada no ES." : "Falha ao notificar o ES.");

                return 0;
            }

            ClearNativeEsFlag(install);
            int exitCode = StartRetroBat(install, passThrough);
            if (exitCode == 0 && WaitForEs())
            {
                // Um download interrompido na sessão anterior continua sozinho.
                Retroboat.Downloads.DownloadProcess.EnsureRunning(install, AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'));

                if (sync != null)
                    NotifyEs(sync);

                if (CloudWatcher.Run(install, settings))
                {
                    SyncConfigsAfterEs(install, settings);

                    // O menu NUVEM pediu o setup e fechou o ES: abre o Retroboat de novo, com o setup, e ele reabre o RetroBat.
                    if (TakeSetupRequest(install))
                        RestartWithSetup(args);
                }
            }

            return exitCode;
        }

        static bool TakeSetupRequest(RetroBatInstall install)
        {
            try
            {
                return SetupRequest.Take(install);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Não foi possível ler o pedido de setup: " + ex.Message);
                return false;
            }
        }

        static void RestartWithSetup(string[] args)
        {
            var arguments = args.Where(a => !string.Equals(a, "--setup", StringComparison.OrdinalIgnoreCase)).Select(Quote).ToList();
            arguments.Insert(0, "--setup");
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", arguments)) { UseShellExecute = false });
                Log.Info("Setup pedido pelo menu NUVEM: Retroboat aberto de novo com --setup.");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao abrir o Retroboat de novo para o setup", ex);
            }
        }

        /// <summary>
        /// Com o ES fechado, as opções e os controles mudados nos menus vão para a nuvem, e o que outro PC mudou
        /// enquanto este jogava fica gravado para a próxima abertura. Falha aqui só vai para o log.
        /// </summary>
        static void SyncConfigsAfterEs(RetroBatInstall install, RetroboatSettings settings)
        {
            try
            {
                var cloud = CloudLocator.Locate(settings);
                if (cloud == null || !cloud.Exists)
                {
                    Log.Info("ES fechado sem o Google Drive: as configurações vão para a nuvem na próxima abertura do Retroboat.");
                    return;
                }

                Log.Info("ES fechado: sincronizando as configurações.");
                Retroboat.Config.CloudConfigs.SyncAll(install, cloud.RetroboatPath);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao sincronizar as configurações depois de fechar o ES", ex);
            }
        }

        /// <summary>Antes de tudo, para a biblioteca, as BIOS e os memory cards já virem da nuvem. Falha aqui nunca impede de jogar.</summary>
        static void OfferToOpenCloudApp(RetroBatInstall install, RetroboatSettings settings)
        {
            try
            {
                CloudStatusFile.ClearDeclined(install);
                CloudAppPrompt.Ensure(install, settings, CloudNeed.Startup);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao oferecer abrir o Google Drive", ex);
            }
        }

        /// <summary>
        /// Todos os sistemas abrem pelo retroboat-launch: ele baixa o jogo que só está na nuvem, acompanha a
        /// partida com os memory cards (PS2) e abre o emulador com as configurações da nuvem, que valem para
        /// qualquer emulador (regra genérica). Sem o Drive, ele só passa adiante. O caminho é relativo a %HOME%
        /// (a pasta do ES), para o RetroBat continuar portátil.
        /// </summary>
        internal static CommandWrapper CreateCommandWrapper(RetroBatInstall install, RetroboatSettings settings)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (!File.Exists(Path.Combine(exeDir, "retroboat-launch.exe")))
            {
                Log.Warn("retroboat-launch.exe não encontrado ao lado do Retroboat.exe: jogos da nuvem não serão baixados ao apertar BAIXAR e os memory cards só sincronizam ao abrir o Retroboat.");
                return null;
            }

            string launcherPath;
            if (RetroBatInstall.SamePath(exeDir, install.RetroboatDataPath))
                launcherPath = @"%HOME%\..\retroboat\retroboat-launch.exe";
            else if (RetroBatInstall.SamePath(exeDir, install.Root))
                launcherPath = @"%HOME%\..\retroboat-launch.exe";
            else
                launcherPath = Path.Combine(exeDir, "retroboat-launch.exe");

            return new CommandWrapper(launcherPath);
        }

        /// <summary>
        /// Põe de volta o status do Retroboat (RB-014, RB-017) nos temas que o instalador de temas do ES
        /// reinstalou ou atualizou. Leva milissegundos; falha aqui nunca impede de jogar.
        /// </summary>
        static void ApplyThemeOverlays(RetroBatInstall install)
        {
            try
            {
                var result = ThemeOverlays.Apply(install, AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'));
                foreach (var item in result.Applied)
                    Log.Info("Tema: " + item);
                if (result.HookWritten)
                    Log.Info("Tema: script " + ThemeOverlays.HookEvent + " do ES gravado.");
                foreach (var warning in result.Warnings)
                    Log.Warn("Tema: " + warning);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao pôr o status do Retroboat nos temas", ex);
            }
        }

        /// <summary>
        /// O script que faz o "Atualizar listas de jogos" do ES sincronizar com a nuvem antes de reler as listas
        /// (Retroboat.exe --refresh). Leva milissegundos; falha aqui nunca impede de jogar.
        /// </summary>
        static void WriteUpdateGamelistsScript(RetroBatInstall install)
        {
            try
            {
                if (EsScripts.WriteUpdateGamelists(install, AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/')))
                    Log.Info("Script " + EsScripts.UpdateGamelistsEvent + " do ES gravado: \"Atualizar listas de jogos\" sincroniza com a nuvem.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Não foi possível gravar o script " + EsScripts.UpdateGamelistsEvent + " do ES: " + ex.Message);
            }
        }

        /// <summary>
        /// Desliga, uma vez, o aviso de BIOS do ES, que acusa falta com uma BIOS válida. Com o ES aberto fica
        /// para o próximo início, porque ele regrava o es_settings.cfg ao sair. Falha aqui nunca impede de jogar.
        /// </summary>
        static void TurnOffEsBiosCheck(RetroBatInstall install, RetroboatSettings settings, string settingsPath)
        {
            if (Process.GetProcessesByName("emulationstation").Length > 0)
                return;

            try
            {
                if (Retroboat.Bios.EsBiosCheck.TurnOffOnce(install, settings, settingsPath))
                    Log.Info("Aviso de BIOS do ES desligado: o Retroboat confere a BIOS pelo conteúdo.");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao desligar o aviso de BIOS do ES", ex);
            }
        }

        /// <summary>
        /// Sincroniza com a nuvem antes do ES ler os arquivos. A janela de progresso só aparece se demorar.
        /// Falha na sincronização nunca impede de jogar.
        /// </summary>
        static StartupSyncReport RunStartupSync(RetroBatInstall install, RetroboatSettings settings, string leadingMessage)
        {
            StartupSyncReport report = null;
            Exception error = null;

            var worker = new Thread(() =>
            {
                try { report = SyncWithCloud(install, settings, leadingMessage); }
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

            LogReport(report);
            return report;
        }

        /// <summary>
        /// "Atualizar listas de jogos" do ES (script do evento update-gamelists, ver <see cref="EsScripts"/>): a mesma
        /// sincronização da abertura, sem janela por cima do ES, que espera este processo antes de reler as listas. Jogos
        /// que chegaram ao Drive com o ES aberto ganham o marcador e aparecem na releitura. Falha aqui só vai para o log:
        /// o ES relê as listas do mesmo jeito.
        /// </summary>
        static int Refresh(RetroBatInstall install, RetroboatSettings settings)
        {
            if (string.IsNullOrEmpty(settings.RomsPath))
            {
                Log.Info("Atualizar listas de jogos: sem pasta de jogos configurada, nada a sincronizar.");
                return 0;
            }

            try
            {
                Log.Info("Atualizar listas de jogos no ES: sincronizando com a nuvem.");

                // Um sistema novo na nuvem precisa do retroboat-launch antes de o ES reler os sistemas.
                ApplyLibrary(install, settings.RomsPath, CreateCommandWrapper(install, settings));

                var report = SyncWithCloud(install, settings);
                LogReport(report);

                // O ES está parado esperando o script: a notificação aparece quando ele terminar de reler as listas.
                if (!EsApi.Notify(string.Join("\n", report.Messages)))
                    Log.Warn("Não foi possível mostrar a notificação no ES.");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao sincronizar pelo Atualizar listas de jogos do ES", ex);
            }

            return 0;
        }

        /// <summary>
        /// Abertura, CloudWatcher e o "Atualizar listas de jogos" do ES (outro processo) podem querer sincronizar ao mesmo
        /// tempo: um de cada vez, senão dois mexem nos mesmos memory cards, configurações do Drive, índice e gamelists.
        /// </summary>
        const string SyncMutexName = @"Local\RetroboatSync";
        static readonly TimeSpan SyncWait = TimeSpan.FromMinutes(2);

        internal static StartupSyncReport SyncWithCloud(RetroBatInstall install, RetroboatSettings settings, string leadingMessage = null)
        {
            using (var mutex = new Mutex(false, SyncMutexName))
            {
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(SyncWait);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true; // Quem sincronizava foi encerrado à força; a vez é desta.
                }

                if (!acquired)
                    throw new TimeoutException("outra sincronização com a nuvem não terminou em " + SyncWait.TotalMinutes + " minutos");

                try
                {
                    // Com o Drive conectando com o ES aberto (CloudWatcher), a biblioteca pode ser adotada só agora.
                    ChooseLibrary(install, settings, out var adopted);
                    var media = new Retroboat.Media.CloudMediaOptions { Videos = settings.CloudVideos, Manuals = settings.CloudManuals, Automatic = settings.AutoMedia };
                    var report = StartupSync.Run(install, settings.CloudPath, settings.RomsPath, media, libraryId: settings.CloudLibrary);
                    foreach (var message in new[] { adopted, leadingMessage }.Where(m => m != null))
                        report.Messages.Insert(0, message);

                    return report;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// RB-021: com uma biblioteca só no Drive, este PC passa a usá-la pelo id, sem perguntar. Nos outros casos nada é
        /// escrito no Drive e a escolha fica para o setup. Falha aqui nunca impede de jogar.
        /// </summary>
        /// <param name="adopted">A frase para a notificação do ES quando adotou agora; senão null.</param>
        /// <returns>null se falhou.</returns>
        static LibraryChoiceResult ChooseLibrary(RetroBatInstall install, RetroboatSettings settings, out string adopted)
        {
            adopted = null;
            try
            {
                var choice = LibraryChoice.ChooseAutomatically(settings);
                switch (choice.Outcome)
                {
                    case LibraryChoiceOutcome.Adopted:
                        settings.Save(RetroboatSettings.GetPath(install));
                        Log.Info("Biblioteca da nuvem adotada: " + choice.Drive.RetroboatPath + " (id " + choice.Identity.Id +
                                 ", criada por " + (choice.Identity.Pc ?? "?") + ")");
                        adopted = "Biblioteca da nuvem: " + choice.Drive.DisplayName;
                        break;

                    case LibraryChoiceOutcome.Several:
                        Log.Warn("Pasta do Retroboat em mais de uma conta do Google Drive (" + string.Join(", ", choice.Candidates.Select(c => c.DisplayName)) +
                                 "): o setup pergunta qual usar.");
                        break;

                    case LibraryChoiceOutcome.Unidentified:
                        Log.Warn("Biblioteca da nuvem não identificada: " + choice.Error);
                        break;
                }

                return choice;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao escolher a biblioteca da nuvem", ex);
                return null;
            }
        }

        internal static void LogReport(StartupSyncReport report)
        {
            foreach (var message in report.Messages)
            {
                if (message.StartsWith("Atenção", StringComparison.Ordinal) || message.StartsWith("Nuvem: Google Drive", StringComparison.Ordinal))
                    Log.Warn(message);
                else
                    Log.Info(message);
            }

            if (report.Folders != null)
            {
                if (report.Folders.SystemsCreated.Count > 0)
                    Log.Info("Pastas de console criadas no Drive: " + string.Join(", ", report.Folders.SystemsCreated));
                foreach (var problem in report.Folders.Errors) Log.Warn("Pasta no Drive: " + problem);
            }

            if (report.Bios != null)
            {
                foreach (var file in report.Bios.Organized) Log.Info("BIOS organizada no Drive: " + file);
                foreach (var file in report.Bios.Downloaded) Log.Info("BIOS baixada: " + file);
                foreach (var file in report.Bios.Uploaded) Log.Info("BIOS enviada: " + file);
                foreach (var problem in report.Bios.Errors) Log.Warn("BIOS: " + problem);
            }

            if (report.Relocation != null && report.Relocation.FilesMoved > 0)
                Log.Info("Mídia movida para fora da pasta de jogos: " + report.Relocation.FilesMoved + " arquivo(s) (" + string.Join(", ", report.Relocation.Systems) + ")");

            if (report.AutoMedia != null)
            {
                Log.Info(string.Format("Capas automáticas (libretro): {0} jogo(s), {1} imagem(ns) baixada(s)", report.AutoMedia.GamesFilled, report.AutoMedia.FilesDownloaded));
                foreach (var game in report.AutoMedia.NotFound) Log.Info("Capa automática não encontrada: " + game);
                foreach (var problem in report.AutoMedia.Errors) Log.Warn("Capas automáticas: " + problem);
            }

            if (report.AutoInfo != null)
            {
                Log.Info(string.Format("Informações automáticas (libretro, Wikidata, Wikipédia): {0} jogo(s)", report.AutoInfo.GamesFilled));
                foreach (var game in report.AutoInfo.NotFound) Log.Info("Informações automáticas não encontradas: " + game);
                foreach (var problem in report.AutoInfo.Errors) Log.Warn("Informações automáticas: " + problem);
            }

            if (report.Media != null)
            {
                Log.Info(string.Format("Capas e descrições: {0} jogo(s) enviados ({1} arquivos), {2} recebidos ({3} arquivos)",
                    report.Media.GamesShared, report.Media.FilesUploaded, report.Media.GamesFilled, report.Media.FilesDownloaded));
                foreach (var problem in report.Media.Errors) Log.Warn("Mídia: " + problem);
            }

            if (report.Saves != null)
            {
                foreach (var item in report.Saves.Uploaded) Log.Info("Memory card enviado (sessão anterior): " + item);
                foreach (var item in report.Saves.Downloaded) Log.Info("Memory card baixado: " + item);
                foreach (var item in report.Saves.DeletedLocal) Log.Info("Memory card apagado em outro PC, apagado aqui (cópia no histórico): " + item);
                foreach (var item in report.Saves.Conflicts) Log.Warn("Memory card com conflito, decidir ao abrir um jogo: " + item);
                foreach (var item in report.Saves.Waiting) Log.Info("Memory card aguardando a versão de outro PC: " + item);
                foreach (var item in report.Saves.Repaired) Log.Warn("Memory card consertado: " + item);
                foreach (var item in report.Saves.DamagedHere.Concat(report.Saves.DamagedInCloud)) Log.Warn("Memory card: " + item);
                foreach (var item in report.Saves.BlankNew) Log.Info("Memory card em branco, ainda sem save (vai para a nuvem no primeiro save): " + item);
                foreach (var item in report.Saves.Skipped) Log.Warn("Memory card não sincronizado: " + item);
                foreach (var problem in report.Saves.Errors) Log.Warn("Memory cards: " + problem);
            }

            if (report.Library != null)
            {
                foreach (var game in report.Library.PlaceholdersCreated) Log.Info("Jogo da nuvem no ES (marcador): " + game);
                foreach (var game in report.Library.PlaceholdersRemoved) Log.Info("Saiu da nuvem, marcador removido: " + game);
            }
        }

        /// <summary>O lançador fica em segundo plano, sem janela, até o ES subir (a intro do RetroBat leva alguns segundos).</summary>
        static bool WaitForEs()
        {
            if (EsApi.WaitUntilRunning(TimeSpan.FromMinutes(3)))
                return true;

            Log.Warn("O ES não respondeu em 3 minutos; o resultado da sincronização ficou só no log.");
            return false;
        }

        /// <summary>Mostra o resultado da sincronização como notificação do ES, pela API local dele.</summary>
        static void NotifyEs(StartupSyncReport report)
        {
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
                var result = ApplyLibrary(install, romsPath, wrapper);
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

        internal static ApplyResult ApplyLibrary(RetroBatInstall install, string romsPath, CommandWrapper wrapper)
        {
            var result = new IsolatedLibrary(install).Apply(romsPath, wrapper: wrapper);
            Log.Info(string.Format("Biblioteca: {0} sistemas, overlay {1}, emulatorLauncher.cfg {2}, {3} gamelists criados, movidos: {4}",
                result.SystemsMapped,
                result.OverlayWritten ? "gravado" : result.OverlayRemoved ? "removido" : "sem mudança",
                result.LauncherConfigUpdated ? "atualizado" : "sem mudança",
                result.GamelistsCreated,
                result.GamelistsMoved.Count == 0 ? "nenhum" : string.Join(", ", result.GamelistsMoved)));

            foreach (var warning in result.Warnings)
                Log.Warn(warning);

            return result;
        }

        /// <summary>O ES do Retroboat recria o es-nativo ao abrir. Com um ES já aberto, o arquivo é dele e fica.</summary>
        static void ClearNativeEsFlag(RetroBatInstall install)
        {
            try
            {
                var running = Process.GetProcessesByName("emulationstation");
                foreach (var process in running)
                    process.Dispose();

                if (running.Length == 0)
                    CloudStatusFile.ClearNativeEs(install);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Não foi possível apagar o sinal do ES do Retroboat: " + ex.Message);
            }
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
                    case "--refresh": own.Refresh = true; break;
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
            public bool Refresh;
            public string RetroBatPath;
        }
    }
}
