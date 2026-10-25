using System;
using System.Text;

namespace Retroboat.Saves
{
    /// <summary>
    /// Confere se um arquivo é um memory card de PS2 inteiro, no formato que o PCSX2 grava: superbloco
    /// ("Sony PS2 Memory Card Format"), tamanho que bate com o número de clusters (com ou sem os 16 bytes de
    /// ECC por página) e a entrada "." do diretório raiz no lugar. Um cartão cortado no meio de uma gravação
    /// (pen drive arrancado, queda de energia), zerado ou trocado por outro arquivo não passa.
    /// Layout: o mesmo do mymc (ps2mc.py) e do FileMcd do PCSX2.
    /// </summary>
    public static class Ps2MemoryCard
    {
        public const string Magic = "Sony PS2 Memory Card Format ";
        const int SpareBytes = 16;
        const int DirEntryNameOffset = 0x40;
        const ushort DirExists = 0x8000;
        const ushort DirDirectory = 0x0020;

        /// <returns>Nulo se o cartão está inteiro; senão, o motivo.</returns>
        public static string Validate(byte[] data)
        {
            if (data.Length < 0x200)
                return "arquivo pequeno demais para um memory card";

            if (Encoding.ASCII.GetString(data, 0, Magic.Length) != Magic)
                return "não começa com o cabeçalho de memory card de PS2 (cartão sem formatar ou danificado)";

            int pageSize = BitConverter.ToUInt16(data, 0x28);
            int pagesPerCluster = BitConverter.ToUInt16(data, 0x2A);
            long clusters = BitConverter.ToUInt32(data, 0x30);
            long allocOffset = BitConverter.ToUInt32(data, 0x34);
            long rootCluster = BitConverter.ToUInt32(data, 0x3C);

            if (pageSize != 512 || pagesPerCluster < 1 || pagesPerCluster > 16 || clusters == 0)
                return "cabeçalho do memory card com valores impossíveis";

            long pages = clusters * pagesPerCluster;
            int rawPage;
            if (data.Length == pages * (pageSize + SpareBytes))
                rawPage = pageSize + SpareBytes;
            else if (data.Length == pages * pageSize)
                rawPage = pageSize;
            else
                return "tamanho não confere com o cabeçalho (" + data.Length + " bytes, esperado " + pages * (pageSize + SpareBytes) + "): o arquivo foi cortado";

            long root = allocOffset + rootCluster;
            if (root >= clusters)
                return "diretório raiz fora do cartão";

            long offset = root * pagesPerCluster * rawPage;
            ushort mode = BitConverter.ToUInt16(data, (int)offset);
            if ((mode & (DirExists | DirDirectory)) != (DirExists | DirDirectory) || data[offset + DirEntryNameOffset] != (byte)'.' || data[offset + DirEntryNameOffset + 1] != 0)
                return "diretório raiz danificado";

            return null;
        }
    }
}
