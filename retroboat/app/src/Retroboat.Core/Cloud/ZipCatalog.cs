using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Retroboat.Cloud
{
    public sealed class ZipEntryInfo
    {
        public string Name { get; set; }
        public ushort Method { get; set; }
        public uint Crc32 { get; set; }
        public long CompressedSize { get; set; }
        public long UncompressedSize { get; set; }
        public long LocalHeaderOffset { get; set; }

        public string FileName => Path.GetFileName(Name.Replace('/', '\\'));
        public bool IsDirectory => Name.EndsWith("/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Lê o diretório central de um ZIP (inclusive ZIP64) só com leituras no fim do arquivo. Numa pasta do
    /// Google Drive para computador, isso baixa poucos KB de um ZIP de vários GB.
    /// </summary>
    public static class ZipCatalog
    {
        const uint EndOfCentralDirectory = 0x06054b50;
        const uint Zip64EndLocator = 0x07064b50;
        const uint Zip64End = 0x06064b50;
        const uint CentralEntry = 0x02014b50;
        const uint LocalHeader = 0x04034b50;

        // Formatos de disco que um emulador lê direto; um ZIP com um desses dentro vale a pena extrair na instalação.
        static readonly HashSet<string> DiscImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".iso", ".chd", ".cso", ".zso", ".rvz", ".wbfs", ".gcz", ".nsp", ".xci", ".pbp"
        };

        public static List<ZipEntryInfo> Read(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess))
                return Read(stream);
        }

        public static List<ZipEntryInfo> Read(Stream stream)
        {
            long length = stream.Length;
            int tailSize = (int)Math.Min(length, 65536 + 22);
            var tail = ReadAt(stream, length - tailSize, tailSize);

            int eocd = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tail, i) == EndOfCentralDirectory)
                {
                    eocd = i;
                    break;
                }
            }

            if (eocd < 0)
                throw new InvalidDataException("não é um ZIP válido (fim do diretório central não encontrado)");

            long entries = BitConverter.ToUInt16(tail, eocd + 10);
            long cdSize = BitConverter.ToUInt32(tail, eocd + 12);
            long cdOffset = BitConverter.ToUInt32(tail, eocd + 16);

            if (eocd >= 20 && BitConverter.ToUInt32(tail, eocd - 20) == Zip64EndLocator)
            {
                long zip64EndOffset = BitConverter.ToInt64(tail, eocd - 20 + 8);
                var zip64 = ReadAt(stream, zip64EndOffset, 56);
                if (BitConverter.ToUInt32(zip64, 0) != Zip64End)
                    throw new InvalidDataException("ZIP64 inválido");

                entries = BitConverter.ToInt64(zip64, 32);
                cdSize = BitConverter.ToInt64(zip64, 40);
                cdOffset = BitConverter.ToInt64(zip64, 48);
            }

            var cd = ReadAt(stream, cdOffset, (int)cdSize);
            var result = new List<ZipEntryInfo>();
            int pos = 0;
            for (long n = 0; n < entries; n++)
            {
                if (pos + 46 > cd.Length || BitConverter.ToUInt32(cd, pos) != CentralEntry)
                    throw new InvalidDataException("diretório central do ZIP corrompido");

                var entry = new ZipEntryInfo
                {
                    Method = BitConverter.ToUInt16(cd, pos + 10),
                    Crc32 = BitConverter.ToUInt32(cd, pos + 16),
                    CompressedSize = BitConverter.ToUInt32(cd, pos + 20),
                    UncompressedSize = BitConverter.ToUInt32(cd, pos + 24),
                    LocalHeaderOffset = BitConverter.ToUInt32(cd, pos + 42)
                };

                int nameLength = BitConverter.ToUInt16(cd, pos + 28);
                int extraLength = BitConverter.ToUInt16(cd, pos + 30);
                int commentLength = BitConverter.ToUInt16(cd, pos + 32);
                ushort flags = BitConverter.ToUInt16(cd, pos + 8);
                var encoding = (flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(437);
                entry.Name = encoding.GetString(cd, pos + 46, nameLength);

                ApplyZip64Extra(cd, pos + 46 + nameLength, extraLength, entry);
                result.Add(entry);
                pos += 46 + nameLength + extraLength + commentLength;
            }

            return result;
        }

        /// <summary>
        /// O arquivo de disco a extrair na instalação: o ZIP precisa ter um único arquivo, num formato de
        /// disco, com compressão que o .NET sabe abrir (armazenado ou deflate). Senão, o ZIP é instalado como está.
        /// </summary>
        public static ZipEntryInfo GetSingleDiscImage(IReadOnlyList<ZipEntryInfo> entries)
        {
            var files = entries.Where(e => !e.IsDirectory).ToList();
            if (files.Count != 1)
                return null;

            var entry = files[0];
            return DiscImages.Contains(Path.GetExtension(entry.FileName)) && (entry.Method == 0 || entry.Method == 8) ? entry : null;
        }

        /// <summary>Onde começam os dados comprimidos de uma entrada (depois do cabeçalho local).</summary>
        public static long GetDataOffset(Stream stream, ZipEntryInfo entry)
        {
            var header = ReadAt(stream, entry.LocalHeaderOffset, 30);
            if (BitConverter.ToUInt32(header, 0) != LocalHeader)
                throw new InvalidDataException("cabeçalho local do ZIP corrompido");

            return entry.LocalHeaderOffset + 30 + BitConverter.ToUInt16(header, 26) + BitConverter.ToUInt16(header, 28);
        }

        static void ApplyZip64Extra(byte[] cd, int start, int length, ZipEntryInfo entry)
        {
            int pos = start;
            while (pos + 4 <= start + length)
            {
                ushort id = BitConverter.ToUInt16(cd, pos);
                ushort size = BitConverter.ToUInt16(cd, pos + 2);
                if (id == 0x0001)
                {
                    int field = pos + 4;
                    if (entry.UncompressedSize == uint.MaxValue) { entry.UncompressedSize = BitConverter.ToInt64(cd, field); field += 8; }
                    if (entry.CompressedSize == uint.MaxValue) { entry.CompressedSize = BitConverter.ToInt64(cd, field); field += 8; }
                    if (entry.LocalHeaderOffset == uint.MaxValue) { entry.LocalHeaderOffset = BitConverter.ToInt64(cd, field); }
                    return;
                }

                pos += 4 + size;
            }
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
                    throw new EndOfStreamException("ZIP truncado");

                total += read;
            }

            return buffer;
        }
    }
}
