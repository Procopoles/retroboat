using System;
using System.IO;
using System.Text;
using Retroboat.Cloud;
using Retroboat.Downloads;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Settings;
using Retroboat.Text;

namespace Retroboat.Launch
{
    /// <summary>
    /// retroboat-launch --desinstalar|--verificar|--reparar "&lt;jogo&gt;": o menu do jogo no ES do Retroboat (RB-004, fase 2)
    /// pede isto para um jogo da nuvem que está neste PC. O texto para o usuário vai para
    /// estado\resposta-&lt;pedido&gt;.txt, que o ES mostra quando o processo termina; o andamento da verificação vai para
    /// estado\verificando (a porcentagem).
    /// Códigos: 0 = feito (ou a cópia confere); 1 = nada mudou; 3 = a cópia está danificada e dá para reparar.
    /// </summary>
    static class LocalCopyFlow
    {
        public const string UninstallVerb = "--desinstalar";
        public const string VerifyVerb = "--verificar";
        public const string RepairVerb = "--reparar";
        public const string ProgressFile = "verificando";

        const int ExitDone = 0;
        const int ExitNothing = 1;
        const int ExitDamaged = 3;

        public static bool Handles(string verb) => verb == UninstallVerb || verb == VerifyVerb || verb == RepairVerb;

        public static string ResponsePath(RetroBatInstall install, string verb) =>
            Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, "resposta-" + verb.TrimStart('-') + ".txt");

