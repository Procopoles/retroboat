using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Integrity;
using Retroboat.Transfers;

namespace Retroboat.Cloud
{
    public enum InstallOutcome
    {
        Installed,
        AlreadyInstalled,
        Cancelled,

        /// <summary>
        /// O sistema abortou a leitura sem ninguém pedir (o Google Drive reiniciou e desmontou o disco). Não é
        /// cancelamento do usuário: o download deve esperar o Drive e continuar.
        /// </summary>
        Interrupted,
        InsufficientSpace,
        Failed
    }

    public sealed class InstallResult
    {
        public InstallOutcome Outcome { get; set; }
        public string Destination { get; set; }
        public long BytesCopied { get; set; }
        public long BytesTotal { get; set; }

        /// <summary>Bytes que já estavam copiados de uma tentativa anterior.</summary>
        public long BytesResumed { get; set; }

        public string Error { get; set; }

        /// <summary>Verificação rápida da cópia local (do arquivo principal, para jogos em pasta).</summary>
        public IntegrityResult Integrity { get; set; }
    }

    /// <summary>
    /// "Instalar" um jogo = copiar da pasta da nuvem para a pasta de jogos local
    /// (&lt;pasta de jogos&gt;\&lt;sistema&gt;\&lt;jogo&gt;), com progresso e retomada.
    /// A cópia vai para "&lt;arquivo&gt;.retroboat-part" e só ganha o nome final quando termina, então
    /// o ES nunca enxerga um jogo pela metade. Se a cópia for interrompida, a próxima continua de onde parou.
    /// </summary>
    public sealed class GameInstaller
    {
        public const string PartSuffix = ".retroboat-part";

        // Folga para não encher o disco até o último byte.
        public const long FreeSpaceMargin = 512L << 20;

        public int BufferSize { get; set; } = 4 << 20;

        /// <summary>
        /// Nome do jogo na pasta de jogos local. Um ZIP com um único disco dentro (ex.: um ISO de PS2) é
        /// extraído na instalação, então o nome local é o do disco. O emulatorLauncher descompactaria o ZIP
        /// inteiro a cada partida.
        /// </summary>
        public static string GetLocalName(CloudGame game) => FindDiscImageInZip(game)?.FileName ?? game.Name;

