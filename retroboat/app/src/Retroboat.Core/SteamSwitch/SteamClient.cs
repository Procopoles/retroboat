using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Retroboat.Logging;

namespace Retroboat.SteamSwitch
{
    /// <summary>
    /// A Steam instalada neste PC, pelo registro do usuário (o mesmo que o instalador da Steam grava), e os comandos do
    /// Big Picture. RB-040: o botão Xbox troca entre o Retroboat e o Big Picture.
    /// </summary>
    public sealed class SteamClient
    {
        const string SteamKey = @"Software\Valve\Steam";
        const string OpenBigPictureUri = "steam://open/bigpicture";
        const string CloseBigPictureUri = "steam://close/bigpicture";

        SteamClient(string installPath, uint activeUser)
        {
            InstallPath = installPath;
            ActiveUser = activeUser;
        }

        /// <summary>A pasta da Steam (a de <c>steam.exe</c>).</summary>
        public string InstallPath { get; }

        /// <summary>A conta logada na Steam aberta (o número da pasta em <c>userdata</c>); 0 com a Steam fechada.</summary>
        public uint ActiveUser { get; }

        /// <summary>A Steam deste usuário do Windows, ou nulo se não está instalada.</summary>
        public static SteamClient Find()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SteamKey))
                {
                    var path = key?.GetValue("SteamPath") as string;
                    if (string.IsNullOrEmpty(path))
                        return null;

                    path = Path.GetFullPath(path.Replace('/', '\\'));
                    if (!File.Exists(Path.Combine(path, "steam.exe")))
                        return null;

                    uint activeUser = 0;
                    using (var process = key.OpenSubKey("ActiveProcess"))
                    {
                        if (process?.GetValue("ActiveUser") is int value)
                            activeUser = unchecked((uint)value);
                    }

                    return new SteamClient(path, activeUser);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException || ex is ArgumentException)
            {
                Log.Warn("Botão Xbox: não foi possível ler a instalação da Steam: " + ex.Message);
                return null;
            }
        }

        /// <summary>O <c>localconfig.vdf</c> da conta logada, onde ficam as opções de controle da Steam; nulo se não há.</summary>
        public string LocalConfigPath => PickLocalConfig(Path.Combine(InstallPath, "userdata"), ActiveUser);

        /// <summary>
        /// A conta logada; com a Steam fechada (sem conta logada), a última que mexeu nas opções.
        /// </summary>
        internal static string PickLocalConfig(string userdata, uint activeUser)
        {
            if (!Directory.Exists(userdata))
                return null;

            if (activeUser != 0)
            {
                var active = Path.Combine(userdata, activeUser.ToString(), "config", "localconfig.vdf");
                if (File.Exists(active))
                    return active;
            }

            return Directory.GetDirectories(userdata)
                .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        public static bool IsRunning => Process.GetProcessesByName("steam").Length > 0;

        /// <summary>Abre o Big Picture (abre a Steam antes, se estiver fechada) e traz ele para a frente.</summary>
        public static bool OpenBigPicture() => Shell(OpenBigPictureUri);

        /// <summary>Sai do Big Picture; a Steam continua aberta na bandeja.</summary>
        public static bool CloseBigPicture() => Shell(CloseBigPictureUri);

        static bool Shell(string uri)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }))
                    return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                Log.Warn("Botão Xbox: " + uri + " falhou: " + ex.Message);
                return false;
            }
        }
    }
}
