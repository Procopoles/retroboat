using System;
using System.IO;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Transfers;

namespace Retroboat.Emulators
{
    /// <summary>
    /// Download junto com o jogo (RB-033): quando um jogo da nuvem é baixado, o emulador que ele vai usar vem no mesmo download,
    /// se falta ou tem versão nova. A barra soma os dois desde o começo, e o jogo só fica pronto depois dos dois. Se o emulador
    /// falhar (sem internet, por exemplo), o jogo fica instalado assim mesmo: o emulador vem pelo aviso ao abrir o jogo.
    /// </summary>
    public static class GameEmulatorDownload
    {
        /// <summary>O emulador que o jogo vai usar, se precisa ser baixado; nulo se não precisa (ou não dá para saber).</summary>
        public static PackageStatus NeedFor(RetroBatInstall install, RepositoryCache server, string systemFolder, string romPath)
        {
            var choice = EmulatorChoice.ForGame(install, systemFolder, romPath);
            var package = choice == null ? null : EmulatorCatalog.For(install, choice.Emulator, choice.Core);
            if (package == null)
                return null;

            var status = PackageStatus.Of(install, package, server);
            return status.NeedsDownload ? status : null;
        }

        /// <param name="source">Nulo: o repositório do RetroBat (os testes passam arquivos locais).</param>
        public static OnDemandResult Install(RetroBatInstall install, CloudIndexEntry entry, string romsRoot,
            Func<IProgress<TransferProgress>, OnDemandResult> installGame, IProgress<TransferProgress> progress, CancellationToken token,
            IPackageSource source = null)
        {
            RepositoryCache server = null;
            PackageStatus need = null;
            try
            {
                server = new RepositoryCache(install, source ?? new RetroBatRepository());
                need = NeedFor(install, server, entry.System, Path.Combine(romsRoot, entry.System, entry.LocalName));
                if (need != null)
                    EmulatorLog.Info(install, "Junto com " + entry.System + "/" + entry.LocalName + ": " + need);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                EmulatorLog.Warn(install, "Emulador do jogo " + entry.System + "/" + entry.LocalName + " não verificado: " + ex.Message);
            }

            var gameProgress = new CombinedProgress(progress, offset: 0, extraTotal: need?.DownloadBytes ?? 0);
            var result = installGame(gameProgress);
            if (result.Outcome != OnDemandOutcome.Installed || need == null)
                return result;

            var emulatorProgress = new CombinedProgress(progress, offset: gameProgress.LastTotal, extraTotal: 0);
            var emulator = new EmulatorInstaller(install, server).Install(need, emulatorProgress, token);
            if (!emulator.Installed)
            {
                EmulatorLog.Warn(install, "O jogo " + entry.System + "/" + entry.LocalName + " foi baixado, mas o " + need.Package.DisplayName + " não"
                    + (emulator.Cancelled ? " (cancelado)" : ": " + emulator.Error) + ". Ele é oferecido ao abrir o jogo.");
            }

            return result;
        }
    }

    /// <summary>
    /// Andamento de uma parte de um download maior: soma ao total o que ainda vem depois (<c>extraTotal</c>) e ao feito o que
    /// já veio antes (<c>offset</c>), para a barra andar uma vez só, do começo ao fim.
    /// </summary>
    public sealed class CombinedProgress : IProgress<TransferProgress>
    {
        readonly IProgress<TransferProgress> _target;
        readonly long _offset;
        readonly long _extraTotal;

        public CombinedProgress(IProgress<TransferProgress> target, long offset, long extraTotal)
        {
            _target = target;
            _offset = offset;
            _extraTotal = extraTotal;
        }

        /// <summary>O total desta parte, sem o que foi somado: é o deslocamento da parte seguinte.</summary>
        public long LastTotal { get; private set; }

        public void Report(TransferProgress value)
        {
            LastTotal = value.BytesTotal;
            if (_target == null)
                return;

            long done = _offset + value.BytesDone;
            long total = _offset + value.BytesTotal + _extraTotal;
            _target.Report(new TransferProgress
            {
                Item = value.Item,
                BytesDone = done,
                BytesTotal = total,
                BytesPerSecond = value.BytesPerSecond,
                Remaining = value.BytesPerSecond > 0 ? TimeSpan.FromSeconds(Math.Max(0, total - done) / value.BytesPerSecond) : value.Remaining
            });
        }
    }
}
