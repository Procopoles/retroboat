using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Retroboat.Logging;

namespace Retroboat.SteamSwitch
{
    /// <summary>Outro programa que também reage ao botão Xbox, e o que o usuário desliga para a troca funcionar.</summary>
    public sealed class GuideButtonConflict
    {
        public GuideButtonConflict(string owner, string title, string fix)
        {
            Owner = owner;
            Title = title;
            Fix = fix;
        }

        /// <summary>Para o log.</summary>
        public string Owner { get; }

        /// <summary>A linha principal do aviso.</summary>
        public string Title { get; }

        /// <summary>Onde desligar, numa linha.</summary>
        public string Fix { get; }
    }

    /// <summary>
    /// RB-040: o botão Xbox só troca entre o Retroboat e a Steam se nenhum outro programa usa o botão. Os dois que vêm
    /// ligados de fábrica são a Xbox Game Bar do Windows e a opção da Steam que leva ao Big Picture pelo botão; com
    /// um deles ligado, um aperto faria duas coisas. Lido a cada aperto: desligar a opção vale na hora, sem reabrir.
    /// Outros programas que capturam o botão (DS4Windows, reWASD...) não deixam rastro confiável e ficam de fora.
    /// </summary>
    public static class GuideButtonConflicts
    {
        const string GameBarKey = @"Software\Microsoft\GameBar";
        const string GameBarValue = "UseNexusForGameBarEnabled";
        const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        const string GameBarPackage = "Microsoft.XboxGamingOverlay_";

        static readonly Regex SteamGuideOption = new Regex("\"Controller_CheckGuideButton\"\\s+\"(\\d+)\"", RegexOptions.CultureInvariant);

        public static readonly GuideButtonConflict GameBar = new GuideButtonConflict(
            "Xbox Game Bar",
            "A Xbox Game Bar usa este botão",
            "Desligue em Configurações › Jogos › Xbox Game Bar");

        public static readonly GuideButtonConflict SteamGuide = new GuideButtonConflict(
            "Steam (botão Guia foca a Steam)",
            "A Steam também usa este botão",
            "Desligue em Steam › Configurações › Controle");

        /// <summary>O primeiro conflito encontrado, ou nulo.</summary>
        public static GuideButtonConflict Find(SteamClient steam)
        {
            if (GameBarUsesButton(ReadGameBarOption(), IsGameBarInstalled()))
                return GameBar;

            if (steam != null && SteamGuideFocusesSteam(ReadText(steam.LocalConfigPath)))
                return SteamGuide;

            return null;
        }

        /// <summary>
        /// A opção "Abrir a Xbox Game Bar usando este botão no controle" vem ligada: sem o valor no registro, a Game Bar
        /// usa o botão. Sem a Game Bar instalada, ninguém usa.
        /// </summary>
        internal static bool GameBarUsesButton(object option, bool installed) => installed && !(option is int value && value == 0);

        /// <summary>
        /// A opção "Botão Guia foca a Steam" vem ligada; o <c>localconfig.vdf</c> só tem a linha depois que o usuário mexe
        /// nela. Sem o arquivo (Steam nunca aberta com uma conta), não há o que a Steam faça com o botão.
        /// </summary>
        internal static bool SteamGuideFocusesSteam(string localConfig)
        {
            if (localConfig == null)
                return false;

            var match = SteamGuideOption.Match(localConfig);
            return !match.Success || match.Groups[1].Value != "0";
        }

        static object ReadGameBarOption()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(GameBarKey))
                    return key?.GetValue(GameBarValue);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                Log.Warn("Botão Xbox: não foi possível ler a opção da Xbox Game Bar: " + ex.Message);
                return null;
            }
        }

        static bool IsGameBarInstalled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PackagesKey))
                    return key != null && key.GetSubKeyNames().Any(n => n.StartsWith(GameBarPackage, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                // Sem como saber: considera instalada, como vem no Windows.
                Log.Warn("Botão Xbox: não foi possível ver se a Xbox Game Bar está instalada: " + ex.Message);
                return true;
            }
        }

        static string ReadText(string path)
        {
            if (path == null)
                return null;

            try
            {
                // A Steam pode estar gravando o arquivo: lê sem bloquear a escrita dela.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Botão Xbox: não foi possível ler " + path + ": " + ex.Message);
                return null;
            }
        }
    }
}
