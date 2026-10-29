using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Retroboat.Transfers;

namespace Retroboat.Integrity
{
    public enum IntegrityStatus
    {
        /// <summary>Assinatura do formato encontrada e conteúdo não zerado.</summary>
        Ok,
        /// <summary>Conteúdo não zerado, mas o formato não tem assinatura conhecida.</summary>
        Unverified,
        /// <summary>A extensão pede uma assinatura que não está lá. Pode ser outro formato ou dano parcial.</summary>
        SignatureMissing,
        /// <summary>Tudo o que foi lido é zero: o arquivo existe, mas o conteúdo se perdeu.</summary>
        Zeroed,
        Empty,
        Unreadable
    }

    public sealed class IntegrityResult
    {
        public IntegrityResult(IntegrityStatus status, string format, string detail)
        {
            Status = status;
            Format = format;
            Detail = detail;
        }

        public IntegrityStatus Status { get; }
        public string Format { get; }
        public string Detail { get; }

        public bool IsCorrupt => Status == IntegrityStatus.Zeroed || Status == IntegrityStatus.Empty;
    }

    public sealed class FullCheckResult
    {
        public long Length { get; set; }
        public string Sha1 { get; set; }
        public string Crc32 { get; set; }
        public bool AllZero { get; set; }
    }

    public static class IntegrityChecker
    {
        public const int SampleSize = 64 * 1024;
        public const int DefaultSamples = 8;

        /// <summary>
        /// Verificação rápida por amostragem: lê poucos blocos (início, assinatura do formato e
        /// pontos espalhados até o fim). Numa pasta sincronizada como o Google Drive, só esses blocos
        /// são baixados, então funciona em jogos que ainda estão só na nuvem.
        /// </summary>
        public static IntegrityResult QuickCheck(string path, int samples = DefaultSamples)
        {
            long length;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new IntegrityResult(IntegrityStatus.Unreadable, null, ex.Message);
            }

            if (length == 0)
                return new IntegrityResult(IntegrityStatus.Empty, null, "arquivo vazio");

            var extension = Path.GetExtension(path);
            var signatures = FormatSignatures.ForExtension(extension);

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess))
                {
                    bool anyNonZero = false;
                    foreach (var offset in GetSampleOffsets(length, samples, signatures))
                    {
                        var block = StreamReading.ReadAt(stream, offset, (int)Math.Min(SampleSize, length - offset));
                        if (!IsAllZero(block, block.Length))
                        {
                            anyNonZero = true;
                            break;
                        }
                    }

                    if (!anyNonZero)
                        return new IntegrityResult(IntegrityStatus.Zeroed, null, "conteúdo zerado em todos os pontos amostrados");

                    if (signatures.Count == 0)
                        return new IntegrityResult(IntegrityStatus.Unverified, null, "formato sem assinatura conhecida");

                    var match = signatures.FirstOrDefault(s => s.Matches(stream, length));
                    if (match != null)
                        return new IntegrityResult(IntegrityStatus.Ok, match.Name, null);

                    var expected = string.Join(" / ", signatures.Select(s => s.Name).Distinct());
                    if (FormatSignatures.IsAmbiguous(extension))
                        return new IntegrityResult(IntegrityStatus.Unverified, null, "não é " + expected + "; a extensão é usada por vários formatos");

                    return new IntegrityResult(IntegrityStatus.SignatureMissing, null, "assinatura de " + expected + " não encontrada");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new IntegrityResult(IntegrityStatus.Unreadable, null, ex.Message);
            }
        }

        /// <summary>Verificação completa: lê o arquivo inteiro e calcula SHA-1 e CRC-32.</summary>
        public static FullCheckResult FullCheck(string path, IProgress<TransferProgress> progress = null, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[4 << 20];
            var crc = new Crc32();
            bool allZero = true;
            long done = 0;

            using (var sha1 = SHA1.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
            {
                var meter = new RateMeter(stream.Length);
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    sha1.TransformBlock(buffer, 0, read, null, 0);
                    crc.Append(buffer, 0, read);
                    if (allZero && !IsAllZero(buffer, read))
                        allZero = false;

                    done += read;
                    meter.Report(progress, Path.GetFileName(path), done);
                }

                sha1.TransformFinalBlock(buffer, 0, 0);
                meter.Report(progress, Path.GetFileName(path), done, force: true);

                return new FullCheckResult
                {
                    Length = done,
                    Sha1 = BitConverter.ToString(sha1.Hash).Replace("-", "").ToLowerInvariant(),
                    Crc32 = crc.Value.ToString("x8"),
                    AllZero = allZero && done > 0
                };
            }
        }

        internal static IEnumerable<long> GetSampleOffsets(long length, int samples, IEnumerable<FormatSignature> signatures)
        {
            var offsets = new SortedSet<long> { 0 };

            long lastStart = Math.Max(0, length - SampleSize);
            int points = Math.Max(2, samples);
            for (int i = 1; i < points; i++)
                offsets.Add(AlignDown(lastStart * i / (points - 1)));

            offsets.Add(lastStart);

            foreach (var signature in signatures)
            {
                if (signature.Offset < length)
                    offsets.Add(signature.Offset);
            }

            return offsets;
        }

        static long AlignDown(long value) => value - value % 4096;

        static bool IsAllZero(byte[] buffer, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] != 0)
                    return false;
            }

            return true;
        }
    }
}
