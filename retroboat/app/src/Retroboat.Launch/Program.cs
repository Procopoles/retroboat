using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Downloads;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Media;
using Retroboat.Settings;
using Retroboat.Text;

namespace Retroboat.Launch
{
    /// <summary>
    /// retroboat-launch.exe: fica na frente do emulatorLauncher no &lt;command&gt; dos sistemas que têm
    /// jogos ou saves na nuvem (overlay es_systems_retroboat.cfg). Uso: retroboat-launch "&lt;programa&gt;" &lt;argumentos&gt;.
    ///
    /// Se o -rom é um marcador de jogo que só está na nuvem, oferece abrir o Google Drive se ele estiver
    /// fechado, põe o jogo na fila de downloads e volta para o ES na hora: o download segue em segundo plano
    /// (DownloadHost), e o usuário continua navegando. Se o sistema tem saves sincronizados (memory cards do
    /// PCSX2), puxa os da nuvem antes, envia cada gravação durante o jogo e o que faltar no fim (SaveFlow). As
    /// configurações do emulador vêm da nuvem antes e vão para lá depois (ConfigFlow).
    /// Depois abre o programa com os mesmos argumentos e devolve ao ES o código de saída dele.
    /// O ES do Retroboat também chama --baixar e --cancelar (A num jogo da nuvem) e --desinstalar, --verificar e --reparar
    /// (menu do jogo, LocalCopyFlow).
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            CommandLine.SplitFirst(Environment.CommandLine, out var rest);
            var target = CommandLine.SplitFirst(rest, out var targetArguments);
            if (target.Length == 0)
            {
                MessageBox.Show("Uso: retroboat-launch \"<programa>\" <argumentos>", "Retroboat", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 2;
            }

            var install = RetroBatInstall.FindFrom(AppDomain.CurrentDomain.BaseDirectory);
            bool isDownloadService = target == DownloadProcess.Argument;
            if (install != null)
                Log.Init(Path.Combine(install.RetroboatDataPath, "logs", isDownloadService ? "downloads.log" : "launch.log"));

            if (isDownloadService)
                return install != null ? DownloadHost.Run(install) : 1;

            Application.EnableVisualStyles();
            try
            {
                return Handle(install, target, targetArguments, args);
            }
            finally
            {
                Theme.ReturnFocusToEs();
            }
        }

        static int Handle(RetroBatInstall install, string target, string targetArguments, string[] args)
        {
            // Pedidos do ES do Retroboat (RB-010), que atende o A num jogo da nuvem sem abrir o jogo.
            if (target == DownloadVerb || target == CancelVerb)
                return install != null ? FromNativeEs(install, target, CommandLine.SplitFirst(targetArguments, out _)) : 1;

            // Menu do jogo no ES do Retroboat: pausar ou retomar a fila inteira, como na Steam.
            if (target == PauseVerb || target == ResumeVerb)
                return install != null ? PauseQueue(install, target == PauseVerb) : 1;

            // Menu do jogo no ES do Retroboat: desinstalar, verificar e reparar a cópia deste PC.
            if (LocalCopyFlow.Handles(target))
                return install != null ? LocalCopyFlow.Run(install, target, CommandLine.SplitFirst(targetArguments, out _), ExeFolder) : 1;

            var rom = CommandLine.GetOption(args, "-rom");
            if (install != null && rom != null && !ContinueToGame(install, rom))
                return 1; // Código diferente de zero: o ES não conta como partida e não mostra erro.

            // Uma fila que ficou de uma sessão anterior continua, mesmo com o RetroBat aberto sem o Retroboat.exe.
            if (install != null)
                DownloadProcess.EnsureRunning(install, ExeFolder);

            // Memory cards: puxa antes, envia cada gravação durante o jogo e o que faltar no fim.
            var saves = install != null ? SaveFlow.Before(install, args, rom) : null;
            if (saves != null && saves.Cancelled)
                return 1;

            // Jogo compactado: a resposta à pergunta "manter descompactado?" do emulatorLauncher vale para os próximos jogos.
            var uncompressed = install != null ? Uncompressed(install, args, rom) : null;
            if (uncompressed?.Argument != null)
                targetArguments += " " + uncompressed.Argument;

            Func<int> run = () => Run(target, targetArguments);
            if (uncompressed != null && uncompressed.Learns)
            {
                var game = run;
                run = () =>
                {
                    int code = uncompressed.Around(game, out var learned);
                    if (learned.HasValue)
                        Log.Info("Jogo descompactado: resposta guardada (" + (learned.Value ? "manter" : "apagar") + "); o RetroBat não pergunta mais neste PC.");
                    return code;
                };
            }

            // Configurações: o emulador abre com as da nuvem, e o que mudou nos menus do ES e do emulador vai para lá.
            if (install != null)
            {
                var emulator = run;
                run = () => ConfigFlow.Around(install, args, emulator);
            }

            return saves == null ? run() : saves.During(run);
        }

