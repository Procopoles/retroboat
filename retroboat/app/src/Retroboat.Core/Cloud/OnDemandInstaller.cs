using System;
using System.IO;
using System.Threading;
using Retroboat.Integrity;
using Retroboat.Library;
using Retroboat.Settings;
using Retroboat.Transfers;

namespace Retroboat.Cloud
{
    public enum OnDemandOutcome
    {
        Installed,
        Cancelled,
        /// <summary>O arquivo na nuvem está danificado; o jogo fica marcado como corrompido.</summary>
        Corrupt,
        Failed
    }

    public sealed class OnDemandResult
    {
        public OnDemandResult(OnDemandOutcome outcome, string message = null)
        {
            Outcome = outcome;
            Message = message;
        }

        public OnDemandOutcome Outcome { get; }

        /// <summary>Texto para o usuário quando não deu certo.</summary>
        public string Message { get; }
    }

    /// <summary>
    /// Instala um jogo da nuvem na hora de jogar: o ES abriu um marcador vazio, o retroboat-launch
    /// baixa o jogo de verdade no lugar dele e só então chama o emulatorLauncher.
    /// </summary>
    public sealed class OnDemandInstaller
    {
        readonly RetroBatInstall _install;
        readonly RetroboatSettings _settings;
        readonly CloudIndex _index;

        public OnDemandInstaller(RetroBatInstall install)
        {
            _install = install;
            _settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
            _index = CloudIndex.Load(CloudLibrarySync.IndexPath(install));
            RomsRoot = string.IsNullOrEmpty(_settings.RomsPath) ? install.DefaultRomsPath : _settings.RomsPath;
        }

        public string RomsRoot { get; }

        /// <summary>
        /// A entrada da nuvem para o arquivo que o ES quer abrir, se ele for um marcador (arquivo vazio de um
        /// jogo que ainda não foi baixado). Nulo para jogos normais, que seguem direto para o emulador.
        /// </summary>
        public CloudIndexEntry FindPending(string romPath)
        {
            if (string.IsNullOrEmpty(romPath))
                return null;

            var local = new FileInfo(romPath);
            if (!local.Exists || local.Length > 0)
                return null;

            var entry = _index.FindByLocalPath(RomsRoot, romPath);
            return entry != null && entry.State != CloudGameState.Installed ? entry : null;
        }

        public OnDemandResult Install(CloudIndexEntry entry, IProgress<TransferProgress> progress, CancellationToken cancellationToken)
        {
            var location = CloudLocator.Locate(_settings.CloudPath);
            if (location == null || !location.Exists)
                return new OnDemandResult(OnDemandOutcome.Failed, "O Google Drive para computador não está disponível. Abra o Google Drive e tente de novo.");

            var game = new CloudLibrary(location.GamesPath).Find(entry.System + "/" + entry.CloudName);
            if (game == null)
                return new OnDemandResult(OnDemandOutcome.Failed, "Este jogo não está mais na nuvem (" + entry.System + "/" + entry.CloudName + ").");

            if (!game.IsDirectory)
            {
                var check = IntegrityChecker.QuickCheck(game.FullPath);
                if (check.IsCorrupt)
                    return MarkCorrupt(entry, "o arquivo no Drive está zerado, sem conteúdo");
            }

            var result = new GameInstaller().Install(game, RomsRoot, progress, cancellationToken);
            switch (result.Outcome)
            {
                case InstallOutcome.Installed:
                case InstallOutcome.AlreadyInstalled:
                    if (result.Integrity != null && result.Integrity.IsCorrupt)
                    {
                        File.Delete(result.Destination);
                        File.WriteAllBytes(result.Destination, new byte[0]);
                        return MarkCorrupt(entry, "a cópia baixada está zerada");
                    }

                    entry.State = CloudGameState.Installed;
                    entry.Problem = null;
                    _index.Set(entry);
                    _index.Save();
                    return new OnDemandResult(OnDemandOutcome.Installed);

                case InstallOutcome.Cancelled:
                    return new OnDemandResult(OnDemandOutcome.Cancelled);

                case InstallOutcome.InsufficientSpace:
                    return new OnDemandResult(OnDemandOutcome.Failed, "Espaço insuficiente: " + result.Error + ".");

                default:
                    if (result.Error != null && result.Error.Contains("CRC-32"))
                        return MarkCorrupt(entry, "o conteúdo do ZIP no Drive não confere (CRC)");

                    return new OnDemandResult(OnDemandOutcome.Failed, "Não foi possível baixar o jogo: " + result.Error + ".");
            }
        }

        OnDemandResult MarkCorrupt(CloudIndexEntry entry, string problem)
        {
            entry.State = CloudGameState.Corrupt;
            entry.Problem = problem;
            _index.Set(entry);
            _index.Save();
            return new OnDemandResult(OnDemandOutcome.Corrupt, "Este jogo está corrompido na nuvem: " + problem + ". Substitua o arquivo no Drive para poder jogar.");
        }
    }
}
