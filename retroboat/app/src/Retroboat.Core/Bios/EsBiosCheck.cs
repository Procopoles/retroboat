using System.IO;
using Retroboat.Accounts;
using Retroboat.Library;
using Retroboat.Settings;

namespace Retroboat.Bios
{
    /// <summary>
    /// A checagem de BIOS do ES (batocera-systems) só aceita, no PS2, o arquivo ps2-0230a-20080220.bin. Com
    /// qualquer outra BIOS válida, ela avisa que falta BIOS a cada jogo, mesmo o jogo abrindo normalmente.
    /// O Retroboat confere a BIOS pelo conteúdo (<see cref="BiosStatus"/>), então desliga essa checagem do ES
    /// uma única vez: quem a ligar de novo no menu do ES não é contrariado.
    /// </summary>
    public static class EsBiosCheck
    {
        public const string SettingName = "CheckBiosesAtLaunch";

        /// <summary>Só deve ser chamado com o ES fechado: ele regrava o es_settings.cfg ao sair.</summary>
        /// <returns>true se a checagem foi desligada agora.</returns>
        public static bool TurnOffOnce(RetroBatInstall install, RetroboatSettings settings, string settingsPath)
        {
            if (settings.EsBiosCheckHandled)
                return false;

            // Sem o arquivo o ES ainda não abriu aqui: um es_settings.cfg só com esta chave pode atrapalhar a
            // configuração inicial do RetroBat. Fica para o próximo início.
            var esSettingsPath = Path.Combine(install.UserEmulationStationPath, "es_settings.cfg");
            if (!File.Exists(esSettingsPath))
                return false;

            var esSettings = EsSettings.Load(esSettingsPath);
            esSettings.SetBool(SettingName, false);
            esSettings.Save();

            settings.EsBiosCheckHandled = true;
            settings.Save(settingsPath);
            return true;
        }
    }
}
