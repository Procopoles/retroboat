using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Integrity;
using Retroboat.Text;

namespace Retroboat.Cloud
{
    public enum VerifyOutcome
    {
        /// <summary>A cópia deste PC confere.</summary>
        Intact,
        /// <summary>A cópia deste PC está danificada ou diferente da nuvem: dá para reparar baixando de novo.</summary>
        Damaged,
        /// <summary>O jogo não está neste PC (só o marcador).</summary>
        NotInstalled
    }

    public sealed class VerifyResult
    {
        public VerifyResult(VerifyOutcome outcome, string detail, bool comparedWithCloud)
        {
            Outcome = outcome;
            Detail = detail;
            ComparedWithCloud = comparedWithCloud;
        }

        public VerifyOutcome Outcome { get; }

        /// <summary>O que estava errado (em Damaged), em minúsculas, para entrar numa frase.</summary>
        public string Detail { get; }

        /// <summary>false = sem o Drive, só a cópia local foi conferida (não zerada, formato reconhecido).</summary>
        public bool ComparedWithCloud { get; }
    }

    public enum UninstallOutcome
    {
        Removed,
        NotInstalled,
        /// <summary>O jogo sumiu da nuvem ou a cópia de lá está danificada: a deste PC é a única boa e fica.</summary>
        OnlyCopy
    }

    public sealed class UninstallResult
    {
        public UninstallResult(UninstallOutcome outcome, long bytesFreed = 0, string detail = null)
        {
            Outcome = outcome;
            BytesFreed = bytesFreed;
            Detail = detail;
        }

