using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Cloud;
using Retroboat.Library;

namespace Retroboat.Settings
{
    /// <summary>
    /// RB-021: o que o menu NUVEM do ES mostra e muda, pelo retroboat-cli settings. O ES só lê o que sai daqui e só grava
    /// por aqui: o <see cref="RetroboatSettings"/> continua sendo o único que escreve o retroboat.ini.
    /// </summary>
    public static class SettingsSummary
    {
        /// <summary>Chaves que o menu pode mudar, com o nome no retroboat.ini.</summary>
        public static readonly IReadOnlyList<string> EditableKeys = new[] { "Videos", "Manuals", "Automatic", "AutoOpen" };

        /// <summary>
        /// Linhas "Chave=valor" para o ES. LibraryState: ok, missing (o Drive está aberto, mas sem a biblioteca deste PC),
        /// none (nenhuma escolhida), offline (sem Google Drive) ou configured ([Cloud] Path à mão).
        /// </summary>
        public static List<KeyValuePair<string, string>> Describe(RetroboatSettings settings, Func<CloudLocation> locate = null)
        {
            var lines = new List<KeyValuePair<string, string>>();
            void Add(string key, string value) => lines.Add(new KeyValuePair<string, string>(key, (value ?? "").Replace("\r", " ").Replace("\n", " ")));

            var location = (locate ?? (() => CloudLocator.Locate(settings)))();
            string state, name = "";
            int games = 0;
            if (!string.IsNullOrWhiteSpace(settings.CloudPath))
            {
                state = location != null && location.Exists ? "configured" : "offline";
                name = settings.CloudPath;
            }
            else if (location == null)
            {
                state = "offline";
            }
            else if (location.LibraryMissing)
            {
                state = "missing";
            }
            else if (string.IsNullOrWhiteSpace(settings.CloudLibrary))
            {
                state = "none";
            }
            else
            {
                state = location.Exists ? "ok" : "missing";
                name = DisplayName(location);
            }

            if ((state == "ok" || state == "configured") && Directory.Exists(location.GamesPath))
                games = new CloudLibrary(location.GamesPath).GetGames().Count;

            Add("LibraryState", state);
            Add("Library", settings.CloudLibrary);
            Add("LibraryName", name);
            Add("Games", games.ToString());
            Add("RomsPath", settings.RomsPath);
            Add("Videos", Bool(settings.CloudVideos));
            Add("Manuals", Bool(settings.CloudManuals));
            Add("Automatic", Bool(settings.AutoMedia));
            Add("AutoOpen", Bool(settings.CloudAppAutoOpen));
            return lines;
        }

        /// <summary>Muda uma das <see cref="EditableKeys"/> (valor 0 ou 1). Biblioteca e pasta de jogos só mudam pelo setup.</summary>
        public static bool TrySet(RetroboatSettings settings, string key, string value, out string error)
        {
            error = null;
            if (value != "0" && value != "1")
            {
                error = "O valor de " + key + " é 0 ou 1.";
                return false;
            }

            bool on = value == "1";
            switch ((key ?? "").ToLowerInvariant())
            {
                case "videos": settings.CloudVideos = on; return true;
                case "manuals": settings.CloudManuals = on; return true;
                case "automatic": settings.AutoMedia = on; return true;
                case "autoopen": settings.CloudAppAutoOpen = on; return true;
                default:
                    error = "Chave desconhecida: " + key + ". Use " + string.Join(", ", EditableKeys) + ". Biblioteca e pasta de jogos mudam pelo setup.";
                    return false;
            }
        }

        /// <summary>Ex.: "Meu Drive (G:)".</summary>
        static string DisplayName(CloudLocation location)
        {
            var myDrive = Path.GetDirectoryName(location.RetroboatPath);
            var root = Path.GetPathRoot(location.RetroboatPath) ?? "";
            return Path.GetFileName(myDrive) + " (" + root.TrimEnd('\\', '/') + ")";
        }

        static string Bool(bool value) => value ? "1" : "0";
    }

    /// <summary>
    /// RB-021: pedido do menu NUVEM para abrir o setup. O ES cria o arquivo e fecha; o Retroboat.exe vê o pedido quando o ES
    /// fecha (ou na próxima abertura), mostra o setup e abre o RetroBat de novo.
    /// </summary>
    public static class SetupRequest
    {
        public const string FileName = "pedido-setup";

        public static string PathFor(RetroBatInstall install) => System.IO.Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, FileName);

        public static bool Exists(RetroBatInstall install) => File.Exists(PathFor(install));

        public static void Create(RetroBatInstall install)
        {
            var path = PathFor(install);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllText(path, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + Environment.NewLine);
        }

        /// <returns>true se havia um pedido.</returns>
        public static bool Take(RetroBatInstall install)
        {
            var path = PathFor(install);
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
    }
}
