using System;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Config;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Settings;

namespace Retroboat.Launch
{
    /// <summary>
    /// Configurações na nuvem em volta de uma partida:
    /// <list type="bullet">
    /// <item>antes: o emulador recebe da nuvem o que mudou em outro PC (ele ainda está fechado) e envia o que mudou aqui;</item>
    /// <item>durante: as opções do ES e os controles mudados nos menus vão para a nuvem, em segundo plano;</item>
    /// <item>depois: o que a pessoa mudou nos menus do emulador vai para a nuvem.</item>
    /// </list>
    /// Sem o Drive, ou se algo falhar aqui, o jogo abre como está: o Retroboat nunca impede de jogar. Não pergunta
    /// nada: quem pergunta sobre o Drive fechado é o BAIXAR e o jogo com memory card.
    /// </summary>
    static class ConfigFlow
    {
        static readonly TimeSpan EsSendWait = TimeSpan.FromSeconds(30);

        public static int Around(RetroBatInstall install, string[] args, Func<int> runEmulator)
        {
            var cloudPath = CloudPath(install);
            if (cloudPath == null)
                return runEmulator();

            var system = CommandLine.GetOption(args, "-system");
            var emulator = CommandLine.GetOption(args, "-emulator");

            Safe("antes do jogo", () => CloudConfigs.BeforeGame(install, cloudPath, system, emulator));

            // Só lê os arquivos do ES (ele está aberto): pode rodar junto com o jogo, sem atrasar a abertura.
            var esChanges = new Thread(() => Safe("opções do ES", () => CloudConfigs.SendEsChanges(install, cloudPath)))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            esChanges.Start();

            try
            {
                return runEmulator();
            }
            finally
            {
                esChanges.Join(EsSendWait);
                Safe("depois do jogo", () => CloudConfigs.AfterGame(install, cloudPath, system, emulator));
            }
        }

        static string CloudPath(RetroBatInstall install)
        {
            try
            {
                var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
                var cloud = CloudLocator.Locate(settings.CloudPath);
                return cloud != null && cloud.Exists ? cloud.RetroboatPath : null;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao achar a nuvem para as configurações; o jogo abre com as deste PC", ex);
                return null;
            }
        }

        static void Safe(string when, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao sincronizar as configurações (" + when + ")", ex);
            }
        }
    }
}