        static string ExeFolder => AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

        const string DownloadVerb = "--baixar";
        const string CancelVerb = "--cancelar";
        const string PauseVerb = "--pausar";
        const string ResumeVerb = "--retomar";

        /// <summary>
        /// retroboat-launch --pausar|--retomar: a fila inteira para ou continua. O serviço percebe sozinho; ao retomar,
        /// ele é aberto se tiver fechado (o ES reabriu, por exemplo). O argumento do jogo, que o ES manda junto, não importa.
        /// 0 = mudou; 1 = já estava assim ou a fila está vazia.
        /// </summary>
        static int PauseQueue(RetroBatInstall install, bool pause)
        {
            var queue = new DownloadQueue(install);
            bool changed = queue.SetPaused(pause);
            Log.Info(changed
                ? (pause ? "Fila de downloads pausada pelo ES." : "Fila de downloads retomada pelo ES.")
                : (pause ? "Pausar pelo ES, mas a fila já estava pausada ou vazia." : "Retomar pelo ES, mas a fila não estava pausada."));

            if (!pause)
                DownloadProcess.EnsureRunning(install, ExeFolder);

            return changed ? 0 : 1;
        }

        /// <summary>
        /// retroboat-launch --baixar|--cancelar "&lt;jogo&gt;": o ES do Retroboat (RB-010) pede isto ao apertar A num jogo
        /// da nuvem, sem passar pela abertura de jogo. A pergunta do Drive, a fila e o serviço são os mesmos do BAIXAR
        /// pelo emulatorLauncher. 0 = feito; 1 = nada mudou (não é da nuvem, "Agora não", não estava na fila).
        /// </summary>
        static int FromNativeEs(RetroBatInstall install, string verb, string rom)
        {
            try
            {
                var entry = new OnDemandInstaller(install).FindPending(rom);
                if (entry == null)
                {
                    Log.Warn("Pedido do ES (" + verb + ") para um jogo que não está só na nuvem: " + rom);
                    return 1;
                }

                var queue = new DownloadQueue(install);
                var key = entry.System + "/" + entry.CloudName;
                if (verb == CancelVerb)
                {
                    bool removed = queue.Remove(key);
                    Log.Info((removed ? "Download cancelado pelo ES: " : "Cancelar pelo ES, mas o jogo não estava na fila: ") + key);
                    return removed ? 0 : 1;
                }

                if (!queue.Contains(key))
                    StartDownload(install, queue, entry);

                if (!queue.Contains(key))
                    return 1;

                DownloadProcess.EnsureRunning(install, ExeFolder);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("Falha no pedido do ES (" + verb + ")", ex);
                return 1;
            }
        }

        /// <returns>false se o jogo é da nuvem: o BAIXAR foi atendido e o ES volta sem abrir o emulador.</returns>
        static bool ContinueToGame(RetroBatInstall install, string rom)
        {
            try
            {
                var entry = new OnDemandInstaller(install).FindPending(rom);
                if (entry == null)
                    return true;

                var queue = new DownloadQueue(install);
                var queued = queue.Find(entry.System + "/" + entry.CloudName);
                if (queued != null)
                    AskAboutDownload(queue, queued);
                else
                    StartDownload(install, queue, entry);

                DownloadProcess.EnsureRunning(install, ExeFolder);
                return false;
            }
            catch (Exception ex)
            {
                // Se o Retroboat falhar aqui, o jogo segue para o emulador como antes.
                Log.Error("Falha ao verificar o jogo na nuvem", ex);
                return true;
            }
        }

