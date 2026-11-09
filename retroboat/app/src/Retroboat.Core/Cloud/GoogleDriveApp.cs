using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Retroboat.Logging;
using Retroboat.Settings;

namespace Retroboat.Cloud
{
    public enum CloudAppState
    {
        /// <summary>O Drive está aberto: não há o que perguntar. A pasta do Retroboat pode ainda não existir.</summary>
        Open,

        /// <summary>Instalado e fechado: dá para abrir.</summary>
        Closed,

        /// <summary>O programa está rodando, mas a unidade ainda não apareceu (subindo, ou sem login).</summary>
        Connecting,

        /// <summary>Abrir o Drive não resolveria: não está instalado, ou a pasta configurada fica em outro disco.</summary>
        NotApplicable
    }

    /// <summary>
    /// O app Google Drive para computador: se está instalado, se está aberto e como abrir. Fica em
    /// %ProgramFiles%\Google\Drive File Stream\&lt;versão&gt;\GoogleDriveFS.exe; versões antigas podem continuar lá.
    /// </summary>
    public static class GoogleDriveApp
    {
        public const string ProcessName = "GoogleDriveFS";
        const string ExecutableName = ProcessName + ".exe";

        // O argumento que o próprio Drive grava na inicialização do Windows: abre na bandeja, sem janela.
        const string StartInTray = "--startup_mode";

        public static CloudAppState GetState(RetroboatSettings settings)
        {
            return GetState(settings?.CloudPath, settings?.CloudLibrary, FindExecutable(), IsRunning());
        }

        public static CloudAppState GetState(string configuredCloudPath, string executable, bool running)
        {
            return GetState(configuredCloudPath, null, executable, running);
        }

        public static CloudAppState GetState(string configuredCloudPath, string libraryId, string executable, bool running)
        {
            var cloud = CloudLocator.Locate(configuredCloudPath, libraryId);
            if (cloud != null && cloud.Exists)
                return CloudAppState.Open;

            if (string.IsNullOrWhiteSpace(configuredCloudPath))
            {
                if (cloud != null)
                    return CloudAppState.Open; // Drive aberto, sem a pasta "retroboat" ou sem a biblioteca deste PC.
            }
            else if (Directory.Exists(Path.GetPathRoot(cloud.RetroboatPath)))
            {
                return CloudAppState.NotApplicable; // O disco está lá e a pasta não: abrir o Drive não faz ela aparecer.
            }

            if (running)
                return CloudAppState.Connecting;

            return executable != null ? CloudAppState.Closed : CloudAppState.NotApplicable;
        }

        public static string FindExecutable()
        {
            var roots = new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            return roots.Where(r => !string.IsNullOrEmpty(r))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(FindExecutableIn)
                        .FirstOrDefault(path => path != null);
        }

        /// <summary>A versão mais nova que tem o programa. Pastas sem número de versão (Drivers) não contam.</summary>
        public static string FindExecutableIn(string programFiles)
        {
            var folder = Path.Combine(programFiles, "Google", "Drive File Stream");
            if (!Directory.Exists(folder))
                return null;

            return Directory.GetDirectories(folder)
                            .Select(dir => new { Version = TryParseVersion(Path.GetFileName(dir)), Path = Path.Combine(dir, ExecutableName) })
                            .Where(item => item.Version != null && File.Exists(item.Path))
                            .OrderByDescending(item => item.Version)
                            .Select(item => item.Path)
                            .FirstOrDefault();
        }

        public static bool IsRunning()
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var process in processes)
                process.Dispose();

            return processes.Length > 0;
        }

        /// <returns>false se não está instalado ou o Windows não conseguiu abrir.</returns>
        public static bool Start()
        {
            var executable = FindExecutable();
            if (executable == null)
                return false;

            try
            {
                // Pelo shell, para o Drive não ficar preso a este processo nem herdar nada dele.
                using (Process.Start(new ProcessStartInfo(executable, StartInTray) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable) }))
                    return true;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                Log.Warn("Não foi possível abrir o Google Drive: " + ex.Message);
                return false;
            }
        }

        static Version TryParseVersion(string text) => Version.TryParse(text, out var version) ? version : null;
    }
}
