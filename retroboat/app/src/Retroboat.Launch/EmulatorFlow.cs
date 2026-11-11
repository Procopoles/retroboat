using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Emulators;
using Retroboat.Library;
using Retroboat.Saves;
using Retroboat.Text;

namespace Retroboat.Launch
{
    /// <summary>
    /// RB-033: o emulador do jogo, antes e depois de abrir. Antes: se falta, pergunta e baixa (com andamento); se o repositório
    /// tem versão nova, atualiza sem perguntar; e confere a compatibilidade. Depois: confere o que o emulador gravou durante o
    /// jogo. Um erro do Retroboat aqui nunca impede o jogo: o emulatorLauncher segue como antes.
    /// </summary>
    sealed class EmulatorFlow
    {
        const string ScreenTitle = "Emulador";
        const string WarningTitle = "Aviso do Retroboat";

        readonly RetroBatInstall _install;
        readonly GameWatch _watch;

        EmulatorFlow(RetroBatInstall install, GameWatch watch)
        {
            _install = install;
            _watch = watch;
        }

        /// <summary>
        /// Antes do jogo. <paramref name="openGame"/> falso: o usuário preferiu não baixar ou voltar. Nulo quando não há o que
        /// acompanhar depois (emulador fora do repositório do RetroBat, ou uma falha do Retroboat).
        /// </summary>
        public static EmulatorFlow Before(RetroBatInstall install, string[] args, out bool openGame)
        {
            openGame = true;
            try
            {
                var choice = EmulatorChoice.FromArguments(args);
                var package = choice == null ? null : EmulatorCatalog.For(install, choice.Emulator, choice.Core);
                if (package == null)
                    return null;

                var server = new RepositoryCache(install, new RetroBatRepository());
                var status = PackageStatus.Of(install, package, server);
                EmulatorLog.Info(install, "Antes do jogo (" + choice + "): " + status);

                if (status.State == PackageState.NotInstalled)
                {
                    if (!InstallMissing(install, server, ref status))
                    {
                        openGame = false;
                        return null;
                    }

                    if (status.State == PackageState.NotInstalled)
                        return null; // Fora do repositório: o emulatorLauncher cuida, como antes.
                }
                else if (status.State == PackageState.UpdateAvailable && status.InRepository)
                {
                    Update(install, server, status);
                }

                var version = PackageStatus.Of(install, package, server).InstalledVersion;
                var issues = EmulatorCompatibility.CheckInstalled(install, package, version);
                var repository = EmulatorCompatibility.CheckRepository(server, package, version);
                if (repository != null)
                    issues.Add(repository);

                EmulatorCompatibility.Log(install, issues, "antes do jogo");
                if (!Warn(install, issues))
                {
                    openGame = false;
                    return null;
                }

                return new EmulatorFlow(install, EmulatorCompatibility.Watch(install, package, version, SaveRules.GetSavesPath(install)));
            }
            catch (Exception ex)
            {
                EmulatorLog.Error(install, "Falha ao conferir o emulador antes do jogo; o jogo segue como antes", ex);
                return null;
            }
        }

        /// <summary>Depois do jogo: o que o emulador gravou fora das pastas que vão para a nuvem, e onde ficaram as configurações.</summary>
        public void AfterGame()
        {
            try
            {
                var issues = EmulatorCompatibility.CheckAfterGame(_watch);
                EmulatorCompatibility.Log(_install, issues, "depois do jogo");
                Warn(_install, issues);
            }
            catch (Exception ex)
            {
                EmulatorLog.Error(_install, "Falha ao conferir o emulador depois do jogo", ex);
            }
        }

