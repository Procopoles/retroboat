using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Launch
{
    /// <summary>
    /// retroboat-launch.exe: fica na frente do emulatorLauncher no &lt;command&gt; dos sistemas que têm
    /// jogos na nuvem (overlay es_systems_retroboat.cfg). Uso: retroboat-launch "&lt;programa&gt;" &lt;argumentos&gt;.
    ///
    /// Se o -rom é um marcador de jogo que só está na nuvem, baixa o jogo com uma tela de progresso e
    /// depois abre o programa. Em qualquer outro caso, abre o programa na hora, com os mesmos argumentos.
    /// Devolve ao ES o código de saída do programa.
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
            if (install != null)
                Log.Init(Path.Combine(install.RetroboatDataPath, "logs", "launch.log"));

            var rom = CommandLine.GetOption(args, "-rom");
            if (install != null && rom != null && !InstallIfInCloud(install, rom))
                return 1; // Download cancelado ou com erro: volta para o ES sem abrir o emulador.

            return Run(target, targetArguments);
        }

        /// <returns>false se o jogo precisava ser baixado e não foi.</returns>
        static bool InstallIfInCloud(RetroBatInstall install, string rom)
        {
            try
            {
                var installer = new OnDemandInstaller(install);
                var entry = installer.FindPending(rom);
                if (entry == null)
                    return true;

                Log.Info("Baixando da nuvem antes de abrir: " + entry.System + "/" + entry.CloudName);
                Application.EnableVisualStyles();
                using (var form = new DownloadForm(installer, entry))
                {
                    Application.Run(form);
                    var result = form.Result;
                    Log.Info("Resultado: " + (result?.Outcome.ToString() ?? "janela fechada") + (result?.Message != null ? " (" + result.Message + ")" : ""));
                    return result != null && result.Outcome == OnDemandOutcome.Installed;
                }
            }
            catch (Exception ex)
            {
                // Se o Retroboat falhar aqui, o jogo segue para o emulador como antes.
                Log.Error("Falha ao verificar o jogo na nuvem", ex);
                return true;
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
