using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Emulators;
using Retroboat.Library;
using Retroboat.Settings;
using Retroboat.Text;

namespace Retroboat.Cli
{
    /// <summary>
    /// retroboat-cli emulators ... (RB-033): o mesmo que o retroboat-launch faz antes do jogo, sem o ES, para manutenção e
    /// testes. A saída diz o que o Retroboat vê; os detalhes ficam em retroboat\logs\emuladores.log.
    /// </summary>
    static class EmulatorCommands
    {
        public const string Usage =
@"Emuladores (sempre a última versão do repositório do RetroBat; RB-033):
  retroboat-cli emulators status --retrobat <pasta do RetroBat> [--emulator NOME [--core CORE]]   (sem --emulator: os dos consoles com jogos)
  retroboat-cli emulators install --retrobat <pasta do RetroBat> --emulator NOME [--core CORE]   (instala ou atualiza, com a conferência)
  retroboat-cli emulators check --retrobat <pasta do RetroBat> --emulator NOME [--core CORE]   (só a verificação de compatibilidade)";

        public static int Status(RetroBatInstall install, string emulator, string core, CancellationToken token)
        {
            var server = new RepositoryCache(install, new RetroBatRepository());
            var packages = emulator != null ? new[] { (System: "", Package: Package(install, emulator, core)) } : ForActiveSystems(install);

            foreach (var (system, package) in packages)
            {
                token.ThrowIfCancellationRequested();
                var status = PackageStatus.Of(install, package, server);
                var state = status.State == PackageState.NotInstalled ? "NÃO INSTALADO"
                    : status.State == PackageState.UpdateAvailable ? "ATUALIZAÇÃO " + status.InstalledVersion + " → " + status.AvailableVersion
                    : "em dia" + (status.InstalledVersion.Length > 0 ? " · " + status.InstalledVersion : "");
                var size = status.NeedsDownload && status.DownloadBytes > 0 ? " · baixa " + Format.Bytes(status.DownloadBytes) : "";
                var repo = status.InRepository ? "" : " · fora do repositório";
                Console.WriteLine((system.Length > 0 ? system.PadRight(14) : "") + package.DisplayName + " (" + package.Key + "): " + state + size + repo);
            }

            if (server.Problem != null)
                Console.WriteLine("Repositório: " + server.Problem);

            return 0;
        }

        public static int Install(RetroBatInstall install, string emulator, string core, CancellationToken token)
        {
            var package = Package(install, emulator, core);
            var server = new RepositoryCache(install, new RetroBatRepository());
            var status = PackageStatus.Of(install, package, server);
            Console.WriteLine(status.ToString());
            if (!status.NeedsDownload)
            {
                Console.WriteLine(status.InRepository ? "Nada a baixar: já está na última versão." : "Não está no repositório do RetroBat" + (server.Problem != null ? " (" + server.Problem + ")" : "") + ".");
                return status.InRepository ? 0 : 1;
            }

            var progress = new ConsoleProgress();
            var result = new EmulatorInstaller(install, server).Install(status, progress, token);
            progress.End();

            if (result.Cancelled)
                throw new OperationCanceledException();

            if (!result.Installed)
            {
                Console.Error.WriteLine("Não instalado: " + result.Error);
                return 1;
            }

            Console.WriteLine(package.DisplayName + " " + result.Version + " instalado e conferido.");
            return PrintIssues(result.Issues);
        }

        public static int Check(RetroBatInstall install, string emulator, string core)
        {
            var package = Package(install, emulator, core);
            var server = new RepositoryCache(install, new RetroBatRepository());
            var status = PackageStatus.Of(install, package, server);
            var issues = EmulatorCompatibility.CheckInstalled(install, package, status.InstalledVersion);
            var repository = EmulatorCompatibility.CheckRepository(server, package, status.InstalledVersion);
            if (repository != null)
                issues.Add(repository);

            EmulatorCompatibility.Log(install, issues, "na verificação pelo retroboat-cli");
            return PrintIssues(issues);
        }

        static int PrintIssues(IReadOnlyList<CompatIssue> issues)
        {
            if (issues.Count == 0)
            {
                Console.WriteLine("Compatibilidade: nenhum problema.");
                return 0;
            }

            foreach (var issue in issues)
            {
                Console.WriteLine("[" + issue.Code + "] " + issue.Message);
                Console.WriteLine("    esperado: " + issue.Expected);
                Console.WriteLine("    achado:   " + issue.Found);
            }

            return 3;
        }

        static EmulatorPackage Package(RetroBatInstall install, string emulator, string core)
        {
            return EmulatorCatalog.For(install, emulator, core)
                ?? throw new InvalidOperationException(emulator + (string.IsNullOrEmpty(core) ? "" : "/" + core) + " não é um emulador do repositório do RetroBat (ou falta --core para libretro).");
        }

        /// <summary>O emulador padrão (ou escolhido no ES) de cada console com jogos neste PC.</summary>
        static IEnumerable<(string System, EmulatorPackage Package)> ForActiveSystems(RetroBatInstall install)
        {
            var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
            var roms = string.IsNullOrEmpty(settings.RomsPath) ? install.DefaultRomsPath : settings.RomsPath;
            foreach (var system in ActiveSystems.Folders(roms).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                var choice = EmulatorChoice.ForGame(install, system, null);
                var package = choice == null ? null : EmulatorCatalog.For(install, choice.Emulator, choice.Core);
                if (package != null)
                    yield return (system, package);
            }
        }
    }
}