        public UninstallOutcome Outcome { get; }
        public long BytesFreed { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// A cópia deste PC de um jogo da nuvem: desinstalar (volta a ser o marcador, e o jogo continua na nuvem) e
    /// verificar a integridade contra a nuvem. Os saves ficam em outra pasta e não são tocados.
    /// </summary>
    public static class LocalCopy
    {
        /// <summary>Blocos comparados com a nuvem num arquivo que não veio de ZIP: lidos por amostragem, sem baixar tudo.</summary>
        public const int CompareSamples = 32;

        public const int CompareBlockSize = 256 * 1024;

        public static string PathOf(CloudIndexEntry entry, string romsRoot) => Path.Combine(romsRoot, entry.System, entry.LocalName);

        /// <summary>Espaço que a cópia ocupa neste PC; 0 se é só o marcador.</summary>
        public static long SizeOnDisk(CloudIndexEntry entry, string romsRoot)
        {
            var path = PathOf(entry, romsRoot);
            if (Directory.Exists(path))
                return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }

        public static bool IsInstalled(CloudIndexEntry entry, string romsRoot) => SizeOnDisk(entry, romsRoot) > 0;

        /// <summary>
        /// Apaga a cópia local e põe o marcador no lugar, como um jogo que nunca foi baixado. Só apaga se o jogo
        /// continua na nuvem e a cópia de lá não está danificada: <paramref name="cloudGame"/> nulo = não está mais na nuvem.
        /// </summary>
        public static UninstallResult Uninstall(CloudIndexEntry entry, string romsRoot, CloudGame cloudGame, string indexPath)
        {
            var path = PathOf(entry, romsRoot);
            long size = SizeOnDisk(entry, romsRoot);
            if (size == 0)
                return new UninstallResult(UninstallOutcome.NotInstalled);

            var problem = CloudProblem(cloudGame);
            if (problem != null)
                return new UninstallResult(UninstallOutcome.OnlyCopy, 0, problem);

            if (Directory.Exists(path))
                Directory.Delete(path, true);

            foreach (var file in new[] { path, path + GameInstaller.PartSuffix })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[0]);

            // Lê o índice de novo antes de gravar: a sincronização pode tê-lo mudado enquanto o menu estava aberto.
            var index = CloudIndex.Load(indexPath);
            var current = index.Find(entry.System, entry.CloudName) ?? entry;
            current.State = CloudGameState.Cloud;
            current.Problem = null;
            index.Set(current);
            index.Save();

            return new UninstallResult(UninstallOutcome.Removed, size);
        }

        static string CloudProblem(CloudGame cloudGame)
        {
            if (cloudGame == null)
                return "o jogo não está mais na nuvem";

            if (cloudGame.Size == 0)
                return "o arquivo na nuvem está vazio";

            if (!cloudGame.IsDirectory && IntegrityChecker.QuickCheck(cloudGame.FullPath).IsCorrupt)
                return "o arquivo na nuvem está zerado";

            return null;
        }

        /// <summary>
        /// Confere a cópia deste PC. Com a nuvem (<paramref name="cloudGame"/> não nulo): um disco que veio de ZIP é lido
        /// inteiro e o CRC-32 comparado com o do ZIP; outro arquivo tem o tamanho conferido e blocos espalhados comparados
        /// byte a byte com a nuvem (só esses blocos saem do Drive); um jogo em pasta tem a lista de arquivos e os
        /// tamanhos conferidos, e o maior arquivo comparado por blocos. Sem a nuvem, só a verificação rápida local.
        /// </summary>
        /// <param name="progress">De 0 a 1.</param>
        public static VerifyResult Verify(CloudIndexEntry entry, string romsRoot, CloudGame cloudGame, IProgress<double> progress = null, CancellationToken cancellationToken = default)
        {
            var path = PathOf(entry, romsRoot);
            bool withCloud = cloudGame != null;

            if (Directory.Exists(path))
                return VerifyFolder(path, cloudGame, progress, cancellationToken);

            var local = new FileInfo(path);
            if (!local.Exists || local.Length == 0)
                return new VerifyResult(VerifyOutcome.NotInstalled, null, withCloud);

            var quick = IntegrityChecker.QuickCheck(path);
            if (quick.IsCorrupt)
                return Damaged("a cópia está zerada", withCloud);

            if (quick.Status == IntegrityStatus.Unreadable)
                return Damaged("não foi possível ler a cópia (" + quick.Detail + ")", withCloud);

            // Sem a nuvem para comparar, só o que é certo conta: assinatura ausente pode ser outro formato.
            if (!withCloud)
            {
                progress?.Report(1);
                return new VerifyResult(VerifyOutcome.Intact, null, false);
            }

            if (cloudGame.IsDirectory)
                return Damaged("na nuvem o jogo é uma pasta, e aqui é um arquivo", true);

            var disc = ZipDisc(cloudGame);
            if (disc != null)
                return VerifyAgainstZip(path, disc, progress, cancellationToken);

            if (local.Length != cloudGame.Size)
                return Damaged("o tamanho é diferente do arquivo na nuvem (" + Format.Bytes(local.Length) + " aqui, " + Format.Bytes(cloudGame.Size) + " na nuvem)", true);

            var difference = CompareSampled(path, cloudGame.FullPath, local.Length, progress, 0, 1, cancellationToken);
            return difference != null ? Damaged(difference, true) : new VerifyResult(VerifyOutcome.Intact, null, true);
        }

        static VerifyResult VerifyFolder(string path, CloudGame cloudGame, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var root = new DirectoryInfo(path);
            var localFiles = root.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            if (localFiles.Count == 0 || localFiles.Sum(f => f.Length) == 0)
                return new VerifyResult(VerifyOutcome.NotInstalled, null, cloudGame != null);

            if (cloudGame == null)
            {
                var largest = localFiles.OrderByDescending(f => f.Length).First();
                progress?.Report(1);
                return IntegrityChecker.QuickCheck(largest.FullName).IsCorrupt
                    ? Damaged(Relative(root, largest) + " está zerado", false)
                    : new VerifyResult(VerifyOutcome.Intact, null, false);
            }

            if (!cloudGame.IsDirectory)
                return Damaged("na nuvem o jogo é um arquivo, e aqui é uma pasta", true);

            // Arquivos a mais aqui não contam: alguns emuladores gravam dentro da pasta do jogo.
            var cloudRoot = new DirectoryInfo(cloudGame.FullPath);
            var byName = localFiles.ToDictionary(f => Relative(root, f), StringComparer.OrdinalIgnoreCase);
            var cloudFiles = cloudRoot.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            foreach (var cloudFile in cloudFiles)
            {
                var name = Relative(cloudRoot, cloudFile);
                if (!byName.TryGetValue(name, out var mine))
                    return Damaged("falta " + name, true);

                if (mine.Length != cloudFile.Length)
                    return Damaged(name + " tem um tamanho diferente do que está na nuvem", true);
            }

            var biggest = cloudFiles.OrderByDescending(f => f.Length).FirstOrDefault();
            if (biggest != null && biggest.Length > 0)
            {
                var name = Relative(cloudRoot, biggest);
                var mine = byName[name];
                if (IntegrityChecker.QuickCheck(mine.FullName).IsCorrupt)
                    return Damaged(name + " está zerado", true);

                var difference = CompareSampled(mine.FullName, biggest.FullName, biggest.Length, progress, 0, 1, cancellationToken);
                if (difference != null)
                    return Damaged(name + ": " + difference, true);
            }

            progress?.Report(1);
            return new VerifyResult(VerifyOutcome.Intact, null, true);
        }

        static VerifyResult VerifyAgainstZip(string path, ZipEntryInfo disc, IProgress<double> progress, CancellationToken cancellationToken)
        {
            long length = new FileInfo(path).Length;
            if (length != disc.UncompressedSize)
                return Damaged("o tamanho é diferente do disco no ZIP da nuvem (" + Format.Bytes(length) + " aqui, " + Format.Bytes(disc.UncompressedSize) + " no ZIP)", true);

            var crc = new Crc32();
            var buffer = new byte[4 << 20];
            long done = 0;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    crc.Append(buffer, 0, read);
                    done += read;
                    progress?.Report(length > 0 ? (double)done / length : 1);
                }
            }