        /// <summary>Com o Drive fechado, pergunta se abre. Se ele não conectar, o jogo entra na fila e espera por ele.</summary>
        internal static void StartDownload(RetroBatInstall install, DownloadQueue queue, CloudIndexEntry entry)
        {
            var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
            if (CloudAppPrompt.Ensure(install, settings, CloudNeed.Download) == CloudAppAnswer.Declined)
                return;

            queue.Add(new DownloadItem
            {
                System = entry.System,
                CloudName = entry.CloudName,
                LocalName = entry.LocalName,
                Title = GameTitle(install, entry)
            });

            Log.Info("Na fila de downloads: " + entry.System + "/" + entry.CloudName);
        }

        /// <summary>O jogo já está baixando e o usuário apertou de novo: é aqui que dá para pausar a fila ou cancelar o jogo.</summary>
        static void AskAboutDownload(DownloadQueue queue, DownloadItem item)
        {
            bool isCurrent = queue.Items(out var paused).FirstOrDefault()?.Key == item.Key;
            string text = paused ? "Os downloads estão pausados. Este jogo continua de onde parou quando você retomar."
                : isCurrent ? "O download continua enquanto você navega." : "Este jogo está na fila e baixa depois dos que vêm antes dele.";
            if (queue.TryReadProgress(item, out long done, out long total) && total > 0)
                text = Format.Percent(done, total) + "   ·   " + Format.Bytes(done) + " de " + Format.Bytes(total) + "\r\n\r\n" + text;

            if (paused)
            {
                int choice = ChoiceForm.Ask("Downloads pausados", item.Title, text, new[] { "Retomar os downloads", "Deixar pausado", "Cancelar o download" });
                if (choice == 0)
                    queue.SetPaused(false);
                else if (choice == 2)
                    queue.Remove(item.Key);
                return;
            }

            int answer = ChoiceForm.Ask("Baixando da nuvem", item.Title, text, new[] { "Continuar baixando", "Pausar os downloads", "Cancelar o download" });
            if (answer == 1)
                queue.SetPaused(true);
            else if (answer == 2)
                queue.Remove(item.Key);
        }

        /// <summary>O nome que o usuário vê no ES; sem gamelist, o do arquivo.</summary>
        internal static string GameTitle(RetroBatInstall install, CloudIndexEntry entry)
        {
            try
            {
                var systems = GameMedia.SystemNamesByFolder(install);
                var systemName = systems.TryGetValue(entry.System, out var name) ? name : entry.System;
                var path = Path.Combine(install.GamelistsPath, systemName, "gamelist.xml");
                if (File.Exists(path))
                {
                    var title = (string)CloudLibrarySync.FindGame(XDocument.Load(path), entry.LocalName)?.Element("name");
                    if (!string.IsNullOrWhiteSpace(title))
                        return title;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                Log.Warn("Nome do jogo não encontrado no gamelist: " + ex.Message);
            }

            return Path.GetFileNameWithoutExtension(entry.LocalName);
        }

        /// <summary>Falha aqui nunca impede de jogar: o emulatorLauncher só volta a perguntar.</summary>
        static UncompressedGame Uncompressed(RetroBatInstall install, string[] args, string rom)
        {
            try
            {
                return UncompressedGame.For(install, RetroboatSettings.Load(RetroboatSettings.GetPath(install)), CommandLine.GetOption(args, "-system"), rom);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ver a resposta sobre jogos descompactados", ex);
                return null;
            }
        }

        static int Run(string target, string arguments)
        {
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(target, arguments) { UseShellExecute = false }))
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao abrir " + target, ex);
                return 1;
            }
        }
    }
}