        static ZipEntryInfo FindDiscImageInZip(CloudGame game)
        {
            if (game.IsDirectory || !game.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                return ZipCatalog.GetSingleDiscImage(ZipCatalog.Read(game.FullPath));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public InstallResult Install(CloudGame game, string romsRoot, IProgress<TransferProgress> progress = null, CancellationToken cancellationToken = default, bool force = false)
        {
            var discImage = FindDiscImageInZip(game);
            if (discImage != null)
                return InstallFromZip(game, discImage, romsRoot, progress, cancellationToken, force);

            var destination = Path.Combine(romsRoot, game.System, game.Name);
            var result = new InstallResult { Destination = destination };

            try
            {
                var plan = PlanFiles(game, destination);
                result.BytesTotal = plan.Sum(p => p.Length);

                if (!force && plan.All(p => p.IsComplete))
                {
                    result.Outcome = InstallOutcome.AlreadyInstalled;
                    result.Integrity = CheckMainFile(game, destination);
                    return result;
                }

                if (force)
                {
                    foreach (var file in plan)
                        file.Reset();
                }

                long alreadyDone = plan.Sum(p => p.BytesDone);
                long remaining = result.BytesTotal - alreadyDone;
                long free = GetFreeSpace(romsRoot);
                if (free >= 0 && free < remaining + FreeSpaceMargin)
                {
                    result.Outcome = InstallOutcome.InsufficientSpace;
                    result.Error = "faltam " + Text.Format.Bytes(remaining + FreeSpaceMargin - free) + " livres em " + Path.GetPathRoot(Path.GetFullPath(romsRoot));
                    return result;
                }

                result.BytesResumed = plan.Sum(p => p.IsComplete ? 0 : p.BytesDone);

                var meter = new RateMeter(result.BytesTotal);
                long done = alreadyDone;
                var buffer = new byte[BufferSize];

                foreach (var file in plan.Where(p => !p.IsComplete))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Destination));
                    done = CopyWithResume(file, buffer, done, meter, progress, game.Name, cancellationToken);
                }

                meter.Report(progress, game.Name, done, force: true);
                result.BytesCopied = done - alreadyDone;
                result.Outcome = InstallOutcome.Installed;
                result.Integrity = CheckMainFile(game, destination);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.Outcome = InstallOutcome.Cancelled;
            }
            catch (OperationCanceledException)
            {
                // A cópia byte a byte continua do .retroboat-part na próxima tentativa.
                Interrupted(result);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Outcome = InstallOutcome.Failed;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// O Windows devolve ERROR_OPERATION_ABORTED quando o disco do Drive é desmontado com uma leitura em
        /// andamento, e o .NET transforma isso em OperationCanceledException, a mesma do cancelamento do usuário.
        /// Só o token diz quem cancelou.
        /// </summary>
        static void Interrupted(InstallResult result)
        {
            result.Outcome = InstallOutcome.Interrupted;
            result.Error = "a leitura do arquivo na nuvem foi interrompida (o Google Drive reiniciou ou desconectou)";
        }

        /// <summary>
        /// Extrai o disco de dentro do ZIP direto da pasta da nuvem para a pasta de jogos, sem guardar o ZIP:
        /// uma passada só e nada de espaço em dobro. Confere o CRC-32 do ZIP no fim. Uma extração
        /// interrompida recomeça do início na próxima vez (o deflate não permite continuar no meio).
        /// </summary>
        InstallResult InstallFromZip(CloudGame game, ZipEntryInfo entry, string romsRoot, IProgress<TransferProgress> progress, CancellationToken cancellationToken, bool force)
        {
            var destination = Path.Combine(romsRoot, game.System, entry.FileName);
            var part = destination + PartSuffix;
            var result = new InstallResult { Destination = destination, BytesTotal = entry.UncompressedSize };

            try
            {
                var existing = new FileInfo(destination);
                if (!force && existing.Exists && existing.Length == entry.UncompressedSize)
                {
                    result.Outcome = InstallOutcome.AlreadyInstalled;
                    result.Integrity = IntegrityChecker.QuickCheck(destination);
                    return result;
                }

                long free = GetFreeSpace(romsRoot);
                if (free >= 0 && free < entry.UncompressedSize + FreeSpaceMargin)
                {
                    result.Outcome = InstallOutcome.InsufficientSpace;
                    result.Error = "faltam " + Text.Format.Bytes(entry.UncompressedSize + FreeSpaceMargin - free) + " livres em " + Path.GetPathRoot(Path.GetFullPath(romsRoot));
                    return result;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                var meter = new RateMeter(entry.UncompressedSize);
                var crc = new Crc32();
                var buffer = new byte[BufferSize];
                long done = 0;

                using (var source = new FileStream(game.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
                {
                    source.Seek(ZipCatalog.GetDataOffset(source, entry), SeekOrigin.Begin);
                    using (var data = entry.Method == 8 ? (Stream)new System.IO.Compression.DeflateStream(source, System.IO.Compression.CompressionMode.Decompress, true) : source)
                    using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan))
                    {
                        long remaining = entry.UncompressedSize;
                        while (remaining > 0)
                        {
                            int read = data.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (read == 0)
                                break;

                            target.Write(buffer, 0, read);
                            crc.Append(buffer, 0, read);
                            done += read;
                            remaining -= read;
                            meter.Report(progress, entry.FileName, done);
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        target.Flush(true);
                    }
                }

                meter.Report(progress, entry.FileName, done, force: true);
                if (done != entry.UncompressedSize || crc.Value != entry.Crc32)
                {
                    File.Delete(part);
                    result.Outcome = InstallOutcome.Failed;
                    result.Error = "o conteúdo extraído não confere com o ZIP (CRC-32 " + crc.Value.ToString("x8") + ", esperado " + entry.Crc32.ToString("x8") + "): o arquivo na nuvem está danificado";
                    return result;
                }

                if (File.Exists(destination))
                    File.Delete(destination);

                File.Move(part, destination);
                File.SetLastWriteTimeUtc(destination, game.LastWriteTimeUtc);
                result.BytesCopied = done;
                result.Outcome = InstallOutcome.Installed;
                result.Integrity = IntegrityChecker.QuickCheck(destination);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDelete(part);
                result.Outcome = InstallOutcome.Cancelled;
            }
            catch (OperationCanceledException)
            {
                TryDelete(part);
                Interrupted(result);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                TryDelete(part);
                result.Outcome = InstallOutcome.Failed;
                result.Error = ex.Message;
            }

            return result;
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Apaga a cópia local (e cópias parciais). O jogo continua na nuvem.</summary>
        public static bool Uninstall(CloudGame game, string romsRoot)
        {
            var destination = Path.Combine(romsRoot, game.System, GetLocalName(game));
            bool removed = false;

            if (game.IsDirectory)
            {
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, true);
                    removed = true;
                }
            }
            else
            {
                foreach (var path in new[] { destination, destination + PartSuffix })
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        removed = true;
                    }
                }
            }

            return removed;
        }