            return crc.Value != disc.Crc32
                ? Damaged("o conteúdo não confere com o ZIP da nuvem (CRC-32)", true)
                : new VerifyResult(VerifyOutcome.Intact, null, true);
        }

        /// <summary>O disco de dentro de um ZIP com um disco só, que a instalação extraiu; nulo para outros arquivos.</summary>
        static ZipEntryInfo ZipDisc(CloudGame cloudGame)
        {
            if (!cloudGame.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                return ZipCatalog.GetSingleDiscImage(ZipCatalog.Read(cloudGame.FullPath));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <returns>Nulo se os blocos conferem; senão, onde diferem.</returns>
        static string CompareSampled(string localPath, string cloudPath, long length, IProgress<double> progress, double from, double to, CancellationToken cancellationToken)
        {
            var offsets = SampleOffsets(length);
            using (var local = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess))
            using (var cloud = new FileStream(cloudPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess))
            {
                for (int i = 0; i < offsets.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int count = (int)Math.Min(CompareBlockSize, length - offsets[i]);
                    var mine = ReadAt(local, offsets[i], count);
                    var theirs = ReadAt(cloud, offsets[i], count);
                    if (mine.Length != theirs.Length || !mine.SequenceEqual(theirs))
                        return "o conteúdo é diferente do arquivo na nuvem (perto de " + Format.Bytes(offsets[i]) + ")";

                    progress?.Report(from + (to - from) * (i + 1) / offsets.Count);
                }
            }

            return null;
        }

        /// <summary>Começo, fim e pontos espalhados entre eles, alinhados ao bloco.</summary>
        internal static List<long> SampleOffsets(long length)
        {
            var offsets = new SortedSet<long> { 0 };
            if (length > CompareBlockSize)
            {
                offsets.Add(length - CompareBlockSize);
                for (int i = 1; i < CompareSamples - 1; i++)
                {
                    long offset = (long)((double)(length - CompareBlockSize) * i / (CompareSamples - 1));
                    offsets.Add(offset / CompareBlockSize * CompareBlockSize);
                }
            }

            return offsets.ToList();
        }

        static byte[] ReadAt(Stream stream, long offset, int count)
        {
            var buffer = new byte[count];
            stream.Seek(offset, SeekOrigin.Begin);
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read == 0)
                    break;

                total += read;
            }

            if (total < count)
                Array.Resize(ref buffer, total);

            return buffer;
        }

        static string Relative(DirectoryInfo root, FileInfo file) => file.FullName.Substring(root.FullName.Length).TrimStart('\\', '/');

        static VerifyResult Damaged(string detail, bool withCloud) => new VerifyResult(VerifyOutcome.Damaged, detail, withCloud);
    }
}