        public static int Run(RetroBatInstall install, string verb, string rom, string exeFolder)
        {
            try
            {
                var installer = new OnDemandInstaller(install);
                var entry = installer.FindByRom(rom);
                if (entry == null)
                {
                    Log.Warn("Pedido do ES (" + verb + ") para um jogo que não é da nuvem: " + rom);
                    return Respond(install, verb, ExitNothing, "Este jogo não faz parte da biblioteca na nuvem.");
                }

                var title = Program.GameTitle(install, entry);
                if (!LocalCopy.IsInstalled(entry, installer.RomsRoot))
                    return Respond(install, verb, ExitNothing, title + " não está neste PC.");

                switch (verb)
                {
                    case UninstallVerb: return Uninstall(install, installer, entry, title);
                    case VerifyVerb: return Verify(install, installer, entry, title);
                    default: return Repair(install, installer, entry, title, exeFolder);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Falha no pedido do ES (" + verb + ")", ex);
                return Respond(install, verb, ExitNothing, "Não deu certo: " + ex.Message);
            }
        }

        static int Uninstall(RetroBatInstall install, OnDemandInstaller installer, CloudIndexEntry entry, string title)
        {
            // Antes de apagar, confere que o jogo continua na nuvem: sem o Drive, não dá para saber.
            var cloudGame = CloudGameWithDrive(install, CloudNeed.Uninstall, entry, out bool reachable);
            if (!reachable)
                return Respond(install, UninstallVerb, ExitNothing, "Sem o Google Drive, " + title + " continua neste PC: o Retroboat só apaga a cópia depois de conferir que o jogo está na nuvem.");

            var result = LocalCopy.Uninstall(entry, installer.RomsRoot, cloudGame, CloudLibrarySync.IndexPath(install));
            switch (result.Outcome)
            {
                case UninstallOutcome.Removed:
                    Log.Info("Desinstalado pelo ES: " + entry.System + "/" + entry.CloudName + " (" + Format.Bytes(result.BytesFreed) + ")");
                    return Respond(install, UninstallVerb, ExitDone, title + " desinstalado  ·  " + Format.Bytes(result.BytesFreed) + " livres");
                case UninstallOutcome.OnlyCopy:
                    Log.Warn("Desinstalar recusado (" + result.Detail + "): " + entry.System + "/" + entry.CloudName);
                    return Respond(install, UninstallVerb, ExitNothing, title + " continua neste PC: " + result.Detail + ", então a cópia daqui é a única boa.");
                default:
                    return Respond(install, UninstallVerb, ExitNothing, title + " não está neste PC.");
            }
        }

        static int Verify(RetroBatInstall install, OnDemandInstaller installer, CloudIndexEntry entry, string title)
        {
            // Sem perguntar pelo Drive: fechado, a verificação confere só a cópia local e diz isso.
            var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
            var location = CloudLocator.Locate(settings.CloudPath);
            bool reachable = location != null && location.Exists;
            var cloudGame = reachable ? FindCloudGame(location, entry) : null;

            var progressPath = Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, ProgressFile);
            int lastPercent = -1;
            var progress = new Progress(fraction =>
            {
                int percent = (int)Math.Floor(Math.Min(1, Math.Max(0, fraction)) * 100);
                if (percent == lastPercent)
                    return;

                lastPercent = percent;
                try { File.WriteAllText(progressPath, percent.ToString(Format.Culture)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            });

            VerifyResult result;
            try
            {
                result = LocalCopy.Verify(entry, installer.RomsRoot, cloudGame, progress);
            }
            finally
            {
                try { File.Delete(progressPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            Log.Info("Verificação pelo ES: " + entry.System + "/" + entry.CloudName + ": " + result.Outcome + (result.Detail != null ? " (" + result.Detail + ")" : "") + (result.ComparedWithCloud ? "" : ", sem a nuvem"));

            switch (result.Outcome)
            {
                case VerifyOutcome.Intact:
                    if (result.ComparedWithCloud)
                        return Respond(install, VerifyVerb, ExitDone, "Os arquivos de " + title + " conferem com a nuvem.");

                    return Respond(install, VerifyVerb, ExitDone, reachable
                        ? "A cópia de " + title + " está boa neste PC, mas o jogo não está mais na nuvem para comparar."
                        : "A cópia de " + title + " está boa neste PC. Com o Google Drive aberto, a verificação também compara com a nuvem.");

                case VerifyOutcome.Damaged:
                    if (reachable && cloudGame == null)
                        return Respond(install, VerifyVerb, ExitNothing, "A cópia de " + title + " neste PC está danificada: " + result.Detail + ".\n\nO jogo não está mais na nuvem, então não dá para baixar de novo.");

                    return Respond(install, VerifyVerb, ExitDamaged, "A cópia de " + title + " neste PC está danificada: " + result.Detail + ".\n\nREPARAR apaga esta cópia e baixa o jogo de novo da nuvem. Os saves ficam.");

                default:
                    return Respond(install, VerifyVerb, ExitNothing, title + " não está neste PC.");
            }
        }

        static int Repair(RetroBatInstall install, OnDemandInstaller installer, CloudIndexEntry entry, string title, string exeFolder)
        {
            var cloudGame = CloudGameWithDrive(install, CloudNeed.Download, entry, out bool reachable);
            if (!reachable)
                return Respond(install, RepairVerb, ExitNothing, "Sem o Google Drive, a cópia de " + title + " ficou como estava.");

            var result = LocalCopy.Uninstall(entry, installer.RomsRoot, cloudGame, CloudLibrarySync.IndexPath(install));
            if (result.Outcome != UninstallOutcome.Removed)
                return Respond(install, RepairVerb, ExitNothing, "A cópia de " + title + " ficou como estava" + (result.Detail != null ? ": " + result.Detail : "") + ".");

            var queue = new DownloadQueue(install);
            Program.StartDownload(install, queue, entry);
            DownloadProcess.EnsureRunning(install, exeFolder);
            Log.Info("Reparo pelo ES: " + entry.System + "/" + entry.CloudName + " volta a baixar.");
            return Respond(install, RepairVerb, ExitDone, title + " está baixando de novo");
        }

        /// <summary>Com o Drive fechado, oferece abrir. reachable = a pasta da nuvem está acessível agora.</summary>
        static CloudGame CloudGameWithDrive(RetroBatInstall install, CloudNeed need, CloudIndexEntry entry, out bool reachable)
        {
            var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
            reachable = false;
            if (CloudAppPrompt.Ensure(install, settings, need) == CloudAppAnswer.Declined)
                return null;

            var location = CloudLocator.Locate(settings.CloudPath);
            reachable = location != null && location.Exists;
            return reachable ? FindCloudGame(location, entry) : null;
        }

        static CloudGame FindCloudGame(CloudLocation location, CloudIndexEntry entry) =>
            new CloudLibrary(location.GamesPath).Find(entry.System + "/" + entry.CloudName);

        static int Respond(RetroBatInstall install, string verb, int code, string text)
        {
            try
            {
                var path = ResponsePath(install, verb);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Resposta para o ES não gravada: " + ex.Message);
            }

            return code;
        }

        /// <summary>IProgress que chama na hora, sem SynchronizationContext (o processo não tem janela).</summary>
        sealed class Progress : IProgress<double>
        {
            readonly Action<double> _report;

            public Progress(Action<double> report) => _report = report;

            public void Report(double value) => _report(value);
        }
    }
}