        /// <summary>O emulador não está no PC: pergunta, baixa e confere. Falso se o jogo não deve abrir.</summary>
        static bool InstallMissing(RetroBatInstall install, RepositoryCache server, ref PackageStatus status)
        {
            var name = status.Package.DisplayName;
            while (true)
            {
                if (!status.InRepository)
                {
                    if (status.Problem == null)
                    {
                        EmulatorLog.Warn(install, name + " não está no repositório do RetroBat; o emulatorLauncher segue como antes.");
                        return true;
                    }

                    int retry = ChoiceForm.Ask(ScreenTitle, "Este jogo precisa do " + name,
                        "O " + name + " ainda não está neste PC, e não deu para falar com o repositório do RetroBat agora.\r\n\r\n" + Sentence(status.Problem),
                        new[] { "Tentar de novo", "Voltar" });
                    if (retry != 0)
                        return false;

                    server.RetryNow();
                    status = PackageStatus.Of(install, status.Package, server);
                    if (status.State != PackageState.NotInstalled)
                        return true;

                    continue;
                }

                var size = status.DownloadBytes > 0 ? "São " + Format.Bytes(status.DownloadBytes) + ", do repositório do RetroBat" : "Ele vem do repositório do RetroBat";
                int answer = ChoiceForm.Ask(ScreenTitle, "Este jogo precisa do " + name,
                    "O " + name + " ainda não está neste PC. " + size + "; o jogo abre assim que terminar.",
                    new[] { "Baixar e jogar", "Agora não" });
                if (answer != 0)
                {
                    EmulatorLog.Info(install, name + ": o usuário preferiu não baixar agora.");
                    return false;
                }

                var result = Download(install, server, status, "Baixando o emulador", name, out bool cancelled);
                if (result != null && result.Installed)
                {
                    status = PackageStatus.Of(install, status.Package, server);
                    return true;
                }

                if (cancelled || (result != null && result.Cancelled))
                    return false;

                int again = ChoiceForm.Ask(ScreenTitle, "Não deu para baixar o " + name, Sentence(result?.Error ?? "erro desconhecido"),
                    new[] { "Tentar de novo", "Voltar" });
                if (again != 0)
                    return false;

                server.RetryNow();
                status = PackageStatus.Of(install, status.Package, server);
                if (status.State != PackageState.NotInstalled)
                    return true;
            }
        }

        /// <summary>Versão nova no repositório: atualiza antes do jogo, sem perguntar (RB-033). Cancelar ou falhar abre com a atual.</summary>
        static void Update(RetroBatInstall install, RepositoryCache server, PackageStatus status)
        {
            var heading = status.Package.DisplayName + (status.InstalledVersion.Length > 0 && status.AvailableVersion.Length > 0
                ? " " + status.InstalledVersion + " → " + status.AvailableVersion : "");
            var result = Download(install, server, status, "Atualizando o emulador", heading, out bool cancelled);
            if (result != null && result.Installed)
                return;

            EmulatorLog.Warn(install, status.Package.DisplayName + ": " + (cancelled || (result?.Cancelled ?? false) ? "atualização cancelada" : "atualização falhou (" + result?.Error + ")")
                + "; o jogo abre com a versão " + status.InstalledVersion + ".");
        }

        static EmulatorInstallResult Download(RetroBatInstall install, RepositoryCache server, PackageStatus status, string title, string heading, out bool cancelled)
        {
            return ProgressForm.Run(title, heading, (progress, token) => new EmulatorInstaller(install, server).Install(status, progress, token), out cancelled);
        }

        /// <summary>
        /// Mostra os problemas que ainda não apareceram (uma vez por problema e versão). Um que impede o jogo aparece sempre e
        /// pergunta. Falso se o usuário preferiu voltar.
        /// </summary>
        static bool Warn(RetroBatInstall install, IReadOnlyList<CompatIssue> issues)
        {
            if (issues == null || issues.Count == 0)
                return true;

            var blocking = issues.Where(i => i.Impact == CompatImpact.Blocking).ToList();
            if (blocking.Count > 0)
            {
                int answer = ChoiceForm.Ask(WarningTitle, EmulatorCompatibility.Heading(blocking), EmulatorCompatibility.UserText(blocking),
                    new[] { "Voltar", "Abrir mesmo assim" });
                if (answer != 1)
                    return false;
            }

            var fresh = EmulatorCompatibility.TakeNotShown(install, issues.Where(i => i.Impact != CompatImpact.Blocking).ToList());
            if (fresh.Count > 0)
                ChoiceForm.Ask(WarningTitle, EmulatorCompatibility.Heading(fresh), EmulatorCompatibility.UserText(fresh), new[] { "Entendi" });

            return true;
        }

        static string Sentence(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return "";

            text = char.ToUpperInvariant(text[0]) + text.Substring(1);
            return text.EndsWith(".", StringComparison.Ordinal) ? text : text + ".";
        }
    }
}
