using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Retroboat.Library;
using Retroboat.Logging;

namespace Retroboat.Config
{
    /// <summary>
    /// Quando as configurações vão e vêm da nuvem. Cada arquivo só é gravado com o programa dono dele fechado: o ES
    /// regrava o es_settings.cfg e o es_input.cfg, e o emulador regrava os .ini dele ao sair. Com o dono aberto, a
    /// fonte só envia (ConfigSyncMode.SendOnly), e o que veio da nuvem espera.
    /// <list type="bullet">
    /// <item>ao abrir o Retroboat (ES ainda fechado): tudo;</item>
    /// <item>antes do jogo: o emulador recebe e envia (ele ainda não abriu); o ES só envia o que mudou nos menus;</item>
    /// <item>depois do jogo: o emulador envia o que mudou nos menus dele;</item>
    /// <item>quando o ES fecha: o ES recebe e envia, e o que chegou vale na próxima abertura.</item>
    /// </list>
    /// </summary>
    public static class CloudConfigs
    {
        public static IReadOnlyList<IConfigSource> EsSources(RetroBatInstall install) =>
            new IConfigSource[] { new EsSettingsConfig(install), new EsInputConfig(install) };

        public static IReadOnlyList<EmulatorConfigSource> EmulatorSources(RetroBatInstall install, string system = null, string emulator = null) =>
            (system == null ? EmulatorConfigRules.All : EmulatorConfigRules.For(system, emulator)).Select(r => new EmulatorConfigSource(install, r)).ToList();

        /// <summary>Todas as fontes, para o CLI.</summary>
        public static IReadOnlyList<IConfigSource> AllSources(RetroBatInstall install) =>
            EsSources(install).Concat(EmulatorSources(install)).ToList();

        /// <summary>Ao abrir o Retroboat, quando o Drive conecta com o ES aberto e quando o ES fecha.</summary>
        public static List<ConfigSyncResult> SyncAll(RetroBatInstall install, string cloudRetroboatPath, string pcName = null)
        {
            var esMode = IsEsRunning() ? ConfigSyncMode.SendOnly : ConfigSyncMode.Full;
            var results = EsSources(install).Select(s => Run(install, cloudRetroboatPath, s, esMode, pcName)).ToList();
            results.AddRange(EmulatorSources(install).Select(s => Run(install, cloudRetroboatPath, s, s.IsRunning() ? ConfigSyncMode.SendOnly : ConfigSyncMode.Full, pcName)));
            return results;
        }

        /// <summary>Antes do jogo, com o emulador ainda fechado: o que o emulador vai ler já vem da nuvem.</summary>
        public static List<ConfigSyncResult> BeforeGame(RetroBatInstall install, string cloudRetroboatPath, string system, string emulator, string pcName = null) =>
            EmulatorSources(install, system, emulator)
                .Select(s => Run(install, cloudRetroboatPath, s, s.IsRunning() ? ConfigSyncMode.SendOnly : ConfigSyncMode.Full, pcName))
                .ToList();

        /// <summary>O que mudou nos menus do ES até o jogo abrir. Não grava nada neste PC, então pode rodar junto com o jogo.</summary>
        public static List<ConfigSyncResult> SendEsChanges(RetroBatInstall install, string cloudRetroboatPath, string pcName = null) =>
            EsSources(install).Select(s => Run(install, cloudRetroboatPath, s, IsEsRunning() ? ConfigSyncMode.SendOnly : ConfigSyncMode.Full, pcName)).ToList();

        /// <summary>Depois do jogo: o que a pessoa mudou nos menus do emulador vai para a nuvem.</summary>
        public static List<ConfigSyncResult> AfterGame(RetroBatInstall install, string cloudRetroboatPath, string system, string emulator, string pcName = null) =>
            BeforeGame(install, cloudRetroboatPath, system, emulator, pcName);

        static ConfigSyncResult Run(RetroBatInstall install, string cloudRetroboatPath, IConfigSource source, ConfigSyncMode mode, string pcName)
        {
            ConfigSyncResult result;
            try
            {
                result = new ConfigSync(source, install.RetroboatDataPath, cloudRetroboatPath, pcName).Sync(mode);
            }
            catch (Exception ex)
            {
                // Qualquer falha numa fonte fica nela: a biblioteca, os memory cards e o jogo seguem.
                result = new ConfigSyncResult(source.DisplayName);
                result.Errors.Add(source.DisplayName + ": " + ex.Message);
                Log.Error("Falha ao sincronizar as configurações (" + source.DisplayName + ")", ex);
            }

            LogResult(result, mode);
            return result;
        }

        static void LogResult(ConfigSyncResult result, ConfigSyncMode mode)
        {
            if (result.Skipped)
                return;

            if (result.Received.Count > 0)
                Log.Info("Configurações (" + result.Source + "): " + result.Received.Count + " da nuvem neste PC: " + Sample(result.Received));
            if (result.Sent.Count > 0)
                Log.Info("Configurações (" + result.Source + "): " + result.Sent.Count + " enviada(s) para a nuvem: " + Sample(result.Sent));
            if (result.Pending.Count > 0)
                Log.Info("Configurações (" + result.Source + "): " + result.Pending.Count + " da nuvem esperando o programa fechar: " + Sample(result.Pending));
            foreach (var conflict in result.Conflicts)
                Log.Info("Configurações (" + result.Source + "): " + conflict);
            foreach (var error in result.Errors)
                Log.Warn("Configurações: " + error);
        }

        static string Sample(List<string> ids)
        {
            const int shown = 15;
            return string.Join(", ", ids.Take(shown)) + (ids.Count > shown ? " e mais " + (ids.Count - shown) : "");
        }

        /// <summary>Frases curtas para a notificação do ES. Nada quando nada mudou.</summary>
        public static IEnumerable<string> Describe(IEnumerable<ConfigSyncResult> results)
        {
            var list = results.Where(r => !r.Skipped).ToList();
            var received = list.Where(r => r.Received.Count > 0).Select(r => r.Source + " (" + r.Received.Count + ")").ToList();
            var sent = list.Where(r => r.Sent.Count > 0).Select(r => r.Source + " (" + r.Sent.Count + ")").ToList();

            if (received.Count > 0)
                yield return "Configurações da nuvem neste PC: " + string.Join(", ", received);
            if (sent.Count > 0)
                yield return "Configurações enviadas para a nuvem: " + string.Join(", ", sent);

            var failed = list.Where(r => r.Errors.Count > 0).Select(r => r.Source).ToList();
            if (failed.Count > 0)
                yield return "Atenção: problema ao sincronizar as configurações (" + string.Join(", ", failed) + "). Detalhes no log do Retroboat.";
        }

        public static bool HasErrors(IEnumerable<ConfigSyncResult> results) => results.Any(r => r.Errors.Count > 0);

        public static bool IsEsRunning()
        {
            var processes = Process.GetProcessesByName("emulationstation");
            foreach (var process in processes)
                process.Dispose();

            return processes.Length > 0;
        }
    }
}