        long CopyWithResume(FilePlan file, byte[] buffer, long done, RateMeter meter, IProgress<TransferProgress> progress, string item, CancellationToken cancellationToken)
        {
            long offset = file.PartLength;

            using (var source = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
            using (var target = new FileStream(file.PartPath, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan))
            {
                if (offset > 0)
                    source.Seek(offset, SeekOrigin.Begin);

                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    target.Write(buffer, 0, read);
                    done += read;
                    meter.Report(progress, item, done);

                    // Cancela entre blocos: o que já foi gravado fica no .retroboat-part para retomar.
                    cancellationToken.ThrowIfCancellationRequested();
                }

                target.Flush(true);
            }

            var copied = new FileInfo(file.PartPath).Length;
            if (copied != file.Length)
                throw new IOException("cópia incompleta de " + Path.GetFileName(file.Source) + ": " + copied + " de " + file.Length + " bytes");

            if (File.Exists(file.Destination))
                File.Delete(file.Destination);

            File.Move(file.PartPath, file.Destination);
            File.SetLastWriteTimeUtc(file.Destination, file.SourceLastWriteUtc);
            return done;
        }

        static List<FilePlan> PlanFiles(CloudGame game, string destination)
        {
            if (!game.IsDirectory)
                return new List<FilePlan> { new FilePlan(new FileInfo(game.FullPath), destination) };

            var root = new DirectoryInfo(game.FullPath);
            return root.EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(f => new FilePlan(f, Path.Combine(destination, f.FullName.Substring(root.FullName.Length).TrimStart('\\', '/'))))
                .ToList();
        }

        static IntegrityResult CheckMainFile(CloudGame game, string destination)
        {
            if (!game.IsDirectory)
                return File.Exists(destination) ? IntegrityChecker.QuickCheck(destination) : null;

            var largest = new DirectoryInfo(destination).EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderByDescending(f => f.Length)
                .FirstOrDefault();

            return largest != null ? IntegrityChecker.QuickCheck(largest.FullName) : null;
        }

        static long GetFreeSpace(string romsRoot)
        {
            try
            {
                return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(romsRoot))).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                return -1;
            }
        }

        sealed class FilePlan
        {
            public FilePlan(FileInfo source, string destination)
            {
                Source = source.FullName;
                Length = source.Length;
                SourceLastWriteUtc = source.LastWriteTimeUtc;
                Destination = destination;
                PartPath = destination + PartSuffix;

                var existing = new FileInfo(destination);
                IsComplete = existing.Exists && existing.Length == Length;

                var part = new FileInfo(PartPath);
                PartLength = !IsComplete && part.Exists && part.Length <= Length ? part.Length : 0;
                if (!IsComplete && part.Exists && part.Length > Length)
                    part.Delete();
            }

            public string Source { get; }
            public long Length { get; }
            public DateTime SourceLastWriteUtc { get; }
            public string Destination { get; }
            public string PartPath { get; }
            public bool IsComplete { get; private set; }
            public long PartLength { get; private set; }

            public long BytesDone => IsComplete ? Length : PartLength;

            public void Reset()
            {
                IsComplete = false;
                PartLength = 0;
            }
        }
    }
}
